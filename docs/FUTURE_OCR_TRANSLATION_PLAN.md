# LexVerse Future OCR And Translation Plan

This file is the handoff note for future development. If this thread context is lost, ask Codex to read this file first.

## Current State

LexVerse currently has a working OCR demo flow:

```text
Windows Graphics Capture -> frame change check -> Windows OCR -> WPF demo display
```

The solution builds successfully, but it is not yet a complete real-time screen translator. Translation, overlay rendering, user-selectable OCR regions, mode-specific OCR behavior, and real tests still need to be added.

## Direction

Do not hard-code one OCR behavior into the UI. Keep the pipeline flexible so each use case can choose a different region strategy, trigger strategy, and interval.

Target pipeline:

```text
Capture
-> Select OCR region
-> Decide whether OCR should run
-> OCR
-> Normalize text
-> Translation cache lookup
-> Translate only when cache misses
-> Overlay/display result
```

## Modes To Support Later

### Subtitle Mode

- User selects the subtitle area.
- OCR runs on a short interval, around 500-700 ms.
- Translation cache prevents translating the same subtitle repeatedly.
- Prefer not to use full-screen percentage change detection, because subtitle changes may affect only a tiny part of the screen.

### Game Dialogue Mode

- User selects the dialogue box or text area.
- OCR interval can be faster, around 300-500 ms.
- Small changes inside the selected region should be enough to trigger OCR.
- Cache repeated dialogue lines.

### Document Or Manga Mode

- User selects a page area, panel, speech bubble, or text block.
- OCR can be manual, interval-based, or triggered after the image becomes stable.
- Prioritize accuracy over speed.
- Cache normalized text so zooming, panning, or revisiting a panel does not call translation again for the same content.

### Full Screen Mode

- OCR the full capture area.
- Use sparingly because it is the heaviest mode.
- Prefer slower interval or stronger filtering.
- Cache translations here too.

## Contracts Added For This Direction

These Core contracts were added so future features can plug in without rewriting the whole app:

```text
src/LexVerse.Core/Pipeline/OcrProcessingMode.cs
src/LexVerse.Core/Pipeline/OcrRegion.cs
src/LexVerse.Core/Pipeline/IOcrRegionProvider.cs
src/LexVerse.Core/Pipeline/IOcrTriggerPolicy.cs
src/LexVerse.Core/Pipeline/RealtimeTranslationOptions.cs
src/LexVerse.Core/Ocr/IOcrTextNormalizer.cs
src/LexVerse.Core/Ocr/WhitespaceOcrTextNormalizer.cs
src/LexVerse.Core/Translation/ITranslationService.cs
src/LexVerse.Core/Translation/ITranslationCache.cs
src/LexVerse.Core/Translation/TranslationCacheKey.cs
src/LexVerse.Core/Translation/InMemoryTranslationCache.cs
```

## How Future Code Should Use These Contracts

UI should only collect user choices:

```text
mode
source language
target language
selected OCR region
start/stop
```

Pipeline/service code should own the actual work:

```text
capture loop
OCR scheduling
text normalization
cache lookup
translation call
overlay update
```

Translation cache keys should use normalized text:

```text
normalized source text + source language + target language + mode
```

This avoids translating the same sentence again because of whitespace, line breaks, or repeated frames.

## Recommended Next Steps

1. Add a real `LexVerse.Translation` project.
2. Implement a mock translation service first, then add a real provider later.
3. Refactor the WPF demo so it calls a pipeline service instead of owning the whole OCR loop.
4. Add user-selectable OCR regions.
5. Add overlay rendering in a separate `LexVerse.Overlay` project.
6. Add tests for:
   - `WhitespaceOcrTextNormalizer`
   - `InMemoryTranslationCache`
   - `ExactFrameChangeDetector`
   - future trigger policies
   - future pipeline cache behavior
7. Add mode implementations:
   - subtitle interval policy
   - game dialogue interval or region-change policy
   - document/manual policy
   - full-screen slower interval policy

## Important Performance Notes

Avoid running OCR on the full screen whenever possible. OCR should usually run on a selected region. Pixel comparison across the whole frame is expensive, and a tiny subtitle change can be missed if the threshold is based on the whole screen.

Prefer this for movie/game scenarios:

```text
selected region + interval OCR + normalized text cache
```

This is simple, avoids missing small text changes, and prevents repeated translation API calls.
