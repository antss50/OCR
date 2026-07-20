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

- `src/LexVerse.App`: production WPF shell and explicit composition root; Popup is the first registered end-to-end feature module.
- `src/LexVerse.Core`: shared contracts, geometry, OCR models, translation models/cache, pipeline logic.
- `src/LexVerse.Application`: feature-module registration, entitlement refresh/cache, and gated use-case execution.
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
- `src/LexVerse.Core/Translation/TranslationPromptOptions.cs`
- `src/LexVerse.Core/Translation/TranslationTermProtector.cs`
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

- Current branch: `feature/pipeline-integration`.
- `feature/ui-new-features` was created from `feature/pipeline-integration` at `7a6747f`, then committed UI/new feature work as `dc02f3b`.
- `feature/pipeline-integration` was fast-forward merged to `dc02f3b` with the UI/new feature work, excluding the experimental source/translation alignment feature.
- `feature/pipeline-integration` tracks `origin/feature/pipeline-integration` and was pushed to GitHub after the UI/new feature merge.
- `feature/ui-new-features` still exists locally at `dc02f3b` and has no upstream.
- `codex/overlay-ocr-block-contrast` still exists locally at the merged overlay commit and has no upstream.
- The current branch baseline includes the Control Center settings panel, language/shortcut configuration, ignored-term handling, comic all-caps translation fix, and text-geometry comic OCR grouping.
- The production MVP adds the production WPF shell, Popup/F6, Document-region translation, privacy consent, local diagnostics, modular feature access, packaging/update automation, and release gates.
- Popup, Region, and Document mode are the free MVP feature matrix. Account, payment, Full screen, and specialized modes are dormant post-MVP extension points.
- `.codegraph/` is a local untracked index and must remain outside product commits.
- The previous Comic OCR `11M` normalization and RAM optimization attempt remain reverted; they are not present in the current working tree.
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

### 2026-06-29 Overlay Branch Merge

- Committed the overlay contrast/font-size work on `codex/overlay-ocr-block-contrast` as `b076fda`.
- Merged `codex/overlay-ocr-block-contrast` into `feature/pipeline-integration` with a fast-forward merge.
- Verification after merge: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore` passed with 0 warnings and 0 errors.
- Git caveat: `feature/pipeline-integration` is ahead of `origin/feature/pipeline-integration` locally and still needs to be pushed if the merge should be shared.

### 2026-07-04 UI/New Feature Branch

- Fetched `origin` and confirmed `feature/pipeline-integration` matched `origin/feature/pipeline-integration` before branching (`0 0`, both at `7a6747f`).
- Created and checked out `feature/ui-new-features` from `feature/pipeline-integration` for UI and new feature development.
- No product code changed.
- Verification: `git status --short --branch` showed `## feature/ui-new-features`; `git branch -vv` showed the new branch at `7a6747f` with no upstream.
- Git caveat: push `feature/ui-new-features` and set upstream when the branch should be shared.

### 2026-07-04 Base44 Command Center UI Pass

- Reworked `samples/LexVerse.Pipeline.Sample/MainWindow.xaml` into a Base44-inspired Lexverse command center.
- Kept the existing realtime pipeline controls alive inside the new UI: pick capture source, start/stop pipeline, OCR mode, source/target languages, debug boxes, black/white overlay blocks, and overlay font-size controls.
- Added static preview surfaces for popup translation states, realtime overlay palettes, and selection overlay geometry so the sample matches the provided design direction before deeper behavior wiring.
- Updated `samples/LexVerse.Pipeline.Sample/MainWindow.xaml.cs` so language dropdowns use their `Tag` values (`en-US`, `vi`, etc.) for pipeline calls, and so selected source/running runtime labels update in the redesigned UI.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore` passed with 0 warnings and 0 errors.
- Caveat: manual visual verification by running the WPF sample is still needed; Region, Full screen, popup preview, palette selection, and selection overlay controls are currently UI previews unless separately wired.

### 2026-07-04 UX Button Feature Wiring

- Changed the sample UI back to a single Control Center instead of separate preview tabs. Popup Overlay, Realtime Overlay, and Selection Overlay are now feature buttons/modes rather than standalone preview pages.
- Fixed the unreadable language/OCR combo boxes by using light input surfaces with black selected/item text.
- Added `samples/LexVerse.Pipeline.Sample/TranslationPopupWindow.*` for the Popup/F6 dictionary flow: highlight text in another app, press F6 or click Popup, show a loading popup only when translation is slow, then show the Lexverse translation result.
- Added global F6 registration in `samples/LexVerse.Pipeline.Sample/MainWindow.xaml.cs`; the flow sends Ctrl+C to the foreground app, reads highlighted text from the clipboard, restores the previous text clipboard when possible, and uses Google translation for the popup result.
- Added `samples/LexVerse.Pipeline.Sample/SelectionOverlayWindow.*` for the Region flow: drag a screen area, Esc cancels, and the selected screen rectangle is used as an OCR region mask.
- Wired `Region` to capture the monitor containing the selected rectangle and run the existing realtime pipeline only on that selected screen region.
- Wired `Full screen` to capture the nearest monitor and run the existing realtime pipeline without a region mask.
- Added `GraphicsCaptureItemFactory.CreateForMonitor` in `src/LexVerse.Infrastructure/Capture/GraphicsCaptureItemFactory.cs` so monitor capture can be started without the picker.
- Removed the old unused picker button flow from the sample window because the UX now uses Popup/F6, Region, and Full screen actions.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore` passed with 0 warnings and 0 errors.
- Caveat: manual visual testing is still needed on Windows for global F6 focus behavior, clipboard restoration, region DPI accuracy, and monitor capture permissions.

### 2026-07-04 Remove Top Mode Nav

