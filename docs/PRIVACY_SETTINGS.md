# Privacy Settings And Consent

LexVerse separates external text processing from local, content-free diagnostics. Neither setting can grant a paid feature; entitlements remain an independent authorization boundary.

## Defaults

- **External text processing: off.** Until the user explicitly enables it, the shared translator boundary rejects Popup, batch, and realtime provider calls before any text reaches the configured provider.
- **Local performance diagnostics: on.** These are bounded operational counters and timings stored only on the device. The user can disable new records immediately.

The Control Center explains that selected, typed, or OCR text is sent to the configured translation provider and asks for confirmation before enabling external processing. Disabling it stops the active realtime coordinator and blocks subsequent provider calls.

Settings are versioned and atomically stored at `%LOCALAPPDATA%\LexVerse\Settings\privacy.json`. Invalid, oversized, unknown-schema, or unknown-field files fall back to the privacy-safe defaults. The file contains only two booleans and a schema version; it contains no selected text, OCR text, translation, token, account identifier, window title, or provider credential.

## Enforcement boundaries

- `ConsentCheckingTextTranslator` wraps the configured external translator once in the composition root. Popup, Region, Full screen, Document, Comic, Subtitle, and Game dialogue paths share it.
- `ConsentAwareOperationalTelemetry` checks the current preference on every record. Turning diagnostics off does not require restart.
- Local exception records remain minimal essential crash diagnostics: exception type, HRESULT, timestamp, and one-way fingerprint. They exclude raw messages, stacks, content, prompts, pixels, coordinates, titles, tokens, and account identifiers.
- Feature access and consent are both required for paid remote modes. Consent never substitutes for entitlement, and entitlement never substitutes for consent.

## Accessibility

The production Control Center includes:

- UI Automation names/help text for the main window, quick-text input, plan selector, paid capture actions, consent controls, and application status;
- a polite live region for status changes;
- keyboard access keys for primary account and translation actions;
- visible keyboard-focus borders on custom buttons;
- natural tab order and a scrollable account/privacy column at smaller supported window sizes;
- explicit text states (`FREE MODE`, `SIGNED OUT`, `CONSENT`, `UPGRADE`, `READY`) rather than color-only meaning.

Before public release, verify Narrator, keyboard-only navigation, 200% DPI, Windows High Contrast, text scaling, minimum window size, and confirmation-dialog focus on clean VMs. Automated source/build checks cannot replace those assistive-technology passes.

## Future remote telemetry

No remote telemetry adapter exists. Adding one requires a separate, explicit upload preference; TLS; a documented processor/retention policy; deletion/export handling where legally required; schema allow-listing; regional routing; rate limits; and a review proving that captured or translated content cannot enter the payload.
