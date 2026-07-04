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

- Current branch: `feature/pipeline-integration`.
- `feature/ui-new-features` was created from `feature/pipeline-integration` at `7a6747f`, then committed UI/new feature work as `dc02f3b`.
- `feature/pipeline-integration` was fast-forward merged to `dc02f3b` with the UI/new feature work, excluding the experimental source/translation alignment feature.
- `feature/pipeline-integration` tracks `origin/feature/pipeline-integration` and was pushed to GitHub after the UI/new feature merge.
- `feature/ui-new-features` still exists locally at `dc02f3b` and has no upstream.
- `codex/overlay-ocr-block-contrast` still exists locally at the merged overlay commit and has no upstream.
- Latest working tree changes are this post-push project state update.
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
10. Replace the Wikipedia-first keyword explainer with a provider-backed AI annotation service if Bedrock/OpenAI-style popup understanding is added later; keep the current lookup path as a low-cost fallback.
11. Review the pushed `feature/pipeline-integration` branch on GitHub before opening or updating a PR.