- Removed the top segmented navigation row (`Control Center`, `Popup Overlay`, `Realtime Overlay`, `Selection Overlay`) from `samples/LexVerse.Pipeline.Sample/MainWindow.xaml`.
- Kept Popup/F6, Region, Full screen, and Stop as the actual feature controls in the command center header/cards.
- Removed the now-unused nav button references from `samples/LexVerse.Pipeline.Sample/MainWindow.xaml.cs` and deleted unused nav styles.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore` passed with 0 warnings and 0 errors.

### 2026-07-04 Region DPI And Popup Keywords

- Fixed a likely Region selection accuracy issue in `samples/LexVerse.Pipeline.Sample/SelectionOverlayWindow.xaml.cs`: the selected rectangle is now converted from WPF DIPs to physical screen pixels with `PointToScreen` before being passed into the realtime OCR region mask.
- Updated the selection size label to display the physical pixel size that the pipeline will actually use.
- Changed the popup yellow panel in `samples/LexVerse.Pipeline.Sample/TranslationPopupWindow.*` from repeating `source -> translation` to an `Important terms` panel.
- Added lightweight keyword/entity extraction and short summaries for proper names and key terms, including contextual summaries for terms like `Theodosius II`, `Aelia Pulcheria`, and `Augusta`.
- Verification: normal `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore` reached compile but could not overwrite the running sample exe locked by `LexVerse.Pipeline.Sample (38760)`.
- Verification fallback: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.

### 2026-07-04 Popup Keyword Summary Refinement

- Replaced the overly generic popup keyword fallback (`ten rieng quan trong...`) in `samples/LexVerse.Pipeline.Sample/TranslationPopupWindow.xaml.cs`.
- Added keyword normalization so leading prepositions such as `In`, `At`, and `From` are stripped before rendering; this prevents labels like `In Philadelphia`.
- Added local summaries for examples seen during manual testing: `Philadelphia`, `Continental Congress`, `British Empire`, `West Germany`, and `The Miracle`, plus existing Byzantine examples.
- Added category fallbacks for likely places and political/organization/state terms; unknown multi-word terms now say they need more context instead of pretending every item is the same kind of important proper noun.
- Kept the summaries ASCII-only for now to avoid the previous mojibake issue in source strings.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.

### 2026-07-04 Popup Two-Step Keyword Lookup And Control Center Polish

- Changed the Popup/F6 result flow so the popup first shows only the selected text translation plus clickable `Important terms` chips; it no longer expands every term automatically.
- Added `samples/LexVerse.Pipeline.Sample/PopupKeywordExplainer.cs`, which looks up a clicked term through Wikipedia, trims the extract to a short summary, and translates that summary through the existing `ITextTranslator`/Google provider when the target language is Vietnamese or another non-English language.
- Added Vietnamese accented loading/error/fallback strings for keyword details; a UTF-8 read check confirmed the source files contain the accented strings correctly even though PowerShell may render them as mojibake.
- Kept built-in accented fallback summaries for common examples (`Theodosius II`, `Aelia Pulcheria`, `Augusta`, `Philadelphia`, `Continental Congress`, `British Empire`, `West Germany`, and `The Miracle`) when lookup fails or lacks enough context.
- Polished `samples/LexVerse.Pipeline.Sample/MainWindow.xaml` closer to the provided Control Center screenshot: custom frameless header with minimize/maximize/close buttons, Windows icon glyphs for Popup/Capture/Preview/Realtime/Full screen/Stop/runtime rows, card label text closer to the mockup, hidden OCR debug checkbox from the main Behavior list, hidden runtime block list, and light ignored-terms input with black text.
- Replaced the unused inline realtime `Start` button with a realtime `Stop` button because `Region` and `Full screen` now start the pipeline directly.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.
- Verification: `git diff --check` passed; it only reported existing LF-to-CRLF working-copy warnings for tracked files.
- Caveat: manual visual testing is still needed for the frameless WPF Control Center, and live keyword explanations need internet access plus working Google translation credentials for localized summaries.

### 2026-07-04 Control Theme And Keyword Click Fix

- Changed the left-panel `FROM`, `TO`, `IGNORED TERMS`, and `OCR MODE` controls back to dark input surfaces with clear borders and white text so they match the theme without blending into the background.
- Replaced the default WPF checkbox and slider visuals in `samples/LexVerse.Pipeline.Sample/MainWindow.xaml` with custom yellow theme templates for checked states, slider fill, and slider thumbs.
- Made popup keyword chips more reliable by handling `PreviewMouseLeftButtonUp`, updating the keyword detail layout immediately, and keeping the translation visible while the summary appears underneath the important-term chips.
- Fixed popup outside-click detection to convert the physical cursor position through `PointFromScreen` before comparing against WPF window dimensions; this avoids DPI-scale mismatches that could make clicks inside the popup behave like outside clicks.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.

### 2026-07-04 Dark ComboBox Template Fix

- Replaced the left-panel ComboBox default Windows rendering with a custom dark template so `FROM`, `TO`, and `OCR MODE` no longer show white/blue system surfaces.
- Styled the dropdown list items with dark backgrounds, white text, and amber selected/highlight states to match `IGNORED TERMS`.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.

### 2026-07-04 ComboBox Selected Text Color Fix

- Forced the selected `FROM`, `TO`, and `OCR MODE` ComboBox text to use `WhiteTextBrush` directly inside the custom template because the default selected-content rendering was still showing black text.
- Forced dropdown item text to use white through the ComboBoxItem template as well.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.

### 2026-07-04 Status Badge Alignment Fix

- Added shared `StatusBadgeBorder` and `StatusBadgeText` styles in `samples/LexVerse.Pipeline.Sample/MainWindow.xaml` so the small `Bedrock` and `Win OCR + Google` pills use a fixed compact height, zero vertical padding, centered text, and explicit line height.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.

### 2026-07-04 Window Drag And Popup Keyword Race Fix

- Set `TranslationPopupWindow` to `ResizeMode="NoResize"` with layout rounding/snapping to remove the stray white resize-frame artifact that could appear around the transparent popup.
- Added edge dragging for the main frameless Control Center shell while excluding interactive controls; the window can now be moved by dragging near the rim as well as the header.
- Enabled popup dragging by default and added popup edge dragging when dragging is allowed.
- Changed popup important-term lookups to use a versioned cancellation flow: clicking another term immediately replaces the current detail panel, and older lookup results/errors are ignored instead of overwriting the newly selected term.
- Improved the `President` lookup query in Peru context so it searches for `President of Peru`.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.

### 2026-07-04 Popup Border And Drag Hardening

- Removed the popup `DropShadowEffect` and explicitly cleared native border/thick-frame/edge styles on `TranslationPopupWindow` after source initialization to eliminate the remaining white rectangular edge.
- Switched main-window and popup dragging from WPF `DragMove()` to native `WM_NCLBUTTONDOWN/HTCAPTION`, which is more reliable for frameless windows.
- Attached the main-window drag handler to the root grid as well as the inner shell so dragging from the outer rim works.
- Changed important-term chip activation from mouse-up to mouse-down so selecting a different term immediately replaces the detail title/loading state before the lookup completes.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.

### 2026-07-04 Popup Source/Translation Alignment Prototype

- Added a first popup comparison/alignment prototype for F6 translations.
- `SelectedTextGeometryReader` tries to read the current selected text range from the source app through Windows UI Automation `TextPattern`, then records screen rectangles for the selected source tokens.
- `PopupWordAlignmentBuilder` maps common English source phrases to Vietnamese target phrases plus exact proper-name/number matches; examples include `emperor` -> `hoàng đế`, `President` -> `Tổng thống`, `United States` -> `Hoa Kỳ`, and matching names/numbers.
- `SourceHighlightOverlayWindow` renders click-through amber highlights over the original selected source word/phrase on the screen.
- `TranslationPopupWindow` now renders aligned translated phrases as hoverable runs; hovering a mapped translated phrase highlights the corresponding source phrase outside the popup.
- Caveat: outside-screen highlighting depends on the source app exposing selected-text geometry through UI Automation. If an app/browser does not expose `TextPattern` ranges, the popup still translates normally but cannot draw the external source highlight until an OCR-coordinate fallback is added.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.

### 2026-07-04 Popup Alignment Browser Fallback Fix

- Fixed the popup source/translation alignment map so Vietnamese target phrases are stored with real UTF-8 accents instead of mojibake; mappings such as `emperor` -> `hoàng đế`, `Continental Congress` -> `Quốc hội Lục địa`, `Declaration of Independence` -> `Tuyên ngôn Độc lập`, and `British Empire` -> `Đế quốc Anh` can now match translated text.
- Expanded popup alignment mappings for the Wikipedia examples currently used in manual testing, including `Philadelphia`, `American colonies`, `American`, `thirteen`, `Theodosius II`, and `Aelia Pulcheria`.
- Hardened `SelectedTextGeometryReader` for browsers and rich text surfaces: it now tries the focused UIA element, its parents, the foreground window, and text-pattern descendants instead of only the top-level window handle.
- Added a selection-rectangle fallback for alignment. If exact token rectangles are unavailable but the source app exposes the selected range rectangles, Lexverse estimates token rectangles across the selected lines so hovering translated phrases can still highlight the original source area.
- Changed target phrase lookup to use accent-insensitive matching so small translation accent variants do not prevent hover alignment.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.
- Caveat: if a source app exposes neither UIA token rectangles nor UIA selected-range rectangles, external highlighting still needs an OCR/screenshot-coordinate fallback.

### 2026-07-04 Popup Full-Text Alignment Fallback

- Changed popup translation/source comparison so alignments now carry an exact target-text start position. This prevents repeated translated words from sharing the wrong source highlight.
- Kept exact dictionary/proper-name/number alignments as the first priority.
- Added an estimated token fallback for translated words that do not have an exact bilingual mapping yet. Remaining Vietnamese tokens are mapped to source tokens by relative order, so nearly the whole translated sentence can be hovered for comparison instead of only known terms.
- Updated `TranslationPopupWindow` to render the new target-position alignments directly rather than searching the same target phrase repeatedly.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.
- Caveat: fallback token alignment is intentionally approximate; exact all-word alignment still needs a translation provider or AI response that returns bilingual alignment pairs.

### 2026-07-04 Popup Alignment Removed Before Merge

- Removed the experimental popup source/translation comparison feature before merging UI work back to `feature/pipeline-integration`.
- Deleted the alignment-only helper files for selected-text geometry reading, word alignment building, and source highlight overlays.
- Restored the F6 popup result to plain translated text plus clickable `Important terms`; it no longer creates hoverable translated runs or draws highlights over the source app.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.

### 2026-07-04 UI/New Features Merge To Pipeline Integration

- Committed the Base44-inspired command center UI, Popup/F6 flow, clickable important-term lookup, Region realtime flow, Full screen realtime flow, monitor capture factory support, themed controls, popup border/drag fixes, and project state updates on `feature/ui-new-features` as `dc02f3b`.
- Confirmed the experimental source/translation alignment files were not staged or committed.
- Fast-forward merged `feature/ui-new-features` into `feature/pipeline-integration`.
- Verification after merge: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.
- Git status: `feature/pipeline-integration` was pushed to `origin/feature/pipeline-integration` after the merge.

### 2026-07-05 Pipeline Sample Memory Optimization

- Confirmed the experimental popup source/translation comparison runtime code is absent from `samples/` and `src/`; only historical notes remain in `docs/PROJECT_STATE.md`.
- Changed `samples/LexVerse.Pipeline.Sample/MainWindow.xaml.cs` so overlay font-size re-rendering stores the last `OverlayRenderFrame` only, rather than the full `RealtimeTranslationPipelineResult`.
- This avoids the UI holding onto `ScreenOcrPipelineResult.Frame.Pixels` after rendering, reducing retained memory while realtime capture is running.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-...` passed with 0 warnings and 0 errors.

