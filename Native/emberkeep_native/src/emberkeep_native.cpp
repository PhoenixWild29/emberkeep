#include "emberkeep_native.h"
#include "llama.h"
#include "ggml-cpu.h"

#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <thread>

// Day 2: real llama.cpp wiring on a single shared model + context.
// Day 3 evolves this into per-NPC sessions on a worker thread.

namespace {
    llama_model*    g_model   = nullptr;
    llama_context*  g_ctx     = nullptr;
    bool            g_backend_initialized = false;

    int pick_threads() {
        unsigned hw = std::thread::hardware_concurrency();
        if (hw == 0) hw = 4;
        // Use physical cores' worth - oversubscription tanks llama.cpp on CPU.
        // hardware_concurrency reports logical cores; halve as a rough heuristic.
        int n = (int)(hw / 2);
        if (n < 1) n = 1;
        return n;
    }
}

extern "C" EK_API int ek_init(const char* model_path) {
    if (!model_path || model_path[0] == '\0') {
        std::fprintf(stderr, "[emberkeep_native] ek_init: empty model_path\n");
        return -1;
    }
    if (g_model) {
        std::fprintf(stderr, "[emberkeep_native] ek_init: already initialized\n");
        return 0;
    }

    if (!g_backend_initialized) {
        llama_backend_init();
        g_backend_initialized = true;
    }

    llama_model_params mparams = llama_model_default_params();
    mparams.n_gpu_layers = 0;          // CPU only for the EmberKeep MVP

    g_model = llama_model_load_from_file(model_path, mparams);
    if (!g_model) {
        std::fprintf(stderr, "[emberkeep_native] ek_init: llama_model_load_from_file failed for '%s'\n",
                     model_path);
        return -2;
    }

    llama_context_params cparams = llama_context_default_params();
    cparams.n_ctx           = 2048;
    cparams.n_batch         = 512;
    cparams.n_threads       = pick_threads();
    cparams.n_threads_batch = pick_threads();

    g_ctx = llama_init_from_model(g_model, cparams);
    if (!g_ctx) {
        std::fprintf(stderr, "[emberkeep_native] ek_init: llama_init_from_model failed\n");
        llama_model_free(g_model);
        g_model = nullptr;
        return -3;
    }

    std::fprintf(stderr, "[emberkeep_native] ek_init: ready (n_ctx=%u, n_threads=%d)\n",
                 (unsigned)cparams.n_ctx, (int)cparams.n_threads);
    return 0;
}

extern "C" EK_API int ek_generate(const char* prompt, int max_tokens,
                                  token_callback_t cb, void* user_data) {
    if (!g_model || !g_ctx) return -1;
    if (!prompt || !cb)     return -1;
    if (max_tokens <= 0)    return 0;

    const llama_vocab* vocab = llama_model_get_vocab(g_model);

    // Tokenize the prompt. First call returns required size when negative.
    int prompt_len = (int)std::strlen(prompt);
    int n_prompt   = -llama_tokenize(vocab, prompt, prompt_len,
                                     nullptr, 0, /*add_special*/ true, /*parse_special*/ true);
    if (n_prompt <= 0) {
        std::fprintf(stderr, "[emberkeep_native] ek_generate: tokenization size probe failed\n");
        return -2;
    }
    std::vector<llama_token> tokens(n_prompt);
    if (llama_tokenize(vocab, prompt, prompt_len,
                       tokens.data(), (int)tokens.size(),
                       true, true) < 0) {
        std::fprintf(stderr, "[emberkeep_native] ek_generate: tokenization failed\n");
        return -2;
    }

    // Set up a default sampler chain: min_p + temperature + rng-based pick.
    auto sparams = llama_sampler_chain_default_params();
    llama_sampler* smpl = llama_sampler_chain_init(sparams);
    llama_sampler_chain_add(smpl, llama_sampler_init_min_p(0.05f, 1));
    llama_sampler_chain_add(smpl, llama_sampler_init_temp (0.8f));
    llama_sampler_chain_add(smpl, llama_sampler_init_dist (LLAMA_DEFAULT_SEED));

    // Prefill: feed the full prompt as one batch.
    llama_batch batch = llama_batch_get_one(tokens.data(), (int)tokens.size());
    if (llama_decode(g_ctx, batch) != 0) {
        std::fprintf(stderr, "[emberkeep_native] ek_generate: prefill llama_decode failed\n");
        llama_sampler_free(smpl);
        return -3;
    }

    int generated = 0;
    llama_token new_token = 0;
    for (int i = 0; i < max_tokens; ++i) {
        new_token = llama_sampler_sample(smpl, g_ctx, -1);

        if (llama_vocab_is_eog(vocab, new_token)) break;

        char piece[256];
        int n = llama_token_to_piece(vocab, new_token, piece, (int)sizeof(piece) - 1,
                                     /*lstrip*/ 0, /*special*/ false);
        if (n < 0) {
            std::fprintf(stderr, "[emberkeep_native] ek_generate: token_to_piece failed\n");
            break;
        }
        piece[n] = '\0';
        cb(piece, user_data);
        ++generated;

        // Decode the new token to advance the KV cache.
        llama_batch step = llama_batch_get_one(&new_token, 1);
        if (llama_decode(g_ctx, step) != 0) {
            std::fprintf(stderr, "[emberkeep_native] ek_generate: step llama_decode failed\n");
            break;
        }
    }

    llama_sampler_free(smpl);
    return generated;
}

extern "C" EK_API void ek_shutdown(void) {
    if (g_ctx)   { llama_free(g_ctx);          g_ctx   = nullptr; }
    if (g_model) { llama_model_free(g_model);  g_model = nullptr; }
    if (g_backend_initialized) {
        llama_backend_free();
        g_backend_initialized = false;
    }
}
