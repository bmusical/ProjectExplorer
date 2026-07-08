# KeyGen — Project Nest License Key Generator

Internal CLI for HxM Blazor Software LLC. Generates and verifies the ECDSA-signed
license keys that unlock Project Nest Explorer. **Internal use only — do not
distribute this tool.**

## One-time setup

A real keypair is already live: `public_key.pem` in this folder matches the
`PublicKeyPem` constant embedded in
`src/ProjectExplorer.Core/Services/LicenseManager.cs`, which is what ships
inside the app.

**Do not run `dotnet run -- setup` again.** It overwrites `public_key.pem` and
`private_key.pem` unconditionally and generates a brand-new keypair — since
the shipped app only trusts the public key baked into `LicenseManager.cs`,
that would silently invalidate every license key already sold. Only run
`setup` if you are deliberately rotating keys (and are prepared to reissue
every customer's license and ship an app update with the new public key).

If you ever do need a fresh keypair:
```
cd tools/KeyGen
dotnet run -- setup
```
- `private_key.pem` — **never commit, never share.** Keep it offline (password
  manager, encrypted drive). Anyone who gets it can mint valid licenses for
  anyone; if you lose it, you can no longer issue new licenses at all.
- `public_key.pem` — safe to keep in the repo. Paste it into `LicenseManager.cs`
  → `PublicKeyPem` so the app can verify keys offline (no network call).

## Generate a license key for a customer

```
cd tools/KeyGen
dotnet run -- generate --email customer@example.com
```

Optional: `--key path\to\private_key.pem` if the private key isn't in the
current folder.

Copy the full string printed under **"LICENSE KEY"** (format `payload.signature`)
and send it to the customer. They paste it into Project Nest Explorer's
registration dialog (`RegistrationDialog`) and click **Activate**.

## Verify a key

Useful when a customer says activation failed:

```
cd tools/KeyGen
dotnet run -- verify --license "PASTE_THEIR_KEY_HERE"
```

Uses `public_key.pem` by default. Reports `VALID` (with the decoded email/date)
or `INVALID`.

## How a key works

Payload: `email|FULL|yyyy-MM-dd`, signed with ECDSA P-256 / SHA-256, each part
base64url-encoded, joined as `payload.signature`. It's a perpetual "full"
unlock — no expiry, no per-key revocation. If a single key leaks or gets
shared around, the only way to shut it off is rotating the whole keypair,
which invalidates every other customer's license too. Treat `private_key.pem`
as the most sensitive secret in this project.

## Free-tier limits (for support conversations)

Unlicensed installs are capped at **3 projects** / **25 leaf nodes** total
(FolderReferences + WebResources + FileReferences; Collections don't count). See
`LicenseManager.FreeProjectLimit` / `FreeLeafNodeLimit`.
