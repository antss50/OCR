# LexVerse Project State

This is the live handoff file for future Codex prompts. Read this file before changing code, and update it after every meaningful code, architecture, git, or verification change.

## Product Goal

LexVerse is a real-time screen translation app. It should translate visible text quickly and accurately, then render translated text in overlay boxes at the correct screen positions without making the user experience feel noisy or uncomfortable.

Long-term modes include:

- translate a specific app/window
- translate a selected screen region
- translate the full screen
- document mode
- game dialogue mode
- movie/subtitle mode
- manga/comic mode
- story/novel reading mode

Each mode should be allowed to use different OCR timing, region selection, change detection, batching, caching, translation prompt/algorithm, and overlay behavior.

## MVP Scope

Current MVP priority: document mode.

Document mode should prioritize:

- accurate OCR grouping and reading order
- stable region/box mapping
- translation quality over ultra-low latency
- cache reuse when zooming, panning, or revisiting the same text
- architecture that can later add game/movie/full-screen modes without rewriting the pipeline

## Current Architecture Snapshot

Solution file: `LexVerse.slnx`.

Important modules:

- `src/LexVerse.Core`: shared contracts, geometry, OCR models, translation models/cache, pipeline logic.
- `src/LexVerse.Infrastructure`: Windows capture, Win32 window tracking, config, Google Cloud translation provider.
- `src/LexVerse.OCR`: Windows OCR service and OCR debug result.
- `src/LexVerse.Overlay`: WPF overlay rendering model/window.
- `samples/LexVerse.Pipeline.Sample`: runnable pipeline sample for manual/visual testing.
- `samples/LexVerse.Overlay.Sample`: overlay sample.
- `samples/LexVerse.Demo.Wpf`: WPF OCR/demo support.
- `tests/LexVerse.Translation.Tests`: current test project, including coordinate mapper and Google translator tests.

Current pipeline direction:

```text
capture
-> select OCR region
-> decide whether OCR should run
-> OCR
-> normalize text
-> cache lookup
-> translate cache misses
-> map frame coordinates to screen coordinates
-> emit overlay frame
```

Key files already present:

- `src/LexVerse.Core/Pipeline/RealtimeTranslationPipeline.cs`
- `src/LexVerse.Core/Pipeline/ScreenOcrPipeline.cs`
- `src/LexVerse.Core/Pipeline/IOcrRegionProvider.cs`
- `src/LexVerse.Core/Pipeline/IOcrTriggerPolicy.cs`
- `src/LexVerse.Core/Pipeline/OcrProcessingMode.cs`
- `src/LexVerse.Core/Pipeline/RealtimeTranslationOptions.cs`
- `src/LexVerse.Core/Geometry/CoordinateMapper.cs`
- `src/LexVerse.Core/Translation/InMemoryTranslationCache.cs`
- `src/LexVerse.Core/Translation/TranslationCacheKey.cs`
- `src/LexVerse.Core/Ocr/OcrTextBlockLayoutGrouper.cs`
- `src/LexVerse.Overlay/PhysicalPixelToDipConverter.cs`

## Engineering Rules

- Keep UI thin. UI collects mode/source/target/region/start-stop choices; pipeline/service code owns capture, OCR scheduling, translation, and overlay frame generation.
- Do not hard-code document-only behavior into shared contracts.
- Prefer small, mode-specific policies behind interfaces over large conditionals in UI code.
- Optimize by avoiding unnecessary OCR and translation calls. Prefer region-based OCR and normalized translation cache keys.
- Preserve coordinate-space clarity. Be explicit about frame pixels, physical screen pixels, DIPs, source rectangles, and DPI.
- For overlay changes, verify visually when possible because automated tests cannot fully catch user discomfort, flicker, or wrong placement.
- For shared geometry, cache, OCR grouping, and pipeline changes, add or update focused tests.

## Git / Collaboration State

As of this update:

- Current branch: `codex/overlay-ocr-block-contrast`.
- Branch base: up-to-date `feature/pipeline-integration` at `2d534a3`.
- `codex/overlay-ocr-block-contrast` has no upstream yet. Push it when the overlay contrast change is ready to share.
- `feature/pipeline-integration` tracks `origin/feature/pipeline-integration`.
- `pipeline.docx` was not present in the latest status check.
- `.gitignore` already ignores common build outputs, Visual Studio state, `.env`, local config, credentials, generated files, and artifacts.

