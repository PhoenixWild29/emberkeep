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

EK_API int ek_init(const char* model_path);

typedef void (*token_callback_t)(const char* token_utf8, void* user_data);
EK_API int ek_generate(const char* prompt, int max_tokens, token_callback_t cb, void* user_data);

EK_API void ek_shutdown(void);

#ifdef __cplusplus
}
#endif
#endif
