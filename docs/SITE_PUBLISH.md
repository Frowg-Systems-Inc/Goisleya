# Publish the Isley update channel

In-app automatic updates read only:

`https://isley-download.gmith.chatgpt.site/Isley-release.json`

Packaging in this repo is not enough. The ChatGPT Sites project must be
redeployed so that URL and `/Isley-Windows-x64.zip` match the staged
`download-site/public` files.

## Project

- Hosting file: `download-site/.openai/hosting.json`
- `project_id`: `appgprj_6a626e064478819196e9d5e5dcb102ab`
- Production URL: `https://isley-download.gmith.chatgpt.site/`

## Publish from ChatGPT / Codex (required)

In ChatGPT Work or Codex with the Sites plugin, open the Isley download-site
project (or this repo's `download-site` folder) and run:

```text
@Sites Deploy the Isley download-site project
appgprj_6a626e064478819196e9d5e5dcb102ab
from the current Git commit that contains download-site/public/Isley-release.json
version 1.3.6 and the matching Isley-Windows-x64.zip. Save a version, then
deploy it to production at isley-download.gmith.chatgpt.site.
```

After deploy, confirm:

```bash
curl -s https://isley-download.gmith.chatgpt.site/Isley-release.json
```

`version` must be newer than installed Isley builds (currently live was 1.3.3).

## Optional Authenticode

Set `ISLEY_CODE_SIGN_PFX` / `ISLEY_CODE_SIGN_PASSWORD` or
`ISLEY_CODE_SIGN_THUMBPRINT` before packaging if you want signed EXEs. See
`docs/WINDOWS_DEFENDER.md`.
