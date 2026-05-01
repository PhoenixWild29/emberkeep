# `emberkeep_native` — Unity native plugin

A C++ shared library that wraps llama.cpp and exposes a small C API consumed by Unity via `DllImport`. On Windows it builds as `emberkeep_native.dll`; the macOS/Linux equivalents (`.bundle`/`.so`) are stretch targets.

## Day 1 status

The plugin is currently a **stub** — `ek_generate` returns hard-coded fake tokens. This validates the Unity ↔ C++ bridge end-to-end before introducing llama.cpp's complexity.

## Build (Windows x64, MVP)

Requires Visual Studio 2022 with the **Desktop development with C++** workload. CMake comes bundled with VS.

From a **"x64 Native Tools Command Prompt for VS 2022"** at the repo root:

```cmd
cd Native
mkdir build
cd build
cmake .. -G "Visual Studio 17 2022" -A x64
cmake --build . --config Release
```

Output DLL: `Native/build/Release/emberkeep_native.dll`.

Copy it into the Unity project:

```cmd
copy /Y build\Release\emberkeep_native.dll ..\Assets\Plugins\x86_64\
```

## Verifying the build (pre-Unity)

From the same dev prompt, confirm exports:

```cmd
dumpbin /exports build\Release\emberkeep_native.dll
```

You should see `ek_init`, `ek_generate`, `ek_shutdown` listed. If they're missing, the `EK_API` macro / `EK_BUILDING_DLL` define is not being applied.

## C API

```c
int  ek_init(const char* model_path);
int  ek_generate(const char* prompt, int max_tokens, token_callback_t cb, void* user_data);
void ek_shutdown(void);
```

Tokens stream out of `ek_generate` via the callback — one UTF-8 string per token, in arrival order. Day 2 wires the callback to llama.cpp's `llama_decode` loop.

## Stretch: macOS

```bash
cd Native && mkdir -p build && cd build
cmake .. -DCMAKE_OSX_ARCHITECTURES=arm64 -DCMAKE_BUILD_TYPE=Release
cmake --build . --config Release
```

Output: `Native/build/emberkeep_native.bundle`. Copy to `Assets/Plugins/macOS/`.
