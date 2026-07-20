# LexVerse operational telemetry and privacy

The user-facing controls and external text-processing consent model are documented in `PRIVACY_SETTINGS.md`.

LexVerse records local operational metrics to diagnose reliability and performance without collecting screen or translation content. The default implementation never uploads telemetry. New local records can be disabled immediately in Control Center; the consent-aware boundary evaluates the current preference before every event and does not require restart.

## Data boundary

The `OperationalEvent` contract is deliberately strongly typed and cannot carry arbitrary strings, dictionaries, or object payloads. It may contain only:

- event/outcome/failure enums;
- a random correlation ID that lives for one realtime session;
- capture kind and processing mode;
- stage durations, block/cache/frame counts, and performance-budget status;
- module count and process working-set size.

It does not contain OCR source text, translated text, clipboard content, translation prompts, screen pixels, coordinates, window titles, file paths, user/account/device identifiers, provider responses, or exception messages/stacks.

Local crash records keep exception type, HRESULT, inner exception type, and a one-way SHA-256 stack fingerprint. Raw exception messages and stack traces are not persisted because provider errors may contain sensitive request context or local paths.

## Storage and performance

Operational events are queued in a bounded in-memory channel. A background writer flushes batches without blocking the WPF or realtime loop. The local JSONL log is limited to 1 MiB plus three rotated files under `%LOCALAPPDATA%\LexVerse\Logs`.

Realtime frame metrics are sampled at the first frame, every twentieth frame, and whenever a performance budget is exceeded. The session-closing event retains total processed frames and total budget violations, avoiding per-frame disk writes while preserving operational signal.

Current warning budgets:

| Mode | Capture | OCR | Translation | Total |
| --- | ---: | ---: | ---: | ---: |
| Document/default | 120 ms | 900 ms | 1,500 ms | 2,000 ms |
| Subtitle | 100 ms | 650 ms | 1,200 ms | 1,600 ms |
| Game dialogue | 100 ms | 650 ms | 1,200 ms | 1,600 ms |
| Comic/manga | 150 ms | 1,200 ms | 1,800 ms | 2,500 ms |

These thresholds produce warnings and telemetry; they never interrupt translation. CI separately enforces a responsive Control Center within 5 seconds, targets 3 seconds, and limits the initial working set to 250 MiB.

## Future remote telemetry gate

A remote adapter must not be enabled merely by replacing the local sink. Production enablement requires explicit user consent, a documented retention/deletion policy, TLS, regional/privacy review, server-side schema enforcement, rate limits, removal of stable device identifiers, and tests proving content fields cannot enter the payload. Local-only operation must remain available.
