using UnityEngine;

namespace EmberKeep.AI {
    [CreateAssetMenu(menuName = "EmberKeep/Merchant Personality", fileName = "MerchantPersonality")]
    public class MerchantPersonality : NpcPersonality {
        [Header("Trinket")]
        public string itemName = "fox-fur cloak";
        [TextArea(2, 4)]
        public string itemDescription =
            "A worn but warm fox-fur cloak. Stitched at the shoulder where a blade caught it.";

        [Header("Pricing")]
        [Tooltip("Sticker price.")]
        public int askingPrice = 15;

        [Tooltip("Below this price she always refuses, regardless of mood.")]
        public int walkAwayBase = 8;

        [Header("Starting mood")]
        [Range(-1f, 1f)]
        public float startingMood = 0f;

        [Header("Dialogue scaffolding")]
        [TextArea(2, 4)]
        [Tooltip("Fixed line shown when the player first opens dialogue. Useful for orienting the player to the price; the LLM takes over for replies to offers.")]
        public string greetingTemplate =
            "{name} looks up from polishing a copper buckle. \"Welcome, traveler. " +
            "I've got a fine {item} on offer - {asking} gold. Make me a number.\"";

        public string FormatGreeting() {
            return greetingTemplate
                .Replace("{name}",   displayName)
                .Replace("{item}",   itemName)
                .Replace("{asking}", askingPrice.ToString());
        }
    }
}
