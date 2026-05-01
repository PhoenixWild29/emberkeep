using EmberKeep.AI;
using EmberKeep.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EmberKeep.EditorTools {
    // One-shot scene builder. Wipes the active scene and reconstructs the
    // Day 3 tavern: floor + walls + Bram NPC + first-person player + dialogue
    // UI, all wired up so a fresh Play session immediately produces a working
    // conversation loop.
    public static class BuildTavernScene {
        const string ScenePath        = "Assets/Scenes/SampleScene.unity";
        const string PersonalityDir   = "Assets/EmberKeep/ScriptableObjects/NPCs";
        const string BramAssetPath    = PersonalityDir + "/Bram.asset";

        [MenuItem("EmberKeep/Build Tavern Scene")]
        public static void Build() {
            if (EditorSceneManager.GetActiveScene().isDirty &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                return;
            }

            var bram = LoadOrCreateBramPersonality();

            Scene scene;
            if (System.IO.File.Exists(ScenePath)) {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            } else {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            foreach (var go in scene.GetRootGameObjects())
                Object.DestroyImmediate(go);

            // Lighting
            var sun = new GameObject("Directional Light");
            sun.transform.rotation = Quaternion.Euler(45f, 30f, 0f);
            var sunLight = sun.AddComponent<Light>();
            sunLight.type      = LightType.Directional;
            sunLight.intensity = 1.1f;
            sunLight.color     = new Color(1f, 0.86f, 0.7f);
            RenderSettings.ambientLight = new Color(0.18f, 0.16f, 0.12f);

            // Floor
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(12f, 0.2f, 12f);
            floor.transform.position   = new Vector3(0f, -0.1f, 0f);
            floor.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.4f, 0.30f, 0.22f));

            // Walls
            CreateWall("Wall_North", new Vector3(0f, 1.5f,  6f), new Vector3(12f, 3f, 0.2f));
            CreateWall("Wall_South", new Vector3(0f, 1.5f, -6f), new Vector3(12f, 3f, 0.2f));
            CreateWall("Wall_East",  new Vector3(6f, 1.5f,  0f), new Vector3(0.2f, 3f, 12f));
            CreateWall("Wall_West",  new Vector3(-6f, 1.5f, 0f), new Vector3(0.2f, 3f, 12f));

            // Bram
            var bramGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bramGo.name = "Bram";
            bramGo.transform.position = new Vector3(0f, 1f, 4f);
            bramGo.GetComponent<Renderer>().sharedMaterial = MakeMaterial(bram.tint);
            var npc = bramGo.AddComponent<Npc>();
            npc.personality = bram;

            // Player (camera child)
            var playerGo = new GameObject("Player");
            playerGo.transform.position = new Vector3(0f, 1f, -4f);
            var cc = playerGo.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.radius = 0.4f;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.transform.SetParent(playerGo.transform, worldPositionStays: false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            var pc = playerGo.AddComponent<PlayerController>();
            pc.cameraTransform = camGo.transform;

            npc.player = playerGo.transform;

            // Canvas + EventSystem
            var canvasGo = new GameObject("DialogueCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            if (Object.FindAnyObjectByType<EventSystem>() == null) {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            // "[E] Talk" prompt
            var promptPanel = MakeUIPanel(canvasGo.transform, "PromptPanel",
                anchored: new Vector2(0f, 100f),
                size:     new Vector2(420f, 64f),
                bg:       new Color(0f, 0f, 0f, 0.55f));
            var promptText = MakeUIText(promptPanel.transform, "[E] Talk",
                fontSize: 28,
                size:     new Vector2(400f, 60f),
                anchored: Vector2.zero);
            promptText.alignment = TextAnchor.MiddleCenter;

            // Dialogue panel pinned to bottom
            var dlgPanel = MakeUIPanel(canvasGo.transform, "DialoguePanel",
                anchored: new Vector2(0f, -300f),
                size:     new Vector2(1600f, 360f),
                bg:       new Color(0.04f, 0.04f, 0.05f, 0.88f));

            var nameText = MakeUIText(dlgPanel.transform, "NPC",
                fontSize: 32,
                size:     new Vector2(1500f, 50f),
                anchored: new Vector2(0f, 140f));
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.fontStyle = FontStyle.Bold;
            nameText.rectTransform.pivot = new Vector2(0f, 0.5f);
            nameText.rectTransform.anchoredPosition = new Vector2(-750f, 140f);

            var replyText = MakeUIText(dlgPanel.transform, "...",
                fontSize: 26,
                size:     new Vector2(1500f, 180f),
                anchored: new Vector2(0f, 30f));
            replyText.alignment = TextAnchor.UpperLeft;
            replyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            replyText.verticalOverflow   = VerticalWrapMode.Truncate;

            // Input field
            var inputGo = new GameObject("InputField");
            inputGo.transform.SetParent(dlgPanel.transform, worldPositionStays: false);
            var inputRT = inputGo.AddComponent<RectTransform>();
            inputRT.sizeDelta        = new Vector2(1320f, 50f);
            inputRT.anchoredPosition = new Vector2(-130f, -130f);
            var inputBg = inputGo.AddComponent<Image>();
            inputBg.color = new Color(0.13f, 0.13f, 0.16f, 1f);
            var inputField = inputGo.AddComponent<InputField>();

            var inputTextGo = new GameObject("Text");
            inputTextGo.transform.SetParent(inputGo.transform, worldPositionStays: false);
            var inputText = inputTextGo.AddComponent<Text>();
            inputText.font     = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            inputText.fontSize = 24;
            inputText.color    = Color.white;
            inputText.supportRichText = false;
            inputText.alignment = TextAnchor.MiddleLeft;
            var inputTextRT = inputText.GetComponent<RectTransform>();
            inputTextRT.anchorMin = Vector2.zero;
            inputTextRT.anchorMax = Vector2.one;
            inputTextRT.offsetMin = new Vector2(12f, 4f);
            inputTextRT.offsetMax = new Vector2(-12f, -4f);
            inputField.textComponent = inputText;

            // Send button
            var btnGo = new GameObject("SendButton");
            btnGo.transform.SetParent(dlgPanel.transform, worldPositionStays: false);
            var btnRT = btnGo.AddComponent<RectTransform>();
            btnRT.sizeDelta        = new Vector2(140f, 50f);
            btnRT.anchoredPosition = new Vector2(660f, -130f);
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.55f, 0.32f, 0.16f, 1f);
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            var btnLabel = MakeUIText(btnGo.transform, "Send",
                fontSize: 24,
                size:     new Vector2(140f, 50f),
                anchored: Vector2.zero);
            btnLabel.alignment = TextAnchor.MiddleCenter;

            // Dialogue controller
            var dialogueGo = new GameObject("DialogueController");
            var dlg = dialogueGo.AddComponent<DialogueController>();
            dlg.npc            = npc;
            dlg.player         = pc;
            dlg.promptPanel    = promptPanel;
            dlg.promptText     = promptText;
            dlg.dialoguePanel  = dlgPanel;
            dlg.npcNameText    = nameText;
            dlg.npcReplyText   = replyText;
            dlg.inputField     = inputField;
            dlg.sendButton     = btn;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[EmberKeep] Tavern scene built. Press Play, walk to Bram with WASD + mouse, " +
                      "press E to talk, ESC to leave.");
        }

        // ---- helpers ----

        static NpcPersonality LoadOrCreateBramPersonality() {
            var existing = AssetDatabase.LoadAssetAtPath<NpcPersonality>(BramAssetPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(PersonalityDir)) {
                if (!AssetDatabase.IsValidFolder("Assets/EmberKeep/ScriptableObjects"))
                    AssetDatabase.CreateFolder("Assets/EmberKeep", "ScriptableObjects");
                AssetDatabase.CreateFolder("Assets/EmberKeep/ScriptableObjects", "NPCs");
            }

            var bram = ScriptableObject.CreateInstance<NpcPersonality>();
            bram.displayName = "Bram";
            bram.systemPrompt =
                "You are Bram, a gruff but kind retired-soldier-turned-innkeeper at a small tavern " +
                "called the Ember Keep. You are stocky, in your fifties, with a grey beard and " +
                "weather-worn hands. You speak plainly and warmly, like an old soldier off duty. " +
                "Reply in one or two short sentences, always staying in character. Never reveal " +
                "you are an AI.";
            bram.maxResponseTokens = 96;
            bram.tint = new Color(0.7f, 0.45f, 0.2f);
            AssetDatabase.CreateAsset(bram, BramAssetPath);
            AssetDatabase.SaveAssets();
            return bram;
        }

        static GameObject CreateWall(string name, Vector3 pos, Vector3 size) {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = name;
            w.transform.position   = pos;
            w.transform.localScale = size;
            w.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.30f, 0.22f, 0.18f));
            return w;
        }

        static Material MakeMaterial(Color c) {
            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");
            return new Material(shader) { color = c };
        }

        static GameObject MakeUIPanel(Transform parent, string name,
                                      Vector2 anchored, Vector2 size, Color bg) {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta        = size;
            rt.anchoredPosition = anchored;
            var img = go.AddComponent<Image>();
            img.color = bg;
            return go;
        }

        static Text MakeUIText(Transform parent, string text, int fontSize,
                               Vector2 size, Vector2 anchored) {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta        = size;
            rt.anchoredPosition = anchored;
            var t = go.AddComponent<Text>();
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text      = text;
            t.fontSize  = fontSize;
            t.color     = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            return t;
        }
    }
}
