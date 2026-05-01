using UnityEngine;
using UnityEngine.InputSystem;

namespace EmberKeep.Game {
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour {
        public float moveSpeed = 4f;
        public float mouseSensitivity = 0.15f;
        public Transform cameraTransform;

        [Tooltip("Set to true to log input values once per second to the Console for debugging.")]
        public bool logInput = false;

        [Tooltip("Disable cursor lock - sometimes interferes with keyboard input on Windows. Useful for debugging.")]
        public bool lockCursorOnPlay = true;

        CharacterController _cc;
        InputAction _w, _a, _s, _d, _look;
        float _pitch;
        bool _inputEnabled = true;
        float _lastLogAt;

        public void SetInputEnabled(bool enabled) {
            _inputEnabled = enabled;
            if (lockCursorOnPlay) {
                Cursor.lockState = enabled ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !enabled;
            }
        }

        void Awake() {
            _cc = GetComponent<CharacterController>();

            _w = new InputAction("MoveForward",  InputActionType.Button, "<Keyboard>/w");
            _s = new InputAction("MoveBackward", InputActionType.Button, "<Keyboard>/s");
            _a = new InputAction("MoveLeft",     InputActionType.Button, "<Keyboard>/a");
            _d = new InputAction("MoveRight",    InputActionType.Button, "<Keyboard>/d");

            _look = new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2");
            _look.AddBinding("<Mouse>/delta");
        }

        void OnEnable() {
            _w?.Enable(); _s?.Enable(); _a?.Enable(); _d?.Enable();
            _look?.Enable();
        }

        void OnDisable() {
            _w?.Disable(); _s?.Disable(); _a?.Disable(); _d?.Disable();
            _look?.Disable();
        }

        void Start() {
            SetInputEnabled(true);
        }

        void Update() {
            if (!_inputEnabled) return;

            // Read input via two paths so we can diagnose discrepancies.
            // Path 1: explicit per-key InputAction polling (canonical Input
            // System path).
            Vector2 moveAction = Vector2.zero;
            if (_w.IsPressed()) moveAction.y += 1f;
            if (_s.IsPressed()) moveAction.y -= 1f;
            if (_a.IsPressed()) moveAction.x -= 1f;
            if (_d.IsPressed()) moveAction.x += 1f;

            // Path 2: direct device query as fallback. Keyboard.current is
            // the singleton for the most-recently-used keyboard device.
            var kb = Keyboard.current;
            Vector2 moveDevice = Vector2.zero;
            if (kb != null) {
                if (kb.wKey.isPressed) moveDevice.y += 1f;
                if (kb.sKey.isPressed) moveDevice.y -= 1f;
                if (kb.aKey.isPressed) moveDevice.x -= 1f;
                if (kb.dKey.isPressed) moveDevice.x += 1f;
            }

            // If either path picked up input, take it.
            Vector2 move = moveAction != Vector2.zero ? moveAction : moveDevice;
            move = Vector2.ClampMagnitude(move, 1f);

            Vector2 look = _look.ReadValue<Vector2>();

            if (logInput && Time.unscaledTime - _lastLogAt > 1f) {
                _lastLogAt = Time.unscaledTime;
                bool anyKey   = kb != null && kb.anyKey.isPressed;
                int  kbCount  = InputSystem.devices.Count;
                Debug.Log(
                    $"[Player] kb={(kb != null)} kbCount={kbCount} anyKey={anyKey}  " +
                    $"action={moveAction} device={moveDevice} chosen={move}  " +
                    $"look={look} pos={transform.position}");
            }

            Vector3 worldMove = transform.right * move.x + transform.forward * move.y;
            worldMove.y = -2f;
            _cc.Move(worldMove * moveSpeed * Time.deltaTime);

            if (cameraTransform != null) {
                transform.Rotate(0f, look.x * mouseSensitivity, 0f);
                _pitch = Mathf.Clamp(_pitch - look.y * mouseSensitivity, -85f, 85f);
                cameraTransform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }
        }

        void OnDestroy() {
            _w?.Dispose(); _s?.Dispose(); _a?.Dispose(); _d?.Dispose();
            _look?.Dispose();
        }
    }
}
