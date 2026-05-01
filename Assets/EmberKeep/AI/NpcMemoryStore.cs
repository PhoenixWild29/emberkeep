using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EmberKeep.AI {
    // Per-NPC summarised conversation log persisted to disk so the player's
    // history with each character survives between sessions. One JSON file
    // per NPC at <persistentDataPath>/npc_memory/{npcId}.json.
    //
    // Each entry is a 1-2 sentence summary written by the LLM after the
    // player ends a conversation. We keep only the N most recent summaries
    // (default 5) so the system prompt stays bounded.
    public static class NpcMemoryStore {
        public const int MaxSummariesPerNpc = 5;

        [Serializable] public class MemoryEntry {
            public string date;     // ISO-8601 UTC
            public string text;
        }

        [Serializable] public class MemoryFile {
            public string npcId;
            public List<MemoryEntry> entries = new List<MemoryEntry>();
        }

        static string MemoryDir => Path.Combine(Application.persistentDataPath, "npc_memory");

        static string PathFor(string npcId) {
            string safe = SanitiseId(npcId);
            return Path.Combine(MemoryDir, safe + ".json");
        }

        static string SanitiseId(string id) {
            if (string.IsNullOrEmpty(id)) return "unknown";
            var chars = id.ToCharArray();
            for (int i = 0; i < chars.Length; i++) {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') chars[i] = '_';
            }
            return new string(chars);
        }

        public static List<MemoryEntry> Load(string npcId) {
            try {
                string path = PathFor(npcId);
                if (!File.Exists(path)) return new List<MemoryEntry>();
                string json = File.ReadAllText(path);
                var file = JsonUtility.FromJson<MemoryFile>(json);
                return file?.entries ?? new List<MemoryEntry>();
            } catch (Exception e) {
                Debug.LogWarning($"[NpcMemoryStore] failed to load '{npcId}': {e.Message}");
                return new List<MemoryEntry>();
            }
        }

        public static void Append(string npcId, string summary) {
            if (string.IsNullOrWhiteSpace(npcId) || string.IsNullOrWhiteSpace(summary)) return;
            try {
                Directory.CreateDirectory(MemoryDir);
                var entries = Load(npcId);
                entries.Add(new MemoryEntry {
                    date = DateTime.UtcNow.ToString("o"),
                    text = summary.Trim(),
                });
                while (entries.Count > MaxSummariesPerNpc) entries.RemoveAt(0);

                var file = new MemoryFile { npcId = npcId, entries = entries };
                File.WriteAllText(PathFor(npcId), JsonUtility.ToJson(file, prettyPrint: true));
            } catch (Exception e) {
                Debug.LogWarning($"[NpcMemoryStore] failed to append for '{npcId}': {e.Message}");
            }
        }

        // Builds a single-string block to inject into a system prompt,
        // formatted like:
        //   Past visits with this traveller:
        //   - 2026-05-01: Asked about food, was recommended venison stew.
        //   - 2026-05-02: Mentioned heading to Saltgrave Cove next.
        // Returns "" when there are no prior summaries.
        public static string FormatForSystemPrompt(string npcId) {
            var entries = Load(npcId);
            if (entries.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append("\n\nPast visits with this traveller (most recent last):\n");
            foreach (var e in entries) {
                sb.Append("- ");
                sb.Append(ShortDate(e.date));
                sb.Append(": ");
                sb.Append(e.text);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        static string ShortDate(string iso) {
            if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return iso;
        }

        public static void Clear(string npcId) {
            try {
                string path = PathFor(npcId);
                if (File.Exists(path)) File.Delete(path);
            } catch (Exception e) {
                Debug.LogWarning($"[NpcMemoryStore] failed to clear '{npcId}': {e.Message}");
            }
        }
    }
}
