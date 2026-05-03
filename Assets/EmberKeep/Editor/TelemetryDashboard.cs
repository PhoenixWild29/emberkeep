using System;
using System.Collections.Generic;
using System.IO;
using EmberKeep.AI;
using UnityEditor;
using UnityEngine;

namespace EmberKeep.EditorTools {
    // Designer-facing dashboard that aggregates telemetry events from the
    // JSONL files at <persistentDataPath>/telemetry/. Pull-mode UI: the
    // window doesn't watch files automatically; click Reload to rescan.
    //
    // The aggregations are all done in C# from the raw events so anyone who
    // pipes the JSONL stream to a different sink later (Mixpanel, BigQuery,
    // Snowflake) gets the same numbers.
    public class TelemetryDashboard : EditorWindow {
        [MenuItem("EmberKeep/Telemetry Dashboard")]
        public static void Open() {
            var w = GetWindow<TelemetryDashboard>("Telemetry");
            w.minSize = new Vector2(640f, 480f);
            w.Show();
        }

        Vector2 _scroll;
        Aggregate _agg;
        string _logDir;
        string _liveOpsPath;
        string _statusMessage = "Click Reload to scan telemetry logs.";

        void OnEnable() {
            _logDir       = Path.Combine(Application.persistentDataPath, "telemetry");
            _liveOpsPath  = LiveOpsConfig.Path;
        }

        void OnGUI() {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("EmberKeep telemetry dashboard", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Source", _logDir, EditorStyles.miniLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Reload", GUILayout.Height(28f))) {
                    Reload();
                }
                if (GUILayout.Button("Open log folder", GUILayout.Height(28f))) {
                    OpenInExplorer(_logDir);
                }
                if (GUILayout.Button("Open liveops.json", GUILayout.Height(28f))) {
                    EnsureLiveOpsExists();
                    OpenInDefaultEditor(_liveOpsPath);
                }
                if (GUILayout.Button("Reload LiveOps config", GUILayout.Height(28f))) {
                    LiveOpsConfig.Reload();
                    _statusMessage = "LiveOpsConfig reloaded. Current values shown below.";
                    Repaint();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", _statusMessage, EditorStyles.wordWrappedLabel);

            DrawLiveOps();
            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_agg != null) DrawAggregate(_agg);
            else EditorGUILayout.HelpBox("No data loaded yet.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        void DrawLiveOps() {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live Ops config (current)", EditorStyles.boldLabel);
            var c = LiveOpsConfig.Current;
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Path",                          _liveOpsPath, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("safety_filter_enabled",         c.safety_filter_enabled.ToString());
            EditorGUILayout.LabelField("telemetry_enabled",             c.telemetry_enabled.ToString());
            EditorGUILayout.LabelField("memory_enabled",                c.memory_enabled.ToString());
            EditorGUILayout.LabelField("max_response_tokens_override",  Field(c.max_response_tokens_override));
            EditorGUILayout.LabelField("max_story_tokens_override",     Field(c.max_story_tokens_override));
            EditorGUILayout.LabelField("per_frame_budget_ms",           c.per_frame_budget_ms.ToString());
            EditorGUILayout.LabelField("ab_variant",                    c.ab_variant ?? "");
            EditorGUI.indentLevel--;
        }

        static string Field(int v) => v > 0 ? v.ToString() : "(personality default)";

