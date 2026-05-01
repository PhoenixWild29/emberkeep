#ifndef EMBERKEEP_NATIVE_H
#define EMBERKEEP_NATIVE_H

#if defined(_WIN32)
  #if defined(EK_BUILDING_DLL)
    #define EK_API __declspec(dllexport)
  #else
    #define EK_API __declspec(dllimport)
  #endif
#else
  #define EK_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Loads the model and creates the inference context. Idempotent: a second call
// while already initialized returns 0 without reloading. Returns 0 on success.
EK_API int ek_init(const char* model_path);

// Sets (or clears, when text is null/empty) the system prompt used by
// subsequent ek_generate calls. Persists until changed.
EK_API void ek_set_system(const char* text);

// Per-token callback for streaming. Called on the same thread that invoked
// ek_generate. The token_utf8 pointer is owned by the callee and is only
// valid for the duration of the call.
typedef void (*token_callback_t)(const char* token_utf8, void* user_data);

// Runs a single chat turn: applies the Llama-3 chat template around the
// (optional) system prompt + the user message, clears the KV cache, prefills,
// then samples up to max_tokens. Stops on EOG / EOT or on interrupt.
// Returns the number of tokens emitted via cb, or a negative value on error.
EK_API int ek_generate(const char* user_message, int max_tokens,
                       token_callback_t cb, void* user_data);

// Asks the in-progress ek_generate (if any) to stop at the next token.
// Safe to call from a different thread than the one running ek_generate.
EK_API void ek_interrupt(void);

// Frees the context, model, and backend.
EK_API void ek_shutdown(void);

#ifdef __cplusplus
}
#endif
#endif
