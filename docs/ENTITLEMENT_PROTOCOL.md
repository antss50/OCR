# Signed Entitlement Protocol

LexVerse treats the entitlement service as the authority for paid desktop capabilities. The product catalog may describe plans and prices, but it never grants access.

## Envelope

The API returns at most 256 KiB of UTF-8 JSON:

```json
{
  "schema": "lexverse.entitlements.v1",
  "keyId": "prod-2026-07",
  "algorithm": "ES256",
  "payload": "base64url(raw UTF-8 payload JSON)",
  "signature": "base64url(64-byte IEEE-P1363 signature)"
}
```

`signature` is ECDSA P-256/SHA-256 over the exact decoded `payload` bytes. It is not a signature over reserialized JSON. The client selects a pinned public key by `keyId`; private signing keys must remain in the entitlement service or its key-management system.

The decoded payload schema is:

```json
{
  "schemaVersion": 1,
  "subjectId": "account-or-installation-subject",
  "revision": "monotonic-or-unique-server-revision",
  "issuedAtUtc": "2026-07-20T12:00:00Z",
  "notBeforeUtc": "2026-07-20T12:00:00Z",
  "expiresAtUtc": "2026-07-23T12:00:00Z",
  "grants": [
    {
      "feature": "translation.region",
      "startsAtUtc": null,
      "endsAtUtc": null,
      "source": "subscription"
    }
  ]
}
```

Unknown JSON fields, schemas, algorithms, keys, subjects, invalid signatures, oversized data, malformed grants, future claims, expired claims, and envelopes valid for more than 72 hours are rejected. Every grant is clamped to the signed envelope expiry, including a grant whose `endsAtUtc` is null.

## Online and offline behavior

The client requests the envelope over HTTPS with an eight-second deadline that covers headers and content. A verified response is immediately usable; persisting it is best effort. The exact signed envelope is stored under `%LOCALAPPDATA%\LexVerse\Entitlements` using a same-directory temporary file and atomic replacement.

If the request fails, the cached envelope is read with the same size limit and fully verified again against the current subject, pinned key, and clock. There is no unsigned offline format and no grace period after `expiresAtUtc`.

Popup translation is the explicit free tier. Region and Full screen fail closed when neither a current remote envelope nor a still-valid signed cache is available.

## Desktop configuration

The current adapter reads these deployment-time variables:

- `LEXVERSE_ENTITLEMENT_ENDPOINT`: absolute HTTPS URL.
- `LEXVERSE_ENTITLEMENT_KEY_ID`: pinned public-key identifier.
- `LEXVERSE_ENTITLEMENT_PUBLIC_KEY_PEM`: P-256 public key PEM; escaped `\n` is accepted.

The subject and bearer token now come from the OAuth account session described in `ACCOUNT_COMMERCE.md`; they are not deployment-time identity variables. Offline fallback can use the cached session's opaque subject even when token refresh is temporarily unavailable, but the signed entitlement must still match that subject.

Missing or invalid configuration does not prevent app startup. It disables paid capabilities while leaving the free tier usable. Production deployment should support overlapping pinned keys during rotation.

## Backend requirements

- Authenticate the caller and derive `subjectId` server-side.
- Recompute grants from authoritative purchase/subscription state; never accept client-supplied plan or price as authority.
- Keep signing private keys out of desktop binaries, logs, CI artifacts, and source control.
- Rotate keys by shipping the next public key before issuing envelopes with its `keyId`.
- Rate-limit issuance, audit revisions without logging feature content beyond necessary product metadata, and revoke by shortening envelope lifetime or ceasing renewal.
