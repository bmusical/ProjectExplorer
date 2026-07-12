# Project Nest — Launch Checklist & Runbook

> **Product:** Project Nest &nbsp;•&nbsp; **Program:** Project Nest Explorer &nbsp;•&nbsp; **Publisher:** HxM Blazor Software LLC
>
> This is a start-to-finish runbook for shipping and maintaining the product. It is written
> specifically for *this* codebase (ECDSA-signed license keys, AutoUpdater.NET, Inno Setup,
> GitHub Releases, Gumroad). Work top to bottom the first time; after that, the **Release a New
> Version** section is the only part you repeat.

---

## Legend

- ⛔ **Blocker** — must be done before you sell a single copy.
- 🔧 **One-time setup** — do once, then forget.
- 🔁 **Every release** — repeat for each new version.
- 💡 **Recommended** — not strictly required, but strongly advised.

---

## 0. Pre-flight — the two things that will bite you

- [x] ⛔ **Replace the license public key.** *(Done — commit `5a95f73` embedded the real ECDSA
  public key in `LicenseManager.cs`. Dev mode, where the app would accept ANY correctly-formatted
  string as a valid license, is no longer active.)* See **Section 2**.
- [x] ⛔ **Make the repository public** (or host `updates.xml` somewhere public). The in-app
  updater fetches `https://raw.githubusercontent.com/bmusical/ProjectExplorer/master/updates/updates.xml`.
  If the repo is private, that URL 404s and auto-update silently fails. *(Done — repo flipped to
  public on 2026-07-08; verified the raw URL now returns 200.)* See **Section 5**.

---

## 1. Brand, identity & legal (🔧 one-time)

- [ ] 🔧 Confirm the naming everywhere:
  - **Product / brand:** *Project Nest*
  - **Program / application:** *Project Nest Explorer*
  - **Company:** *HxM Blazor Software LLC*
- [x] ✅ **URLs & support email are now consistent on `blaznaccess.com`:**
  | File | Value |
  |------|-------|
  | `RegistrationDialog.cs` → `lnkBuy` | `https://blaznaccess.com/landing/project-nest` *(swap for the direct Gumroad product URL once live)* |
  | `RegistrationDialog.cs` → invalid-key message | `support@blaznaccess.com` |
  | `installer/ProjectExplorer.iss` → `AppPublisherURL` | `https://blaznaccess.com/landing/project-nest` |
  | `installer/ProjectExplorer.iss` → `AppSupportURL` | `mailto:support@blaznaccess.com` |

  > Canonical support address is **support@blaznaccess.com**. The only remaining TODO here is to
  > repoint `lnkBuy` at the *direct Gumroad checkout URL* when the product page is published.
- [ ] 🔧 Register / confirm ownership of the domain used in those URLs.
- [ ] 🔧 Set up the support inbox (e.g. `support@yourdomain.com`) and make sure you actually receive mail there.
- [ ] 💡 Write a one-line tagline (already in-app: *"All your projects, one place."*) and keep it consistent across Gumroad, the landing page, and the About box.
- [x] 💡 Decide on a EULA / license terms. *(Done — `LICENSE-EULA.txt` at repo root covers personal + internal commercial use, no key redistribution, no reverse engineering/reselling/competing-product use, "as-is" warranty disclaimer. Wired into the installer via `LicenseFile=` in `installer/ProjectExplorer.iss`, so Setup shows an Accept/Decline page before install.)*

---

## 2. License key system (⛔ blocker + 🔧 one-time)

Your keys are **ECDSA P-256 signed** payloads of the form `email|FULL|yyyy-MM-dd`, encoded as
`base64url(payload).base64url(signature)`. Verification happens **offline** using an embedded
public key — no license server needed. The `tools/KeyGen` console app is your key factory.

### 2.1 Generate your production keypair (do this ONCE)

- [ ] 🔧 On a machine you trust, run the key generator:
  ```bash
  cd tools/KeyGen
  dotnet run -- setup
  ```
  This writes `public_key.pem` and `private_key.pem`.
