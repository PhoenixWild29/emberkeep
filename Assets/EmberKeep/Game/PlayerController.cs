using UnityEngine;
using UnityEngine.InputSystem;

namespace EmberKeep.Game {
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour {
        public float moveSpeed = 4f;
        public float mouseSensitivity = 0.15f;
        public Transform cameraTransform;

        CharacterController _cc;
        float _pitch;
        bool _inputEnabled = true;

        public void SetInputEnabled(bool enabled) {
            _inputEnabled = enabled;
            Cursor.lockState = enabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !enabled;
        }

        void Awake() {
            _cc = GetComponent<CharacterController>();
        }

        void Start() {
            SetInputEnabled(true);
        }

        void Update() {
            if (!_inputEnabled) return;

            var kb = Keyboard.current;
            Vector2 input = Vector2.zero;
            if (kb != null) {
                if (kb.wKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed) input.y -= 1f;
                if (kb.aKey.isPressed) input.x -= 1f;
                if (kb.dKey.isPressed) input.x += 1f;
                input = Vector2.ClampMagnitude(input, 1f);
            }

            Vector3 worldMove = transform.right * input.x + transform.forward * input.y;
            // Gentle gravity push so the controller stays glued to the floor.
            worldMove.y = -2f;
            _cc.Move(worldMove * moveSpeed * Time.deltaTime);

            var mouse = Mouse.current;
            if (mouse != null && cameraTransform != null) {
                Vector2 d = mouse.delta.ReadValue();
                transform.Rotate(0f, d.x * mouseSensitivity, 0f);
                _pitch = Mathf.Clamp(_pitch - d.y * mouseSensitivity, -85f, 85f);
                cameraTransform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }
        }
    }
}
