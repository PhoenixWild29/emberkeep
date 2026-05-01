using UnityEngine;

namespace EmberKeep.AI {
    [CreateAssetMenu(menuName = "EmberKeep/Storyteller Personality", fileName = "StorytellerPersonality")]
    public class StorytellerPersonality : NpcPersonality {
        [Header("Story content")]
        [Tooltip("Topics Finn falls back to when the traveler is vague.")]
        [TextArea(1, 2)]
        public string[] storyTopics = {
            "the time you fought a frost-troll on a high mountain pass in winter",
            "the haunted lighthouse at Saltgrave Cove and what kept its lamp lit",
            "the dragon that guarded a copper mine for fifty years before vanishing",
            "the night the moon went dark over the Ember Valley",
            "the bargain you struck with a hedge-witch in the Greythorn Marsh",
            "the bandit captain who turned out to be a princess in disguise",
        };

        [Header("Generation")]
        [Range(64, 512)]
        [Tooltip("Story length cap. Larger than a normal NPC reply because Finn tells full short stories, not single lines.")]
        public int maxStoryTokens = 256;

        [Header("Dialogue scaffolding")]
        [TextArea(2, 4)]
        public string greetingTemplate =
            "{name} draws a stool nearer the fire and looks you up and down. " +
            "\"You've the look of someone who'd take a tale, traveller. " +
            "Name a topic, or let me pick.\"";

        public string FormatGreeting() {
            return greetingTemplate.Replace("{name}", displayName);
        }
    }
}
