# Performance Captures — Instructions

This folder holds the screenshots that back up EmberKeep's performance
claims. Recruiters and hiring managers will look for them. Per
[`EmberKeep_Project_Spec.md` §11](../../EmberKeep_Project_Spec.md), every
number in the README must be measured on the development machine — never
invented.

Capture five artefacts:

| File                       | Captures                                             |
|----------------------------|------------------------------------------------------|
| `hud_during_story.png`     | FPS overlay during a Finn story (LLM: busy)          |
| `hud_idle.png`             | FPS overlay at rest (LLM: idle)                      |
| `profiler_during_story.png`| Unity Profiler graph during a Finn story             |
| `profiler_main_thread.png` | Profiler Hierarchy view of one frame, main-thread breakdown |
| `tok_per_sec.txt`          | Hand-recorded `tokens/sec` from a story streaming run |

---

## 1. HUD captures (1 minute, easy)

1. Open Unity, **Press Play**, click in Game view (cursor locks).
2. Walk to **Finn** (red capsule, west wall), press **E**.
3. Type `tell me a story`, press Enter.
4. **While the story streams**, take a Windows screenshot (`Win+Shift+S`,
   region selector, capture roughly the top-right HUD plus a chunk of the
   dialogue panel showing partial story text). Save as
   `docs/perf/hud_during_story.png`.
5. Wait for the story to finish. The HUD's `LLM` should flip to `idle`.
6. Take another screenshot of just the HUD. Save as
   `docs/perf/hud_idle.png`.

The two screenshots together show: idle FPS (e.g., 340), in-flight FPS
(e.g., 140) — both well above the 60 FPS target, with the 8 ms per-frame
budget visible.

## 2. Profiler captures (5 minutes, fiddlier)

Unity Profiler is the single most credible performance evidence in the
README. It shows that main-thread frames stay under 16.67 ms during
generation.

1. **Stop Play** if you're still running.
2. Top menu: **Window → Analysis → Profiler.** A Profiler panel opens
   (you may want to dock it as a separate floating window for the
   screenshot).
3. In the Profiler toolbar at the top:
   - The **Record** button (red dot) should already be enabled. If not,
     click it.
   - Make sure **CPU Usage** is the active module on the left. The default
     view shows multi-color bars stacked over time.
4. **Press Play** in Unity Editor.
5. Click into the Game view, walk to Finn, press E, type `tell me a story`,
   press Enter.
6. **While the story is streaming**, look at the Profiler. The CPU Usage
   bars should:
   - Stay short (each frame's stack should be a thin slice — meaning short
     frames).
   - Show the worker thread doing significant work (red/orange bars on
     non-main-thread lanes), but the main thread (the top "Main Thread"
     row) should stay green/short.
7. **Take a Windows screenshot of the Profiler window** showing the CPU
   Usage timeline during the story stream. Save as
   `docs/perf/profiler_during_story.png`.
8. Now click on **a single frame** in the Profiler timeline (during the
   story stream) — the bottom panel switches to a Hierarchy view showing
   what that one frame did.
9. In the bottom Hierarchy panel, sort by **Time ms** descending. The top
   entry should be a small number (ideally < 5 ms). Take a screenshot of
   this Hierarchy view. Save as `docs/perf/profiler_main_thread.png`.

## 3. tok/s recording (30 seconds)

The C++ side already times generation. The simplest way to capture
tokens/sec is to:

1. With Unity in Play mode and a fresh story prompt, watch the Console.
2. After the story completes, scroll back to the last
   `[EmberKeep] done. N tokens in M ms (avg X ms/tok)` line if Day1Test
   is what's running, or check the timestamps on Finn's tokens (each one
   logged with `+Xms` if you have Day 1 Test attached).
3. Record `tokens / second = 1000 / avg_ms_per_tok`.
4. Open `docs/perf/tok_per_sec.txt` and record:
   ```
   2026-05-02
   Hardware: <CPU model, RAM>
   Model:    Llama-3.2-3B-Instruct-Q4_K_M
   Threads:  <pick_threads() result, typically 4-6>
   Result:   <X.X> tokens/sec   |   <Y> ms time-to-first-token
   ```

---

## After captures are saved

Tell Claude (or whichever LLM you're working with) the values from the
HUD and the `tok_per_sec.txt`, and the README's **Performance** section
gets populated with real measured numbers. The four screenshots are
linked from the README and from the eventual technical blog post.