### 2026-07-05 Runtime Translation Style Prompt

- Added `TranslationPromptOptions` in `src/LexVerse.Core/Translation` so a user-written translation instruction can flow through translator calls without changing older implementations.
- Extended `ITextTranslator` with prompt-aware overloads; `GoogleCloudTextTranslator` accepts the overloads but still ignores prompt instructions because the current Google Cloud Translation V2 API path is not prompt-driven.
- Added `TranslationPrompt` to `RealtimeTranslationOptions` and included its cache key in `TranslationCacheKey`, preventing prompt/style changes from reusing old translation cache entries.
- Updated `RealtimeTranslationPipeline` so the last cached translation result is also keyed by source language, target language, mode, and prompt cache key. The sample updates the prompt before each realtime loop iteration, so changing the Runtime prompt can apply on the next pass without restarting realtime.
- Added a `TRANSLATION STYLE` text area in the Control Center Runtime card. Popup/F6 translation, popup keyword summary localization, and realtime translation now all pass the same runtime prompt options.
- Added a pipeline cache test proving that changing the prompt reuses cached OCR but retranslates instead of returning the previous translation.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-prompt` passed with 0 warnings and 0 errors.
- Verification: `dotnet test tests/LexVerse.Translation.Tests/LexVerse.Translation.Tests.csproj --no-restore` passed: 15 passed, 0 failed, 0 skipped.
- Caveat: a real AI/Bedrock/OpenAI-style translator still needs to consume `TranslationPromptOptions.Instruction` for visible style changes. The current Google translator keeps behavior unchanged while preserving the plumbing.

### 2026-07-05 Ignored Terms Preservation And Overlay Suppression Fix

- Confirmed `IgnoredTermsBox` was only present in the WPF UI and was not connected to translation behavior.
- Extended `TranslationPromptOptions` with `IgnoredTerms` and `PreserveAcronymsAndTechnicalTerms`; these values are included in the translation prompt cache key so changing ignored terms does not reuse stale translations.
- Added `TranslationTermProtector`, which replaces ignored terms/acronyms with stable temporary tokens before provider translation and restores the original terms afterward. This makes ignored terms work with the current Google translator even though Google does not consume style prompts.
- Added ignored-only OCR suppression in `RealtimeTranslationPipeline`: blocks such as `HP`, `MP 10/20`, `EXP: 45`, `FPS 60`, or `HP, MP, EXP, FPS` are skipped before translation/cache/overlay, while real sentences like `HP increased after battle` still translate and preserve `HP`.
- Wired the Control Center `IGNORED TERMS` box and `Keep acronyms / technical terms` checkbox into the same options used by Popup/F6, keyword summary localization, Region realtime, and Full screen realtime.
- Updated Runtime status text to show prompt/ignored-term/acronym-preservation state.
- Added `TranslationTermProtectorTests` for explicit ignored terms, automatic acronym preservation, ignored-only stat suppression, and sentence preservation.
- Added a realtime pipeline test proving ignored game-stat OCR blocks are omitted from translated blocks/overlay items.
- Verification: `dotnet test tests/LexVerse.Translation.Tests/LexVerse.Translation.Tests.csproj --no-restore` passed: 24 passed, 0 failed, 0 skipped.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-ignored-terms` passed with 0 warnings and 0 errors.

### 2026-07-05 Control Center Settings Panel