        void DrawAggregate(Aggregate a) {
            EditorGUILayout.LabelField("Headline", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Files scanned",            a.filesScanned.ToString());
            EditorGUILayout.LabelField("Total events",             a.totalEvents.ToString());
            EditorGUILayout.LabelField("Distinct sessions",        a.sessionsSeen.ToString());
            EditorGUILayout.LabelField("Dialogues started",        a.dialoguesStarted.ToString());
            EditorGUILayout.LabelField("LLM turns",                a.llmTurns.ToString());
            EditorGUILayout.LabelField("Avg tokens per turn",      a.llmTurns > 0 ? (a.totalTokens / a.llmTurns).ToString() : "-");
            EditorGUILayout.LabelField("Avg duration per turn",    a.llmTurns > 0 ? $"{a.totalDurationMs / a.llmTurns} ms" : "-");
            EditorGUILayout.LabelField("Avg tokens / sec",         a.llmTurns > 0 ? $"{(a.totalTokens / Math.Max(1.0, a.totalDurationMs / 1000.0)):F2}" : "-");
            EditorGUILayout.LabelField("Safety blocks",            a.safetyBlocks.ToString());
            EditorGUILayout.LabelField("NPCs generated",           a.npcGenerated.ToString());
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Per-NPC turn counts", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (var kv in a.turnsByNpc) {
                EditorGUILayout.LabelField(kv.Key, kv.Value.ToString());
            }
            if (a.turnsByNpc.Count == 0) EditorGUILayout.LabelField("(no LLM turns recorded)");
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Safety blocks by reason", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (var kv in a.blocksByReason) {
                EditorGUILayout.LabelField(kv.Key, kv.Value.ToString());
            }
            if (a.blocksByReason.Count == 0) EditorGUILayout.LabelField("(no blocks)");
            EditorGUI.indentLevel--;
        }

        void Reload() {
            _agg = new Aggregate();
            try {
                if (!Directory.Exists(_logDir)) {
                    _statusMessage = $"No telemetry folder yet at {_logDir}. Run the game and have at least one conversation to populate it.";
                    return;
                }
                var files = Directory.GetFiles(_logDir, "*.jsonl", SearchOption.TopDirectoryOnly);
                _agg.filesScanned = files.Length;
                foreach (var f in files) {
                    foreach (var raw in File.ReadLines(f)) {
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        IngestLine(raw, _agg);
                    }
                }
                _statusMessage = $"Reloaded. {_agg.filesScanned} file(s), {_agg.totalEvents} event(s).";
            } catch (Exception e) {
                _statusMessage = $"Reload failed: {e.Message}";
            }
            Repaint();
        }

        // Lightweight key-extracting parser. We don't need a full JSON parser
        // for these flat one-line events - regex-style key probes are
        // sufficient and avoid pulling in a JSON library.
        static void IngestLine(string raw, Aggregate a) {
            a.totalEvents++;
            string evt    = ExtractString(raw, "event");
            string npcId  = ExtractString(raw, "npc_id");
            string session = ExtractString(raw, "session_id");
            if (!string.IsNullOrEmpty(session)) a.sessionsSet.Add(session);

            switch (evt) {
                case "dialogue_started":
                    a.dialoguesStarted++;
                    break;
                case "llm_turn_completed":
                    a.llmTurns++;
                    a.totalTokens     += ExtractInt(raw, "tokens");
                    a.totalDurationMs += ExtractInt(raw, "duration_ms");
                    if (!string.IsNullOrEmpty(npcId)) {
                        if (!a.turnsByNpc.TryGetValue(npcId, out var n)) n = 0;
                        a.turnsByNpc[npcId] = n + 1;
                    }
                    break;
                case "safety_blocked":
                    a.safetyBlocks++;
                    string reason = ExtractString(raw, "reason");
                    if (!string.IsNullOrEmpty(reason)) {
                        if (!a.blocksByReason.TryGetValue(reason, out var c)) c = 0;
                        a.blocksByReason[reason] = c + 1;
                    }
                    break;
                case "npc_generated":
                    a.npcGenerated++;
                    break;
            }
        }

        static string ExtractString(string raw, string key) {
            string needle = "\"" + key + "\":\"";
            int i = raw.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            int end = raw.IndexOf('"', i);
            if (end < 0) return null;
            return raw.Substring(i, end - i);
        }

        static int ExtractInt(string raw, string key) {
            string needle = "\"" + key + "\":";
            int i = raw.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return 0;
            i += needle.Length;
            int j = i;
            while (j < raw.Length && (char.IsDigit(raw[j]) || raw[j] == '-')) j++;
            if (j == i) return 0;
            int.TryParse(raw.Substring(i, j - i), out var v);
            return v;
        }

        void EnsureLiveOpsExists() {
            if (!File.Exists(_liveOpsPath)) {
                LiveOpsConfig.WriteDefaults(_liveOpsPath, new LiveOpsConfig());
            }
        }

        static void OpenInExplorer(string path) {
            try {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start("explorer.exe", path);
            } catch (Exception e) {
                Debug.LogWarning($"Couldn't open folder: {e.Message}");
            }
        }

        static void OpenInDefaultEditor(string path) {
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName        = path,
                    UseShellExecute = true,
                });
            } catch (Exception e) {
                Debug.LogWarning($"Couldn't open file: {e.Message}");
            }
        }

        class Aggregate {
            public int filesScanned;
            public int totalEvents;
            public int dialoguesStarted;
            public int llmTurns;
            public int safetyBlocks;
            public int npcGenerated;
            public long totalTokens;
            public long totalDurationMs;
            public HashSet<string> sessionsSet = new HashSet<string>();
            public Dictionary<string,int> turnsByNpc = new Dictionary<string,int>();
            public Dictionary<string,int> blocksByReason = new Dictionary<string,int>();

            public int sessionsSeen => sessionsSet.Count;
        }
    }
}
