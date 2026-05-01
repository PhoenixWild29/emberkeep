using System;
using System.IO;
using System.Threading;
using EmberKeep.AI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace EmberKeep.Game {
    public class DialogueController : MonoBehaviour {
        [Header("Scene refs")]
        public Npc npc;
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

            bool canTalk = npc != null && npc.PlayerInRange;
            if (promptPanel) promptPanel.SetActive(canTalk);
            if (canTalk && promptText) promptText.text = $"[E]  Talk to {npc.DisplayName}";

            if (canTalk && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                StartDialogue();
        }

        void StartDialogue() {
            _inDialogue = true;
            if (player) player.SetInputEnabled(false);
            if (promptPanel)   promptPanel.SetActive(false);
            if (dialoguePanel) dialoguePanel.SetActive(true);

            if (npcNameText) npcNameText.text = npc.DisplayName;
            if (npcReplyText) npcReplyText.text = "";

            if (npc != null && npc.personality != null)
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
            if (dialoguePanel) dialoguePanel.SetActive(false);
            if (player) player.SetInputEnabled(true);
        }

        async void OnSendClicked() {
            if (!_inDialogue) return;
            if (inputField == null || string.IsNullOrWhiteSpace(inputField.text)) return;

            string userMsg = inputField.text;
            inputField.text = "";
            inputField.interactable = false;
            if (sendButton) sendButton.interactable = false;
            if (npcReplyText) npcReplyText.text = "";

            _genCts?.Cancel();
            _genCts = new CancellationTokenSource();
            int maxTokens = npc?.personality?.maxResponseTokens ?? 96;

            try {
                await foreach (var token in LlmService.Instance.GenerateAsync(
                                   userMsg, maxTokens, _genCts.Token)) {
                    if (npcReplyText) npcReplyText.text += token;
                }
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
