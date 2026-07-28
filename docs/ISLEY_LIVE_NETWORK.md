# Isley Live Network

Isley Live Network is an independent, operator-authorized way to give players a
continuous minimap without depending on another community's website, player
panel, or private service.

The live path is:

```text
The Isle dedicated server
  -> authorized plugin and/or private Evrima RCON
  -> Isley Server Bridge
  -> signed HTTPS frames
  -> Isley Relay
  -> Steam-authenticated WebSocket
  -> Isley desktop player nodes
```

Players never connect directly to one another. The relay fans out the newest
authorized frame, so it does not expose player IP addresses or raw Steam IDs.
If the relay or desktop is slow, each one-frame queue discards superseded
positions and vitals instead of accumulating delay. The overlay labels a stream
as delayed after one second and stalled after three seconds so old values are
never presented as continuously live.

## What each source can provide

| Field | Authorized plugin | Evrima RCON |
| --- | --- | --- |
| Position | Authoritative | Authoritative sample |
| Facing direction | Authoritative when supplied | Inferred from movement |
| Health and growth | Supported | Supported |
| Stamina, food, water | Supported | Supported |
| Sickness and conditions | Supported | Not exposed |
| AI animals | Supported | Not exposed |
| Practical cadence | Source-controlled; 10-20 Hz recommended | 2-5 Hz, subject to server response time |

RCON cannot reveal stationary facing, conditions, or AI animals through the
documented player-data response. Isley reports those capabilities as unavailable
instead of inventing values. A high-cadence authorized plugin is the complete
path for real-time turns, sickness, and animal awareness.

## Awareness modes

The bridge starts in `PrivacyFiltered` mode. Each entity can be visible only to
itself, to friends, to explicit viewers, or to the whole server according to the
authorized source and the player's relay privacy profile.

A server operator may deliberately set:

```json
"ServerWideAwareness": true
```

This changes every entity supplied by that bridge to `Server` visibility and
labels the client stream `server-wide`. It creates the requested constant
all-position awareness network for that participating server. It does not
discover entities the plugin or RCON source did not supply, and a player client
cannot enable it.

In the Isley overlay, signed-in players can:

- allow or disable verified Steam-friend visibility;
- add an explicit SteamID64 viewer;
- remove an explicit viewer;
- see connected player-node count, visible entity count, stream rate, relay
  age, and whether coverage is server-wide or consent-filtered.

Other players' raw Steam IDs, health, growth, food, water, stamina, species, and
conditions are removed before a viewer snapshot is sent. Friend labels are
shown only after an authorized friend decision.

## Server Bridge setup

The bridge runs beside the game server. Keep its HTTP listener and RCON
connection private.

### Secure guided start

The server-network ZIP includes `Start-IsleyServerBridge.ps1`. It validates the
server ID, HTTPS relay URL, source mode, port, and cadence; prompts privately for
the relay secret and RCON password or plugin key; and passes those values only
to the bridge process. It does not save credentials to a file or print them.

From the extracted server-network folder:

```powershell
.\Start-IsleyServerBridge.ps1 `
  -ServerId "my-isle-server" `
  -ServerName "My Isle Server" `
  -RelayUrl "https://relay.example/" `
  -SourceMode Rcon
```

Use `-SourceMode Plugin` for an authorized plugin or `Both` when both sources
are available. Add `-ServerWideAwareness` only when the server owner has
deliberately chosen and disclosed that visibility policy. The default remains
self-only. Verified Steam-friend visibility still requires each player's
overlay opt-in even when the bridge marks entities as Friends-eligible.

### Manual configuration

Configure `Isley.ServerBridge/appsettings.json`, preferably through environment
variables or a secret manager:

```powershell
$env:Bridge__ServerId = "my-isle-server"
$env:Bridge__ServerName = "My Isle Server"
$env:Bridge__RelayUrl = "https://relay.example/"
$env:Bridge__RelaySecret = "<at-least-32-random-characters>"
$env:Bridge__SourceMode = "Rcon"
$env:Rcon__Host = "127.0.0.1"
$env:Rcon__Port = "8888"
$env:Rcon__Password = "<server-rcon-password>"
$env:Rcon__PollIntervalMilliseconds = "200"
dotnet Isley.ServerBridge.dll
```

