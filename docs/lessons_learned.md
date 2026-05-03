# What shipping a local LLM inside Unity actually taught me

I built [EmberKeep](https://github.com/PhoenixWild29/emberkeep) — a Unity 6 tech demo where every NPC in a small tavern runs a quantized 3B-parameter Llama model locally on the player's machine — over a short window between job applications. The build was fully scoped, fully measured, and fully on-device. No cloud calls during gameplay, no GPU, no engine fakery. What follows is what I'd actually tell another engineer thinking about running generative AI inside a real game engine.

---

## The fastest inference settings are rarely the right inference settings

llama.cpp's defaults pick `hardware_concurrency() / 2` for inference threads. On my 16-logical-core laptop that's 8 threads, which is exactly what you want if your one job is "produce tokens as quickly as possible." Inside Unity, it was a disaster. Eight workers thrashing the L2 starved the main thread; FPS during generation dropped to 46. Cap at 6 threads — leaving room for Unity's main + render + OS — and the engine recovered to 71 FPS while tokens-per-second barely changed (llama.cpp on Q4 is memory-bandwidth-bound past 4-6 threads anyway). The lesson: in a real-time engine, you tune for frame stability, not throughput. The two are not the same number.

## BT + LLM hybrid is the actual production answer

Pure-LLM dialogue has unpredictable latency and unpredictable outputs. Pure behavior-tree dialogue can't write a convincing line. The shippable answer is the hybrid: a deterministic, designer-tunable behavior tree picks the *intent* — accept, refuse, haggle, mood drift — and the LLM writes the *line* given that intent. The BT is ~70 lines of C#, no library. Mira the merchant uses it. The dialogue UI even shows the BT's chosen intent in a bracketed debug tag, which proves the BT is real and not a fallback. This is the pattern I'd reach for in production every time.

## Persistent character memory is RAG without the vector store

After each conversation the LLM summarizes the exchange in one or two sentences. The summary persists to a small JSON file per NPC (rolling 5-entry window). On the next visit, the summaries are prepended to that NPC's system prompt under "Past visits with this traveller." No embeddings, no Pinecone, no chunking. The traveler tells Bram he's heading to Saltgrave Cove, ESCs out, comes back later, and Bram says *"you said you were thinkin' of headin' to Saltgrave Cove, aint ya?"* For character continuity in games, that's the entire problem solved. Vector DBs are a complexity tax most projects do not actually need.

## Telemetry that ships nowhere is still telemetry

I built a local-first event pipeline before I built any backend integration. Background-thread JSONL writer, Mixpanel-compatible schema, designer-facing aggregation dashboard. None of it leaves the device. But the schema is wire-compatible with a real backend — swapping the file writer for an `HttpClient.PostAsync` call is one line. The lesson: do not wait for "real" infrastructure to start measuring. Local-first instrumentation gives you the same insight today, with one less dependency between you and shipping.

## Honesty is faster than spin

Every performance number in my README is one of seven measured values: 71 FPS sustained, 7.57 ms main-thread time, 9.0 tok/s, 5.86 s cold time-to-first-token, 6 inference threads, on a specific named CPU. No invented metrics. Profiler captures and a sample telemetry log are in the repo so anyone can verify. Spec §14 of the project — written before any code — said *"if a number isn't measured on your hardware, it's not on the resume."* That rule made every documentation pass faster than it would have been if I'd had to re-litigate aspirational claims. Tight scope plus honest measurements beats loose scope plus marketing language, every single time.

## Production AI is mostly the boring parts

The interesting part is the model. The hard part is everything around it: which thread, which queue, which lifetime, which on-disk format, which kill switch, which observability hook, which graceful failure. EmberKeep is mostly boring code — P/Invoke marshalling, a SemaphoreSlim, a JSON file watcher, a regex prompt scanner — wrapped around two interesting calls (`llama_decode`, `llama_sampler_sample`). That ratio is the actual lesson. If you're building production GenAI features and you find yourself spending most of your time on prompt engineering, you have the ratio inverted.

---

The repo is at [github.com/PhoenixWild29/emberkeep](https://github.com/PhoenixWild29/emberkeep). The architecture diagram, the measured performance log, and a representative telemetry slice are all under [`docs/`](https://github.com/PhoenixWild29/emberkeep/tree/main/docs). Cloning and running it requires a Llama 3.2 3B GGUF in `Models/` and a Windows x64 box; everything else builds from source.

If you're hiring for production GenAI work in games — frame budgets, NPC dialogue systems, on-device inference — I'm at sshamber01@gmail.com.

— Samuel Shamber