- [ ] ⛔ **Guard `private_key.pem` like cash.** Anyone with it can mint free keys.
  - Store it OFFLINE (encrypted USB, password manager attachment, or a secrets vault).
  - **NEVER** commit it. Confirm `*.pem` is git-ignored (add it if not — see Section 8).
  - Keep at least one backup in a separate location. If you lose it, you can never sign keys
    that validate against the shipped public key, and you'd have to release a new build.

### 2.2 Embed the PUBLIC key into the app

- [x] ⛔ Open `src/ProjectExplorer.Core/Services/LicenseManager.cs`.
- [x] ⛔ Replace:
  ```csharp
  private const string PublicKeyPem = "DEVELOPMENT_KEY_PLACEHOLDER";
  ```
  with the full contents of `public_key.pem`, e.g.:
  ```csharp
  private const string PublicKeyPem =
      "-----BEGIN PUBLIC KEY-----\n" +
      "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE...your key...\n" +
      "-----END PUBLIC KEY-----";
  ```
  (The public key is safe to embed and ship — it can only *verify*, not *sign*.)
  *(Done in commit `5a95f73` — `LicenseManager.cs` now embeds a real PEM key, not the placeholder.)*
- [ ] ⛔ Rebuild and confirm dev mode is OFF: a made-up string like `foo|FULL|2025-01-01` should now
  be **rejected** in the Registration dialog. Only keys signed by your private key should activate.
  *(Code change is in place; still worth a manual rebuild-and-verify pass before shipping.)*

### 2.3 Mint a key for a customer (🔁 per sale)

- [ ] 🔁 When someone buys, run:
  ```bash
  cd tools/KeyGen
  dotnet run -- generate --email customer@example.com
  ```
  Copy the printed `LICENSE KEY` and deliver it (Gumroad can do this automatically — Section 3).
- [ ] 💡 Sanity-check any key before sending:
  ```bash
  dotnet run -- verify --license "PASTED.KEY" --key public_key.pem
  ```

### 2.4 Test the full activation loop

- [ ] ⛔ Build the app with the real public key, generate a key with the real private key, paste it
  into **Help ▸ Register / License…**, and confirm it activates and persists across restarts.
- [ ] 💡 Note the current limits so your store copy matches: **Free = 3 projects, 25 references.**
  Licensed = unlimited. (Defined in `LicenseManager.cs`.)

> **Known limitation (fine to ship, good to know):** keys are per-email but not hardware-locked and
> there's no revocation. A customer could share their key. For a $-priced indie tool this is a normal
> trade-off (offline, no server, no privacy concerns). If piracy ever becomes a real problem, options
> are: add a machine-id to the payload, or add an online activation check. Don't build that now.

---

## 3. Gumroad setup (🔧 one-time + 🔁 light per release)

Gumroad is a good fit: it handles checkout, taxes/VAT, and can deliver a unique license key per sale.

### 3.1 Account & product

- [ ] 🔧 Create a Gumroad account and complete payout details (bank/PayPal) and tax info.
- [ ] 🔧 Create a new **Product** → type: **Digital product**.
- [ ] 🔧 Name it **Project Nest Explorer**, set the price, and write the description
  (use the tagline + the persona pain-points from `CLAUDE.md`).
- [ ] 🔧 Add screenshots (the polished main window, the tree + list, the About box) and the app icon as the cover.
- [ ] 🔧 In **Content / File**, upload the installer **OR** — better — just link to the GitHub Release
  download so you only maintain one copy of the binary. (If you upload to Gumroad you must re-upload
  every release.)

### 3.2 License key delivery — pick ONE model

**Model A — Gumroad's built-in license keys (simplest, but keys are random, not signed by you):**
- [ ] Enable **"Generate a unique license key per sale"** in the product settings.
- ⚠️ These Gumroad keys are random UUIDs and are **NOT** compatible with your ECDSA verifier. To use
  this model you'd have to change `LicenseManager` to call Gumroad's license-verify API (needs
  network + your Gumroad product ID). More moving parts. **Not recommended for v1.**

