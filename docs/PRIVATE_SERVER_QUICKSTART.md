# Private Server Quickstart

The fastest path from a private Isle server to every player seeing live
positions, vitals, and shared proximity voice in Isley.

## Players: 30 seconds

1. Copy the Isley link your server admin shared, for example
   `https://relay.example/join/my-isle-server`.
2. In Isley press `Ctrl+Shift+P` (Quick Commands), type **connect**, and run
   **Connect to private server**. Isley reads the link from your clipboard,
   fills it in, and starts the connection.
3. A browser tab opens for Steam sign-in the first time. Approve it — Isley
   finishes automatically and keeps the 30-day session in Windows Credential
   Manager.

That's it. From then on Isley auto-resumes the server feed whenever The Isle
starts, and Auto proximity voice joins your server's shared lobby.

The link can also be pasted manually under Tools → the Isley Live Network
panel, which offers Connect / Disconnect / Forget session controls.

## Operators: about 10 minutes

Isley's live path is `game source → Isley Server Bridge → Isley Relay →
players`. Full detail: [ISLEY_LIVE_NETWORK.md](ISLEY_LIVE_NETWORK.md).

Download and extract the 1.4.0 server-network kit
(`Isley-Server-Network.zip`) from the download site or the
[GitHub Releases page](https://github.com/Frowg-Systems-Inc/Goisleya/releases);
it contains the relay, bridge, and guided launcher used below.

1. **Relay (the only public piece).** Host `Isley.Relay` behind HTTPS with
   WebSocket upgrade:

   ```powershell
   $env:Relay__PublicBaseUrl = "https://relay.example/"
   $env:Relay__DataProtectionKeysPath = "D:\IsleyRelay\keys"
   $env:Relay__StatePath = "D:\IsleyRelay\state"
   $env:Relay__Bridges__0__ServerId = "my-isle-server"
   $env:Relay__Bridges__0__Secret = "<unique-random-secret>"
   dotnet Isley.Relay.dll
   ```

2. **Bridge (next to your game server, never public).** Run the guided
   launcher from the server kit — it prompts for the RCON or plugin
   credentials securely and never writes them to disk:

   ```powershell
   ./Start-IsleyServerBridge.ps1
   ```

3. **Health check**, then share the join link:

   ```text
   GET https://relay.example/health/ready
   → give players: https://relay.example/join/my-isle-server
   ```

4. Pick an awareness mode (consent-filtered by default, or explicit
   server-wide awareness) — see the awareness section of
   [ISLEY_LIVE_NETWORK.md](ISLEY_LIVE_NETWORK.md).

5. **Watch the relay.** Aggregate operational counters (frames relayed,
   rejections, active bridges/viewers, uptime) are exposed at the
   loopback-only `/metrics` endpoint — see the
   [relay metrics section](ISLEY_LIVE_NETWORK.md#relay-metrics-and-bridge-diagnostics)
   of the live-network doc.

**Updating:** player clients on 1.4.0 or newer update themselves; from the
NEXT release (1.4.0 → 1.4.x) the updater downloads a delta package instead
of the full ZIP when the manifest offers one — same hash and zip-safety
verification, automatic full-package fallback. Relay and bridge do not
self-update: deploy the matching `Isley-Server-Network.zip` when a release
changes them.

## What players get on a connected server

- Live self + consented friend markers with facing and speed;
- exact health/food/water/growth when the source provides them;
- a shared proximity voice lobby scoped to the server;
- automatic session resume on game start — no per-session setup.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| "That participating server link is not valid" | Link is plain HTTP on a public host (rejected) or malformed — re-copy it |
| Stuck at "Opening Steam" | Browser blocked the sign-in tab; open the printed URL manually |
| Connected but no marker | Bridge is not sending your player yet — check `health/ready` and bridge logs |
| Marker freezes | Feed is stale; Isley drops data older than 10 s by design — check the bridge source |
