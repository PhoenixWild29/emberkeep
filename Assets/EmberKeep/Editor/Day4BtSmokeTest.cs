using EmberKeep.AI;
using EmberKeep.BehaviorTrees;
using EmberKeep.Game;
using UnityEditor;
using UnityEngine;

namespace EmberKeep.EditorTools {
    // One-shot smoke test for Day 4-A. Constructs a transient MerchantBrain
    // (no scene, no LLM, no Play mode) and feeds it a sequence of offers,
    // logging the decision and mood after each. Run with:
    //   EmberKeep > Day 4 BT Smoke Test
    public static class Day4BtSmokeTest {
        [MenuItem("EmberKeep/Day 4 BT Smoke Test")]
        public static void Run() {
            Debug.Log("=== Day 4-A behaviour-tree smoke test ===");

            var p = ScriptableObject.CreateInstance<MerchantPersonality>();
            p.itemName        = "fox-fur cloak";
            p.askingPrice     = 15;
            p.walkAwayBase    = 8;
            p.startingMood    = 0f;

            var brain = new MerchantBrain(p);
            Debug.Log(Header(p));

            Debug.Log("\n-- Scenario 1: cold-open lowballs (mood drifts negative) --");
            Step(brain, 5);    // refuse, mood -0.4
            Step(brain, 5);    // refuse, mood -0.8 -> floor effectively rises
            Step(brain, 8);    // 8 < EffectiveFloor(8 - (-0.8)*2 = 9.6 -> 10), refuse, mood clamps to -1
            Step(brain, 9);    // still refuses (floor ~10)

            brain.ResetMood();
            Debug.Log("\n-- Scenario 2: clean haggle from neutral --");
            Step(brain, 12);   // haggle counter = (12+15)/2 = 13(.5 -> 14)
            Step(brain, 13);   // haggle counter ~14
            Step(brain, 14);   // haggle counter ~14
            Step(brain, 14);   // haggle counter ~14

            brain.ResetMood();
            Debug.Log("\n-- Scenario 3: accept on the asking price --");
            Step(brain, 15);   // accept, mood +0.3

            brain.ResetMood();
            Debug.Log("\n-- Scenario 4: warm mood lowers floor and softens counter --");
            // Pre-warm: simulate a successful sale
            Step(brain, 16);   // accept, mood +0.3
            Step(brain, 16);   // accept, mood +0.6
            Step(brain, 7);    // EffectiveFloor = 8 - 0.6*2 = 6.8 -> 7, so 7 < 7 false -> haggle
            Step(brain, 6);    // 6 < 7 -> refuse

            brain.ResetMood();
            Debug.Log("\n-- Scenario 5: progressive haggle to a deal --");
            Step(brain, 10);   // haggle, counter ~12 or 13
            Step(brain, 13);   // haggle, counter ~14
            Step(brain, 14);   // haggle, counter ~14 or 15
            Step(brain, 15);   // accept

            Debug.Log("\n=== Day 4-A smoke test complete ===");
        }

        static string Header(MerchantPersonality p) {
            return $"item='{p.itemName}'  asking={p.askingPrice}  walkAwayBase={p.walkAwayBase}";
        }

        static void Step(MerchantBrain brain, int offer) {
            var d = brain.EvaluateOffer(offer);
            Debug.Log(
                $"  offer={offer,3}  -> {d.intent,-6}  " +
                FormatTail(d) +
                $"  | floor={d.effectiveFloor,2}  " +
                $"mood {d.moodBefore:+0.00;-0.00; 0.00} -> {d.moodAfter:+0.00;-0.00; 0.00}"
            );
        }

        static string FormatTail(MerchantBrain.Decision d) {
            switch (d.intent) {
                case HaggleIntent.Accept: return $"accepted at {d.acceptedAt}g    ";
                case HaggleIntent.Haggle: return $"counter at {d.counterOffer}g     ";
                case HaggleIntent.Refuse: return "won't sell at that price";
                default:                  return "?                     ";
            }
        }
    }
}
