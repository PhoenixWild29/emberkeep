using System;
using System.IO;
using System.Threading;
using EmberKeep.AI;
using UnityEditor;
using UnityEngine;

namespace EmberKeep.EditorTools {
    // Day 6-A: "Generate NPC" Editor tool.
    // Designer types a one-line concept, picks a type, hits Generate. We call
    // LlmService directly from the editor, stream the JSON response into the
    // preview pane, parse it, and save a ScriptableObject asset under
    // Assets/EmberKeep/ScriptableObjects/NPCs/.
    //
    // The point of this window is to demonstrate the "AI superpower" pattern
    // from spec section 6.6: a designer-facing tool that uses the local LLM
    // to compress an hour of prompt-engineering work down to ten seconds.
    public class GenerateNpcWindow : EditorWindow {
        public enum NpcType {
            Plain,        // -> NpcPersonality
            Merchant,     // -> MerchantPersonality
            Storyteller,  // -> StorytellerPersonality
        }

        const string PersonalityDir = "Assets/EmberKeep/ScriptableObjects/NPCs";
        const string DefaultModel   = "Llama-3.2-3B-Instruct-Q4_K_M.gguf";

        string  _concept     = "grumpy retired sailor with a missing eye and a soft spot for stray cats";
        NpcType _type        = NpcType.Plain;
        string  _modelFile   = DefaultModel;
        string  _streamingJson = "";
        string  _statusMessage = "Type a concept and press Generate.";
        Vector2 _outputScroll;

        bool _busy;
        GeneratedNpcData _parsed;
        CancellationTokenSource _cts;

        [MenuItem("EmberKeep/Generate NPC")]
        public static void Open() {
            var w = GetWindow<GenerateNpcWindow>("Generate NPC");
            w.minSize = new Vector2(480f, 520f);
            w.Show();
        }