**Model B — Your own signed keys, delivered via Gumroad (recommended, matches your code):**
- [ ] 🔧 Because your keys embed the customer email, you generate them per-sale. Two ways:
  - **Manual (fine for low volume):** When you get a Gumroad sale notification, run
    `dotnet run -- generate --email <buyer_email>` and paste the key into the Gumroad receipt / send it
    by email. Add a note on the product page: *"Your license key is emailed within 24h."*
  - **Automated (for higher volume):** Use a **Gumroad Ping/Webhook** → a tiny serverless function
    (e.g. an Azure Function / AWS Lambda / a small always-on box) that runs the KeyGen signing logic
    with your private key and emails the buyer. **Only build this once manual delivery becomes a chore.**
- [ ] 🔧 Add post-purchase content on Gumroad: the download link, "how to activate" (Help ▸ Register),
  and the support email.

### 3.3 Test purchase

- [ ] ⛔ Use Gumroad's test/preview to walk the full buyer flow: buy → receive key → download → install → activate.

---

## 4. Build & package the installer (🔁 every release)

Everything is scripted in `installer/build-installer.ps1`.

### 4.1 Prerequisites (🔧 one-time on the build machine)

- [ ] 🔧 Install the **.NET 10 SDK**.
- [ ] 🔧 Install **Inno Setup 6** (latest is **6.4.3**) from https://jrsoftware.org/isinfo.php and
  ensure `iscc.exe` is on PATH (or at `C:\Program Files (x86)\Inno Setup 6\iscc.exe`).
  > All script comments now correctly reference Inno Setup **6**. Every directive used
  > (`PrivilegesRequiredOverridesAllowed`, `WizardStyle=modern`, `InfoAfterFile`, `lzma2/ultra64`)
  > is fully supported in 6.x — no need for a 7.x preview.
- [ ] 💡 (Optional) A **code-signing certificate** — see Section 6.

### 4.2 Build

- [ ] 🔁 From the repo root:
  ```powershell
  .\installer\build-installer.ps1 -Version 1.0.1 -UpdateXml
  ```
  This will:
  1. `dotnet publish` a self-contained single-file `win-x64` exe → `publish\ProjectNest.exe`
  2. Run Inno Setup → `installer-output\ProjectNest-1.0.1-Setup.exe`
  3. (`-UpdateXml`) rewrite `updates\updates.xml` with the new version + download URL
- [ ] 🔁 Test the produced installer on a **clean** Windows VM (no .NET installed) to confirm the
  self-contained build actually runs standalone.
- [ ] 🔁 Verify the app icon shows correctly on the taskbar, Start menu shortcut, and Add/Remove Programs.

---

## 5. GitHub Releases & auto-update (🔧 one-time + 🔁 every release)

The in-app updater (AutoUpdater.NET) reads `updates/updates.xml` from the repo's **`master`** branch and
compares `<version>` to the running assembly version. If newer, it prompts the user and downloads the
`<url>` installer.

### 5.1 Make the update feed reachable (⛔ one-time)

- [x] ⛔ **The repository must be PUBLIC** for `raw.githubusercontent.com/.../updates.xml` to load.
  *(Done — repo visibility changed to Public on 2026-07-08 via Settings → General → Danger Zone.
  The repo name stays `ProjectExplorer` — see CLAUDE.md's Naming section for why that's
  intentionally distinct from the "Project Nest" / "Project Nest Explorer" branding.)*
  - Alternatively, keep the repo private and host `updates.xml` on your public website, then update
    the `UpdateCheckUrl` constant in `MainForm.cs` to that URL. (Public repo is simpler.)
- [x] ✅ **Branch name mismatch fixed:** the updater URL in `MainForm.cs` now points at **`/master/`**,
  matching this repo's default branch. (There is no `main` branch — that was resolved.)

