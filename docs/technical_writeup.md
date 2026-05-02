# Shipping a Local LLM Inside Unity at 60 FPS: What I Learned Building EmberKeep

Most "AI in games" demos right now are a thin Unity wrapper around an HTTP call to GPT-4. They look magical in a thirty-second clip, but I would not put one in a game I had to ship. They cost real money per player per minute, the latency floor is set by whatever the network feels like, the experience falls over the moment a player boards a plane, and every line a player types has been mailed to a third party. None of those properties survive contact with a publishing schedule.

The interesting question is not "can we wire an LLM to a game" — that is a weekend. The interesting question is what it takes to run a generative LLM *inside* a game engine, on consumer hardware, without dropping a frame. EmberKeep is my six-day attempt at a working answer: a Unity 6 / C# tavern scene where every NPC runs a quantized 3B-parameter Llama 3.2 model locally, on CPU, with no network calls. This post is about the production-hard parts — threading, frame budget, the BT+LLM hybrid, persistent memory, safety, and editor tooling. I am not going to talk about prompt engineering or fine-tuning.

## The architecture in one diagram

```
Unity Main Thread (C#)                Worker Thread (Task.Run)              Native Plugin (C++)
  DialogueController  ──SetSystem──▶  LlamaCppBridge  ──ek_set_system──▶   emberkeep_native.dll
        │                              (P/Invoke)                                │
   GenerateAsync                                                                 │
        │                                                                       ▼
        ▼                          token cb (UTF-8 ptr)                     llama.cpp
  IAsyncEnumerable<string> ◀── SPSC queue ◀── enqueue(tok) ◀── ek_generate ◀── Llama 3.2 3B Q4_K_M
   (await foreach)         ConcurrentQueue<string> + SemaphoreSlim                 (n_ctx=4096)
```

Three threads. Unity's main thread owns rendering, input, the BT tick, the dialogue Canvas, and the singleton `LlmService` (`Assets/EmberKeep/AI/LlmService.cs`). When a turn fires, `LlmService.GenerateAsync` schedules `ek_generate` on a `Task.Run` worker; the native plugin produces tokens on its own ggml thread pool and pushes each one through a P/Invoke callback into an SPSC queue (`AsyncTokenQueue` at `LlmService.cs:141`, a `ConcurrentQueue<string>` plus `SemaphoreSlim`). The main thread `await foreach`-es the queue into UI text. KV-cache state is reset per turn with `llama_memory_clear` and the system prompt is re-applied from each NPC's personality plus their rolling memory block — cheap per-NPC scoping without juggling concurrent contexts.

## Frame stability is the headline result

If a generative AI demo drops below 60 FPS while the model is talking, nothing else about it matters. So this was the number I tuned toward.

On an Intel Core Ultra 7 155H with no discrete GPU and the model on CPU, EmberKeep sustains **71 FPS with a 7.57 ms main-thread frame time during active generation at ~9.0 tok/s.** The 16.67 ms 60-FPS budget is comfortably half-full. That headroom is what fell out of one specific tuning call inside the native plugin.

`pick_threads()` in `Native/emberkeep_native/src/emberkeep_native.cpp:25` originally used `std::thread::hardware_concurrency() / 2`, which on a 16-logical-core laptop hands llama.cpp eight inference threads. That looked fine in micro-benchmarks: peak tok/s. In Unity it was a disaster. With eight workers thrashing the L2 and the memory bus, the main thread starved and the FPS overlay collapsed to 46 during generation. The render thread was waiting on the main thread, the main thread was waiting on cache lines, and the model still finished one millisecond faster.

The fix was to deliberately under-subscribe. `pick_threads()` now caps inference at six and leaves headroom for Unity main, render, audio, and the OS. The reason, noted in source: llama.cpp on a Q4-quantised model is memory-bandwidth-bound past four to six threads anyway, so the extra cores were stealing from the engine for diminishing tok/s returns. The tok/s drop is small. The frame-time recovery is enormous: 71 FPS sustained. This trade-off does not show up if you measure inference in isolation — only when the model has to share a machine with a real game loop.

## The BT + LLM hybrid

The merchant is the part I want a games AI engineer to look at. Mira is selling a dagger. The player can type whatever they like — a number, a story, an insult — and Mira responds in character. But the *decision* of whether to accept, refuse, or haggle is not made by the language model. It is made by a hand-rolled behavior tree, about seventy lines of C# in `Assets/EmberKeep/Game/MerchantBrain.cs`, with no library dependencies.

