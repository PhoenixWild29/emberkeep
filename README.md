# EmberKeep

> A Unity 6 tech demo where every NPC runs a quantized 3B-parameter LLM locally on the player's machine, holds persistent memory across sessions, and stays within a strict per-frame inference budget so the game maintains a steady 60 FPS.

**Status:** In active development — Day 1 of a 7-day MVP. See [Roadmap](#roadmap).

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

```
+-----------------------------------------------------------+
|                     Unity Main Thread                     |
|                                                           |
|   NPC GameObject  <-->  Dialogue UI    Behavior Tree      |
|         |                                  |              |
|         +---->   LlmService (C#)     <-----+              |
|                  - Prompt builder                         |
|                  - Token queue poller                     |
|                  - Memory store (JSON)                    |
+-----------------------|-----------------------------------+
                        |  Lock-free SPSC queue
                        |  (tokens up, prompts down)
+-----------------------|-----------------------------------+
|                       v       Inference Worker Thread     |
|         LlamaCppBridge (C#)  -->  emberkeep_native.dll    |
|                                   - llama.cpp wrapper     |
|                                   - KV-cache mgmt         |
|                                   - Per-NPC sessions      |
|                                          |                |
|                                          v                |
|                              Llama-3.2-3B Q4_K_M (~2.0GB) |
+-----------------------------------------------------------+
```

**Why this design:** the main thread never blocks on inference. Tokens are produced on a worker thread and dequeued at most once per frame, capped at a per-frame budget (target 8ms = half of a 60 FPS frame). The result is that generation feels real-time to the player, but the render loop never starves.

## Performance

Benchmarks pending — will be measured on the development machine and published after MVP completes. See [Roadmap](#roadmap).

| Metric | Value | Hardware |
|---|---|---|
| Model | Llama-3.2-3B-Instruct, Q4_K_M | — |
| On-disk size | ~2.0 GB | — |
| Resident memory | _Pending measurement_ | Windows x64 |
| Time-to-first-token | _Pending measurement_ | Windows x64 (CPU) |
| Tokens / second | _Pending measurement_ | Windows x64 (CPU) |
| Frame rate during inference | _Target: steady 60 FPS_ | Windows x64 |
| Per-frame inference budget | 8ms (target) | — |

Profiler screenshots will land in [`docs/perf/`](docs/perf/) once captured on Day 6.

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

```
emberkeep/
├── Assets/                       # Unity project root
│   ├── EmberKeep/
│   │   ├── Game/                 # Scene, NPC GameObjects, dialogue UI
│   │   ├── AI/                   # C# LlmService, prompt builder, memory store
│   │   ├── BehaviorTrees/        # Hand-rolled BT for Mira
│   │   ├── Editor/               # "Generate NPC" inspector tool
│   │   └── ScriptableObjects/    # NPC personality assets
│   └── Plugins/
│       └── x86_64/
│           └── emberkeep_native.dll
├── Native/
│   ├── emberkeep_native/         # C++ source for the plugin
│   ├── llama.cpp/                # git submodule
│   └── CMakeLists.txt
├── Models/
│   └── README.md                 # Instructions for downloading the GGUF
├── docs/
│   ├── architecture.png
│   └── perf/                     # Profiler screenshots
└── README.md
```

## Building from source

See [`Native/README.md`](Native/README.md) for native plugin build instructions.
See [`Models/README.md`](Models/README.md) for fetching the Llama-3.2-3B Q4_K_M weights.

The full build spec — scope rules, architecture rationale, day-by-day schedule, honesty rules — is in [`EmberKeep_Project_Spec.md`](EmberKeep_Project_Spec.md).

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
- [ ] Stretch — macOS / Linux native plugin builds

## License

MIT. The Llama-3.2 model weights are subject to [Meta's license](https://www.llama.com/llama3_2/license/).

## About

Built by [Samuel Shamber](https://github.com/PhoenixWild29) as a tech demonstrator for shipping GenAI features inside production game engines.
