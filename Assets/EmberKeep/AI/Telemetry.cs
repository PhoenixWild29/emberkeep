using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace EmberKeep.AI {
    // Local-first telemetry. Events are appended as JSONL (one JSON object per
    // line) to <persistentDataPath>/telemetry/YYYY-MM-DD.jsonl. The schema is
    // wire-compatible with what a Mixpanel/Amplitude/GameAnalytics HTTP POST
    // would expect, so the only step needed to ship to a backend is to swap
    // the file writer for an HttpClient call.
    //
    // Writes are batched and serialised on a background thread so the call
    // sites (DialogueController, GenerateNpcWindow, etc.) never touch disk
    // from the main thread.
    public static class Telemetry {
        public const string SchemaVersion = "1";

        static readonly object _lock = new object();
        static readonly Guid _sessionId = Guid.NewGuid();
        static readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();
        static readonly ManualResetEventSlim _wake = new ManualResetEventSlim(false);
        static Thread _writer;
        static volatile bool _stopRequested;
        static string _logPath;

        public static string SessionId => _sessionId.ToString("N");
        public static string LogPath   => _logPath;

        // Lazy init so we don't open a file on disk just because the
        // assembly was loaded by the editor.
        static void EnsureStarted() {
            if (_writer != null) return;
            lock (_lock) {
                if (_writer != null) return;

                string dir = Path.Combine(Application.persistentDataPath, "telemetry");
                Directory.CreateDirectory(dir);
                _logPath = Path.Combine(dir, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl");

                _writer = new Thread(WriterLoop) {
                    IsBackground = true,
                    Name         = "EmberKeep.Telemetry"
                };
                _writer.Start();
                Application.quitting += Flush;
            }
        }

        // Public API ----------------------------------------------------------

        public static void Emit(string eventName, IDictionary<string, object> data = null) {
            if (!LiveOpsConfig.Current.telemetry_enabled) return;
            EnsureStarted();
            try {
                string line = BuildLine(eventName, data);
                _pending.Enqueue(line);
                _wake.Set();
            } catch (Exception e) {
                Debug.LogWarning($"[Telemetry] enqueue failed: {e.Message}");
            }
        }

        // Convenience wrappers used at the call sites. Strongly-typed where
        // it matters; fall back to Emit() for ad-hoc events.

        public static void DialogueStarted(string npcId, int memorySummaryCount) {
            Emit("dialogue_started", new Dictionary<string, object> {
                {"npc_id", npcId},
                {"memory_summaries_loaded", memorySummaryCount},
            });
        }

        public static void DialogueEnded(string npcId, int turnCount, long durationMs) {
            Emit("dialogue_ended", new Dictionary<string, object> {
                {"npc_id", npcId},
                {"turn_count", turnCount},
                {"duration_ms", durationMs},
            });
        }

        public static void LlmTurnCompleted(
            string npcId, int tokens, long durationMs, long ttftMs,
            string intent, string topic) {
            double tps = durationMs > 0 ? tokens / (durationMs / 1000.0) : 0.0;
            Emit("llm_turn_completed", new Dictionary<string, object> {
                {"npc_id",       npcId},
                {"tokens",       tokens},
                {"duration_ms",  durationMs},
                {"ttft_ms",      ttftMs},
                {"tokens_per_s", Math.Round(tps, 2)},
                {"intent",       intent ?? ""},
                {"topic",        topic  ?? ""},
            });
        }

        public static void SafetyBlocked(
            string npcId, string stage, string reason, string excerpt) {
            // Truncate excerpt so we never persist an entire jailbreak attempt
            string safe = string.IsNullOrEmpty(excerpt) ? "" :
                excerpt.Length > 80 ? excerpt.Substring(0, 80) + "..." : excerpt;
            Emit("safety_blocked", new Dictionary<string, object> {
                {"npc_id", npcId},
                {"stage",  stage},   // "input" or "output"
                {"reason", reason},
                {"excerpt", safe},
            });
        }

        public static void NpcGenerated(string concept, string npcType, string displayName) {
            Emit("npc_generated", new Dictionary<string, object> {
                {"concept",      concept},
                {"npc_type",     npcType},
                {"display_name", displayName},
            });
        }

        public static void Flush() {
            // Block briefly so any pending lines reach disk before quit.
            try {
                _wake.Set();
                Thread.Sleep(50);
            } catch { }
        }

        public static void Shutdown() {
            _stopRequested = true;
            _wake.Set();
            try { _writer?.Join(500); } catch { }
            _writer = null;
        }

        // Implementation ------------------------------------------------------

        static string BuildLine(string eventName, IDictionary<string, object> data) {
            var sb = new StringBuilder(256);
            sb.Append('{');
            AppendField(sb, "ts",         DateTime.UtcNow.ToString("o"));     sb.Append(',');
            AppendField(sb, "schema",     SchemaVersion);                     sb.Append(',');
            AppendField(sb, "session_id", SessionId);                         sb.Append(',');
            AppendField(sb, "event",      eventName);
            if (data != null) {
                foreach (var kv in data) {
                    sb.Append(',');
                    AppendKey(sb, kv.Key);
                    sb.Append(':');
                    AppendValue(sb, kv.Value);
                }
            }
            sb.Append('}');
            return sb.ToString();
        }

        static void AppendField(StringBuilder sb, string key, string value) {
            AppendKey(sb, key);
            sb.Append(':');
            AppendString(sb, value);
        }

        static void AppendKey(StringBuilder sb, string key) {
            sb.Append('"').Append(EscapeString(key)).Append('"');
        }

        static void AppendValue(StringBuilder sb, object value) {
            if (value == null) { sb.Append("null"); return; }
            switch (value) {
                case bool b:   sb.Append(b ? "true" : "false"); return;
                case int i:    sb.Append(i); return;
                case long l:   sb.Append(l); return;
                case float f:  sb.Append(f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)); return;
                default:       AppendString(sb, value.ToString()); return;
            }
        }

        static void AppendString(StringBuilder sb, string s) {
            sb.Append('"').Append(EscapeString(s ?? "")).Append('"');
        }

        static string EscapeString(string s) {
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s) {
                switch (c) {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else          sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        static void WriterLoop() {
            while (!_stopRequested) {
                _wake.Wait();
                _wake.Reset();
                try {
                    using (var fs = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read)) {
                        using (var sw = new StreamWriter(fs, new UTF8Encoding(false))) {
                            while (_pending.TryDequeue(out var line)) {
                                sw.WriteLine(line);
                            }
                        }
                    }
                } catch (Exception e) {
                    Debug.LogWarning($"[Telemetry] write failed: {e.Message}");
                    Thread.Sleep(200);
                }
            }
        }
    }
}