        void OnGUI() {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Concept", EditorStyles.boldLabel);
            _concept = EditorGUILayout.TextField(_concept);

            EditorGUILayout.Space();
            _type = (NpcType)EditorGUILayout.EnumPopup("NPC Type", _type);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_busy || string.IsNullOrWhiteSpace(_concept))) {
                if (GUILayout.Button("Generate", GUILayout.Height(28f))) {
                    _ = RunGenerationAsync();
                }
            }
            using (new EditorGUI.DisabledScope(!_busy)) {
                if (GUILayout.Button("Cancel", GUILayout.Height(20f))) {
                    _cts?.Cancel();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", _statusMessage, EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("LLM output", EditorStyles.boldLabel);
            _outputScroll = EditorGUILayout.BeginScrollView(_outputScroll, GUILayout.MinHeight(200f));
            EditorGUILayout.TextArea(_streamingJson, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (_parsed != null) {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Parsed preview", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Display name", _parsed.displayName ?? "(missing)");
                EditorGUILayout.LabelField("Tint", _parsed.tint ?? "(missing)");
                using (new EditorGUI.DisabledScope(_busy)) {
                    if (GUILayout.Button($"Save as {SafeAssetName(_parsed.displayName)}.asset",
                                         GUILayout.Height(28f))) {
                        SaveAsset(_parsed);
                    }
                }
            }
        }

        async System.Threading.Tasks.Task RunGenerationAsync() {
            if (_busy) return;
            _busy = true;
            _streamingJson = "";
            _parsed = null;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            Repaint();

            try {
                string modelPath = ResolveModelPath(_modelFile);
                if (!File.Exists(modelPath)) {
                    _statusMessage = $"Model not found at {modelPath}.";
                    return;
                }

                _statusMessage = "Initialising model (one-time, ~4s)...";
                Repaint();
                await LlmService.Instance.InitializeAsync(modelPath);

                _statusMessage = "Generating...";
                Repaint();

                string sys  = BuildSystemPrompt(_type);
                string user = "Concept: " + _concept;
                LlmService.Instance.SetSystem(sys);

                int max = 512;
                await foreach (var token in LlmService.Instance.GenerateAsync(
                                   user, max, _cts.Token)) {
                    _streamingJson += token;
                    Repaint();
                }

                _parsed = TryParse(_streamingJson);
                _statusMessage = _parsed != null
                    ? "Parsed successfully. Review the preview and click Save."
                    : "Could not parse the JSON. You can edit the LLM output above and re-run, or click Generate again.";
            } catch (OperationCanceledException) {
                _statusMessage = "Cancelled.";
            } catch (Exception e) {
                _statusMessage = $"Error: {e.Message}";
                Debug.LogException(e);
            } finally {
                _busy = false;
                Repaint();
            }
        }

        // ---- Prompts ----

        static string BuildSystemPrompt(NpcType type) {
            string fields;
            switch (type) {
                case NpcType.Merchant:
                    fields =
                        "displayName (string), systemPrompt (string, 2-4 sentences), " +
                        "refusalLine (string, 1 sentence in character), tint (string, '#RRGGBB' hex), " +
                        "itemName (string), itemDescription (string), " +
                        "askingPrice (int, between 5 and 100), " +
                        "walkAwayBase (int, between 1 and the asking price)";
                    break;
                case NpcType.Storyteller:
                    fields =
                        "displayName (string), systemPrompt (string, 2-4 sentences), " +
                        "refusalLine (string, 1 sentence in character), tint (string, '#RRGGBB' hex), " +
                        "storyTopics (array of 4 to 6 strings, each a short sentence " +
                        "describing a story topic the NPC could tell)";
                    break;
                default:
                    fields =
                        "displayName (string), systemPrompt (string, 2-4 sentences), " +
                        "refusalLine (string, 1 sentence in character), tint (string, '#RRGGBB' hex)";
                    break;
            }

            return
                "You are a fantasy-tavern NPC designer. Given a one-line concept from a game " +
                "designer, output a SINGLE JSON object with exactly these fields: " + fields + ". " +
                "Do not include code fences. Do not include any text outside the JSON object. " +
                "The systemPrompt should establish the character's voice and quirks and " +
                "instruct them to never reveal they are an AI. The tint should fit the " +
                "character (warm browns for sailors, deep greens for hunters, etc.).";
        }

        // ---- Parsing ----

        static GeneratedNpcData TryParse(string raw) {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string cleaned = raw.Trim();

            // Strip ```json ... ``` fences if the model added them anyway.
            if (cleaned.StartsWith("```")) {
                int firstNl = cleaned.IndexOf('\n');
                if (firstNl >= 0) cleaned = cleaned.Substring(firstNl + 1);
                int endFence = cleaned.LastIndexOf("```");
                if (endFence >= 0) cleaned = cleaned.Substring(0, endFence);
                cleaned = cleaned.Trim();
            }

            // Take from the first '{' to the last '}', so trailing chatter
            // ("Hope this helps!") doesn't break parsing.
            int start = cleaned.IndexOf('{');
            int end   = cleaned.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            string json = cleaned.Substring(start, end - start + 1);

            try {
                return JsonUtility.FromJson<GeneratedNpcData>(json);
            } catch {
                return null;
            }
        }

        // ---- Save ----

        void SaveAsset(GeneratedNpcData data) {
            string safe = SafeAssetName(data.displayName);
            if (!AssetDatabase.IsValidFolder(PersonalityDir)) {
                if (!AssetDatabase.IsValidFolder("Assets/EmberKeep/ScriptableObjects"))
                    AssetDatabase.CreateFolder("Assets/EmberKeep", "ScriptableObjects");
                AssetDatabase.CreateFolder("Assets/EmberKeep/ScriptableObjects", "NPCs");
            }
            string path = AssetDatabase.GenerateUniqueAssetPath($"{PersonalityDir}/{safe}.asset");

            ScriptableObject so;
            switch (_type) {
                case NpcType.Merchant: {
                    var m = ScriptableObject.CreateInstance<MerchantPersonality>();
                    ApplyBaseFields(m, data);
                    m.itemName        = !string.IsNullOrWhiteSpace(data.itemName) ? data.itemName : "trinket";
                    m.itemDescription = data.itemDescription ?? "";
                    m.askingPrice     = data.askingPrice  > 0 ? data.askingPrice  : 15;
                    m.walkAwayBase    = data.walkAwayBase > 0 ? data.walkAwayBase : Mathf.Max(1, m.askingPrice / 2);
                    so = m;
                    break;
                }
                case NpcType.Storyteller: {
                    var s = ScriptableObject.CreateInstance<StorytellerPersonality>();
                    ApplyBaseFields(s, data);
                    if (data.storyTopics != null && data.storyTopics.Length > 0)
                        s.storyTopics = data.storyTopics;
                    so = s;
                    break;
                }
                default: {
                    var p = ScriptableObject.CreateInstance<NpcPersonality>();
                    ApplyBaseFields(p, data);
                    so = p;
                    break;
                }
            }

            AssetDatabase.CreateAsset(so, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(so);
            Selection.activeObject = so;
            Telemetry.NpcGenerated(_concept, _type.ToString(), data.displayName);
            _statusMessage = $"Saved {path}. Open BuildTavernScene to spawn this NPC, or assign it manually.";
        }

        static void ApplyBaseFields(NpcPersonality p, GeneratedNpcData d) {
            p.displayName  = !string.IsNullOrWhiteSpace(d.displayName) ? d.displayName : "Unknown";
            p.systemPrompt = d.systemPrompt ?? "";
            if (!string.IsNullOrWhiteSpace(d.refusalLine)) p.refusalLine = d.refusalLine;
            if (!string.IsNullOrWhiteSpace(d.tint) &&
                ColorUtility.TryParseHtmlString(d.tint.Trim(), out var c)) {
                p.tint = c;
            }
        }

        static string SafeAssetName(string n) {
            if (string.IsNullOrWhiteSpace(n)) return "GeneratedNpc";
            var chars = n.ToCharArray();
            for (int i = 0; i < chars.Length; i++) {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') chars[i] = '_';
            }
            string result = new string(chars).Trim('_');
            return string.IsNullOrEmpty(result) ? "GeneratedNpc" : result;
        }

        static string ResolveModelPath(string fileOrPath) {
            if (Path.IsPathRooted(fileOrPath)) return fileOrPath;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(root, "Models", fileOrPath);
        }

        // Mirrors what the LLM is asked to emit. Optional fields stay default
        // when the LLM omits them or the asset type doesn't use them.
        [Serializable]
        public class GeneratedNpcData {
            public string displayName;
            public string systemPrompt;
            public string refusalLine;
            public string tint;
            public string itemName;
            public string itemDescription;
            public int    askingPrice;
            public int    walkAwayBase;
            public string[] storyTopics;
        }
    }
}
