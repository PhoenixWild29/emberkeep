using EmberKeep.AI;
using UnityEngine;

namespace EmberKeep.Game {
    // Top-right HUD that shows live FPS, frame time, and the LLM busy state.
    // The whole point of the worker-thread inference architecture is that the
    // FPS counter stays steady (~60) while LLM is busy, which is the single
    // most persuasive frame to capture in a demo video.
    public class FpsOverlay : MonoBehaviour {
        [Tooltip("How many frames to average frame-time over.")]
        public int sampleWindow = 60;

        [Tooltip("Per-frame inference-related main-thread work cap, in ms. Documented as 8ms in the spec.")]
        public float perFrameBudgetMs = 8f;

        float[] _samples;
        int     _sampleIndex;
        int     _sampleCount;
        GUIStyle _boxStyle;
        GUIStyle _textStyle;
        Texture2D _bgTex;

        void Awake() {
            _samples = new float[Mathf.Max(8, sampleWindow)];
        }

        void Update() {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            _samples[_sampleIndex] = dt * 1000f;   // ms
            _sampleIndex = (_sampleIndex + 1) % _samples.Length;
            if (_sampleCount < _samples.Length) _sampleCount++;
        }

        void OnGUI() {
            EnsureStyles();
            float avgMs = AverageMs();
            float fps   = avgMs > 0f ? 1000f / avgMs : 0f;
            bool  busy  = LlmService.Instance != null && LlmService.Instance.IsBusy;
            string text =
                $"FPS:  {fps,5:F1}\n" +
                $"frame: {avgMs,4:F1} ms\n" +
                $"budget: {perFrameBudgetMs:F0} ms\n" +
                $"LLM:  {(busy ? "busy" : "idle")}";

            const float w = 170f;
            const float h = 80f;
            const float pad = 10f;
            var rect = new Rect(Screen.width - w - pad, pad, w, h);
            GUI.Box(rect, GUIContent.none, _boxStyle);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 8f, rect.height - 4f),
                      text, _textStyle);
        }

        float AverageMs() {
            if (_sampleCount == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < _sampleCount; i++) sum += _samples[i];
            return sum / _sampleCount;
        }

        void EnsureStyles() {
            if (_textStyle == null) {
                _textStyle = new GUIStyle(GUI.skin.label) {
                    fontSize  = 14,
                    alignment = TextAnchor.UpperLeft,
                    richText  = false,
                    normal    = { textColor = Color.white }
                };
            }
            if (_boxStyle == null) {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
                _bgTex.Apply();
                _boxStyle = new GUIStyle(GUI.skin.box) {
                    normal = { background = _bgTex }
                };
            }
        }

        void OnDestroy() {
            if (_bgTex != null) Destroy(_bgTex);
        }
    }
}
