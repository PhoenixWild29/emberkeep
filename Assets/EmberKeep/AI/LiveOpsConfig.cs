using System;
using System.IO;
using UnityEngine;

namespace EmberKeep.AI {
    // JSON-driven runtime configuration. Lives at
    // <persistentDataPath>/liveops.json. Re-read on demand via Reload(); the
    // dialogue layer calls Reload() every time a new dialogue starts so a
    // designer can edit the file mid-session and see changes on the next
    // turn without rebuilding the player.
    //
    // The schema is intentionally flat and forgiving: missing fields fall
    // back to defaults, unknown fields are ignored, and a bad JSON file
    // produces a warning + falls back to defaults rather than crashing.
    //
    // To swap the file source for a remote URL later (the actual Live Ops
    // pattern), only Load() needs to change - call sites use the typed
    // accessors on Current.
    [Serializable]
    public class LiveOpsConfig {
        public int    version                       = 1;
        public bool   safety_filter_enabled         = true;
        public bool   telemetry_enabled             = true;
        public bool   memory_enabled                = true;
        public int    max_response_tokens_override  = 0;   // 0 = use personality default
        public int    max_story_tokens_override     = 0;   // 0 = use personality default
        public int    per_frame_budget_ms           = 8;
        public string ab_variant                    = "A";
        public string config_note                   = "Edit this file at <persistentDataPath>/liveops.json to override runtime behaviour without rebuilding.";

        // Singleton accessor.
        static LiveOpsConfig _current;
        public static LiveOpsConfig Current {
            get {
                if (_current == null) Reload();
                return _current;
            }
        }

        public static string Path => System.IO.Path.Combine(
            Application.persistentDataPath, "liveops.json");

        public static void Reload() {
            try {
                string p = Path;
                if (!File.Exists(p)) {
                    _current = new LiveOpsConfig();
                    WriteDefaults(p, _current);
                    return;
                }
                string json = File.ReadAllText(p);
                var parsed = JsonUtility.FromJson<LiveOpsConfig>(json);
                _current = parsed ?? new LiveOpsConfig();
            } catch (Exception e) {
                Debug.LogWarning($"[LiveOpsConfig] failed to load - using defaults. {e.Message}");
                _current = new LiveOpsConfig();
            }
        }

        public static void WriteDefaults(string path, LiveOpsConfig defaults) {
            try {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(defaults, prettyPrint: true));
            } catch (Exception e) {
                Debug.LogWarning($"[LiveOpsConfig] failed to seed default config: {e.Message}");
            }
        }

        // Typed accessors: each one folds the override against the per-NPC
        // defaults, returning the value the runtime should actually use.

        public static int ResolveMaxResponseTokens(int personalityDefault) {
            int o = Current.max_response_tokens_override;
            return o > 0 ? o : personalityDefault;
        }

        public static int ResolveMaxStoryTokens(int personalityDefault) {
            int o = Current.max_story_tokens_override;
            return o > 0 ? o : personalityDefault;
        }
    }
}
