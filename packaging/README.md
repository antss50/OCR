# LexVerse Windows distribution

LexVerse uses one self-contained MSIX package per release channel. Windows App Installer owns installation, integrity verification, background/on-launch updates, and repair. Stable and Beta use separate package identities, so a tester can keep both installed without Beta replacing Stable.

## Build a package

Run from the repository root on Windows with the .NET SDK and Windows SDK packaging tools installed:

```powershell
.\scripts\package-msix.ps1 `
  -Version 1.0.0.0 `
  -Channel stable `
  -ReleaseBaseUri https://downloads.example.com/lexverse `
  -Publisher 'CN=Your verified certificate subject' `
  -PublisherDisplayName 'Your company' `
  -CertificateThumbprint 'CERTIFICATE_SHA1_THUMBPRINT'
```

The certificate subject must exactly match the `Publisher` value. The script signs with SHA-256, timestamps the signature, verifies it, and writes the MSIX, `.appinstaller`, and SHA-256 checksum under `artifacts/release/<channel>/<version>/win-x64/`.

For a local or CI packaging smoke test only:

```powershell
.\scripts\package-msix.ps1 `
  -Version 1.0.0.0 `
  -Channel beta `
  -ReleaseBaseUri https://downloads.example.com/lexverse `
  -Unsigned
```

Unsigned packages are deliberately opt-in and are not installable as production releases.

## Build a protected signed candidate in GitHub Actions

The manual `LexVerse signed release candidate` workflow accepts package/channel versions, channel, HTTPS release root, exact publisher subject, and publisher display name. It will only build a commit reachable from `main`, then repeats Release build, tests, startup/memory budgets, and dependency audit before signing.

Create protected GitHub environments named `lexverse-beta` and `lexverse-stable`. Configure required reviewers, prevent self-review, restrict deployment branches to `main`, and store these environment secrets:

- `SIGNING_PFX_BASE64`: base64 of the code-signing PFX;
- `SIGNING_PFX_PASSWORD`: PFX password.

The PFX is imported into the ephemeral runner's current-user certificate store, never made exportable, and removed in an `always()` cleanup step. The packaging script requires an accessible private key, current certificate validity, an exact X.500 subject match with `Publisher`, and Code Signing EKU when the certificate declares EKUs.

The workflow does not publish to the customer channel. It creates a reviewable immutable candidate containing the signed MSIX, checksum, App Installer pointer, release metadata, and an offline Sigstore provenance bundle. Verify provenance with GitHub CLI before publishing:

```powershell
gh attestation verify .\LexVerse-beta-1.2.0.0-x64.msix `
  --repo thisishiu/LexVerse-Real-time-screen-translate
```

This separation ensures environment approval grants access to signing material but does not silently replace the live channel pointer.

## Publish atomically

1. Upload the versioned `.msix` and checksum first.
2. Verify the uploaded MSIX hash matches the generated `.sha256` file.
3. Upload `LexVerse-<channel>.appinstaller` last. It is the channel pointer and must never reference an artifact that is not already available.
4. Keep older versioned MSIX files available for rollback and repair.

The App Installer checks on launch and in the background. `ForceUpdateFromAnyVersion` permits an operator to point a channel manifest at a previously signed version for rollback. During rollback, keep the channel-manifest version increasing even though the package version decreases:

```powershell
.\scripts\package-msix.ps1 `
  -Version 1.4.0.0 `
  -AppInstallerVersion 1.5.0.1 `
  -Channel stable `
  -ReleaseBaseUri https://downloads.example.com/lexverse `
  -Publisher 'CN=Your verified certificate subject' `
  -CertificateThumbprint 'CERTIFICATE_SHA1_THUMBPRINT'
```

Stable and Beta have independent identities and update streams.

## Production requirements

- Use HTTPS hosting with stable URLs and the correct MIME types for `.msix` and `.appinstaller`.
- Sign every public package using Microsoft Store signing, Microsoft Artifact Signing, or a trusted code-signing certificate. Timestamp all non-Store signatures.
- Keep signing material outside the repository. CI should import a short-lived certificate or use a managed signing service; never pass a PFX password in source or commit a PFX.
- Publish release notes and retain at least the current and previous known-good package per channel.
- Test install, update, downgrade rollback, repair, uninstall, settings preservation, and launch on a clean supported Windows VM before promoting Beta to Stable.
- Follow `packaging/BETA_REHEARSAL.md` and retain its evidence bundle for every promoted build.
