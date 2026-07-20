# Account And Commerce Integration

LexVerse is an OAuth public native client. It does not contain a client secret and never treats catalog price data as purchase authority.

## Native sign-in

The desktop app uses Authorization Code with PKCE `S256`:

1. Generate a 32-byte cryptographically random verifier and state.
2. Bind an ephemeral IPv4 loopback port and construct `http://127.0.0.1:{port}/oauth/callback/`.
3. Open the HTTPS authorization endpoint in the system browser.
4. Accept one bounded callback request, require the exact path and constant-time state match, then exchange the code with the original verifier.
5. Fetch the authoritative account profile using the bearer access token.

This follows [RFC 8252 native-app browser and loopback guidance](https://www.rfc-editor.org/info/rfc8252/) and [RFC 7636 PKCE S256](https://www.rfc-editor.org/info/rfc7636/). The authorization server must register LexVerse as a public native client, allow an ephemeral port for the exact loopback path, require PKCE, and never expect a distributed desktop secret.

OAuth token fields use their standard names: `access_token`, `refresh_token`, `token_type`, and `expires_in`. Access tokens refresh one minute before expiry. Refresh tokens and the minimal cached profile are encrypted with Windows DPAPI for the current Windows user and atomically stored at `%LOCALAPPDATA%\LexVerse\Account\session.dat`. Sign-out deletes this store and immediately invalidates all in-memory paid grants.

Deployment variables:

- `LEXVERSE_OAUTH_AUTHORIZATION_ENDPOINT`
- `LEXVERSE_OAUTH_TOKEN_ENDPOINT`
- `LEXVERSE_ACCOUNT_ENDPOINT`
- `LEXVERSE_OAUTH_CLIENT_ID`
- `LEXVERSE_OAUTH_SCOPES`, space-separated

All remote endpoints must be absolute HTTPS URLs without embedded credentials. Missing or invalid configuration keeps the app in explicit free mode.

The account endpoint returns bounded JSON:

```json
{
  "subjectId": "opaque-stable-account-id",
  "displayName": "Reader"
}
```

`subjectId` must be the same server-derived identifier signed into entitlement envelopes. Do not use a mutable display name as the entitlement subject.

## Remote catalog

`LEXVERSE_CATALOG_ENDPOINT` returns display metadata only. The client enforces bounded response, plan, offer, and feature counts, then maps it into the Core `ProductCatalog` model.

```json
{
  "revision": "catalog-42",
  "publishedAtUtc": "2026-07-20T12:00:00Z",
  "plans": [{
    "id": "pro",
    "displayName": "LexVerse Pro",
    "features": ["translation.region", "translation.full-screen"],
    "isPublic": true
  }],
  "offers": [{
    "id": "pro-monthly-vnd",
    "planId": "pro",
    "amount": 99000,
    "currencyCode": "VND",
    "billingPeriod": { "count": 1, "unit": "Month" },
    "trialPeriod": { "count": 7, "unit": "Day" },
    "isPublic": true
  }]
}
```

Prices, periods, visibility, and plan membership can change without a desktop release. The UI may show this data while signed out, but it cannot grant access from it.

## Checkout and restore

Deployment variables:

- `LEXVERSE_CHECKOUT_ENDPOINT`
- `LEXVERSE_RESTORE_ENDPOINT`
- `LEXVERSE_ENTITLEMENT_ENDPOINT`

Checkout sends an authenticated POST with a new `Idempotency-Key` and only the selected `offerId`:

```json
{ "offerId": "pro-monthly-vnd" }
```

The backend must reload that offer from its authoritative store mapping, confirm currency/amount/availability there, bind the checkout to the authenticated subject, and return a short-lived HTTPS hosted-checkout URI. The client opens it in the system browser; it never submits a client price.

Restore sends an authenticated POST with an empty JSON object. The backend reconciles store purchases/subscriptions idempotently and updates the account's entitlement state. LexVerse then refreshes the signed entitlement envelope. If that refresh is temporarily unavailable, the account remains signed in but paid execution still fails closed unless a current signed cache exists.

## Backend scale and safety

- Keep account, catalog, checkout, restore, and entitlement services stateless behind shared durable stores so instances can scale horizontally.
- Use provider webhook idempotency and a durable purchase ledger; desktop restore is reconciliation, not entitlement issuance based on client claims.
- Apply per-subject and per-IP rate limits, bounded deadlines, correlation IDs that exclude tokens/PII, and structured audit events.
- Store OAuth and signing keys in a managed secret/KMS system. Entitlement signing keys are separate from OAuth credentials.
- Validate hosted-checkout return URLs server-side. The desktop accepts only bounded HTTPS URLs without user-info.
- Test token revocation, account switching, duplicate checkout clicks, delayed webhooks, refunds, subscription expiry, restore on a second device, and entitlement-key rotation before public release.
