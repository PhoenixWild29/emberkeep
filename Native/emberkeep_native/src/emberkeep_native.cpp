#include "emberkeep_native.h"
#include "llama.h"

#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

// Day 3 native side: chat-template-aware single-turn generation with KV reset
// per call and an interrupt flag. Single shared context; per-NPC sessions are
// a later evolution.

namespace {
    llama_model*    g_model   = nullptr;
    llama_context*  g_ctx     = nullptr;
    bool            g_backend_initialized = false;

    std::mutex      g_gen_mutex;       // serialises ek_generate / ek_set_system
    std::atomic<bool> g_interrupt{false};
    std::string     g_system_prompt;

    int pick_threads() {
        unsigned hw = std::thread::hardware_concurrency();
        if (hw == 0) hw = 4;
        int n = (int)(hw / 2);
        if (n < 1) n = 1;
        return n;
    }

    // Llama-3 chat-template formatter via llama.cpp's built-in helper. We pass
    // the literal "llama3" template name rather than reading from the GGUF
    // metadata: keeps behaviour stable across model files.
    bool format_llama3_chat(const std::string& system, const std::string& user,
                            std::string& out) {
        std::vector<llama_chat_message> msgs;
        if (!system.empty()) msgs.push_back({"system", system.c_str()});
        msgs.push_back({"user", user.c_str()});

        std::vector<char> buf(8192);
        int32_t n = llama_chat_apply_template("llama3", msgs.data(), msgs.size(),
                                              /*add_ass*/ true,
                                              buf.data(), (int32_t)buf.size());
        if (n > (int32_t)buf.size()) {
            buf.resize((size_t)n);
            n = llama_chat_apply_template("llama3", msgs.data(), msgs.size(),
                                          true, buf.data(), (int32_t)buf.size());
        }
        if (n < 0) return false;
        out.assign(buf.data(), (size_t)n);
        return true;
    }
}

extern "C" EK_API int ek_init(const char* model_path) {
    if (!model_path || model_path[0] == '\0') {
        std::fprintf(stderr, "[emberkeep_native] ek_init: empty model_path\n");
        return -1;
    }
    if (g_model) return 0;

    if (!g_backend_initialized) {
        llama_backend_init();
        g_backend_initialized = true;
    }

    llama_model_params mparams = llama_model_default_params();
    mparams.n_gpu_layers = 0;

    g_model = llama_model_load_from_file(model_path, mparams);
    if (!g_model) {
        std::fprintf(stderr, "[emberkeep_native] ek_init: load failed for '%s'\n", model_path);
        return -2;
    }

    llama_context_params cparams = llama_context_default_params();
    cparams.n_ctx           = 4096;
    cparams.n_batch         = 512;
    cparams.n_threads       = pick_threads();
    cparams.n_threads_batch = pick_threads();

    g_ctx = llama_init_from_model(g_model, cparams);
    if (!g_ctx) {
        std::fprintf(stderr, "[emberkeep_native] ek_init: context init failed\n");
        llama_model_free(g_model);
        g_model = nullptr;
        return -3;
    }

    std::fprintf(stderr, "[emberkeep_native] ek_init: ready (n_ctx=%u, n_threads=%d)\n",
                 (unsigned)cparams.n_ctx, (int)cparams.n_threads);
    return 0;
}

extern "C" EK_API void ek_set_system(const char* text) {
    std::lock_guard<std::mutex> lk(g_gen_mutex);
    g_system_prompt = (text ? text : "");
}

extern "C" EK_API void ek_interrupt(void) {
    g_interrupt.store(true, std::memory_order_release);
}

extern "C" EK_API int ek_generate(const char* user_message, int max_tokens,
                                  token_callback_t cb, void* user_data) {
    if (!g_model || !g_ctx)          return -1;
    if (!user_message || !cb)        return -1;
    if (max_tokens <= 0)             return 0;

    std::unique_lock<std::mutex> lk(g_gen_mutex, std::try_to_lock);
    if (!lk.owns_lock()) {
        std::fprintf(stderr, "[emberkeep_native] ek_generate: another call in progress\n");
        return -4;
    }
    g_interrupt.store(false, std::memory_order_release);

    std::string prompt;
    if (!format_llama3_chat(g_system_prompt, user_message, prompt)) {
        std::fprintf(stderr, "[emberkeep_native] ek_generate: chat template format failed\n");
        return -2;
    }

    // Reset KV cache so each turn starts fresh. Multi-turn continuation is a
    // later evolution that will use seq-id-scoped clears.
    llama_memory_clear(llama_get_memory(g_ctx), /*data*/ true);

    const llama_vocab* vocab = llama_model_get_vocab(g_model);

    int prompt_len = (int)prompt.size();
    int n_prompt = -llama_tokenize(vocab, prompt.c_str(), prompt_len,
                                   nullptr, 0, /*add_special*/ true, /*parse_special*/ true);
    if (n_prompt <= 0) return -2;
    std::vector<llama_token> tokens((size_t)n_prompt);
    if (llama_tokenize(vocab, prompt.c_str(), prompt_len,
                       tokens.data(), (int32_t)tokens.size(),
                       true, true) < 0) return -2;

    auto sparams = llama_sampler_chain_default_params();
    llama_sampler* smpl = llama_sampler_chain_init(sparams);
    llama_sampler_chain_add(smpl, llama_sampler_init_min_p(0.05f, 1));
    llama_sampler_chain_add(smpl, llama_sampler_init_temp (0.8f));
    llama_sampler_chain_add(smpl, llama_sampler_init_dist (LLAMA_DEFAULT_SEED));

    llama_batch batch = llama_batch_get_one(tokens.data(), (int32_t)tokens.size());
    if (llama_decode(g_ctx, batch) != 0) {
        std::fprintf(stderr, "[emberkeep_native] ek_generate: prefill decode failed\n");
        llama_sampler_free(smpl);
        return -3;
    }

    int generated = 0;
    llama_token new_token = 0;
    for (int i = 0; i < max_tokens; ++i) {
        if (g_interrupt.load(std::memory_order_acquire)) break;

        new_token = llama_sampler_sample(smpl, g_ctx, -1);
        if (llama_vocab_is_eog(vocab, new_token)) break;

        char piece[256];
        int n = llama_token_to_piece(vocab, new_token, piece, (int)sizeof(piece) - 1,
                                     0, /*special*/ false);
        if (n < 0) break;
        piece[n] = '\0';
        cb(piece, user_data);
        ++generated;

        llama_batch step = llama_batch_get_one(&new_token, 1);
        if (llama_decode(g_ctx, step) != 0) break;
    }

    llama_sampler_free(smpl);
    return generated;
}

extern "C" EK_API void ek_shutdown(void) {
    std::lock_guard<std::mutex> lk(g_gen_mutex);
    if (g_ctx)   { llama_free(g_ctx);          g_ctx   = nullptr; }
    if (g_model) { llama_model_free(g_model);  g_model = nullptr; }
    if (g_backend_initialized) {
        llama_backend_free();
        g_backend_initialized = false;
    }
    g_system_prompt.clear();
}