- Added `samples/LexVerse.Pipeline.Sample/AppUserSettings.cs` to persist local app settings under `%APPDATA%\LexVerse\pipeline-sample-settings.ini`.
- Added a gear settings panel in the Control Center with only the two requested groups: `Language` and `Shortcuts`.
- `Language` currently switches the app UI between English and Vietnamese labels. This is separate from `FROM`/`TO`, which still control translation source and target languages.
- `Shortcuts` lets the user rebind Popup translate, Region translate, Full screen realtime, Stop realtime, and Reset/clear overlay. Defaults are F6, Ctrl+Shift+R, Ctrl+Shift+F, Esc, and Ctrl+Shift+Backspace.
- Shortcut changes check for duplicates before saving. The Popup shortcut also re-registers the global hotkey and rejects combinations already taken by another app.
- The header Popup hotkey badge, Runtime hotkey row, tooltip text, and status text now reflect the configured Popup shortcut instead of hard-coded F6.
- Optimized the settings storage path to use a small key/value file instead of loading `System.Text.Json` at app startup.
- Memory diagnostic on this machine: a hidden idle launch of `HEAD` before the Settings panel measured about 188 MB Working Set / 145 MB Private Memory; the Settings build measured about 206 MB / 160 MB. The Settings feature appears to add roughly 15-18 MB in this measurement, while most idle memory is still the WPF/.NET/OCR/capture baseline.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-settings` passed with 0 warnings and 0 errors.
- Verification: `dotnet test tests/LexVerse.Translation.Tests/LexVerse.Translation.Tests.csproj --no-restore` passed: 24 passed, 0 failed, 0 skipped.

### 2026-07-09 Comic Uppercase Dialogue Fix

- Root cause for an English comic speech bubble such as `WHAT DO YOU THINK YOU'RE DOING?`: with `Keep acronyms / technical terms` enabled, automatic acronym preservation treated each all-caps dialogue word as a protected term; ignored-only suppression could then skip blocks, or the translator would receive only keep-tokens and restore the original English text.
- Changed `TranslationTermProtector` so ignored-only suppression only considers explicit ignored terms, not automatic acronym matches.
- Added an all-caps prose heuristic so automatic acronym preservation still protects mixed-case technical terms such as `OCR`, `HP`, and `EXP`, but does not tokenize comic-style all-caps dialogue before translation.
- Added focused tests for the screenshot-style all-caps dialogue lines.
- Verification: `dotnet test tests/LexVerse.Translation.Tests/LexVerse.Translation.Tests.csproj --no-restore` passed: 28 passed, 0 failed, 0 skipped.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-comic-uppercase` passed with 0 warnings and 0 errors.
- Git caveat: `feature/pipeline-integration` was already up to date with origin before this change; the fix is currently uncommitted in the working tree.

### 2026-07-09 Reverted Comic / Manga OCR Mode

- At user request, widened the rollback to remove the initial Comic / manga OCR mode as well because the app still did not behave like the earlier version.
- Removed `OcrProcessingMode.Comic`, `RealtimeTranslationOptions.Comic`, the comic grouping mode/grouper/test files, the WPF `Comic / manga` dropdown option, and the `WindowsOcrService` grouping-mode constructor path.
- Kept the earlier all-caps dialogue/acronym-preservation fix only.
- The previous Comic OCR `11M` normalization and RAM/crop optimization attempt remains reverted.
- Verification: `dotnet test tests/LexVerse.Translation.Tests/LexVerse.Translation.Tests.csproj --no-restore` passed: 28 passed, 0 failed, 0 skipped.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-revert-comic-mode` passed with 0 warnings and 0 errors.

### 2026-07-18 Comic / Manga OCR Mode Added Back

