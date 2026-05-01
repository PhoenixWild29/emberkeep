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
    }
}
