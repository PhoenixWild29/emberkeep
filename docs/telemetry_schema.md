# EmberKeep Telemetry Schema

Local-first telemetry pipeline. Events are emitted from the running game and
the editor tools to a JSONL file at
`<persistentDataPath>/telemetry/YYYY-MM-DD.jsonl`. The schema is deliberately
flat and wire-compatible with Mixpanel / Amplitude / GameAnalytics — swapping
the file writer in [`Telemetry.cs`](../Assets/EmberKeep/AI/Telemetry.cs) for an
HttpClient POST is a one-line change.

> A representative log slice ships in the repo at
> [`docs/perf/sample_telemetry.jsonl`](perf/sample_telemetry.jsonl). Read it
> alongside this doc for concrete shapes.

## Envelope

Every event carries the same five envelope fields:

| Field | Type | Description |
|---|---|---|
| `ts` | ISO-8601 string (UTC) | Time the event was emitted on the client |
| `schema` | string | Schema version. Currently `"1"`. Bumped if any field is removed or has its semantics changed |
| `session_id` | hex string (32 chars) | Stable for the lifetime of one process; rotated whenever the game / editor restarts |
| `event` | string | Discriminator for the event-specific payload — see table below |
| `npc_id` | string | Stable per-NPC key (the asset name of the personality SO, e.g. `"Bram"`, `"Mira"`, `"Finn"`). Empty for `npc_generated`. |

## Events

### `dialogue_started`

Fires the moment the player presses E and the dialogue panel opens.

| Field | Type | Description |
|---|---|---|
| `memory_summaries_loaded` | int | How many past-conversation summaries were injected into the system prompt for this turn (0-5) |

### `dialogue_ended`

Fires on ESC or scene exit. The summarisation that follows runs after this
event in a fire-and-forget Task; it does not have a separate event yet.

| Field | Type | Description |
|---|---|---|
| `turn_count` | int | Number of completed user turns during this dialogue |
| `duration_ms` | long | Wall-clock from `dialogue_started` to `dialogue_ended` |

### `llm_turn_completed`

Fires after every LLM reply finishes streaming back to the UI.

| Field | Type | Description |
|---|---|---|
| `tokens` | int | Tokens emitted by the model on this turn |
| `duration_ms` | long | Wall-clock from request start to last token |
| `ttft_ms` | long | Time-to-first-token in milliseconds |
| `tokens_per_s` | float | `tokens / (duration_ms / 1000)` |
| `intent` | string | For merchant turns, the BT-chosen intent (`Accept`, `Refuse`, `Haggle`). Empty for non-merchant turns |
| `topic` | string | For storyteller turns, the topic that drove the system prompt. Empty for non-storyteller turns |

### `safety_blocked`

Fires whenever [`SafetyFilter`](../Assets/EmberKeep/AI/SafetyFilter.cs)
intercepts content at either end of the LLM call.

| Field | Type | Description |
|---|---|---|
| `stage` | string | `"input"` (pre-generation) or `"output"` (post-generation) |
| `reason` | string | One of `JailbreakAttempt`, `RolePlayEscape`, `ExplicitContent`, `HarmfulRequest`, `BlockedKeyword` |
| `excerpt` | string | First 80 chars of the offending text (input scans only). Truncated so we never persist a full jailbreak attempt |

### `npc_generated`

Fires when the editor's [`Generate NPC`](../Assets/EmberKeep/Editor/GenerateNpcWindow.cs)
window saves a new ScriptableObject.

| Field | Type | Description |
|---|---|---|
| `concept` | string | The one-line concept the designer typed |
| `npc_type` | string | `Plain`, `Merchant`, or `Storyteller` |
| `display_name` | string | The LLM-chosen `displayName` field on the new asset |

## What the dashboard does with this

[`EmberKeep > Telemetry Dashboard`](../Assets/EmberKeep/Editor/TelemetryDashboard.cs)
reads every JSONL file in the telemetry folder and aggregates:

- Total dialogues started, total LLM turns, total safety blocks, total NPCs
  generated.
- Average tokens per turn, average duration per turn, average tokens/sec.
- Per-NPC turn counts.
- Safety blocks broken down by reason.
- Distinct sessions seen.

The dashboard's aggregations are computed in C# from the raw events, so a
pipeline that ships these JSONL lines to BigQuery / Snowflake / Mixpanel
gets the same numbers from the same source data.

## Live Ops kill switches

Telemetry emission is gated by [`LiveOpsConfig.telemetry_enabled`](../Assets/EmberKeep/AI/LiveOpsConfig.cs)
(a runtime-mutable flag in `<persistentDataPath>/liveops.json`). Setting that
to `false` and saving stops emission on the next dialogue. Other Live Ops
flags (`safety_filter_enabled`, `memory_enabled`,
`max_response_tokens_override`, `max_story_tokens_override`,
`per_frame_budget_ms`, `ab_variant`) can also be flipped at runtime without
rebuilding the player. See the dashboard for the live values.

## Why this was built local-first

- **Privacy.** No data leaves the device by default. Per Honesty Rule §14 of
  the spec, the project doesn't claim a production telemetry deployment;
  it claims an instrumentation layer that's wire-compatible with one.
- **No external account required.** Anyone cloning the repo can run the demo,
  populate their own logs, and verify the dashboard works without signing
  up for Mixpanel / Amplitude / GameAnalytics.
- **One-line backend swap.** When a real backend is desired, the `Emit()`
  call site in `Telemetry.cs` becomes `await httpClient.PostAsJsonAsync(...)`
  with no schema changes.

## Wiring a backend (sketch)

```csharp
// In Telemetry.cs WriterLoop(), instead of the FileStream block:
using var http = new HttpClient();
while (_pending.TryDequeue(out var line)) {
    var content = new StringContent(line, Encoding.UTF8, "application/json");
    _ = http.PostAsync("https://api.mixpanel.com/track", content); // fire and forget
}
```

In production this would be batched (Mixpanel's `/track-batch` accepts up to
50 events per call), have retry-with-backoff, and read the API key from
`LiveOpsConfig` rather than hard-coding it. None of that is implemented;
this sketch only documents the contract.
