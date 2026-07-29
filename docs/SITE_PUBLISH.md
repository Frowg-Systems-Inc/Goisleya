# Publish the Isley update channel

In-app automatic updates read only:

`https://isley-download.gmith.chatgpt.site/Isley-release.json`

(Beta-enabled clients additionally read `Isley-release-beta.json` from the
same host — see `docs/ISLEY_UPDATER_DELTA.md` §2 and §6.)

Packaging in this repo is not enough. The ChatGPT Sites project must be
redeployed so that URL and the zips match the staged `download-site/public`
files.

## Project

- Hosting file: `download-site/.openai/hosting.json`
  (`project_id: appgprj_6a626e064478819196e9d5e5dcb102ab`; no D1/R2 bindings).
- `download-site/worker/index.ts` is the Cloudflare Worker entry the Sites
  deploy builds from: it serves the Next.js app plus the static files under
  `public/` and routes `/_vinext/image` through image optimization. It holds
  no release data, so publishing new `public/` files never requires a worker
  change.
- Production URL: `https://isley-download.gmith.chatgpt.site/`

## What the release workflow stages

The manual `Package Isley release` workflow refreshes, per run:

- stable channel: `public/Isley-Windows-x64.zip`,
  `public/Isley-Server-Network.zip`, `public/Isley-release.json`, the
  download-page constants, and — when a delta was produced —
  `public/Isley-delta-<from>-<to>.zip`.
- beta channel: `public/Isley-Windows-x64-beta.zip` and
  `public/Isley-release-beta.json` (plus an optional delta zip); every stable
  file stays untouched.

Everything lands in the run's `isley-release-package` artifact and in the
pushed branch's `download-site/public/`; deploy from the commit that contains
the staged files.

## Publish from ChatGPT / Codex (required, manual)

In ChatGPT Work or Codex with the Sites plugin, open the Isley download-site
project (or this repo's `download-site` folder) and run:

```text
@Sites Deploy the Isley download-site project
appgprj_6a626e064478819196e9d5e5dcb102ab
from the current Git commit that contains download-site/public/Isley-release.json
version <X.Y.Z> and the matching Isley-Windows-x64.zip. Save a version, then
deploy it to production at isley-download.gmith.chatgpt.site.
```

After deploy, confirm:

```bash
curl -s https://isley-download.gmith.chatgpt.site/Isley-release.json
```

`version` must equal the just-released X.Y.Z (newer than installed Isley
builds). For a beta deploy also confirm:

```bash
curl -s https://isley-download.gmith.chatgpt.site/Isley-release-beta.json
```

shows `"channel": "beta"`, and when a delta shipped, that
`https://isley-download.gmith.chatgpt.site/Isley-delta-<from>-<to>.zip`
returns HTTP 200 (clients fall back to the full package if it is missing).

## Optional Authenticode

Set `ISLEY_CODE_SIGN_PFX` / `ISLEY_CODE_SIGN_PASSWORD` or
`ISLEY_CODE_SIGN_THUMBPRINT` before packaging if you want signed EXEs. See
`docs/WINDOWS_DEFENDER.md`.
