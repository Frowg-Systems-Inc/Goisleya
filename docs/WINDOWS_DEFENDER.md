# Windows Defender and Isley

## What `Trojan:Win32/Wacatac.B!ml` means

The `!ml` suffix is a Microsoft Defender **machine-learning heuristic**, not a
known-malware signature match. It commonly fires on new, **unsigned** Windows
apps that:

- ship as portable ZIP archives
- include a helper that replaces files and relaunches the app
- use normal overlay APIs (hotkeys, screen sampling for Visible HUD Sensor)

Isley does **not** inject into The Isle, read game memory, inspect packets, or
install persistence outside its portable folder and optional Live Network
credentials. Release packaging keeps that contract and verifies it in CI.

## Durable fix (publishers)

1. **Sign every shipped EXE/DLL** with an OV or EV code-signing certificate
   from a public CA (DigiCert, Sectigo, SSL.com, Certum, etc.). Signing is
   wired into `scripts/package-isley-1.3.ps1` — it runs before the archives
   are zipped and hashed, and every binary is re-verified with
   `signtool verify /pa` before packaging completes. Set:
   - `ISLEY_CODE_SIGN_PFX` (path) or `ISLEY_CODE_SIGN_PFX_BASE64` (base64 PFX
     content — use this for GitHub Actions secrets; the script materializes
     and deletes a temp file) plus `ISLEY_CODE_SIGN_PASSWORD` when required,
     or `ISLEY_CODE_SIGN_THUMBPRINT` for a certificate already in the Windows
     cert store. Optional `ISLEY_CODE_SIGN_TIMESTAMP_URL` (defaults to
     DigiCert).
2. Package with `scripts/package-isley-1.3.ps1` (locally or via the
   `Package Isley release` GitHub workflow, which passes the secrets through).
3. Submit each new release ZIP and `Isley.exe` to Microsoft as incorrectly
   detected software:
   https://www.microsoft.com/wdsi/filesubmission  
   Choose Software Developer → Incorrectly detected → include the SHA-256 from
   `Isley-release.json` and a short product description.
4. Prefer downloads from the stable site
   `https://isley-download.gmith.chatgpt.site/` instead of anonymous file hosts.

## Immediate steps for players

1. Confirm the ZIP SHA-256 matches the download page / `Isley-release.json`.
2. In Windows Security → Protection history, mark the detection as
   **Allowed** if you trust this Isley build.
3. Optional temporary exclusion: the extracted Isley folder only (not all of
   Downloads).
4. Re-download from the official Isley download site after a signed release is
   published.

Exclusions are a last resort. Signing + Microsoft false-positive submission is
what clears SmartScreen/Defender reputation for everyone.
