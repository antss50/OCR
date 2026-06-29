---
name: lexverse-project
description: Maintain and use LexVerse project memory. Use when working in the LexVerse repository, before changing code, reviewing architecture, handling git pull/merge/conflicts, updating documentation, implementing OCR/translation/overlay/pipeline features, or preserving project context for future Codex prompts.
---

# LexVerse Project

## Required First Steps

1. Read `docs/PROJECT_STATE.md` before making decisions.
2. Check git state with `git status --short --branch`, `git branch -vv`, and relevant remote/upstream information.
3. If the task involves code changes, fetch or pull the current branch when safe. If pulling needs network approval, request it. If local work or untracked files may be overwritten, stop and explain the risk before merging.
4. Inspect the touched modules before editing. Prefer the repo's existing Core/Infrastructure/OCR/Overlay/sample boundaries.

## Project Direction

LexVerse is a real-time screen translation app. The product goal is fast, accurate screen translation with precise, comfortable overlays.

The MVP target is document mode only, but every change should keep the design scalable for later modes:

- app/window translation
- selected-region translation
- full-screen translation
- document, game, movie/subtitle, manga/comic, and story/novel modes

Each mode may need its own OCR region policy, trigger policy, batching strategy, cache behavior, and translation prompt/algorithm. Do not hard-code document-only choices into shared contracts.

## Architecture Rules

- Keep domain contracts and mode-independent logic in `src/LexVerse.Core`.
- Keep OS capture, Win32, filesystem/config, and external providers in `src/LexVerse.Infrastructure`.
- Keep OCR engine implementation in `src/LexVerse.OCR`.
- Keep overlay rendering in `src/LexVerse.Overlay`.
- Keep runnable/manual visual demos in `samples/`.
- Keep tests in `tests/`; broaden tests when changing shared pipeline or geometry behavior.

For pipeline work, preserve this flow:

```text
capture -> region selection -> trigger/change policy -> OCR -> text normalization
-> translation cache -> translation -> coordinate mapping -> overlay frame
```

## Product Quality Bar

- Optimize for low latency without wasting OCR/translation calls.
- Prefer selected regions over full-screen OCR whenever possible.
- Cache normalized text by source language, target language, and mode.
- Keep overlay text boxes aligned to the original screen coordinates.
- Avoid visually noisy overlays; prioritize stable placement, readable sizing, and minimal flicker.
- For app UI or overlay behavior, test visually when feasible with screenshots/capture/manual sample apps.

## Git And Collaboration

- Treat the repo as shared by multiple people.
- Before code changes, check branch/upstream and current dirty files.
- Do not overwrite user or teammate changes.
- Resolve conflicts by reading both sides and preserving intent. If ownership is unclear or conflict risk is high, ask before choosing.
- Keep secrets and local files out of commits. `.env`, credentials, build outputs, Visual Studio state, and generated artifacts should stay ignored.
- If a branch has no upstream, set it to the matching `origin/<branch>` when the remote branch exists.

## Required Last Steps

After every code or architecture change:

1. Run the smallest meaningful verification available: build, targeted tests, sample app, or visual check depending on the change.
2. Update `docs/PROJECT_STATE.md` with:
   - what changed
   - current branch/git caveats
   - verification run and result
   - next recommended work
3. If the recurring workflow itself changes, update this skill too.

## Important Project Memory

`docs/PROJECT_STATE.md` is the canonical live handoff file. Load it for current status, decisions, risks, and next steps. `docs/FUTURE_OCR_TRANSLATION_PLAN.md` is older planning context and should be kept in sync or superseded by `PROJECT_STATE.md` when architecture changes.
