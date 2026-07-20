# LexVerse Production Architecture

This document turns the production goal into architectural boundaries and an incremental delivery order. `PROJECT_STATE.md` remains the live handoff log.

## Product principles

- A feature is a stable product capability, not a button or a class name.
- Product plans assign feature keys; runtime code asks for an entitlement before executing a paid capability.
- Checkout price, renewal date, trial duration, refunds, and entitlement issuance are server/store authority. The desktop client may display a cached catalog but must not grant access from that catalog.
- OCR, translation, capture, overlay, billing, updates, and telemetry depend on contracts rather than concrete providers.
- The production app is assembled in `src/LexVerse.App`; `samples/` remain diagnostics and visual demos.
- Local-first processing and bounded caches are preferred for latency and privacy. Remote calls must have timeouts, cancellation, retry budgets, and observable failure states.

## Target boundaries

```text
LexVerse.App (WPF shell and composition root)
  -> Product/Application services
     -> LexVerse.Core contracts and policies
  -> Infrastructure adapters
     -> Windows capture, OCR, translation providers, entitlement API,
        signed offline cache, updater, diagnostics
  -> LexVerse.Overlay
```

UI code only coordinates user intent and renders state. It must not construct providers, run billing rules, or contain the realtime loop.

## Commercial model

Feature keys are stable identifiers such as `translation.popup`, `translation.region`, `translation.full-screen`, `mode.document`, and `mode.comic`. A remotely configurable product catalog groups those keys into plans. An independently refreshed entitlement snapshot grants individual keys to a subject for an optional UTC validity window.

The Core catalog models display offers separately from plans. A plan may have multiple remote offers with different prices, currencies, billing periods, and trial periods. A billing adapter maps store offers to catalog plan IDs and returns verified entitlements. This allows price, currency, promotion, billing interval, and plan duration to change without releasing a new desktop binary. The offer is display metadata only: checkout must resolve its ID again at the authoritative store/backend.

Runtime flow:

```text
user action -> feature access check -> allowed: execute application use case
                                   -> denied: explain/upgrade/refresh UX
```

Backend operations must repeat authorization; hiding a desktop button is not a security boundary.

## Delivery milestones

1. **Product foundation**
   - Stable feature keys, versioned plans, time-bound entitlements, provider contract, tests.
   - Decide initial free/trial/paid feature matrix outside the binary.
2. **Production application shell**
   - Convert `LexVerse.App` from console placeholder to WPF composition root.
   - Move Control Center behavior out of `Pipeline.Sample` into application services and view models.
   - Add dependency injection, structured configuration, global exception handling, and single-instance behavior.
3. **Feature modules**
   - Package Popup, Region, Full screen, Document, and Comic as independently registered modules.
   - Each module declares its feature key, commands, settings, and required providers.
4. **Distribution and updates**
   - Produce signed Windows packages and a stable/beta release channel.
   - Add signed update manifests, atomic update/rollback, release notes, and compatibility checks.
5. **Operations and scale**
   - Keep the desktop pipeline horizontally independent; scale remote translation, identity, entitlement, and catalog services statelessly.
   - Add correlation IDs, privacy-aware crash reports, latency/error metrics, rate limits, circuit breakers, and cost budgets.
6. **Quality and UX**
   - CI gates for build, unit/integration tests, packaging smoke tests, dependency/security scanning, and performance budgets.
   - Measure capture-to-overlay latency, OCR/translation cache hit rate, memory stability, overlay flicker, startup time, update success, and task completion UX.

## Initial production gates

- No paid use case runs without an explicit feature access decision.
- No secrets or service-account JSON ship in the client package.
- All remote calls have cancellation and bounded timeouts.
- App startup and background loops have observable, user-safe failure handling.
- Update artifacts and entitlement caches are cryptographically verified.
- A failed update can roll back without losing user settings.
- Settings and catalog schemas are versioned and migration-tested.
- Critical UI flows are keyboard accessible and verified at common Windows DPI scales.

## Current implementation status

