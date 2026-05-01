using System;
using System.IO;
using System.Runtime.InteropServices;
using EmberKeep.AI;
using UnityEngine;

public class Day1Test : MonoBehaviour {
    [Tooltip("Filename inside <project>/Models/, or absolute path to a .gguf file.")]
    public string modelFile = "Llama-3.2-3B-Instruct-Q4_K_M.gguf";

    [Tooltip("Prompt sent to the model when Play starts.")]
    [TextArea(2, 6)]
    public string prompt = "You are an old innkeeper greeting a traveler. Reply in one short sentence: ";

    [Range(8, 256)]
    public int maxTokens = 64;

    void Start() {
        string modelPath = ResolveModelPath(modelFile);
        Debug.Log($"[EmberKeep] resolving model path: {modelPath}");

        if (!File.Exists(modelPath)) {
            Debug.LogError($"[EmberKeep] model not found at '{modelPath}'. " +
                           $"Place the GGUF in <project>/Models/ or set an absolute path.");
            return;
        }

        int rc = LlamaCppBridge.ek_init(modelPath);
        Debug.Log($"[EmberKeep] ek_init returned {rc}");
        if (rc != 0) return;

        LlamaCppBridge.TokenCallback cb = (tokenPtr, userData) => {
            string tok = LlamaCppBridge.PtrToUtf8(tokenPtr);
            Debug.Log($"[EmberKeep] TOKEN: '{tok}'");
        };

        var handle = GCHandle.Alloc(cb);
        try {
            int n = LlamaCppBridge.ek_generate(prompt, maxTokens, cb, IntPtr.Zero);
            Debug.Log($"[EmberKeep] Generated {n} tokens");
        } finally {
            handle.Free();
        }
    }

    static string ResolveModelPath(string fileOrPath) {
        if (Path.IsPathRooted(fileOrPath)) return fileOrPath;
        // Application.dataPath -> <project>/Assets, so .. goes to project root.
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, "Models", fileOrPath);
    }

    void OnApplicationQuit() {
        LlamaCppBridge.ek_shutdown();
    }
}
