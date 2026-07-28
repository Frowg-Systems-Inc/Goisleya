# Upgrade from RCON to an authorized plugin

Evrima RCON is a valid Live Network source, but it cannot supply every field.

| Field | RCON | Authorized plugin |
| --- | --- | --- |
| Position | Yes | Yes |
| Facing while stationary | No (motion-inferred only) | Yes when supplied |
| Health / growth / food / water / stamina | Yes | Yes |
| Sickness / conditions | No | Yes |
| AI animals | No | Yes |
| Practical cadence | ~2–5 Hz | Source-controlled (10–20 Hz recommended) |

## When to upgrade

If Isley’s FEED chip shows `NO-FACING`, missing `COND`, or missing `AI`, the
participating server is on RCON limits — not a broken client. Enable a plugin
publisher into the Bridge loopback endpoint.

## Steps

1. Keep Relay HTTPS public; keep Bridge private beside the game server.
2. Set Bridge source:
   ```powershell
   $env:Bridge__SourceMode = "Plugin"   # or "Both" during cutover
   $env:Bridge__PluginEnabled = "true"
   $env:Bridge__PluginKey = "<at-least-32-random-characters>"
   ```
3. Leave `AllowRemotePlugin` off unless the plugin path is on a trusted private network.
4. Dry-run the reference publisher from the server kit:
   ```powershell
   .\publish-plugin-frame.ps1 -DryRun
   .\publish-plugin-frame.ps1
   ```
5. Confirm Bridge `GET http://127.0.0.1:5210/status` shows source live and entities.
6. In the player overlay, the FEED chip should gain `FACING`, `COND`, and/or `AI`
   only when the plugin actually supplies them.

Isley never invents missing RCON fields. The plugin is the complete path.
