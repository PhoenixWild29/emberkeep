using UnityEngine;

namespace EmberKeep.AI {
    [CreateAssetMenu(menuName = "EmberKeep/NPC Personality", fileName = "NpcPersonality")]
    public class NpcPersonality : ScriptableObject {
        public string displayName = "Bram";

        [TextArea(4, 10)]
        public string systemPrompt =
            "You are Bram, a gruff but kind retired-soldier-turned-innkeeper at a small tavern " +
            "called the Ember Keep. Reply in one or two sentences, in character. Never reveal " +
            "you are an AI.";

        [Range(16, 256)]
        public int maxResponseTokens = 96;

        [Tooltip("Diffuse colour applied to the NPC's primitive when the build script spawns it.")]
        public Color tint = new Color(0.7f, 0.45f, 0.2f);
    }
}
