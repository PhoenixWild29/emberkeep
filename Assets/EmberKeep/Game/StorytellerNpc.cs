using EmberKeep.AI;
using UnityEngine;

namespace EmberKeep.Game {
    // Old Finn the Storyteller. Treats every player message as a story
    // prompt: if the message is vague, Finn picks a random topic; otherwise
    // he uses the traveller's words as the story seed.
    public class StorytellerNpc : Npc {
        public StorytellerPersonality StorytellerPersonality => personality as StorytellerPersonality;

        public string PickStoryTopic(string userMessage) {
            var p = StorytellerPersonality;
            if (p == null || p.storyTopics == null || p.storyTopics.Length == 0)
                return string.IsNullOrWhiteSpace(userMessage) ? "an old adventure" : userMessage.Trim();

            string trimmed = userMessage?.Trim() ?? "";
            if (IsVague(trimmed)) {
                int idx = Random.Range(0, p.storyTopics.Length);
                return p.storyTopics[idx];
            }
            return trimmed;
        }

        // Anything obviously a request-for-anything ("tell me a story", "anything",
        // empty input, etc.) gets a random topic. Specific requests pass through.
        static bool IsVague(string s) {
            if (string.IsNullOrWhiteSpace(s)) return true;
            if (s.Length < 12) return true;
            string lower = s.ToLowerInvariant();
            if (lower.Contains("tell me a story")) return true;
            if (lower.Contains("a story")) return true;
            if (lower == "anything" || lower == "you pick" || lower == "surprise me") return true;
            return false;
        }
    }
}
