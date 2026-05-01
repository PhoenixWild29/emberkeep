using EmberKeep.AI;
using UnityEngine;

namespace EmberKeep.Game {
    // Mira-style NPC. The behaviour-tree logic lives in MerchantBrain so it
    // can be unit-tested without instantiating a GameObject. This component
    // just owns a brain instance and exposes its API to the dialogue layer.
    public class MerchantNpc : Npc {
        public MerchantPersonality MerchantPersonality => personality as MerchantPersonality;

        MerchantBrain _brain;
        public MerchantBrain Brain {
            get {
                if (_brain == null && MerchantPersonality != null) {
                    _brain = new MerchantBrain(MerchantPersonality);
                }
                return _brain;
            }
        }

        public float Mood => Brain != null ? Brain.Mood : 0f;

        public MerchantBrain.Decision EvaluateOffer(int playerOffer) {
            if (Brain == null) {
                Debug.LogError($"[MerchantNpc] '{name}' is missing a MerchantPersonality.");
                return default;
            }
            return Brain.EvaluateOffer(playerOffer);
        }

        public void ResetMood() => Brain?.ResetMood();
    }
}