`PollIntervalMilliseconds` defaults to 200 ms (a 5 Hz target) and accepts
200 ms or greater. Lower is not always faster: the server's RCON response time
is the real limit. The bridge keeps one connection, reconnects with backoff,
and retains only the newest frames.

For plugin mode:

```powershell
$env:Bridge__SourceMode = "Plugin"
$env:Bridge__PluginEnabled = "true"
$env:Bridge__PluginKey = "<at-least-32-random-characters>"
dotnet Isley.ServerBridge.dll
```

Post a `PluginTelemetryFrame` to the loopback-only endpoint:

```text
POST http://127.0.0.1:5210/plugin/v1/telemetry
X-Isley-Plugin-Key: <plugin key>
Content-Type: application/json
```

Use [`PLUGIN_TELEMETRY_EXAMPLE.json`](PLUGIN_TELEMETRY_EXAMPLE.json) as the
version-1 payload example. JSON enum names are accepted as shown.

The endpoint rejects remote callers by default. Do not enable
`AllowRemotePlugin` unless the plugin connection is isolated and authenticated
by a trusted private network.

Useful bridge checks:

```text
GET http://127.0.0.1:5210/health/live
GET http://127.0.0.1:5210/health/ready
GET http://127.0.0.1:5210/status
```

## Relay setup

The relay is the only public component. Put it behind HTTPS with WebSocket
upgrade support. Give every bridge a unique random secret.

```powershell
$env:Relay__PublicBaseUrl = "https://relay.example/"
$env:Relay__DataProtectionKeysPath = "D:\IsleyRelay\keys"
$env:Relay__StatePath = "D:\IsleyRelay\state"
$env:Relay__Bridges__0__ServerId = "my-isle-server"
$env:Relay__Bridges__0__Secret = "<same-bridge-secret>"
$env:Steam__WebApiKey = "<optional-key-for-verified-friend-lookup>"
dotnet Isley.Relay.dll
```

Persist and protect the data-protection key directory. Losing it invalidates
Steam sessions. The Steam Web API key is optional: self, explicit grants, and
server-wide entities still work without it, while automatic verified-friend
matching stays fail-closed.

Useful relay checks:

```text
GET https://relay.example/health/live
GET https://relay.example/health/ready
```

Give players either join format:

```text
https://relay.example/join/my-isle-server
https://relay.example/?server=my-isle-server
```

Public plain HTTP relay links are rejected. Loopback HTTP remains available for
local development.

## Authentication and transport guarantees

- Steam sign-in uses OpenID in the player's browser. Isley never receives a
  Steam password.
- The 30-day relay token is stored in Windows Credential Manager.
- Every bridge frame is authenticated with HMAC-SHA256 over the server ID,
  timestamp, nonce, and exact body hash.
- The relay rejects clock-skewed, replayed, mismatched, stale-session, and
  out-of-order frames.
- Frames are limited to 512 entities and 512 KB; IDs, coordinates, percentages,
  timestamps, labels, conditions, and grants are validated.
- Ingest is rate-limited and clients reconnect with bounded exponential backoff.
- RCON credentials never leave the bridge and are never sent to a player.

## Scaling

One relay instance supports many bridges and player nodes, with per-server
in-memory newest-frame storage and bounded per-client queues. For a multi-relay
deployment, add a shared backplane for frames/presence and a durable shared
privacy store before placing instances behind a load balancer. Do not run
multiple independent instances against the same hostname and assume presence or
privacy state will synchronize.

## Verification

Run:

```powershell
dotnet run --project Verification\TelemetryPlatformVerifier\TelemetryPlatformVerifier.csproj -c Release
```

The verifier starts real local relay and bridge processes, signs a bridge frame,
opens a bearer-authenticated WebSocket, and proves:

- replay defense and sequence ordering;
- plugin-to-bridge-to-relay delivery;
- self position, facing, vitals, and sickness;
- friend and AI visibility;
- private-player and non-self-vitals redaction;
- server-wide policy transformation;
- connected-node, visible-entity, and update-rate metadata.

Isley remains an external companion. This network does not inject into the game,
read game memory, inspect player traffic, automate input, or bypass anti-cheat.

The bundled
[`THE_ISLE_TELEMETRY_INTERFACE_REQUEST.md`](THE_ISLE_TELEMETRY_INTERFACE_REQUEST.md)
documents the narrow, player-consented telemetry interface Isley is requesting
from The Isle developers.