### 5.2 Cut a release (🔁 every release)

**`updates/updates.xml` is owned by `.github/workflows/release.yml`, not you — never commit it as
part of a version bump.** The repo is public, so committing it before the GitHub Release and
installer asset exist would advertise a version that 404s for anyone who checks for updates in
that window.

- [ ] 🔁 Bump `<Version>` / `AssemblyVersion` / `FileVersion` in `ProjectExplorer.WinForms.csproj`
  and add a new `## [X.Y.Z] — YYYY-MM-DD` section to `CHANGELOG.md`. Commit and push both to `master`.
- [ ] 🔁 Tag and push (no `v` prefix — a `v`-prefixed tag won't match the workflow's trigger
  pattern and simply won't run):
  ```powershell
  .\installer\cut-release.ps1 -Version 1.0.1
  ```
  This double-checks the csproj/`CHANGELOG.md` bump made it to `master`, pushes the tag, and streams
  the GitHub Actions run live via `gh run watch`. (Or by hand: `git tag 1.0.1 && git push origin
  1.0.1`.) The workflow builds the installer, creates the GitHub Release with
  `ProjectNest-1.0.1-Setup.exe` attached, and only then commits `updates/updates.xml` pointing at
  this version — see `docs/RELEASE.md` for the full step-by-step and the manual/offline fallback
  (clean-VM testing, code-signing before the release goes public) if you need either first.
- [ ] 🔁 **The uploaded asset filename MUST exactly match** the `<url>` in `updates.xml`
  (`ProjectNest-<version>-Setup.exe`). A mismatch = broken auto-update download.

### 5.3 Verify the update path (🔁 first few releases)

- [ ] 🔁 Install an **older** version, launch it, and confirm it detects the new release, downloads,
  and upgrades cleanly (data in `%APPDATA%\ProjectExplorer\projects.json` must survive the upgrade —
  the installer's `[UninstallDelete]` intentionally leaves user data alone).

---

## 6. Code signing (💡 strongly recommended)

Unsigned exes trigger **SmartScreen "Unknown publisher"** warnings that scare buyers and tank
conversion.

- [ ] 💡 **Recommended: Certum "Code Signing in the Cloud" (OV)**, ~$108–120/yr through resellers
  (e.g. [SSLmentor lists it from $116/yr](https://www.sslmentor.com/certum/certumcodecloud); reseller
  pricing shifts, so shop around at purchase time). It's signed via Certum's free
  [SimplySign](https://www.certum.eu/en/simplysign/) mobile app instead of a physical USB token — the
  cloud-hosted key still satisfies the CA/Browser Forum's hardware-key rule, you just approve each
  signing session from your phone (a TOTP-style prompt, ~2hr authorized window) rather than plugging
  in a dongle.
  - ⚠️ Certum also sells a much cheaper **"Open Source Code Signing"** tier (~$50/yr), but that requires
    the signed software to ship under an OSI-approved open-source license. ProjectExplorer ships under a
    commercial EULA (`LICENSE-EULA.txt`) with paid license keys, so it does **not** qualify — use the
    standard OV tier above, not the open-source one.
  - Reseller-priced Sectigo/Comodo OV certs are a fine alternative (~$219/yr) if you'd rather have a more
    globally-recognized CA name; Certum's SimplySign mainly wins on not requiring a separate token purchase.
  - EV (e.g. [Certum EV Cloud](https://www.sslmentor.com/certum/certumcodecloudev) ~$226/yr, or
    Sectigo/DigiCert EV ~$280–350+/yr with heavier business vetting) clears SmartScreen instantly instead
    of building reputation over time — worth revisiting post-launch if OV's reputation-building window is
    hurting early conversion, but overkill to start with for a low-volume v1.
  - As of March 2026, CA/Browser Forum rules cap **all** code-signing certs (OV and EV) at 459 days
    (~15 months) max validity — even a "multi-year" plan needs a free reissue partway through. Budget for
    that regardless of vendor.
- [ ] 💡 Sign **both** the app exe and the installer exe with `signtool`:
  ```powershell
  signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a `
    "publish\ProjectNest.exe"
  signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a `
    "installer-output\ProjectNest-1.0.1-Setup.exe"
  ```
- [ ] 💡 Add the signing step into `build-installer.ps1` (after publish, before/after Inno Setup) so
  it's scripted. Note: with SimplySign the *script* is automatic but each release still needs a manual
  phone approval to open the ~2hr signing window first — it's not unattended/headless CI signing unless
  you upgrade to a plan that supports that later.
- [ ] 💡 If you can't sign yet: document that users will see a SmartScreen prompt and must click
  **More info → Run anyway**. Add this to the Gumroad post-purchase notes so it's not a surprise.

---

## 7. Small code/consistency fixes to make before/at launch

These are tracked here so nothing slips. (The trivial-and-safe ones are handled in the companion PR;
the judgment calls are left for you.)

- [x] ⛔ Replace `PublicKeyPem` placeholder (Section 2.2). *(Done — commit `5a95f73`.)*
- [x] ✅ Updater URL now uses `/master/` (repo's real default branch; no `main` exists). *(handled in companion PR)*
- [x] ✅ Support email + landing URL reconciled to `blaznaccess.com` across `RegistrationDialog.cs` and the installer. *(handled in companion PR)*
- [x] 🔧 Fixed the stray "Inno Setup 7" comments in `build-installer.ps1` **and** `ProjectExplorer.iss` — all now correctly say **Inno Setup 6**. *(handled in companion PR)*
- [ ] 🔧 Ensure `*.pem`, `publish/`, and `installer-output/` are git-ignored. *(handled in companion PR)*
- [ ] 💡 Keep `updates/updates.xml` `<mandatory>` = `false` unless you ship a critical fix.

---

## 8. Repo hygiene (🔧 one-time)

- [ ] 🔧 Confirm `.gitignore` excludes secrets and build output: `*.pem`, `publish/`, `installer-output/`,
  `bin/`, `obj/`. *(handled in companion PR)*
- [ ] 🔧 Do **not** commit `private_key.pem`, `public_key.pem` is fine to keep locally but is embedded
  in code anyway.
- [x] 💡 Add a short top-level `README.md` for the public repo: what the product is, a screenshot, the
  Gumroad buy link, and a "Releases" pointer. (Buyers and the curious will land here once it's public.)
  *(Added — swap in a real screenshot once the UI is final.)*
- [ ] 💡 Add an `Issues` template so users can report bugs.

---

## 9. Post-launch (🔁 ongoing)

- [ ] 🔁 Watch the support inbox; keep a FAQ of the first real questions.
- [ ] 🔁 Track sales in Gumroad; reconcile against keys issued.
- [ ] 💡 Collect feature requests against the roadmap already in `CLAUDE.md`
  (Open CMD here, drag-drop reparenting, Reveal in Explorer, search/filter, etc.).
- [x] 💡 Keep a `CHANGELOG.md` so release notes are quick to assemble each time. Add a new
  `[X.Y.Z]` section on every release (see `docs/RELEASE.md` step 2).

---

## Quick reference — "Release a new version" in 5 steps

> Also written up standalone at [`docs/RELEASE.md`](RELEASE.md) for faster linking once you're
> past first-launch setup — including the manual/offline fallback if you need clean-VM testing or
> code-signing before the release goes public, which this tag-triggered path skips.

1. Bump version in `ProjectExplorer.WinForms.csproj`.
2. Add a `## [X.Y.Z] — YYYY-MM-DD` section to `CHANGELOG.md`.
3. Commit and push both to `master`.
4. `.\installer\cut-release.ps1 -Version X.Y.Z` — tags, pushes, and watches
   `.github/workflows/release.yml` build the installer, publish the GitHub Release, and commit
   `updates/updates.xml` for you.
5. Verify an older install auto-updates to the new version.
