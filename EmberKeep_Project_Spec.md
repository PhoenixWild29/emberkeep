# EmberKeep — Complete Project Specification

> **Purpose of this document:** A single canonical spec for the EmberKeep portfolio project, designed to be fed to LLMs (Cursor, Claude Code, ChatGPT, Copilot Chat, etc.) as project context while you build. It contains the pitch, architecture, scope, build instructions, README template, performance benchmarking guidance, and resume integration — everything in one place.
>
> **How to use it with AI tools:**
> - Drop this file into Cursor as `/docs/SPEC.md` and reference it in prompts: *"Implement the LlmService class as defined in SPEC.md."*
> - Paste the relevant section into ChatGPT/Claude when asking implementation questions.
> - Keep this file open in a side pane while coding so you don't drift from scope.

---

## Table of Contents

1. [Why This Project Exists](#1-why-this-project-exists)
2. [The Job This Is Targeting](#2-the-job-this-is-targeting)
3. [One-Sentence Pitch](#3-one-sentence-pitch)
4. [Scope (MVP)](#4-scope-mvp)
5. [Architecture](#5-architecture)
6. [Required Features](#6-required-features)
7. [Stretch Features](#7-stretch-features)
8. [7-Day Build Schedule](#8-7-day-build-schedule)
9. [Day 1 — Mac Mini Build Instructions](#9-day-1--mac-mini-build-instructions)
10. [README.md Template](#10-readmemd-template-for-the-repo)
11. [Performance Benchmarking — How to Get Real Numbers](#11-performance-benchmarking--how-to-get-real-numbers)
12. [Resume Integration](#12-resume-integration)
13. [Application Strategy](#13-application-strategy)
14. [Honesty Rules](#14-honesty-rules-do-not-violate)
15. [Deliverables Checklist](#15-deliverables-checklist)
16. [Common Pitfalls](#16-common-pitfalls)

---

## 1. Why This Project Exists

EmberKeep is a portfolio project built specifically to bridge the credibility gap for an experienced ML engineer applying to game-industry GenAI roles without prior shipped-game experience. Every design decision in this spec is in service of one of two goals:

1. **Prove engine fluency** — that the candidate can write C# inside Unity, integrate native plugins, and reason about frame budgets and memory footprints.
2. **Prove production-GenAI fluency** — that the candidate understands the shippable patterns (quantization, on-device inference, streaming, BT+LLM hybrid) rather than just calling cloud APIs.

If a feature in this spec doesn't serve one of those two goals, it's been cut.

---

## 2. The Job This Is Targeting

**Senior ML Engineer, GenAI — Games** at Netflix (Req JR39900, posted 2026-04-16, USA Remote).

The JD's hard requirements that this project addresses:

| JD Requirement | How EmberKeep Addresses It |
|---|---|
| Unity / Unreal / proprietary game engines | Built in Unity 6 (C#) |
| C++ and C# proficiency | Native plugin in C++, gameplay code in C# |
| GenAI in production pipelines | Quantized LLM running in-engine |
| Optimize model latency / memory footprints | INT4 quantization, KV-cache, streaming, frame-budget profiling |
| Don't compromise frame rates | Worker-thread isolation, per-frame token budget, target 60 FPS |
| Intelligent NPC behaviors | Behavior-tree + LLM hybrid (the production-correct pattern) |
| Procedural asset generation | Procedural item descriptions and flavor text |
| "AI superpower" pipeline tools | Unity Editor tool: "Generate NPC Personality" |
| Bridge research → production | Optional speculative-decoding implementation |
| Ethical implementation, data privacy | Local-only inference, no cloud, no PII, content filter |

---

## 3. One-Sentence Pitch

> EmberKeep is a Unity 6 tech demo where every NPC in a small tavern runs a quantized 3B-parameter LLM locally on the player's machine, holds persistent memory of past conversations, and stays within a strict per-frame inference budget so the game maintains a steady 60 FPS — built to demonstrate the production-hard parts of shipping GenAI in real games.

This sentence appears verbatim at the top of the README, in the resume bullet, and in cover letter language. Memorize it.

---

## 4. Scope (MVP)

**Setting:** A single small tavern scene. Three NPCs. First-person walk-around. That's it.

**Hard scope rules:**
- No combat. No inventory. No quests beyond Old Finn's storytelling.
- No URP/HDRP — Built-in Render Pipeline only.
- No multiplayer, no save system beyond the per-NPC memory JSON.
- No custom 3D art — use Unity Asset Store free assets or simple primitives.
- One scene file.

The point is the AI tech, not the game. Resist all temptation to expand scope.

### The Three NPCs

| NPC | Technique Demonstrated | Resume Bullet It Earns |
|---|---|---|
| **Bram the Innkeeper** | Pure-LLM dialogue with persistent cross-session memory via prompt-injected summaries | "Persistent NPC memory via summarized history injection" |
| **Mira the Merchant** | Behavior-tree-driven intent (haggle / refuse / accept) + LLM-generated dialogue lines | "BT + LLM hybrid — production-correct game AI pattern" |
| **Old Finn the Storyteller** | On-demand procedural short stories with streaming token rendering | "Procedural narrative generation with perceived-latency masking" |

---

## 5. Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Unity Main Thread                        │
│                                                                  │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────────┐  │
│  │   NPC GO     │    │  Dialogue UI │    │ Behavior Tree    │  │
│  │  (MonoBhvr)  │◄──►│   (UGUI)     │    │  (Mira only)     │  │
│  └──────┬───────┘    └──────────────┘    └────────┬─────────┘  │
│         │                                          │            │
│         │      ┌─────────────────────────┐         │            │
│         └─────►│   LlmService (C#)       │◄────────┘            │
│                │   - Prompt builder      │                       │
│                │   - Token queue poller  │                       │
│                │   - Memory store (JSON) │                       │
│                └────────┬────────────────┘                       │
│                         │ async / await                          │
└─────────────────────────┼────────────────────────────────────────┘
                          │
                          │  Lock-free SPSC queue
                          │  (tokens stream up, prompts stream down)
                          │
┌─────────────────────────┼────────────────────────────────────────┐
│                         ▼            Inference Worker Thread     │
│                ┌──────────────────────────┐                       │
│                │   LlamaCppBridge (C#)    │                       │
│                │   P/Invoke wrapper       │                       │
│                └──────────┬───────────────┘                       │
│                           │ DllImport                             │
│                           ▼                                       │
│                ┌──────────────────────────┐                       │
│                │  emberkeep_native (C++)  │                       │
│                │  - llama.cpp wrapper     │                       │
│                │  - KV-cache mgmt         │                       │
│                │  - Per-NPC sessions      │                       │
│                └──────────┬───────────────┘                       │
│                           │                                       │
│                           ▼                                       │
│                ┌──────────────────────────┐                       │
│                │   Llama-3.2-3B Q4_K_M    │                       │
│                │   (~2.0 GB on disk)      │                       │
│                └──────────────────────────┘                       │
└──────────────────────────────────────────────────────────────────┘
```

### Why this design

- **Main thread never blocks on inference.** Tokens are produced on a worker thread and dequeued at most once per frame, capped at a per-frame budget (default 8ms target). The render loop never starves.
- **Lock-free single-producer / single-consumer queue.** No mutex contention between Unity and the worker thread.
- **Per-NPC KV-cache.** Each NPC has its own cache so context-switching between NPCs doesn't require re-prefilling. Models are shared; only caches are per-NPC.
- **Streaming token rendering.** First token visible to the player in <500ms (target — measure on your hardware). Hides total generation latency.

### Inference Stack

- **llama.cpp** compiled as a Unity native plugin (`.dll` on Windows — MVP target; `.bundle` on macOS, `.so` on Linux as stretch).
- **Model:** Llama-3.2-3B-Instruct, Q4_K_M quantization (~2.0 GB on disk).
- **Source:** [bartowski/Llama-3.2-3B-Instruct-GGUF on Hugging Face](https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF)
- **Backend (MVP):** CPU on Windows x64 via llama.cpp's CPU backend (AVX2 / AVX-512). CUDA and Metal are stretch goals.

### Project Folder Structure

```
emberkeep/
├── Assets/
│   ├── EmberKeep/
│   │   ├── Game/              # Scene, NPC GameObjects, dialogue UI
│   │   ├── AI/                # C# LlmService, prompt builder, memory store
│   │   ├── BehaviorTrees/     # Hand-rolled BT for Mira
│   │   ├── Editor/            # "Generate NPC" inspector tool
│   │   └── ScriptableObjects/ # NPC personality assets
│   └── Plugins/
│       └── x86_64/
│           └── emberkeep_native.dll      # Windows MVP target
│       # (macOS/Linux stretch: Plugins/macOS/*.bundle, Plugins/Linux/*.so)
├── Native/
│   ├── emberkeep_native/      # C++ source for the plugin
│   │   ├── include/
│   │   │   └── emberkeep_native.h
│   │   └── src/
│   │       └── emberkeep_native.cpp
│   ├── llama.cpp/             # git submodule
│   └── CMakeLists.txt
├── Models/
│   └── README.md              # Instructions for downloading the GGUF
├── docs/
│   ├── architecture.png
│   ├── perf/                  # Profiler screenshots
│   └── video-thumbnail.png
└── README.md
```

---

## 6. Required Features

These are non-negotiable for the MVP. Each one corresponds to a resume bullet.

### 6.1 Quantized Inference Plugin
- llama.cpp compiled as a Unity native plugin.
- Clean C# API: `await llm.GenerateAsync(prompt, cancellationToken)`.
- Returns `IAsyncEnumerable<string>` for streaming tokens.

### 6.2 Frame-Budget Enforcement
- Inference runs on a worker thread; main thread polls a token queue at most once per frame.
- Hard cap on per-frame poll work (target 8ms).
- Unity Profiler screenshots showing held frame rate during active generation must be in `docs/perf/`.
- **This is the single most important screenshot in your README.**

### 6.3 BT + LLM Hybrid for Mira
- Hand-rolled behavior tree in ~200 lines of C#. Do not import a library — writing it yourself is the proof.
- BT picks the *intent* (haggle / refuse / accept based on player offer + NPC mood).
- LLM generates the *line* given the chosen intent.
- README contains a diagram explaining this pattern.
- **This is the bullet that proves you understand game AI, not just ML.**

### 6.4 Persistent NPC Memory
- After each conversation, LLM summarizes the exchange in 2-3 sentences.
- Summary is stored in `Application.persistentDataPath/npc_memory/{npcId}.json`.
- On next conversation, summary is injected into the system prompt.
- Demonstrates RAG-style grounding without overengineering with vector stores.

### 6.5 Streaming Token Rendering
- Words appear one at a time in the dialogue UI as they're generated.
- Typewriter effect — coupled to actual token arrival, not faked with a timer.
- Hides perceived latency.
- README mentions: "perceived latency masking, the same technique ChatGPT uses."

### 6.6 Editor Tool: "Generate NPC Personality"
- Unity inspector window accessible via `EmberKeep > Generate NPC`.
- Designer types a one-line concept ("grumpy retired pirate").
- LLM generates a full NPC profile: name, backstory, voice description, three sample lines.
- Output saved as a ScriptableObject in `Assets/EmberKeep/ScriptableObjects/NPCs/`.
- **This is your "AI superpower tool" bullet — the JD literally uses that phrase.**

### 6.7 Safety Layer
- Pre-generation: rule-based filter blocking prompts attempting jailbreaks, NSFW, or role-play escapes.
- Post-generation: keyword filter on output; if triggered, NPC says a graceful refusal line in character.
- README has a "Safety" section explaining the layered approach.
- **This is your "ethical implementation" bullet.**

---

## 7. Stretch Features

Skip these if MVP isn't shipping in 5 days. A shipped MVP beats an unfinished ambitious project every time. (This is itself a Live Ops lesson — say so in the writeup.)

### 7.1 Speculative Decoding
- Two-model setup: Llama-3.2-1B drafts tokens, Llama-3.2-3B verifies.
- Implements [Leviathan et al. 2023](https://arxiv.org/abs/2211.17192).
- Benchmark the speedup vs single-model 3B.
- **This is the "translate research papers to production" bullet.**

### 7.2 Procedural Quest Generation
- Old Finn generates not just a story but a single-objective fetch quest.
- Quest item is a generated ScriptableObject with name, description, and visual stub.

### 7.3 Local TTS
- Pipe NPC output through [Piper](https://github.com/rhasspy/piper) for spoken dialogue.
- Synthesis runs on a separate worker thread; audio plays as it's produced.

### 7.4 Cross-Platform Builds
- MVP targets Windows x64 (CPU) only.
- Stretch: add macOS Apple Silicon (.bundle, Metal backend) and Linux x64 (.so) build targets.

---

## 8. 7-Day Build Schedule

| Day | Goal | Deliverable |
|---|---|---|
| **1** | Unity ↔ C++ bridge proven with stub native plugin | One generated token logged to Unity console |
| **2** | Real llama.cpp loaded; model generates real tokens | Real LLM output in Unity console |
| **3** | Tavern scene + Bram + dialogue UI + conversation loop | Walk up to Bram, have a conversation |
| **4** | Mira + hand-rolled behavior tree + BT→LLM intent wiring | Mira haggles, refuses, accepts based on BT |
| **5** | Old Finn streaming story + persistent memory JSON + safety filter | All three NPCs functional |
| **6** | Editor "Generate NPC" tool + Profiler screenshots + FPS overlay | Demo-ready build |
| **7** | Demo video + README polish + blog post + commit history cleanup | Public repo ready to link from resume |

Days 8-10 are buffer. You will need them. Everyone always needs them.

---

## 9. Day 1 — Windows Build Instructions

**Goal:** By end of day, a single C# script in Unity logs **one generated token** to the console from a (stubbed) native plugin. This is the hardest day because it's all environment setup — once tokens flow into Unity, the rest is application code.

Assumes **Windows 11 x64 + CPU inference** (no NVIDIA GPU). The Mac instructions previously in this section are preserved in the [macOS appendix](#9a-macos-build-instructions-stretch).

### Step 1 — Prerequisites

Required tooling (most likely already on the machine — verify each):

| Tool | Verify with | Install if missing |
|---|---|---|
| Visual Studio 2022 (Community is fine) with **Desktop development with C++** workload | `dir "C:\Program Files\Microsoft Visual Studio\2022"` | [visualstudio.microsoft.com](https://visualstudio.microsoft.com/) — pick the C++ desktop workload; CMake is bundled with this. |
| Git for Windows | `git --version` | [git-scm.com](https://git-scm.com/download/win) |
| Git LFS | `git-lfs --version` | bundled with recent Git for Windows installs |
| GitHub CLI | `gh --version` | [cli.github.com](https://cli.github.com/) |
| Unity Hub + Unity 6 LTS | Unity Hub from Start menu | [unity.com/download](https://unity.com/download) → install Hub, then in Hub install **Unity 6000.0 LTS** with the **Windows Build Support (IL2CPP)** module included by default |

CMake comes bundled with VS at `C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe`. Either run all CMake commands from a **"x64 Native Tools Command Prompt for VS 2022"** (Start menu) so `cmake` and `cl` are on PATH, or pass the absolute path to `cmake.exe`.

**Create the Unity project:** Open Unity Hub → New project → **3D (Built-in Render Pipeline)** template → name `emberkeep` → location `C:\Users\<you>\OneDrive\EmberKeep\` (or wherever this spec lives). Confirm Unity finishes generating the project before moving on.

### Step 2 — Project Skeleton

From a Git Bash shell at the project root (`emberkeep/`):

```bash
# Initialize git (skip if Unity already did)
git init
git lfs install
echo "Models/*.gguf filter=lfs diff=lfs merge=lfs -text" > .gitattributes
curl -o .gitignore https://raw.githubusercontent.com/github/gitignore/main/Unity.gitignore

# Native plugin scaffolding
mkdir -p Native/emberkeep_native/src
mkdir -p Native/emberkeep_native/include
mkdir -p Models
mkdir -p docs/perf

# llama.cpp as a submodule (Day 1 stub doesn't depend on it; wire up Day 2)
cd Native
git submodule add https://github.com/ggerganov/llama.cpp.git llama.cpp
cd llama.cpp
git checkout b3447   # pin to a known-good tag
cd ../..

# Hook up GitHub remote (repo: PhoenixWild29/emberkeep)
git remote add origin https://github.com/PhoenixWild29/emberkeep.git

# First commit — proves the project is real and dated
git add .
git commit -m "Initial scaffold: Unity 6 project + llama.cpp submodule"
git branch -M main
git push -u origin main
```

### Step 3 — Native Plugin Source

**`Native/emberkeep_native/include/emberkeep_native.h`:**

```c
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
```

**`Native/emberkeep_native/src/emberkeep_native.cpp`** (Day 1 stub):

```cpp
#include "emberkeep_native.h"
#include <string>
#include <cstring>

// DAY 1 STUB — replace with real llama.cpp calls on Day 2.
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
```

The `EK_API` macro lives in the header — on Windows it expands to `__declspec(dllexport)` when building the DLL, which is required so MSVC actually exposes the C symbols (otherwise Unity's `DllImport` will throw `EntryPointNotFoundException`).

**`Native/CMakeLists.txt`:**

```cmake
cmake_minimum_required(VERSION 3.20)
project(emberkeep_native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Day 1: stub only. Day 2: uncomment llama.cpp.
# add_subdirectory(llama.cpp)

add_library(emberkeep_native SHARED
    emberkeep_native/src/emberkeep_native.cpp
)
target_include_directories(emberkeep_native PUBLIC emberkeep_native/include)
target_compile_definitions(emberkeep_native PRIVATE EK_BUILDING_DLL)
# target_link_libraries(emberkeep_native PRIVATE llama)   # Day 2

if(WIN32)
    set_target_properties(emberkeep_native PROPERTIES
        PREFIX ""        # no "lib" prefix on Windows
        SUFFIX ".dll"
    )
elseif(APPLE)
    set_target_properties(emberkeep_native PROPERTIES
        BUNDLE TRUE
        BUNDLE_EXTENSION "bundle"
        PREFIX ""
    )
endif()
```

**Build (Windows):**

From a **"x64 Native Tools Command Prompt for VS 2022"** (Start menu → Visual Studio 2022 folder):

```cmd
cd Native
mkdir build
cd build
cmake .. -G "Visual Studio 17 2022" -A x64
cmake --build . --config Release
dir Release\emberkeep_native.dll
```

If you don't have the VS dev prompt handy, you can also run from any shell using the bundled CMake — substitute `"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"` for `cmake`.

### Step 4 — Drop the Plugin into Unity

From the project root (Git Bash):

```bash
mkdir -p Assets/Plugins/x86_64
cp Native/build/Release/emberkeep_native.dll Assets/Plugins/x86_64/
```

In Unity Editor: select `emberkeep_native.dll` in the Project window. In Inspector under **Platform settings**, enable only **Standalone → Windows** with CPU set to **x86_64**. Click Apply.

### Step 5 — C# Bridge

**`Assets/EmberKeep/AI/LlamaCppBridge.cs`:**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EmberKeep.AI {
    public static class LlamaCppBridge {
        const string DLL = "emberkeep_native";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void TokenCallback(IntPtr tokenUtf8, IntPtr userData);

        [DllImport(DLL)] public static extern int ek_init(string modelPath);
        [DllImport(DLL)] public static extern int ek_generate(string prompt, int maxTokens,
                                                              TokenCallback cb, IntPtr userData);
        [DllImport(DLL)] public static extern void ek_shutdown();

        public static string PtrToUtf8(IntPtr ptr) {
            if (ptr == IntPtr.Zero) return string.Empty;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            var bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
```

**`Assets/EmberKeep/AI/Day1Test.cs`:**

```csharp
using UnityEngine;
using EmberKeep.AI;
using System;
using System.Runtime.InteropServices;

public class Day1Test : MonoBehaviour {
    void Start() {
        int rc = LlamaCppBridge.ek_init("");
        Debug.Log($"ek_init returned {rc}");

        LlamaCppBridge.TokenCallback cb = (tokenPtr, userData) => {
            string tok = LlamaCppBridge.PtrToUtf8(tokenPtr);
            Debug.Log($"TOKEN: '{tok}'");
        };
        var handle = GCHandle.Alloc(cb);
        try {
            int n = LlamaCppBridge.ek_generate("Hello", 16, cb, IntPtr.Zero);
            Debug.Log($"Generated {n} tokens");
        } finally {
            handle.Free();
        }
    }

    void OnApplicationQuit() {
        LlamaCppBridge.ek_shutdown();
    }
}
```

Attach `Day1Test.cs` to any GameObject. Press Play. Expected console output:

```
ek_init returned 0
TOKEN: 'Hello'
TOKEN: ', '
TOKEN: 'traveler'
...
Generated 9 tokens
```

If you see those logs, **Day 1 is done.**

### Step 6 — Commit and Push

```bash
git add .
git commit -m "Day 1: native plugin stub + Unity C# bridge — token streaming end-to-end"
gh auth login    # one-time, if not done
git push origin main
```

The repo `PhoenixWild29/emberkeep` was created in advance; we set the remote in Step 2 so this just pushes. Push the README from Section 10 in this same Day 1 commit so the repo is presentable from the moment a recruiter clones it.

### Day 1 Common Gotchas

- **`DllNotFoundException: emberkeep_native`** → DLL isn't in `Assets/Plugins/x86_64/`, or platform settings aren't set, or the DLL has unresolved dependencies. Run `dumpbin /dependents emberkeep_native.dll` from the VS dev prompt to inspect imports — for the Day 1 stub, only `KERNEL32.dll` and `VCRUNTIME140.dll` should show.
- **`EntryPointNotFoundException: ek_init`** → C symbols not exported. Verify the `EK_API` macro and `EK_BUILDING_DLL` define are both present, and check exports with `dumpbin /exports emberkeep_native.dll` — you should see `ek_init`, `ek_generate`, `ek_shutdown` listed.
- **MSVCP / MSVCR runtime errors at Unity load time** → the user running Unity needs the Visual C++ Redistributable installed. Most dev machines already have it via VS itself; if shipping, bundle vc_redist.x64.exe.
- **CMake can't find a compiler** → you're not in the VS Developer Command Prompt. Either launch one from the Start menu or pass `-G "Visual Studio 17 2022" -A x64` to `cmake` so it locates MSVC itself.
- **Unity Editor still loads the old DLL after rebuild** → Unity caches plugin handles. Close and reopen Unity (or stop Play mode → reimport the DLL) after each rebuild.

### Day 2 Preview

Uncomment the `llama.cpp` lines in `CMakeLists.txt`, replace the stub `ek_generate` with real `llama_decode` calls, download the GGUF model into `Models/`, and call `ek_init` with the actual model path. The Unity-side code does not change — that's the point of getting the bridge working first. On Windows + CPU, llama.cpp builds with AVX2/AVX-512 by default; no extra flags needed for MVP.

---

## 9a. macOS Build Instructions (Stretch)

Preserved for the eventual macOS port. Assumes Apple Silicon (M1/M2/M3/M4); for Intel Macs, swap `arm64` for `x86_64`.

```bash
xcode-select --install
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
brew install cmake git git-lfs gh
```

Install Unity 6 with the **Mac Build Support (Apple Silicon)** module. The native plugin builds as a `.bundle` (with `BUNDLE TRUE` set in `CMakeLists.txt`) and goes in `Assets/Plugins/macOS/`. In the Unity Inspector, enable **macOS** with CPU set to **Apple silicon**. CMake config line: `-DCMAKE_OSX_ARCHITECTURES=arm64`.

Backend on Apple Silicon should switch to Metal once the stretch port is undertaken — `cmake -DGGML_METAL=ON` for llama.cpp.

---

## 10. README.md Template (for the repo)

Copy this into the repo's root `README.md` on Day 1. Performance numbers are **intentionally blank** until measured.

````markdown
# EmberKeep

> A Unity 6 tech demo where every NPC runs a quantized 3B-parameter LLM locally on the player's machine, holds persistent memory across sessions, and stays within a strict per-frame inference budget so the game maintains a steady 60 FPS.

**Status:** In active development. See [Roadmap](#roadmap).

[![Demo Video](docs/video-thumbnail.png)](https://youtu.be/YOUR_VIDEO_ID)

**[▶ Watch the 90-second demo](https://youtu.be/YOUR_VIDEO_ID)** · **[📖 Technical writeup](https://medium.com/@you/emberkeep)** · **[🏗 Architecture](#architecture)**

---

## Why I built this

Most "AI in games" demos call out to GPT-4 over the network. That's not shippable: it's expensive at scale, latency is unpredictable, it requires always-online play, and player conversations leave the device. EmberKeep is a working answer to the harder question — **what does it actually take to run a generative LLM *inside* a game engine, on consumer hardware, without dropping a frame?**

The codebase demonstrates the production-hard parts of shipping GenAI in real games: quantized on-device inference, frame-budget enforcement, behavior-tree + LLM hybrid NPCs, persistent character memory, and an editor tool that turns the LLM into an "AI superpower" for the design team.

## What's in the demo

| NPC | Technique Demonstrated |
|---|---|
| **Bram the Innkeeper** | Pure-LLM dialogue with persistent cross-session memory via prompt-injected summaries |
| **Mira the Merchant** | Behavior-tree-driven intent (haggle / refuse / accept) with LLM-generated dialogue lines — the production-correct pattern for shippable NPC AI |
| **Old Finn the Storyteller** | On-demand procedural short stories with streaming token rendering and perceived-latency masking |
| **"Generate NPC" Editor Tool** | Unity inspector window: type a one-line concept, get a full NPC ScriptableObject with backstory, voice profile, and sample lines |

## Architecture

[Insert ASCII diagram from Section 5 of SPEC.md]

**Why this design:** the main thread never blocks on inference. Tokens are produced on a worker thread and dequeued at most once per frame, capped at a per-frame budget. The result is that generation feels real-time to the player, but the render loop never starves.

## Performance

Benchmarks pending — will be measured on Apple M-series and published after MVP completes. See [Roadmap](#roadmap).

| Metric | Value | Hardware |
|---|---|---|
| Model | Llama-3.2-3B-Instruct, Q4_K_M | — |
| On-disk size | ~2.0 GB | — |
| Resident memory | _Pending measurement_ | Apple Silicon |
| Time-to-first-token | _Pending measurement_ | Apple Silicon |
| Tokens / second | _Pending measurement_ | Apple Silicon |
| Frame rate during inference | _Target: steady 60 FPS_ | Apple Silicon |
| Per-frame inference budget | 8ms (target) | — |

Profiler screenshots will be in [`docs/perf/`](docs/perf/) after MVP.

## How it maps to shipping a real game

| Production Concern | How EmberKeep Addresses It |
|---|---|
| Frame-rate stability | Worker-thread inference, per-frame token budget, lock-free queue |
| Memory footprint | Q4_K_M quantization (4-bit), shared model across NPCs, per-NPC KV-cache only |
| Cold-start latency | Model loaded once at scene-load, kept warm via empty-prompt prefill |
| Perceived latency | Streaming token rendering, typewriter UI |
| Character consistency | System-prompt grounding + summarized memory injection |
| Safety / refusal | Pre-generation prompt filter + post-generation content filter |
| Live Ops iteration | NPC personalities are ScriptableObjects — designers edit without code changes |
| No PII leaves device | Fully on-device; no network calls during gameplay |

## Project structure

[Insert folder structure from Section 5 of SPEC.md]

## Building from source

See [`Native/README.md`](Native/README.md) for native plugin build instructions.
See [`Models/README.md`](Models/README.md) for fetching the Llama-3.2-3B Q4_K_M weights from [bartowski/Llama-3.2-3B-Instruct-GGUF](https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF).

## Roadmap

- [x] Day 1 — Native plugin scaffolding + Unity C# bridge (stub)
- [ ] Day 2 — Real llama.cpp integration, first real LLM token in Unity
- [ ] Day 3 — Tavern scene + Bram conversation loop
- [ ] Day 4 — Mira behavior tree + BT→LLM intent wiring
- [ ] Day 5 — Old Finn streaming + persistent memory + safety filter
- [ ] Day 6 — "Generate NPC" editor tool + Profiler captures
- [ ] Day 7 — Demo video + technical blog post
- [ ] Stretch — Speculative decoding (Llama-3.2-1B drafts, 3B verifies)
- [ ] Stretch — Local TTS via Piper
- [ ] Stretch — Windows/Linux native plugin builds

## License

MIT. The Llama-3.2 model weights are subject to [Meta's license](https://www.llama.com/llama3_2/license/).

## About

Built by [Samuel Shamber](https://linkedin.com/in/YOUR_HANDLE) as a tech demonstrator for shipping GenAI features inside production game engines.
````

---

## 11. Performance Benchmarking — How to Get Real Numbers

**Do not put performance numbers in the README until you've measured them on your actual hardware.** The numbers below are sanity-check ranges only — use them to confirm your build is healthy, not to fill in the README.

### Sanity-Check Ranges (publicly documented benchmarks)

For **Llama-3.2-3B-Instruct Q4_K_M** via llama.cpp:

| Hardware | Expected tok/s (eval) | Expected TTFT |
|---|---|---|
| Modern Windows desktop, CPU only (8C/16T, AVX2) | ~6-12 tok/s | ~400-900 ms |
| Modern Windows desktop, CPU only (AVX-512) | ~10-18 tok/s | ~300-700 ms |
| RTX 4060 (Windows, CUDA — stretch goal) | ~80-120 tok/s | ~50-150 ms |
| Apple M1/M2/M3 (Metal — stretch goal) | ~25-75 tok/s | ~80-300 ms |

CPU-only is the MVP target for EmberKeep. Frame-rate stability does **not** depend on tok/s — inference runs on a worker thread with a per-frame dequeue budget, so tok/s only governs perceived dialogue speed. If your numbers land in these ranges, you're healthy. Far below means llama.cpp wasn't built with AVX2/AVX-512 enabled, or thread count is set wrong.

### How llama.cpp Reports Numbers

Once Day 2 is done, every generation prints something like this — copy these into your README's Performance table:

```
llama_print_timings: load time = 412.33 ms
llama_print_timings: sample time = 8.21 ms / 128 runs (0.06 ms per token, 15592 tok/s)
llama_print_timings: prompt eval time = 89.44 ms / 24 tokens (3.73 ms per token, 268 tok/s)
llama_print_timings: eval time = 4127.91 ms / 127 runs (32.50 ms per token, 30.76 tok/s)
llama_print_timings: total time = 4225.56 ms
```

- **Time-to-first-token** = prompt eval time + first sample.
- **Tokens / sec** = the `tok/s` from the `eval time` line.

### Resident Memory

```bash
# While Unity is running with the model loaded:
ps -A -o pid,rss,comm | grep Unity
# RSS is in KB. Divide by 1024 for MB, by 1048576 for GB.
```

### Frame Rate During Inference

- Use Unity's built-in **Profiler** window: Window > Analysis > Profiler.
- Capture during active LLM generation.
- Look for Main Thread `Time ms` — it should stay under 16.67ms for 60 FPS.
- Save profiler screenshots to `docs/perf/`.

### Per-Frame Inference Budget

This is a *target you set in code*, not a measurement. Default 8ms = half of a 60 FPS frame budget. Set as a constant in `LlmService` and verify under Profiler that you stay under it.

---

## 12. Resume Integration

### Selected Projects Entry (use this until you have measurements)

> **EmberKeep — On-Device LLM NPCs in Unity 6** *(in active development)* — [github.com/PhoenixWild29/emberkeep](#)
>
> Building a Unity 6 / C# tech demo that runs a Q4-quantized Llama-3.2-3B locally inside the engine via a custom llama.cpp native plugin. Architecture isolates inference on a worker thread with a per-frame token-dequeue budget so the render loop never starves. NPCs use a behavior-tree + LLM hybrid for intent-aware dialogue, persistent cross-session memory, and streaming token rendering. Includes a Unity Editor tool that turns one-line concepts into full LLM-generated NPC ScriptableObjects.

### Selected Projects Entry (after measurements)

Replace once you have real numbers:

> **EmberKeep — On-Device LLM NPCs in Unity 6** — [github.com/PhoenixWild29/emberkeep](#) | [demo video](#) | [technical writeup](#)
>
> Built a Unity 6 / C# tech demo running a Q4-quantized Llama-3.2-3B locally inside the engine via a custom llama.cpp native plugin. Sustains [INSERT MEASURED FPS] FPS during active inference at [INSERT MEASURED tok/s] tokens/sec on [INSERT YOUR HARDWARE]. NPCs use a behavior-tree + LLM hybrid for intent-aware dialogue, persistent cross-session memory, and streaming token rendering. Includes a Unity Editor tool that turns one-line concepts into full LLM-generated NPC ScriptableObjects.

### Where it goes on the resume

Directly under the Professional Summary, **above** Professional Experience. This is the first concrete proof a recruiter sees and it should not be buried.

---

## 13. Application Strategy

### Two Honest Paths

**Path A — Apply today with WIP repo:**
1. Surgical resume cuts (delete every "transferable to," kill the "Game & Creative AI Transferables" skill row, trim certs to IBM AI Engineering + AWS SAA).
2. Execute Day 1.
3. Push public repo with the full README from Section 10 (Performance section says "Pending measurement").
4. Use the *in-development* Selected Projects bullet from Section 12.
5. Submit application tonight or tomorrow morning.
6. Continue Days 2-7 after applying.
7. After 5-7 days of silence (or after the recruiter screen), email a gentle bump: "wanted to share — the project I linked is now feature-complete, here's the demo video."

**Path B — Apply in 5-7 days with measured demo:**
1. Same surgical resume cuts.
2. Execute Days 1-5 to get a credible MVP.
3. Measure performance, fill in real numbers.
4. Update README and resume bullet with real metrics.
5. Apply with the *measured-results* Selected Projects bullet.

**Both are defensible. Inventing numbers is not.**

### Recommended: Path A

The Netflix req JR39900 is a senior IC role at FAANG — it stays open 6-10 weeks, not days. But the recruiter triage window is roughly the first 2-3 weeks after posting (i.e., right now). Applying with a WIP-but-credible repo today gets you in the triage pile during the high-attention window. The repo and resume update mid-next-week so by the time you hear back, both are stronger.

### If You Have a Netflix Referral

Apply now with the referral. Referrals bypass the ATS keyword filter that's most likely to kill the application on "5+ years games industry." Ship EmberKeep in parallel as a follow-up artifact to the recruiter after the screen.

---

## 14. Honesty Rules (Do Not Violate)

These rules exist because the technical screen at Netflix will catch any fabrication, and being caught means a permanent flag in their ATS shared across all future reqs.

1. **No invented metrics on the resume or in the README.** If a number isn't measured on your hardware, it's not on the resume. Use "in active development" or "pending measurement" — both are credible.
2. **No claims about features that aren't in the public repo.** If you say "BT + LLM hybrid" on the resume, the BT must be in the repo when a recruiter clones it.
3. **Performance claims must be reproducible.** A hiring manager will clone your repo and run it. Their numbers should match yours within ±20%.
4. **The 60 FPS claim has to be earned.** State it as a *target* until you've verified with Profiler captures.
5. **Don't claim shipped-game experience.** You don't have it. The project is your bridge — frame it that way.
6. **Source attribution in the README.** llama.cpp, the GGUF, the Llama license — all credited.

---

## 15. Deliverables Checklist

By the end of the project, you must have:

- [ ] Public GitHub repo at `github.com/PhoenixWild29/emberkeep` with clean commit history (no "wip" or "asdf" messages).
- [ ] README.md following the template in Section 10, with measured performance numbers filled in.
- [ ] A 90-second demo video on YouTube (unlisted is fine) showing: walking up to each NPC, having a conversation, the editor tool generating an NPC, and an FPS counter visible during generation. **The FPS counter visible during inference is the single most persuasive frame in the video.**
- [ ] A 1,500-word technical blog post on Medium or a personal site titled something like *"Shipping a Local LLM Inside Unity at 60 FPS: What I Learned Building EmberKeep."*
- [ ] Profiler screenshots in `docs/perf/`.
- [ ] Architecture diagram as a PNG in `docs/architecture.png`.
- [ ] Resume updated with the Selected Projects entry from Section 12.

---

## 16. Common Pitfalls

- **Scope creep.** You will be tempted to add combat, inventory, multiple scenes, or a save system. Don't. The point is the AI tech.
- **Importing a behavior tree library for Mira.** The hand-rolled BT is the proof. Importing one undermines the bullet.
- **Calling cloud APIs as a "shortcut."** Defeats the entire premise. The project must run fully offline.
- **Skipping the Profiler captures.** Without them, the 60 FPS claim is unverifiable. Capture them on Day 6.
- **Writing the blog post last and rushing it.** The blog post is half the candidacy value. Outline it on Day 1, write a paragraph each day, polish on Day 7.
- **Pushing dirty commit history.** Squash WIP commits before going public. Recruiters do read commit logs.
- **Not pinning the llama.cpp submodule.** llama.cpp's API changes frequently. Pin a tag (e.g., `b3447`) and document it.
- **Building Day 2 native plugin without verifying Day 1 stub works first.** Don't skip Day 1's stub validation — debugging "is it the bridge or is it llama.cpp?" without that baseline is miserable.

---

*End of spec. Treat this document as the single source of truth. If something in the code contradicts this doc, fix the code or update the doc — don't let them drift.*
