# KeyGen — License Key Generator

Internal console tool for **Project Nest Explorer**'s license system. Not shipped to customers —
this is the "key factory" you run yourself to mint license keys.

## How the license system works

Project Nest Explorer is freemium: **Free = 3 projects, 25 leaf references** (Collections don't
count). A license key removes those limits. Verification happens **entirely offline** — the app
embeds a public key and checks signatures locally, so there's no license server to run or pay for.

- A license key is an **ECDSA P-256 signed** payload of the form `email|FULL|yyyy-MM-dd`.
- The key string shipped to a customer is `base64url(payload).base64url(signature)`.
- The app (`src/ProjectExplorer.Core/Services/LicenseManager.cs`) holds the **public** key and can
  only *verify* signatures, never create them.
- This tool holds the **private** key and is the only thing that can *mint* valid keys.

## Commands

Run all commands from this directory (`tools/KeyGen`):

### 1. `setup` — generate your production keypair (run once, ever)

```bash
dotnet run -- setup
```

Writes `public_key.pem` and `private_key.pem` to the current directory and prints the public key
to the console.

- Copy the printed public key into `LicenseManager.cs` as the `PublicKeyPem` constant. **If that
  constant is ever set to the literal string `"DEVELOPMENT_KEY_PLACEHOLDER"`, the app accepts any
  correctly-formatted string as a valid key — dev mode only, never ship it.** (For the current
  build this has already been done: the real key was embedded in commit `5a95f73`.) If you ever
  need to rotate keys — e.g. the private key is compromised — regenerate here and repeat this step.
- `*.pem` is already git-ignored (see repo `.gitignore`). Never commit `private_key.pem`.
- **Guard `private_key.pem` like cash.** Anyone who has it can mint free keys for your product.
  Store it offline (encrypted USB, password manager attachment, or a secrets vault) with at least
  one backup in a separate location. If you lose it, you can never sign a key that validates
  against the public key you've already shipped — you'd have to rebuild and re-release with a new
  keypair.

### 2. `generate` — mint a key for a customer (run per sale)

```bash
dotnet run -- generate --email customer@example.com
# or, if the private key isn't in the current directory:
dotnet run -- generate --email customer@example.com --key /path/to/private_key.pem
```

Prints a `LICENSE KEY` line — copy/paste that value and deliver it to the customer. They paste it
into **Help ▸ Register / License…** in the app.

### 3. `verify` — sanity-check a key before sending it

```bash
dotnet run -- verify --license "PASTED.KEY"
# or against a specific public key file:
dotnet run -- verify --license "PASTED.KEY" --key /path/to/public_key.pem
```

Reports whether the signature is valid and, if so, prints the decoded payload.

## Known limitations (by design, fine for v1)

Keys are per-email but not hardware-locked, and there's no revocation list — a customer could
share their key with someone else. For an indie-priced tool this is a normal, accepted trade-off
(no license server, no privacy concerns, no support burden). If piracy ever becomes a real
problem, options are: add a machine ID to the payload, or add an online activation check. Don't
build that preemptively.

## See also

`docs/LAUNCH_CHECKLIST.md` Section 2 covers this same flow in the context of the full launch
runbook (branding, Gumroad delivery model, testing the activation loop end-to-end).