- Added `OcrProcessingMode.Comic` and `RealtimeTranslationOptions.Comic` back for comic/manga reading.
- Added `OcrTextBlockGroupingMode` plus `ComicSpeechBubbleGrouper` in Core. The comic grouper clusters OCR lines by text geometry: close vertical spacing, similar font size, and center/horizontal alignment, so stacked lines inside one speech bubble become one dialogue block.
- Updated `WindowsOcrService` so the caller can choose `Layout` grouping or `ComicSpeechBubbles` grouping while keeping `Layout` as the default for existing modes.
- Wired the WPF pipeline sample `OCR MODE` dropdown with `Comic / manga`; selecting it uses comic grouping only for that mode.
- Added tests for centered stacked bubble lines, side-by-side bubbles, and vertically distant bubbles.
- Caveat: this mode groups recognized text lines; it does not perform image-based detection of drawn balloon outlines yet.
- Verification: `dotnet test tests/LexVerse.Translation.Tests/LexVerse.Translation.Tests.csproj --no-restore` passed: 31 passed, 0 failed, 0 skipped.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-comic-mode` passed with 0 warnings and 0 errors.

### 2026-07-18 Reverted Comic Today/Gonna Experiments

- At user request, reverted the experimental fixes attempted after Comic mode was added back because they made OCR/translation behavior worse.
- Removed the Comic short-final-line/orphan-continuation grouper changes, OCR high-contrast/2x variant passes, `CONNA` -> `GONNA` normalizer, realtime source-language wiring, and acronym allowlist changes.
- Current working tree keeps the earlier Comic / manga mode only: it adds the dropdown option, `OcrProcessingMode.Comic`, `RealtimeTranslationOptions.Comic`, `OcrTextBlockGroupingMode`, and the basic `ComicSpeechBubbleGrouper` text-geometry grouping.
- Verification: `dotnet test tests/LexVerse.Translation.Tests/LexVerse.Translation.Tests.csproj --no-restore` passed: 31 passed, 0 failed, 0 skipped.
- Verification: `dotnet build samples/LexVerse.Pipeline.Sample/LexVerse.Pipeline.Sample.csproj --no-restore -o %TEMP%\lexverse-pipeline-sample-build-comic-rollback` passed with 0 warnings and 0 errors.

### 2026-07-20 Production Architecture And Product Entitlements

- Audited the repository against the production goal. The runnable product behavior is still concentrated in `samples/LexVerse.Pipeline.Sample`, while `src/LexVerse.App` remains a console placeholder. No existing licensing/entitlement, installer/updater, CI release, telemetry, or global production error-handling mechanism was found.
- Added `docs/PRODUCTION_ARCHITECTURE.md` with target boundaries, commercial/security rules, delivery milestones, and initial production gates.
- Added the first product-domain foundation in `src/LexVerse.Core/Product`: stable feature keys, known LexVerse capability keys, versioned plan metadata, immutable time-bound entitlement snapshots, explicit access decisions, and an entitlement-provider contract.
- Kept price, billing interval, checkout, renewal, and entitlement issuance outside the desktop authority. These values should come from a remote store/backend catalog so they can change without rebuilding the client; backend operations must re-authorize paid use cases.
- Added focused tests covering feature-key normalization, invalid keys, active/permanent/future/expired/missing grants, duplicate plan IDs, case-insensitive plan lookup, and feature deduplication.
- Verification: `dotnet test tests/LexVerse.Translation.Tests/LexVerse.Translation.Tests.csproj --no-restore` passed: 40 passed, 0 failed, 0 skipped.
- Verification: filtered product tests passed: 9 passed, 0 failed, 0 skipped.
- Verification: `dotnet build LexVerse.slnx --no-restore` passed with 0 warnings and 0 errors.
- Git caveat: production foundation changes are uncommitted. `.codegraph/` remains an unrelated untracked index directory and was not modified as product code.
- Next recommended production work: create the WPF composition root in `src/LexVerse.App`, extract application use cases from `Pipeline.Sample`, and require feature-access decisions at each paid capability boundary before wiring a real entitlement adapter.

### 2026-07-20 Application Boundary, Module Registry, And Remote Offers

- Added `src/LexVerse.Application` as a platform-neutral application layer and included it in `LexVerse.slnx`.
- Added `FeatureAccessService`, which shares a short-lived verified entitlement snapshot across modules, serializes refreshes, uses an atomic immutable cache state for concurrent callers, throttles provider failures, and fails closed with `EntitlementsUnavailable`. Caller cancellation preserves the previous cache state.
- Added `FeatureGate` and `FeatureAccessDeniedException`; paid use cases can now enforce authorization at execution time instead of relying on hidden/disabled buttons.
- Added `FeatureModuleDescriptor`, `IFeatureModule`, and `FeatureModuleRegistry`. The registry fails during startup when module IDs collide or more than one module claims the same feature key, enabling deterministic add/remove/replace behavior in the future composition root.
- Extended the versioned product catalog with remote `ProductOffer` entries, currency-safe `Money`, billing/trial `SubscriptionPeriod`, and `IProductCatalogProvider`. Plans remain stable feature groupings while offer price, currency, billing interval, and trial duration can change remotely without a desktop release.
- Preserved the commercial security boundary: catalog prices are display metadata; checkout must resolve the offer ID with the authoritative store/backend, which issues verified entitlements.
- Added tests for entitlement caching/refresh/failure retry, forced-refresh errors, gated execution, duplicate module registration/ownership, remote offers, billing/trial periods, and invalid plan references.
- Verification: focused product/application/module tests passed: 21 passed, 0 failed, 0 skipped.
- Verification: full `LexVerse.Translation.Tests` suite passed: 52 passed, 0 failed, 0 skipped.
- Verification: `dotnet build LexVerse.slnx --no-restore` passed with 0 warnings and 0 errors.
- Git caveat: all production architecture work remains uncommitted on `feature/pipeline-integration`; `.codegraph/` remains an unrelated untracked index directory.
- Next recommended production work: convert `src/LexVerse.App` to WPF, create its composition root, register feature modules there, then extract Popup as the first end-to-end gated use case from `Pipeline.Sample`.

### 2026-07-20 Production WPF Shell And Gated Popup Module

- Converted `src/LexVerse.App` from a console placeholder to a WPF `WinExe` with explicit startup in `App.xaml.cs`; the shell is created only by `AppCompositionRoot` rather than by `StartupUri` or UI-side provider construction.
- Added a polished production Control Center shell with native window controls, clear Popup/F6 guidance, source/target language selection, quick-text fallback, module status, provider privacy guidance, and a persistent status region.
- Added `PopupTranslationUseCase` to `LexVerse.Application`. Both selected-text and quick-text paths authorize `translation.popup` through `FeatureGate` before reading clipboard content or calling a provider.
- Added `WindowsSelectedTextReader` to Infrastructure. It sends Ctrl+C with `SendInput`, polls the clipboard sequence with a bounded timeout instead of a fixed wait, supports cancellation, and restores prior text clipboard content on a best-effort basis.
- Added `DeferredTextTranslator`, so Google credentials/provider initialization happen on first request. Missing credentials no longer prevent the WPF shell from starting and presenting recovery guidance.
- Added `PopupFeatureModule` and registered it in `FeatureModuleRegistry`. The current composition root grants Popup via a clearly named bundled-free entitlement; this is an explicit bootstrap product decision and must be replaced by verified remote/signed-offline entitlements before paid launches.
- Added a topmost translation popup with loading, result, user-safe error, drag, close, screen-bound placement, and copy-result behavior.
- Added global F6 registration in the thin WPF shell. Starting a new Popup operation cancels the previous operation and app shutdown releases the hotkey and cancellation resources.
- Added `LocalExceptionLog` and startup/dispatcher/task/app-domain exception handling. Fatal startup/UI errors are recorded as local JSONL under `%LOCALAPPDATA%\LexVerse\Logs` and shown as user-safe messages.
- Qualified WPF `System.Windows.Application` in the existing sample/overlay app classes after the new `LexVerse.Application` namespace exposed a compile-time name collision; sample behavior is unchanged.
- Added five focused tests proving the Popup gate runs before clipboard/provider access, no-selection avoids provider calls, explicit quick text uses the same gate, language tags normalize, and deferred provider initialization occurs once.
- Verification: focused Popup tests passed: 5 passed, 0 failed, 0 skipped.
- Verification: full test suite passed: 57 passed, 0 failed, 0 skipped.
- Verification: `dotnet build LexVerse.slnx --no-restore` passed with 0 warnings and 0 errors after namespace qualification.
- Verification: hidden startup smoke kept `LexVerse.App` alive for 3 seconds without credentials, then stopped the test process.
- Visual verification: captured the foreground app window at 980x650; layout rendered without clipping or overlap, language controls remained readable, and F6/module/status states were visible. The temporary screenshot was written to `C:\tmp\lexverse-app-visual.png`, outside the repository.
- Caveat: live F6 clipboard behavior against another Windows app and a successful Google translation with real credentials still require manual end-to-end testing.
- Git caveat: the production architecture and WPF changes remain uncommitted on `feature/pipeline-integration`; `.codegraph/` remains an unrelated untracked index directory.
- Next recommended production work: extract Region/Full screen realtime orchestration into a cancellable application service and register it as the second production module; keep overlay rendering and monitor capture behind existing Core/Infrastructure contracts.

### 2026-07-20 Production Region And Full-Screen Realtime Module

- Extracted Region and Full screen orchestration from `samples/LexVerse.Pipeline.Sample` into platform-neutral `LexVerse.Application.Realtime` contracts and `RealtimeTranslationCoordinator`.
- The coordinator authorizes the requested feature before creating capture resources, owns start/restart/stop and OCR cadence, publishes explicit state/frame events, supports cancellation, and deterministically disposes its active session/output after failures or shutdown.
- Added `WindowsRealtimePipelineSessionFactory` in Infrastructure. It selects the correct monitor, creates Windows Graphics Capture and OCR resources, chooses Comic speech-bubble grouping when requested, and adapts `RealtimeTranslationPipeline` results into application-level frame updates.
- Added `WindowsMonitorService` and the Core `ScreenRectOcrRegionProvider`; selected physical screen rectangles are clipped to the captured monitor and mapped into frame-local OCR pixels without WPF dependencies.
- Added the registered `RealtimeFeatureModule`, production Region selector, and `WpfRealtimeOverlayOutput`. Overlay items are reconciled in place to reduce flicker, and the Control Center exposes Document, Comic/manga, Subtitle, and Game dialogue profiles.
- Region and Full screen now have separate entitlement checks (`translation.region` and `translation.full-screen`). The bootstrap composition root grants both alongside Popup only to keep the current offline vertical slice usable; paid release builds still require verified remote plus signed-offline entitlements.
- Main Control Center, Popup, and translation overlay windows opt out of Windows display capture, preventing LexVerse from OCRing its own UI. Closing the app asynchronously stops realtime work before releasing the composition root.
- Added five tests covering gated session creation, frame processing and cleanup, restart disposal, fault state/release, and physical-region-to-frame mapping.
- Hidden startup smoke confirmed the app remains alive without Google credentials; provider initialization is still deferred until translation is requested.
- Visual verification at 980x650 confirmed the realtime module, processing selector, Region/Full screen/Stop controls, module states, and status area render without overlap. The temporary QA image is `C:\tmp\lexverse-realtime-module-visual.png`, outside the repository.
- Caveat: a real credentialed Windows capture -> OCR -> Google translation -> overlay pass still requires manual end-to-end validation, including multi-monitor DPI alignment and installed OCR language packs.
- Git caveat: the production work remains uncommitted on `feature/pipeline-integration`; `.codegraph/` remains an unrelated untracked index directory.
- Next recommended production milestone: signed packaging and update channels, followed by verified remote/signed-offline entitlement adapters and automated release gates.

### 2026-07-20 MSIX Distribution And CI Quality Gates

- Chose self-contained MSIX plus Windows App Installer for customer distribution. The package does not require a preinstalled .NET runtime; Windows owns clean install/uninstall, package integrity, on-launch/background updates, and repair.
- Added `scripts/package-msix.ps1` with explicit Stable/Beta identities, four-part version validation, HTTPS enforcement, generated package assets/manifest, ReadyToRun publishing, Windows SDK tool discovery, MakeAppx packaging, SHA-256 checksum generation, and deterministic staging cleanup.
- Production packaging fails closed unless a certificate-store thumbprint is supplied. Signing uses SHA-256 plus an RFC 3161 timestamp and is verified after signing; `-Unsigned` is an explicit local/CI-only escape hatch.
- Added independent `AppInstallerVersion`. Operators can monotonically advance the channel manifest while pointing it at an older signed package, enabling controlled rollback through `ForceUpdateFromAnyVersion`.
- Added `packaging/README.md` with build, atomic publication, rollback, signing, retention, and clean-VM release requirements. The versioned MSIX/checksum must be uploaded and verified before replacing the channel `.appinstaller` pointer.
- Added `.github/workflows/ci.yml` for Windows build/test/package gates: least-privilege repository access, concurrent-run cancellation, .NET 10 setup, restore, warnings-as-errors build, test results on failure, transitive vulnerability audit, packaging guard tests, and an unsigned MSIX smoke artifact.
- Added `global.json` pinned to .NET SDK 10.0.301 with patch-only roll-forward, so developer and CI builds use the same feature band. Added weekly Dependabot monitoring for NuGet and GitHub Actions; NuGet minor/patch changes are grouped while majors remain individually reviewable.
- Local packaging smoke succeeded through self-contained publish and MakeAppx validation: 431 payload files, 78.81 MiB MSIX, matching SHA-256, well-formed App Installer XML, and no retained staging directory.
- Safety smoke confirmed insecure HTTP hosting and missing production signing configuration are both rejected before release output is created.
- NuGet audit reported no known vulnerable direct or transitive packages across all solution projects using the current advisory sources.
- Verification: full Debug and Release tests passed 62/62; Release `dotnet build LexVerse.slnx --configuration Release --no-restore --warnaserror` passed with 0 warnings and 0 errors; the final capture-protected app passed a hidden 3-second startup smoke without credentials.
- Caveat: the smoke MSIX is unsigned and intentionally not installable as a public build. A trusted signing identity, real HTTPS download host/MIME configuration, clean-VM install/update/rollback/repair tests, and production release promotion remain external release prerequisites.
- Next recommended production work: add privacy-aware operational telemetry and performance budgets, then complete a signed Beta release rehearsal on clean Windows VMs.

### 2026-07-20 Privacy-Safe Telemetry And Performance Budgets

- Added a platform-neutral, strongly typed `OperationalEvent`/`IOperationalTelemetry` boundary in Application. The schema has no arbitrary string, dictionary, or object payload and therefore cannot carry OCR text, translations, clipboard content, prompts, pixels, coordinates, window titles, or user/device identifiers.
- Instrumented process startup/shutdown, Popup selected/quick translation outcomes, realtime session lifecycle, sampled frames, capture/OCR/translation/total latency, cache counts, block counts, memory, and budget violations. Telemetry failures are fail-open and cannot break translation or entitlement enforcement.
- Added `LocalOperationalTelemetry`: a bounded drop-oldest channel, background batching, independent five-second flush deadline, deterministic shutdown flush, 1 MiB file limit, and three rotated files. Realtime records the first frame, every twentieth frame, and every over-budget frame; the closing session event keeps total frames and total violations.
- Hardened `LocalExceptionLog`: raw exception messages and stack traces are no longer persisted. Local crash entries retain source, exception/inner type, HRESULT, timestamp, and a SHA-256 fingerprint for grouping without exposing provider request context or local paths.
- Added mode-specific warning budgets for capture, OCR, translation, and total frame time. Over-budget frames continue translating, appear as `slower than target` in Control Center, and are captured as privacy-safe metrics.
- Added `scripts/test-startup-budget.ps1` and wired it into CI. It requires the exact responsive `LexVerse Control Center` window within a 5,000 ms hard budget, reports a 3,000 ms target, enforces an initial 250 MiB working-set limit, and closes the app gracefully with a bounded fallback.
- The startup gate exposed a real WPF shutdown bug: when realtime cleanup completed synchronously, the re-entrant second `Close()` was ignored indefinitely. The final close is now posted through the Dispatcher, and startup smoke verifies graceful process exit plus telemetry flush.
- Realtime stop now completes deterministic cleanup once begun even if the caller cancellation token changes. Session/output cleanup failures are contained, surfaced as `cleanup-failed`, and recorded without leaking exception text.
- Added `docs/TELEMETRY_PRIVACY.md` documenting permitted/forbidden data, storage/retention, sampling, budgets, and the consent/TLS/schema/rate-limit gates required before any future remote adapter.
- Added tests for schema privacy, background deadline flush, bounded rotation, crash redaction, budget evaluation, Popup outcomes/fail-open behavior, realtime session correlation, sampling/session totals, and cleanup failure handling.
- Verification: Release build with warnings as errors passed with 0 warnings and 0 errors; full Release suite passed 72/72.
- Runtime verification outside the workspace sandbox created and flushed `%LOCALAPPDATA%\LexVerse\Logs\operational-metrics.jsonl` with content-free startup/shutdown events. Latest startup was 1,848.2 ms with 117.1 MiB working set; telemetry measured 1,749.4 ms from OS process start to the post-show event. The 5-second hard budget and 3-second target both passed on that run, though earlier cold runs reached about 4 seconds, so startup-target stability remains an optimization item.
- Git caveat: all production work remains uncommitted on `feature/pipeline-integration`; `.codegraph/` remains an unrelated untracked index directory.
- Next recommended production work: add a consent/settings surface and signed-offline entitlement adapter, then perform a signed Beta install/update/rollback rehearsal on clean Windows VMs.

### 2026-07-20 Signed Paid Entitlements And Offline Fail-Closed Access

- Replaced the bootstrap provider that granted Popup, Region, and Full screen with an explicit free-tier wrapper that grants only `translation.popup`. Paid capabilities now fail closed when verified claims are unavailable.
- Added a strict ES256 entitlement verifier: P-256/SHA-256 with fixed 64-byte IEEE-P1363 signatures, pinned public keys selected by `keyId`, exact-payload-byte verification, subject binding, two-minute clock skew, 72-hour maximum lifetime, strict JSON fields, bounded envelope/grant sizes, and grant expiry clamping.
- Added the HTTPS remote entitlement provider with an eight-second deadline covering headers and streamed content, a 256 KiB response limit, caller-cancellation preservation, and generic failure surfaces. A verified online response remains usable if local persistence fails.
- Added a bounded signed-envelope file cache under `%LOCALAPPDATA%\LexVerse\Entitlements`. It stores the exact envelope with same-directory temporary-file replacement and re-verifies signature, identity, key, and time before every offline use; unsigned or expired data never grants access.
- Added environment-based composition for endpoint, subject, key ID, and public-key PEM. Missing/invalid deployment configuration leaves the app operational in free mode instead of causing startup failure. The private signing key remains a backend-only concern.
- Updated Control Center to resolve paid feature access on load, independently lock Region and Full screen, and show `UPGRADE` when neither capability is available. Runtime feature gates remain authoritative behind the visual state.
- Added `docs/ENTITLEMENT_PROTOCOL.md` with the wire schema, cryptographic rules, cache behavior, deployment configuration, key rotation, and backend authority requirements.
- Added six security-focused tests using real generated P-256 keys: valid signing and expiry clamping, tampering/subject/expiry rejection, unknown-key and lifetime rejection, exact signed-cache offline fallback, tampered-cache rejection, and free/paid isolation.
- Verification: Release build passed with 0 warnings and 0 errors; full Release suite passed 78/78.
- Runtime verification: the unconfigured free-mode app produced a responsive Control Center in 2,792.2 ms with 117.2 MiB working set, meeting the 3,000 ms target, 5,000 ms hard limit, and 250 MiB memory budget, then closed cleanly.
- Caveat: the production identity/authentication service, purchase restore/checkout, authoritative entitlement issuer, public-key rotation deployment, and customer upgrade/account UX are not implemented yet.
- Next recommended production work: implement authenticated account/session and catalog/checkout/restore flows, then add consent/settings/accessibility and rehearse a signed Beta release on clean VMs.

### 2026-07-20 OAuth Account, Remote Plans, Checkout And Restore

- Added platform-neutral account and commerce contracts plus `CommerceCoordinator`. It serializes initialization/sign-in/checkout/restore/sign-out, exposes explicit UI states, validates checkout against the current public catalog, refreshes entitlements after account changes, and retains a signed-in session even when entitlement refresh is temporarily unavailable.
- Added immediate, generation-guarded feature-cache invalidation. Sign-out can no longer leave a previously cached paid grant usable until the normal five-minute refresh boundary, and an entitlement request already in flight cannot write the old account's grant back after sign-out.
- Added OAuth Authorization Code + PKCE S256 for a public Windows native client. Sign-in uses the system browser, a random ephemeral `127.0.0.1` callback, 256-bit verifier/state, exact callback path, constant-time state validation, bounded headers, three-minute deadline, standard snake-case token responses, and refresh-token rotation support. No desktop client secret is accepted or sent.
- Added current-user Windows DPAPI storage for access/refresh tokens and the minimal cached account profile, with bounded reads, plaintext buffer zeroing, same-directory atomic replacement, write-through flush, and deletion on sign-out.
- Added bounded HTTPS adapters for the remotely adjustable plan/offer catalog and commerce backend. Checkout sends bearer auth, a fresh idempotency key, and only authoritative `offerId`; restore is an authenticated idempotent reconciliation call. Hosted checkout URLs are bounded HTTPS URLs without embedded credentials.
- Bound entitlement requests to the OAuth session's server-derived subject and bearer token. Offline fallback may use the cached opaque subject, but the ES256 envelope must still match it; account switching therefore cannot reuse another subject's signed grant.
- Added an Account & plan card to Control Center with free-mode/signed-out/signed-in/error states, public offer browsing, secure browser sign-in/checkout, restore/refresh, sign-out, independent locked Region/Full screen controls, and scrolling at smaller window heights.
- Added `docs/ACCOUNT_COMMERCE.md` with endpoint schemas, environment configuration, PKCE/DPAPI behavior, backend authority, horizontal-scale guidance, and release tests. Updated entitlement and production architecture documentation for account-bound access.
- Account and commerce operations now link to component lifetime cancellation. Closing the app cancels a pending browser/loopback sign-in instead of leaving a three-minute background operation racing disposed composition resources.
- Added eight focused tests covering signed-out catalog UX state, authoritative known-offer checkout, immediate sign-out invalidation, remote price/period mapping, real loopback PKCE plus DPAPI ciphertext, bearer/idempotency/no-client-price checkout, direct entitlement cache invalidation, the in-flight refresh/sign-out race, and shutdown cancellation during browser sign-in.
- Verification: Release warnings-as-errors build passed with 0 warnings and 0 errors; full Release suite passed 86/86.
- Runtime verification: unconfigured free mode remained responsive in 2,606.1 ms with 122.1 MiB working set, meeting the 3-second target, 5-second hard limit, and 250 MiB memory budget, then closed cleanly.
- Visual QA caveat: `PrintWindow` returned a black frame because the Control Center intentionally uses `WDA_EXCLUDEFROMCAPTURE`; this preserves self-capture/privacy protection. XAML structure was inspected and runtime startup passed, but a human foreground visual check is still recommended for the new scrollable account card.
- External prerequisites: real OAuth registration/service, payment-provider catalog/checkout/webhooks, restore reconciliation, entitlement issuer/key rotation, and end-to-end sandbox payment tests are not present in this repository and remain required before public launch.
- Next recommended production work: add consent/privacy/settings and accessibility automation, then implement release promotion/attestation and rehearse signed Beta install/update/rollback/account purchase flows on clean VMs.

### 2026-07-20 Privacy Consent And Accessibility Baseline

- Added versioned privacy preferences with privacy-safe defaults: external text processing is off until explicitly confirmed, while bounded content-free local diagnostics can be independently disabled. Settings are strictly parsed, size-bounded, atomically persisted, and fall back safely when invalid.
- Wrapped the single production translator with `ConsentCheckingTextTranslator`. Popup and every realtime/batch mode now share an enforcement point immediately before provider invocation, so entitlement or a new UI route cannot bypass consent.
- Added `ConsentAwareOperationalTelemetry`; toggling local diagnostics takes effect on the next event without restart. Existing crash redaction and telemetry schema restrictions remain unchanged.
- Added Control Center privacy controls with a clear external-processing confirmation, immediate realtime stop on revocation, consent-aware Popup/Region/Full screen states, and user-safe recovery text.
- Added an accessibility baseline: UI Automation names/help text, polite live status, keyboard access keys, visible keyboard-focus borders, non-color-only state labels, natural tab navigation, and scrolling for the account/privacy column at the supported minimum height.
- Added `docs/PRIVACY_SETTINGS.md` and updated the telemetry/privacy and production architecture documents.
- Added three tests proving all batch translation calls are blocked before the provider until consent, local telemetry drops immediately when disabled, and privacy-safe defaults plus versioned persistence round-trip correctly.
- Verification: Release warnings-as-errors build passed with 0 warnings and 0 errors; full Release suite passed 89/89.
- Runtime caveat: the post-privacy WPF startup recheck could not run because the execution environment rejected further outside-sandbox usage after its quota was reached. The immediately preceding account UI build passed the same startup gate at 2,606.1 ms/122.1 MiB, and the current XAML compiles, but a fresh runtime/accessibility pass remains required.
- Visual caveat: capture protection prevents automated screenshots of the production window. Perform a human Narrator, keyboard-only, 200% DPI, High Contrast, minimum-size scrolling, consent-dialog focus, and revocation-during-realtime pass on the clean Beta VM.
- Next recommended production work: prepare release promotion/attestation and run a signed Beta install/update/rollback/account/checkout/consent rehearsal on clean Windows VMs with the real HTTPS services and signing identity.

### 2026-07-20 Signed Release Candidate And Beta Rehearsal Gate

- Added `.github/workflows/release-candidate.yml`, a manual candidate workflow that requires the source commit to be reachable from `main`, repeats Release build/tests/startup/dependency audit, and serializes releases per channel without canceling an in-progress signing run.
- Split validation from the protected signing job. `lexverse-beta`/`lexverse-stable` environment approval gates access to base64 PFX/password secrets; the certificate is imported non-exportable into the ephemeral current-user store and removed with the temporary PFX in an `always()` cleanup step.
- Hardened `package-msix.ps1` so production signing now requires exactly one matching certificate, accessible private key, current validity, exact X.500 publisher-subject bytes, and Code Signing EKU when EKUs are present, in addition to the existing SHA-256 timestamp/signature verification.
- Added `test-signed-release-candidate.ps1` to independently require expected artifacts, minimum package size, matching SHA-256, valid Authenticode signer and timestamp, exact publisher, channel/package/App Installer versions, exact HTTPS channel URIs, and no retained staging directory.
- Signed candidates now receive immutable commit/workflow/hash metadata and a GitHub `actions/attest@v4` SLSA provenance attestation plus offline Sigstore bundle before `actions/upload-artifact@v7` retention. The workflow deliberately does not replace the live customer App Installer pointer.
- Expanded `packaging/README.md` with protected-environment setup, signing-secret handling, attestation verification, and candidate/publish separation. Added `packaging/BETA_REHEARSAL.md` with artifact, install, privacy, account, sandbox checkout, signed-offline entitlement, accessibility, update, rollback, repair, uninstall, and evidence gates.
- Verification: both PowerShell release scripts parse successfully; release workflow YAML parses successfully; HTTPS, mandatory-signing, and unknown-certificate identity guards passed locally without creating release output.
- External caveat: no real signing certificate, protected GitHub environment secrets, HTTPS release host, payment/identity services, or clean VM were available, so the signed workflow and rehearsal remain unexecuted external gates rather than claimed passes.
- Next recommended production work: configure `lexverse-beta`, run the signed candidate workflow with sandbox services, execute `BETA_REHEARSAL.md` on clean VMs, fix all findings, then promote by uploading versioned artifacts first and the App Installer pointer last.

## Latest Scope Decision

### 2026-07-20 Production MVP Scope Correction

- Narrowed the launch product to two free workflows: Popup/F6 (including Quick text) and Document-region translation.
- Added `MvpProductPolicy` as the single tested free-feature matrix. Popup, Region, and Document mode no longer depend on account/payment configuration; Full screen and specialized processing modes remain deferred.
- Simplified the production Control Center to a single Document translation action. Full screen and Comic/Subtitle/Game selectors are hidden from the MVP experience.
- Account/plan UI is hidden when commerce is unconfigured. Existing commerce and signed-entitlement extension points remain dormant for future validation instead of becoming MVP launch dependencies.
- Kept privacy consent, local diagnostics controls, packaging/update, crash safety, accessibility, and performance gates because they directly affect a dependable customer MVP.
- The authoritative in/out list and clean-VM acceptance criteria are in `docs/MVP_PRODUCTION_SCOPE.md`.
- Verification: Release warnings-as-errors build passed with 0 warnings and 0 errors; the full Release suite passed 90/90, including the new exact MVP feature-matrix test.

## Next Recommended Work

1. Manually run `samples/LexVerse.Pipeline.Sample`, pick a document app, and confirm translated OCR blocks render as black boxes with white text.
2. Toggle `Black blocks` off/on while the pipeline is running and confirm both palettes remain readable and aligned.
3. Use the overlay font `-` and `+` controls while the pipeline is running and confirm the active overlay text resizes without drifting away from OCR boxes.
4. While the pipeline is running, open another app over the selected app and confirm the overlay hides; return focus to the selected app and confirm it reappears in the selected app bounds.
5. Run `samples/LexVerse.Pipeline.Sample` visually and confirm the single frameless Control Center opens with the expected header icons/window controls, dark left-panel inputs, yellow checkbox/slider controls, and without the removed top mode navigation.
6. Test Popup/F6 by highlighting text in another app, pressing F6, and confirming loading/result popup placement, close-on-outside-click, optional dragging, and clickable `Important terms` summaries appearing below the chips without hiding the translation.
7. Test Region at the current Windows display scale by dragging a screen area and confirming realtime OCR/translation overlays stay inside the selected physical region.
8. Test Full screen on the primary/nearest monitor and confirm overlays still align to source coordinates.
9. Confirm the F6 popup no longer renders source/translation comparison highlights; it should show only the translation and clickable `Important terms`.
10. Test ignored terms live with Popup/F6 and Region realtime, especially multi-word terms and game stats such as HP, MP, and EXP.
11. Open the new Settings gear panel and verify that English/Tiếng Việt app language switching, duplicate shortcut warnings, and Popup global hotkey re-registration behave correctly.
12. Live-test an all-caps comic speech bubble with `Keep acronyms / technical terms` enabled and confirm the text is translated instead of restored unchanged.
13. Live-test `OCR MODE = Comic / manga` on selected comic bubbles and full pages; confirm one bubble becomes one translated overlay block while separate bubbles stay separate.
14. Add image-based speech balloon segmentation later if text-geometry grouping is not enough for overlapping, irregular, or nested manga bubbles.
15. Replace the Wikipedia-first keyword explainer with a provider-backed AI annotation service if Bedrock/OpenAI-style popup understanding is added later; keep the current lookup path as a low-cost fallback.
16. Add a prompt-aware AI translator provider that consumes `TranslationPromptOptions.Instruction`; Google Cloud Translation V2 currently ignores the runtime prompt text.
17. Review the pushed `feature/pipeline-integration` branch on GitHub before opening or updating a PR.
