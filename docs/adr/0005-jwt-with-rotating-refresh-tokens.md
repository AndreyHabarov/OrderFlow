# ADR 0005: Short-lived JWT access tokens with rotating refresh tokens

- Status: accepted
- Date: 2026-10-07

## Context
The API needs authentication and two roles (`Customer`, `Admin`). The React app and later services will call the API, and a stolen token must have a limited blast radius.

## Options considered
1. **Cookie sessions (server-side).** Simple and revocable, but ties the API to browsers and needs shared session storage once it scales out.
2. **Long-lived JWT only.** Stateless, but cannot be revoked and a leak is dangerous for weeks.
3. **Short-lived JWT (15 minutes) plus a single-use refresh token stored hashed in PostgreSQL, rotated on every refresh.** Stateless checks on every request, revocation through the refresh token, reuse detection.
4. **ASP.NET Core Identity or an external identity provider.** Powerful, but hides the mechanics this project is meant to teach, and an identity provider is heavy for a local compose setup.

## Decision
Option 3.
- Access token: HS256 JWT with `sub`, `email`, `role`; 15 minutes. The signing key must be at least 32 characters, comes from user-secrets or the `JWT_SIGNING_KEY` environment variable, and the API refuses to start without it (`ValidateOnStart`).
- Refresh token: 32 random bytes, only its SHA-256 is stored. Each use revokes the token and links it to its replacement. **Presenting an already revoked token revokes every token of that user** (a rotated token being used again means it was copied).
- Passwords are hashed with ASP.NET Core's `PasswordHasher` (PBKDF2 with a per-hash salt). Unknown emails still spend verification time, and login errors are identical for "wrong password" and "unknown email".
- Registration always creates a `Customer`; the first `Admin` comes from the development seeder (`Seed:AdminEmail`, `Seed:AdminPassword`).
- Claim names are kept as issued (`MapInboundClaims = false`), `ICurrentUser` reads `sub`.

## Consequences
- An access token stays valid until it expires even after logout (up to 15 minutes). Acceptable here; a denylist would add state.
- Reuse detection also logs out an honest user whose client lost a refresh response and retried. That is the intended trade-off.
- Symmetric signing means every service that validates tokens holds the key. With separate services in stage 2 the choice between sharing the key and asymmetric keys (RS256/ES256 with a public key) must be revisited.
- Refresh tokens are returned in the response body. A browser client should keep them in memory or an HttpOnly cookie; the React app (S1-09) decides.
- No rate limiting on login yet (candidate for stage 5).

## Interview angle
"Why a short access token and a refresh token? What is refresh token rotation and what does reuse detection protect against? Why store only a hash? HS256 vs RS256? How do you avoid user enumeration on login?"
