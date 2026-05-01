using System;
using System.Runtime.InteropServices;
using EmberKeep.AI;
using UnityEngine;

public class Day1Test : MonoBehaviour {
    void Start() {
        int rc = LlamaCppBridge.ek_init("");
        Debug.Log($"[EmberKeep] ek_init returned {rc}");

        LlamaCppBridge.TokenCallback cb = (tokenPtr, userData) => {
            string tok = LlamaCppBridge.PtrToUtf8(tokenPtr);
            Debug.Log($"[EmberKeep] TOKEN: '{tok}'");
        };

        var handle = GCHandle.Alloc(cb);
        try {
            int n = LlamaCppBridge.ek_generate("Hello", 16, cb, IntPtr.Zero);
            Debug.Log($"[EmberKeep] Generated {n} tokens");
        } finally {
            handle.Free();
        }
    }

    void OnApplicationQuit() {
        LlamaCppBridge.ek_shutdown();
    }
}