- `LexVerse.Application` now owns entitlement refresh/cache, feature gating, module registration, the Popup translation use case, and cancellable realtime orchestration.
- `LexVerse.App` is a WPF composition root rather than a console placeholder.
- Popup selection and quick-text translation run through the same entitlement gate; the Google provider initializes only on first use.
- Region and Full screen run through a registered realtime module. The application coordinator owns authorization, start/restart/stop, cadence, state, partial/final output, and deterministic cleanup; Windows capture/OCR/pipeline creation remains behind an Infrastructure factory, and WPF only renders neutral frame updates.
- Document, Comic/manga, Subtitle, and Game dialogue are runtime processing profiles selected by the realtime module rather than separately constructed pipelines. Region coordinates are clipped and converted from physical screen space into captured-frame pixels in Core.
- LexVerse control, popup, and overlay windows opt out of Windows display capture so the product does not OCR its own UI. Overlay items are reconciled in place to reduce frame-to-frame flicker.
- Popup is the explicit free tier. Region and Full screen require an ES256-signed remote entitlement or a still-valid signed offline envelope; paid grants are no longer bundled in the binary. The verifier pins P-256 public keys by `keyId`, binds claims to the authenticated server-derived subject, limits offline lifetime to 72 hours, and clamps every grant to envelope expiry. Missing identity/backend configuration fails closed for paid features without preventing startup.
- Account and commerce orchestration now lives in Application contracts. Infrastructure implements OAuth Authorization Code + PKCE S256 through the system browser and an ephemeral loopback callback, DPAPI current-user session storage, bounded remote catalog mapping, bearer-authenticated idempotent checkout creation, purchase restore, and account-bound entitlement requests. Sign-out invalidates paid access immediately.
- Control Center exposes account status, remotely supplied public offers, hosted checkout, restore/refresh, and sign-out. Checkout sends only `offerId`; backend/store price and purchase state remain authoritative.
- External text processing is consent-denied by default and enforced once at the shared translator boundary used by Popup and all realtime modes. Versioned privacy settings also control local content-free diagnostics at runtime. Control Center exposes explicit consent/upgrade/free-mode states plus UI Automation names, a status live region, keyboard access keys, visible focus borders, and a scrollable account/privacy surface.
- `scripts/package-msix.ps1` creates a self-contained ReadyToRun MSIX plus channel App Installer and checksum. Stable/Beta have separate identities; HTTPS and signing are mandatory unless unsigned mode is explicitly selected for CI/local validation. The App Installer version can advance independently of the package version so a channel can roll back to a previously signed package.
- `.github/workflows/ci.yml` builds with warnings as errors, runs the complete test suite, audits direct/transitive NuGet advisories, verifies packaging safety guards, and creates/validates an unsigned MSIX fixture on Windows. `global.json` pins the SDK feature band and Dependabot monitors NuGet plus workflow actions weekly.
- `.github/workflows/release-candidate.yml` is a manually approved signed-candidate pipeline restricted to commits reachable from `main`. Protected Beta/Stable environments gate ephemeral PFX access; packaging validates certificate identity/EKU/private key/validity, then verifies signature/timestamp/hash/update pointers and emits immutable metadata plus GitHub/Sigstore provenance. It deliberately stops before live publication.
- `OperationalEvent` is a strongly typed, content-free telemetry boundary. Startup, Popup, realtime sessions, sampled frames, stage latency, cache counts, budget violations, and memory are written asynchronously to a bounded rotating local JSONL sink; no remote upload is enabled. Crash records omit raw messages/stacks and retain only type/HRESULT plus a one-way fingerprint.
- CI enforces a responsive Control Center within a 5-second hard budget (3-second target) and an initial 250 MiB working-set budget. Runtime mode-specific budgets warn and emit metrics without interrupting translation.
- A real code-signing identity and download host, deployed OAuth/catalog/checkout/restore/entitlement services plus key rotation, payment-provider webhook/reconciliation tests, protected GitHub release environments/secrets, and the clean-VM signed rehearsal remain before a public production release. The repository now defines promotion/attestation and credentialed/privacy/accessibility evidence gates, but external evidence has not yet been produced.
