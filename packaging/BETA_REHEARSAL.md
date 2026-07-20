# Signed Beta Rehearsal

This runbook is a release gate, not an informal smoke test. Record the candidate commit, workflow run, package/appinstaller versions, SHA-256, signer, timestamp certificate, tester, VM image, Windows build, DPI, and result for every step.

## Preconditions

- Candidate was produced by `.github/workflows/release-candidate.yml` from a commit reachable from `main`.
- `test-signed-release-candidate.ps1` passed and the GitHub/Sigstore attestation verifies for the repository.
- The `lexverse-beta` environment approval was performed by someone other than the workflow initiator.
- Versioned MSIX and checksum are staged on HTTPS with correct MIME types, but the live Beta `.appinstaller` pointer has not changed yet.
- OAuth, catalog, sandbox checkout, restore, and signed entitlement endpoints use the intended Beta configuration and keys.
- The previous known-good signed Beta package and its monotonically newer rollback App Installer version are retained.
- Two clean supported Windows VMs are available: one standard 100% DPI and one 200% DPI with Narrator/High Contrast testing.

## Artifact verification

1. Download the candidate from the intended HTTPS host, not from the runner workspace.
2. Compare its SHA-256 with both the candidate metadata and checksum file.
3. Run `Get-AuthenticodeSignature`; require `Valid`, the intended publisher, and a timestamp certificate.
4. Run `gh attestation verify` against the downloaded MSIX and repository.
5. Parse the App Installer file and confirm channel identity, publisher, package version, monotonically increasing App Installer version, and exact HTTPS URIs.

## Clean install and first run

1. Snapshot VM A, then install through the staged Beta `.appinstaller` file.
2. Confirm Windows shows the expected verified publisher and Beta identity.
3. Launch without provider/account configuration failure. Require a responsive Control Center within 5 seconds and initial working set below 250 MiB.
4. Confirm Popup is marked `CONSENT`, Region/Full screen are locked or consent-blocked, and no text leaves the device before external processing is accepted.
5. Decline the consent dialog once; confirm the checkbox reverts and provider test logs show no request.
6. Accept consent, restart, and confirm the versioned preference persists.
7. Toggle local diagnostics off; confirm no new operational events are appended. Re-enable and confirm content-free events resume.

## Account, purchase, restore and revocation

1. Sign in through the system browser. Confirm callback returns to LexVerse, no embedded web view appears, and the account label contains only the intended display name.
2. Verify a refresh token is not visible in files/process command lines and `session.dat` is DPAPI ciphertext bound to the Windows user.
3. Compare displayed plan/price/period with the Beta catalog service.
4. Start sandbox checkout. Confirm one backend checkout is created for repeated UI clicks/idempotency key, and the provider—not the desktop amount—determines charge details.
5. Complete checkout and run Restore/refresh. Confirm the signed entitlement subject matches the signed-in account and only purchased features unlock.
6. Disconnect network and restart. Confirm the still-current signed envelope works, then advance/test beyond expiry and confirm paid access fails closed.
7. Sign out while an entitlement refresh is delayed. Confirm paid access locks immediately and the late response cannot restore it.
8. Sign in as a second account. Confirm the first account's cached envelope is rejected.
9. Reconcile a refund/cancellation in the sandbox backend and confirm restore/short envelope lifetime removes access.

## Translation and accessibility

1. Exercise quick text, F6 selected text, Region, and Full screen with real Beta provider credentials and consent.
2. Confirm consent revocation during realtime stops capture/translation output and subsequent provider calls.
3. Check multi-monitor physical-pixel alignment, 100%/200% DPI, OCR language-pack failure, provider timeout, and offline recovery.
4. Complete keyboard-only navigation. Every interactive control must be reachable with a visible focus indicator and no focus trap.
5. With Narrator, confirm account state, plan selector, privacy controls, locked/consent/upgrade states, and polite status changes are announced meaningfully.
6. Enable Windows High Contrast and text scaling; record any clipped, low-contrast, or unreachable content. Verify minimum-window scrolling.

## Update, rollback, repair and uninstall

1. Install previous known-good Beta on VM B and create non-secret settings for languages/privacy.
2. Publish candidate MSIX/checksum, verify them remotely, then replace the Beta `.appinstaller` pointer last.
3. Launch VM B; confirm update detection, successful activation, settings preservation, account session behavior, and no duplicate package identity.
4. Repair/reset as appropriate and verify expected settings/token behavior is documented.
5. Publish a higher App Installer version pointing to the retained previous signed package. Confirm `ForceUpdateFromAnyVersion` rollback succeeds and settings remain compatible.
6. Restore the candidate pointer with another higher App Installer version and update again.
7. Uninstall. Confirm the package is removed cleanly; document whether `%LOCALAPPDATA%\LexVerse` user data is retained and provide a user-facing deletion path before Stable.

## Promotion decision

Promotion requires all mandatory checks above, no unresolved P0/P1 defects, no content/token leakage, verified update and rollback, approved privacy/accessibility evidence, and an identified rollback owner. Upload versioned artifacts first and the live App Installer pointer last. Never overwrite a versioned MSIX.

Archive screenshots/photos (capture protection may block programmatic screenshots), logs with secrets/content removed, hashes, signature output, attestation result, provider sandbox receipts, and the signed-off checklist. A failed rehearsal produces a new package or App Installer version; do not mutate the rejected candidate.
