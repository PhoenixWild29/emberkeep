#include "emberkeep_native.h"
#include <string>
#include <cstring>

// DAY 1 STUB - replace with real llama.cpp calls on Day 2.
// This validates the Unity <-> native bridge before wiring up the LLM.

static bool g_initialized = false;

extern "C" EK_API int ek_init(const char* model_path) {
    g_initialized = true;
    return 0;
}

extern "C" EK_API int ek_generate(const char* prompt, int max_tokens,
                                  token_callback_t cb, void* user_data) {
    if (!g_initialized) return -1;
    const char* fake_tokens[] = {"Hello", ", ", "traveler", ". ",
                                 "Welcome", " to", " the", " tavern", "."};
    int n = sizeof(fake_tokens) / sizeof(fake_tokens[0]);
    int out = (n < max_tokens) ? n : max_tokens;
    for (int i = 0; i < out; i++) {
        cb(fake_tokens[i], user_data);
    }
    return out;
}

extern "C" EK_API void ek_shutdown(void) {
    g_initialized = false;
}
