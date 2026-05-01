using EmberKeep.AI;
using UnityEngine;

namespace EmberKeep.Game {
    public class Npc : MonoBehaviour {
        public NpcPersonality personality;
        public float interactRange = 2.5f;

        [Tooltip("Optional - assigned by the build script when the player exists.")]
        public Transform player;

        public string DisplayName =>
            personality != null ? personality.displayName : gameObject.name;

        public bool PlayerInRange =>
            player != null &&
            Vector3.Distance(player.position, transform.position) <= interactRange;
    }
}