The tree is a `BtSelector` over three branches: accept (offer ≥ asking price), refuse (offer < an effective walk-away floor that depends on Mira's running mood), and haggle (everything else, with a counter-offer computed as the midpoint of the offers, nudged by mood). When the player sends a numeric message, `DialogueController` parses it (`DialogueController.cs:314`), ticks the tree against Mira's blackboard, and gets back an intent. Only *then* does the LLM get involved, with a tightly scoped job: the system prompt is built in `BuildMerchantSystemPrompt` (`DialogueController.cs:343`) with the chosen intent baked in ("You have decided to ACCEPT…", "…to COUNTER-OFFER 23 gold…"), and the model is told to write that line in Mira's voice in one or two sentences.

This is the production answer to the most common failure mode of LLM-driven NPCs: the model deciding the mechanics. A pure-LLM merchant will give away the player's gold for a compliment, refuse a fair offer because the dice landed wrong, and be impossible to balance because every patch to the model invalidates last week's playtest. With BT-decides-mechanics-and-LLM-writes-flavour, designers tune `walkAwayBase`, `startingMood`, and the BT structure exactly like any other game system, and the LLM only contributes the part it is good at — the wording.

## Persistent cross-session memory without a vector store

After every conversation, `DialogueController.EndDialogue` (`DialogueController.cs:135`) fires off a background `SummarizeAsync` on the same `LlmService`: it asks the model to write a one- or two-sentence summary of the turn, then `NpcMemoryStore.Append` (`Assets/EmberKeep/AI/NpcMemoryStore.cs`) writes it to `<persistentDataPath>/npc_memory/{npcId}.json` as the newest entry in a rolling five-entry window. On the next visit, `NpcMemoryStore.FormatForSystemPrompt` builds a "Past visits with this traveller" block appended to that NPC's system prompt before generation.

The result is a small piece of magic. The player tells Bram the innkeeper they are heading to Saltgrave Cove, presses ESC, walks around, comes back five minutes later, and Bram asks: *"you said you were thinkin' of headin' to Saltgrave Cove, aint ya?"* No vector database, no embedding service, no retrieval ranking. Just a per-NPC JSON file with five summarised lines and a string concat. For a tavern, this is the right shape — RAG-style grounding with about thirty lines of file I/O and zero external dependencies. A real RPG with hundreds of NPCs would graduate to embeddings, but I would still start here, because the failure modes you hit at small scale surface design problems early.

## Safety, layered

`SafetyFilter` (`Assets/EmberKeep/AI/SafetyFilter.cs`) runs in two stages. Before the LLM is invoked, `IsPromptBlocked` regex-scans player input against four pattern groups: jailbreak attempts ("ignore previous instructions", "developer mode", "DAN"), role-play escapes ("are you an AI", "what model are you", "break character"), explicit content, and harmful instructions. If any fires, the turn aborts before a single token is generated and the NPC delivers their hand-written `refusalLine` — Bram says something gruff, Mira sharp, Finn the storyteller fey. After generation, `ContainsBlockedContent` keyword-scans the reply as a last-line net.

Doing it in-character is operational, not aesthetic. The filter never reveals itself, so it cannot be probed: a player who notices "ah, the regex caught me" learns how to write around it. From the player's seat, the world's NPCs simply have things they will not discuss, which is how NPCs in any other game work.

## The editor superpower

`EmberKeep > Generate NPC` opens a window (`Assets/EmberKeep/Editor/GenerateNpcWindow.cs`) where a designer types a concept like "old monk who collects ghost stories", picks an NPC type, and clicks Generate. The local LLM streams a JSON ScriptableObject — display name, system prompt, refusal line, tint, and type-specific fields like a storyteller's `storyTopics` — into the preview pane in real time. One more click writes the asset to `Assets/EmberKeep/ScriptableObjects/NPCs/`. An hour of NPC writing compresses into about ten seconds. This is the model as a force-multiplier for the team that *makes* the game, not just for the player who plays it.

## What I cut, what I would build next

I deliberately did not ship speculative decoding (1B drafts, 3B verifies — the obvious next throughput win), local TTS via Piper, or a CUDA backend. Six days, tight scope. Next on the list: bench speculative decoding to see whether 9 tok/s doubles cleanly, a designer-facing dialogue-history viewer for auditing summarised memory, and per-NPC voice samples so Bram and Finn sound different.

Honest disclaimer: 9 tok/s on a 16-core laptop is fast enough for tavern dialogue but not for a 500-NPC city. The path to that scale is GPU offload, smaller drafters, and being thoughtful about which NPCs are *actually* speaking on a given frame. The architecture is designed to grow into that, not to be that.

EmberKeep is a working answer to what production-shippable on-device LLM-driven NPCs look like in a real engine. Source, perf captures, and demo video at **github.com/PhoenixWild29/emberkeep**. If you are hiring for games GenAI, I would love to talk: **sshamber01@gmail.com**.

---

*Llama 3.2 is provided under Meta's Llama 3.2 Community License. Inference is powered by [llama.cpp](https://github.com/ggml-org/llama.cpp), MIT-licensed, statically linked into `emberkeep_native.dll`.*
