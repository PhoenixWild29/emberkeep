using EmberKeep.AI;
using EmberKeep.BehaviorTrees;
using UnityEngine;

namespace EmberKeep.Game {
    // Plain (non-MonoBehaviour) class that owns Mira's behaviour tree and
    // mood. Lives outside MonoBehaviour so unit tests / editor smoke tests
    // can construct it directly without touching the scene.
    public class MerchantBrain {
        readonly MerchantPersonality _personality;
        readonly BtNode _root;
        readonly Blackboard _bb;
        float _mood;

        public float Mood => _mood;
        public MerchantPersonality Personality => _personality;

        public MerchantBrain(MerchantPersonality personality) {
            _personality = personality;
            _mood = personality != null ? personality.startingMood : 0f;
            _bb = new Blackboard();
            _root = BuildTree();
        }

        // Mira's selling tree:
        //   Selector
        //     Accept-branch:  Sequence (offer >= asking) -> set intent Accept
        //     Refuse-branch:  Sequence (offer <  effective floor) -> set intent Refuse
        //     Haggle-branch:  Action -> set intent Haggle, compute counter-offer
        static BtNode BuildTree() {
            var accept = new BtSequence(
                new BtCondition(bb => bb.playerOffer >= bb.askingPrice),
                new BtAction(bb => {
                    bb.intent     = HaggleIntent.Accept;
                    bb.acceptedAt = bb.playerOffer;
                })
            );

            var refuse = new BtSequence(
                new BtCondition(bb => bb.playerOffer < EffectiveFloor(bb)),
                new BtAction(bb => {
                    bb.intent       = HaggleIntent.Refuse;
                    bb.counterOffer = 0;
                })
            );

            var haggle = new BtAction(bb => {
                int midpoint = (bb.playerOffer + bb.askingPrice) / 2;
                int concession = Mathf.RoundToInt(bb.mood * 1f);
                int counter = Mathf.Clamp(midpoint - concession,
                                          bb.playerOffer + 1,
                                          bb.askingPrice);
                bb.intent       = HaggleIntent.Haggle;
                bb.counterOffer = counter;
                return NodeStatus.Success;
            });

            return new BtSelector(accept, refuse, haggle);
        }

        // Friendlier mood lowers the walk-away floor so she'll listen to
        // smaller offers. Sour mood pushes the floor up.
        static int EffectiveFloor(Blackboard bb) {
            float adjusted = bb.walkAwayBase - (bb.mood * 2f);
            return Mathf.RoundToInt(adjusted);
        }

        public struct Decision {
            public HaggleIntent intent;
            public int counterOffer;
            public int acceptedAt;
            public float moodBefore;
            public float moodAfter;
            public int effectiveFloor;
        }

        public Decision EvaluateOffer(int playerOffer) {
            if (_root == null || _personality == null) {
                return new Decision { intent = HaggleIntent.None };
            }

            _bb.askingPrice  = _personality.askingPrice;
            _bb.walkAwayBase = _personality.walkAwayBase;
            _bb.playerOffer  = playerOffer;
            _bb.mood         = _mood;
            _bb.intent       = HaggleIntent.None;
            _bb.counterOffer = 0;
            _bb.acceptedAt   = 0;

            float before = _mood;
            int floor = EffectiveFloor(_bb);
            _root.Tick(_bb);

            float after = ApplyMoodDelta(_bb);
            _mood = after;

            return new Decision {
                intent         = _bb.intent,
                counterOffer   = _bb.counterOffer,
                acceptedAt     = _bb.acceptedAt,
                moodBefore     = before,
                moodAfter      = after,
                effectiveFloor = floor,
            };
        }

        // Tuned so a single deal swings mood by ~30% and good-faith haggling
        // barely nudges it.
        float ApplyMoodDelta(Blackboard bb) {
            float delta = 0f;
            switch (bb.intent) {
                case HaggleIntent.Accept:
                    delta = +0.3f;
                    break;
                case HaggleIntent.Refuse:
                    delta = -0.4f;
                    break;
                case HaggleIntent.Haggle:
                    int midpoint = (bb.playerOffer + bb.askingPrice) / 2;
                    delta = bb.playerOffer >= midpoint ? +0.05f : -0.05f;
                    break;
            }
            return Mathf.Clamp(_mood + delta, -1f, 1f);
        }

        public void ResetMood() {
            _mood = _personality != null ? _personality.startingMood : 0f;
        }
    }
}
