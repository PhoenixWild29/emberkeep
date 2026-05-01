namespace EmberKeep.BehaviorTrees {
    // Shared state passed through every BT tick. Specific to Mira's haggling
    // tree for now; if we add more tree-driven NPCs later we can split this
    // into a base class + per-NPC subclass.
    public enum HaggleIntent { None, Accept, Refuse, Haggle }

    public sealed class Blackboard {
        // --- inputs (set before Tick) ---
        public int   askingPrice;        // sticker price for the item
        public int   walkAwayBase;       // base floor below which she always refuses
        public int   playerOffer;        // gold the player just offered
        public float mood = 0f;          // [-1, 1] running affinity for the player

        // --- outputs (read after Tick) ---
        public HaggleIntent intent;      // what the BT decided
        public int   counterOffer;       // valid only when intent == Haggle
        public int   acceptedAt;         // valid only when intent == Accept
    }
}