Before changing code in future prompts:

```powershell
git status --short --branch
git branch -vv
git remote -v
```

If the branch has a valid upstream and the worktree is safe, pull before coding. If local changes, untracked files, or conflicts appear, inspect and preserve teammate/user work.

## Verification Guidance

Use the smallest meaningful verification:

- Core logic: targeted `dotnet test`.
- Shared pipeline/geometry changes: run relevant tests and consider adding tests.
- Overlay or screen capture behavior: run the relevant sample app and inspect/capture the screen if feasible.
- Translation provider changes: avoid real API calls unless credentials and cost are intended; prefer mocks or tests that do not hit external services.

## Recent Update Log

### 2026-06-29

- Added project memory file `docs/PROJECT_STATE.md`.
- Added local Codex skill `.codex/skills/lexverse-project` to force future prompts to read project state, check git, preserve collaboration safety, and update this file after changes.
- No product code changed.
- Verification: `quick_validate.py .codex\skills\lexverse-project` passed with `Skill is valid!`.

### 2026-06-29 Pipeline Responsiveness And Overlay Scope

- Pulled `feature/pipeline-integration`; remote was already up to date.
- Reduced document-mode OCR interval from 900 ms to 450 ms in `RealtimeTranslationOptions`.
- Changed `samples/LexVerse.Pipeline.Sample` loop timing so OCR/dịch runs on a cadence; if processing already took longer than the interval, the next capture starts immediately instead of waiting another full interval.
- Made the pipeline sample default to document mode.
- Added `SetPhysicalBounds` to `LexVerse.Overlay.MainWindow` so overlay windows can be constrained to a selected source rectangle instead of always covering the whole virtual screen.
- Updated the pipeline sample to resize the overlay to the selected source `SourceScreenRect` and hide it when the selected HWND is not visible, minimized, or not the foreground/root foreground window. This prevents translated boxes from staying on top of unrelated apps when the user opens another app.
- Verification: `dotnet test LexVerse.slnx` passed outside sandbox: 14 passed, 0 failed.
- Verification caveat: a later sandboxed `dotnet test LexVerse.slnx --no-restore` failed only because the real Google Cloud translation test could not open a socket to `translation.googleapis.com:443`.
- Caveat: visual verification with a real selected document app and another foreground app is still recommended.

### 2026-06-29 Overlay OCR Block Contrast

- Pulled `feature/pipeline-integration`; remote was already up to date.
- Created branch `codex/overlay-ocr-block-contrast` from `feature/pipeline-integration`.
- Added a `Black blocks` option in `samples/LexVerse.Pipeline.Sample`, enabled by default, so translated OCR overlay blocks render with a black background and white text.
- Kept the old white-background/black-text style available by unchecking `Black blocks`; debug boxes still use their existing diagnostic border.
- Added overlay font-size controls in `samples/LexVerse.Pipeline.Sample`: `-`, `100%`, and `+` controls adjust translated block text from 60% to 180% in 10% steps and re-render the current overlay frame immediately.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore` passed with 0 warnings and 0 errors after both overlay contrast and font-size control changes.
- Caveat: manual visual verification with a selected document app is still recommended before merging back to `feature/pipeline-integration`.

## Next Recommended Work

1. Manually run `samples/LexVerse.Pipeline.Sample`, pick a document app, and confirm translated OCR blocks render as black boxes with white text.
2. Toggle `Black blocks` off/on while the pipeline is running and confirm both palettes remain readable and aligned.
3. Use the overlay font `-` and `+` controls while the pipeline is running and confirm the active overlay text resizes without drifting away from OCR boxes.
4. While the pipeline is running, open another app over the selected app and confirm the overlay hides; return focus to the selected app and confirm it reappears in the selected app bounds.
5. If the visual result is acceptable, merge `codex/overlay-ocr-block-contrast` back into `feature/pipeline-integration`.
6. For MVP document mode, identify the next concrete slice: region selection UI, OCR grouping accuracy, translation quality/prompting, overlay placement, or pipeline sample polish.
