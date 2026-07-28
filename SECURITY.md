# Security Policy

## Supported versions

Only the latest release of Isley (see `download-site/public/Isley-release.json`
or GitHub Releases) receives security fixes.

## Reporting a vulnerability

**Do not open a public issue for security problems.**

Email the maintainers via the contact listed on the Frowg-Systems-Inc GitHub
organization profile, or use GitHub's private vulnerability reporting
(Security tab → "Report a vulnerability").

Include: affected component (overlay app, Relay, ServerBridge, VoiceServer,
Updater, download site), reproduction steps, and impact. We aim to acknowledge
reports within 72 hours.

## Scope notes

- The auto-updater trusts the pinned HTTPS manifest host; release integrity
  beyond that requires code signing (see `docs/WINDOWS_DEFENDER.md`).
- Voice rooms use 64-hex unguessable room keys with AES-GCM sealed signaling;
  peers self-report proximity positions by design (documented consent model).
- `Isley.ServerBridge` RCON credentials live in local `appsettings.json` —
  never commit real values.
