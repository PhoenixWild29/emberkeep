using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EmberKeep.AI {
    // Singleton wrapper around the native plugin that keeps generation off the
    // main thread. GenerateAsync returns an IAsyncEnumerable<string> so callers
    // can `await foreach` to stream tokens straight into UI as they arrive.
    public class LlmService : IDisposable {
        static LlmService _instance;
        public static LlmService Instance => _instance ??= new LlmService();

        bool _initialized;
        readonly object _lifecycleLock = new object();
        Task _activeGeneration;

        public bool IsInitialized => _initialized;

        public Task InitializeAsync(string modelPath) {
            return Task.Run(() => {
                lock (_lifecycleLock) {
                    if (_initialized) return;
                    if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
                        throw new FileNotFoundException("LlmService: model file not found", modelPath);

                    int rc = LlamaCppBridge.ek_init(modelPath);
                    if (rc != 0) throw new InvalidOperationException($"ek_init failed (rc={rc})");
                    _initialized = true;
                }
            });
        }

        public void SetSystem(string text) {
            EnsureInitialized();
            LlamaCppBridge.ek_set_system(text ?? string.Empty);
        }

        // Single-shot generation that concatenates the full reply into a
        // string. Used for background summarisation. Goes through the same
        // native mutex as GenerateAsync so it serialises naturally.
        public async Task<string> SummarizeAsync(
            string systemPrompt, string userPrompt, int maxTokens,
            CancellationToken ct = default) {

            EnsureInitialized();
            if (string.IsNullOrEmpty(userPrompt)) return string.Empty;
            if (maxTokens <= 0)                  return string.Empty;

            SetSystem(systemPrompt ?? string.Empty);

            var sb = new StringBuilder();
            await foreach (var tok in GenerateAsync(userPrompt, maxTokens, ct)
                                          .ConfigureAwait(false)) {
                sb.Append(tok);
            }
            return sb.ToString().Trim();
        }

        // Streams generated tokens. Cancellation interrupts the in-flight
        // native generation at the next sample boundary.
        public async IAsyncEnumerable<string> GenerateAsync(
            string userMessage,
            int maxTokens,
            [EnumeratorCancellation] CancellationToken ct = default) {

            EnsureInitialized();
            if (string.IsNullOrEmpty(userMessage)) yield break;
            if (maxTokens <= 0) yield break;

            var queue = new AsyncTokenQueue();

            using var ctReg = ct.Register(() => {
                try { LlamaCppBridge.ek_interrupt(); } catch { }
            });

            var workerTask = Task.Run(() => {
                LlamaCppBridge.TokenCallback cb = (tokenPtr, _) => {
                    string tok = LlamaCppBridge.PtrToUtf8(tokenPtr);
                    if (tok.Length > 0) queue.Enqueue(tok);
                };
                var handle = GCHandle.Alloc(cb);
                try {
                    int rc = LlamaCppBridge.ek_generate(userMessage, maxTokens, cb, IntPtr.Zero);
                    if (rc < 0) Debug.LogWarning($"[LlmService] ek_generate returned {rc}");
                } catch (Exception e) {
                    Debug.LogException(e);
                } finally {
                    handle.Free();
                    queue.Complete();
                }
            });
            _activeGeneration = workerTask;

            try {
                while (true) {
                    string token;
                    bool got;
                    try {
                        (got, token) = await queue.DequeueAsync(ct).ConfigureAwait(false);
                    } catch (OperationCanceledException) {
                        break;
                    }
                    if (!got) break;
                    yield return token;
                }
            } finally {
                try { LlamaCppBridge.ek_interrupt(); } catch { }
                try { await workerTask.ConfigureAwait(false); } catch { }
                _activeGeneration = null;
            }
        }

        void EnsureInitialized() {
            if (!_initialized)
                throw new InvalidOperationException("LlmService: call InitializeAsync first");
        }

        public void Dispose() {
            lock (_lifecycleLock) {
                if (!_initialized) return;
                try { LlamaCppBridge.ek_interrupt(); } catch { }
                try { _activeGeneration?.Wait(2000); } catch { }
                LlamaCppBridge.ek_shutdown();
                _initialized = false;
            }
        }
    }

    // Single-producer / single-consumer async queue. Producer is the native
    // callback's worker thread; consumer is whatever ran the await foreach
    // (typically Unity's main thread). Uses SemaphoreSlim's async wait so the
    // consumer never blocks a thread while waiting for the next token.
    internal sealed class AsyncTokenQueue {
        readonly ConcurrentQueue<string> _items = new ConcurrentQueue<string>();
        readonly SemaphoreSlim _sem = new SemaphoreSlim(0);
        volatile bool _completed;

        public void Enqueue(string token) {
            if (_completed) return;
            _items.Enqueue(token);
            _sem.Release();
        }

        public void Complete() {
            if (_completed) return;
            _completed = true;
            _sem.Release(); // wake any pending DequeueAsync to observe completion
        }

        // Returns (true, token) or (false, null) on completion.
        public async Task<(bool, string)> DequeueAsync(CancellationToken ct) {
            await _sem.WaitAsync(ct).ConfigureAwait(false);
            if (_items.TryDequeue(out var tok)) return (true, tok);
            // Either we were woken by Complete with nothing pending, or a
            // spurious wake-up; check again - with completion, queue stays empty.
            if (_completed) return (false, null);
            return (false, null);
        }
    }
}
