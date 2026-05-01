using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EmberKeep.AI;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Driver for the Day 1 -> Day 3 verification milestones. Latest behavior
// (Day 3): worker-thread streaming generation through LlmService, with a
// per-frame "tick" log so the console proves the main thread stays alive
// during inference.
//
// Two visible signals during a successful Day 3 run:
//   - "[EmberKeep] tick #N" lines from Update() keep ticking throughout
//     generation
//   - "[EmberKeep] +Xms TOKEN: '...'" lines appear progressively, not in
//     one burst at the end
public class Day1Test : MonoBehaviour {
    [Tooltip("Filename inside <project>/Models/, or absolute path to a .gguf file.")]
    public string modelFile = "Llama-3.2-3B-Instruct-Q4_K_M.gguf";

    [TextArea(2, 4)]
    public string systemPrompt =
        "You are Bram, a gruff but kind innkeeper at a small tavern called the Ember Keep. " +
        "Reply in one or two sentences, in character.";

    [TextArea(2, 4)]
    public string userMessage = "Hello, innkeeper. What's good to eat tonight?";

    [Range(16, 256)]
    public int maxTokens = 96;

    [Tooltip("Seconds to wait after init before triggering generation.")]
    public float startDelay = 0.5f;

    int _tick;
    CancellationTokenSource _cts;
    bool _generating;

    async void Start() {
        _cts = new CancellationTokenSource();
        string path = ResolveModelPath(modelFile);
        Debug.Log($"[EmberKeep] resolving model path: {path}");
        if (!File.Exists(path)) {
            Debug.LogError($"[EmberKeep] model not found at '{path}'");
            return;
        }

        Debug.Log("[EmberKeep] InitializeAsync starting on worker thread...");
        var sw = Stopwatch.StartNew();
        try {
            await LlmService.Instance.InitializeAsync(path);
        } catch (Exception e) {
            Debug.LogException(e);
            return;
        }
        Debug.Log($"[EmberKeep] InitializeAsync complete after {sw.ElapsedMilliseconds}ms");

        LlmService.Instance.SetSystem(systemPrompt);

        if (startDelay > 0)
            await Task.Delay(TimeSpan.FromSeconds(startDelay), _cts.Token);

        _generating = true;
        Debug.Log($"[EmberKeep] GenerateAsync starting. user='{userMessage}'");
        var genSw = Stopwatch.StartNew();
        long lastTokenAt = 0;
        int tokenCount = 0;

        try {
            await foreach (var token in LlmService.Instance.GenerateAsync(
                               userMessage, maxTokens, _cts.Token)) {
                long now = genSw.ElapsedMilliseconds;
                long delta = now - lastTokenAt;
                lastTokenAt = now;
                ++tokenCount;
                Debug.Log($"[EmberKeep] +{delta,5}ms TOKEN: '{token}'");
            }
        } catch (OperationCanceledException) {
            Debug.Log("[EmberKeep] generation cancelled");
        } catch (Exception e) {
            Debug.LogException(e);
        }

        Debug.Log($"[EmberKeep] done. {tokenCount} tokens in {genSw.ElapsedMilliseconds}ms " +
                  $"(avg {(tokenCount > 0 ? genSw.ElapsedMilliseconds / tokenCount : 0)}ms/tok)");
        _generating = false;
    }

    void Update() {
        ++_tick;
        // Log roughly twice a second while generating, to prove the main thread
        // is alive during inference. (60 FPS / 30 = 2 Hz.)
        if (_generating && _tick % 30 == 0) {
            Debug.Log($"[EmberKeep] tick #{_tick} (main thread alive)");
        }
    }

    static string ResolveModelPath(string fileOrPath) {
        if (Path.IsPathRooted(fileOrPath)) return fileOrPath;
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, "Models", fileOrPath);
    }

    void OnApplicationQuit() {
        _cts?.Cancel();
        LlmService.Instance.Dispose();
    }
}
