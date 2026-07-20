# LexVerse Production MVP Scope

## Product goal

Ship a dependable Windows MVP for the two workflows customers need first:

1. Translate highlighted text with the Popup/F6 workflow.
2. Select a document region and keep its translated overlay updated.

Both workflows are free in the MVP and work without an account, catalog, checkout, or entitlement service.

## Launch scope

- Popup/F6 and Quick text translation.
- Document-region OCR, translation, and overlay.
- Explicit consent before text is sent to the configured translation provider.
- Content-free local diagnostics that customers can disable.
- Clear provider/OCR/error recovery states.
- Responsive startup, bounded memory, and deterministic shutdown.
- MSIX/App Installer packaging, update channels, crash redaction, CI build/test, and release verification.
- Keyboard navigation and the current accessibility baseline.

## Deferred until after MVP validation

- Full-screen translation.
- Comic/manga, subtitle, and game-dialogue processing profiles.
- Account sign-in, paid plans, checkout, restore, and purchase UX.
- Production commerce backend and paid entitlement issuance.
- Advanced remote telemetry and growth/commerce analytics.

The code-level extension points for these capabilities may remain dormant. They are not launch dependencies and must not appear in the default unconfigured customer experience.

## Release acceptance

- A new user can install or update the signed package without manual runtime setup.
- With no commerce configuration, the Control Center shows only the MVP workflows and privacy controls.
- Popup and document-region translation never report an upgrade requirement.
- Translation cannot send text before explicit consent.
- Missing credentials, provider failure, OCR failure, or network loss produces a recoverable message rather than a crash.
- Release build and all automated tests pass with zero warnings.
- A clean Windows VM passes install, first launch, consent, Popup, document-region, update, repair, uninstall, keyboard, high-DPI, and shutdown checks.
