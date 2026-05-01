using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using EmberKeep.AI;
using EmberKeep.BehaviorTrees;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace EmberKeep.Game {
    public class DialogueController : MonoBehaviour {
        [Header("Scene refs")]
        public List<Npc> npcs = new List<Npc>();
        public PlayerController player;

        [Header("UI refs")]
        public GameObject promptPanel;     // floating "[E] Talk" hint
        public Text       promptText;
        public GameObject dialoguePanel;   // bottom-of-screen chat panel
        public Text       npcNameText;
        public Text       npcReplyText;
        public InputField inputField;
        public Button     sendButton;

        [Header("Model")]
        [Tooltip("Filename inside <project>/Models/, or absolute path to a .gguf file.")]
        public string modelFile = "Llama-3.2-3B-Instruct-Q4_K_M.gguf";

        bool _initialized;
        bool _inDialogue;
        Npc _currentNpc;
        Npc _candidate;
        CancellationTokenSource _genCts;

        async void Start() {
            if (promptPanel)   promptPanel.SetActive(false);
            if (dialoguePanel) dialoguePanel.SetActive(false);
            if (sendButton)    sendButton.onClick.AddListener(OnSendClicked);
            if (inputField)    inputField.onSubmit.AddListener(_ => OnSendClicked());

            string path = ResolveModelPath(modelFile);
            if (!File.Exists(path)) {
                Debug.LogError($"[Dialogue] model not found: {path}");
                return;
            }

            try {
                await LlmService.Instance.InitializeAsync(path);
                _initialized = true;
                Debug.Log("[Dialogue] LlmService ready.");
            } catch (Exception e) {
                Debug.LogException(e);
            }
        }

        void Update() {
            if (!_initialized) return;

            if (_inDialogue) {
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    EndDialogue();
                return;
            }

            _candidate = FindClosestNpcInRange();
            bool canTalk = _candidate != null;
            if (promptPanel) promptPanel.SetActive(canTalk);
            if (canTalk && promptText) promptText.text = $"[E]  Talk to {_candidate.DisplayName}";

            if (canTalk && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                StartDialogue(_candidate);
        }

        Npc FindClosestNpcInRange() {
            Npc best = null;
            float bestSqr = float.PositiveInfinity;
            Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;
            foreach (var n in npcs) {
                if (n == null || !n.PlayerInRange) continue;
                float sqr = (n.transform.position - playerPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = n; }
            }
            return best;
        }

        void StartDialogue(Npc npc) {
            _inDialogue = true;
            _currentNpc = npc;
            if (player) player.SetInputEnabled(false);
            if (promptPanel)   promptPanel.SetActive(false);
            if (dialoguePanel) dialoguePanel.SetActive(true);

            if (npcNameText) npcNameText.text = npc.DisplayName;

            // Greeting line: merchants get their formatted scaffolded line so
            // the player immediately knows what's on sale; everyone else gets
            // an empty reply box and the LLM kicks in on first message.
            if (npcReplyText) {
                if (npc is MerchantNpc m && m.MerchantPersonality != null) {
                    npcReplyText.text = m.MerchantPersonality.FormatGreeting();
                } else {
                    npcReplyText.text = "";
                }
            }

            // Set the base system prompt for free-chat mode. Merchant offers
            // override this per-message inside OnSendClicked.
            if (npc.personality != null)
                LlmService.Instance.SetSystem(npc.personality.systemPrompt);

            if (inputField) {
                inputField.text = "";
                inputField.interactable = true;
                inputField.ActivateInputField();
            }
            if (sendButton) sendButton.interactable = true;
        }

        void EndDialogue() {
            _genCts?.Cancel();
            _inDialogue = false;
            _currentNpc = null;
            if (dialoguePanel) dialoguePanel.SetActive(false);
            if (player) player.SetInputEnabled(true);
        }

        async void OnSendClicked() {
            if (!_inDialogue || _currentNpc == null) return;
            if (inputField == null || string.IsNullOrWhiteSpace(inputField.text)) return;

            string raw = inputField.text.Trim();
            inputField.text = "";
            inputField.interactable = false;
            if (sendButton) sendButton.interactable = false;
            if (npcReplyText) npcReplyText.text = "";

            // Decide whether this turn goes through the merchant haggling path
            // (BT decides intent, LLM generates the line) or the plain chat
            // path (LLM responds to free-form user text).
            string systemPrompt;
            string userPrompt;
            string outcomeTag = null;

            if (_currentNpc is MerchantNpc merchant && TryParseOffer(raw, out int offer)) {
                var d = merchant.EvaluateOffer(offer);
                systemPrompt = BuildMerchantSystemPrompt(merchant.MerchantPersonality, d, offer);
                userPrompt   = $"The traveler offers {offer} gold for the " +
                               $"{merchant.MerchantPersonality.itemName}.";
                outcomeTag = $"  [intent={d.intent}, mood {d.moodBefore:+0.00;-0.00; 0.00}->{d.moodAfter:+0.00;-0.00; 0.00}]";
            } else {
                systemPrompt = _currentNpc.personality.systemPrompt;
                userPrompt   = raw;
            }

            LlmService.Instance.SetSystem(systemPrompt);

            _genCts?.Cancel();
            _genCts = new CancellationTokenSource();
            int maxTokens = _currentNpc.personality?.maxResponseTokens ?? 96;

            try {
                await foreach (var token in LlmService.Instance.GenerateAsync(
                                   userPrompt, maxTokens, _genCts.Token)) {
                    if (npcReplyText) npcReplyText.text += token;
                }
                if (outcomeTag != null && npcReplyText)
                    npcReplyText.text += outcomeTag;
            } catch (OperationCanceledException) {
                // expected on EndDialogue / new prompt
            } catch (Exception e) {
                Debug.LogException(e);
            }

            if (_inDialogue) {
                if (inputField) {
                    inputField.interactable = true;
                    inputField.ActivateInputField();
                }
                if (sendButton) sendButton.interactable = true;
            }
        }

        static bool TryParseOffer(string raw, out int gold) {
            gold = 0;
            // Strict parse: only interpret as an offer if the message is
            // basically just a number. Free-form chat that happens to contain
            // a number ("I have 5 gold but...") falls through to plain chat.
            string trimmed = raw.Trim().TrimEnd('g', 'G').TrimEnd().Replace(",", "");
            if (int.TryParse(trimmed, out gold) && gold > 0 && gold < 100000) return true;
            // Also allow "<number> gold"
            var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out gold) &&
                parts[1].StartsWith("gold", StringComparison.OrdinalIgnoreCase) &&
                gold > 0 && gold < 100000) return true;
            gold = 0;
            return false;
        }

        static string BuildMerchantSystemPrompt(MerchantPersonality p,
                                                MerchantBrain.Decision d,
                                                int offer) {
            string instruction = d.intent switch {
                HaggleIntent.Accept =>
                    $"You have decided to ACCEPT the traveler's offer of {d.acceptedAt} gold " +
                    "for the {ITEM}. Express agreement warmly but in your gruff merchant voice.",
                HaggleIntent.Refuse =>
                    "You have decided to REFUSE this offer outright; it is far below what " +
                    "you'll part with the {ITEM} for. Make clear you won't sell at that price, " +
                    "but stay civil.",
                HaggleIntent.Haggle =>
                    $"You have decided to COUNTER-OFFER {d.counterOffer} gold for the {{ITEM}}. " +
                    "State that price firmly in character, mention the gold amount explicitly so " +
                    "the traveler can decide whether to match.",
                _ => "",
            };
            instruction = instruction.Replace("{ITEM}", p.itemName);
            return p.systemPrompt +
                   "\n\n" + instruction +
                   "\nReply in one or two short sentences only.";
        }

        static string ResolveModelPath(string fileOrPath) {
            if (Path.IsPathRooted(fileOrPath)) return fileOrPath;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(root, "Models", fileOrPath);
        }

        void OnApplicationQuit() {
            _genCts?.Cancel();
            LlmService.Instance.Dispose();
        }
    }
}
