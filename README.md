# Isley

Isley is an independent Windows companion overlay for *The Isle*. It provides
a live minimap with a schematic offline fallback, navigation and survival tools, push-to-talk proximity voice,
friends, vitals, guides, lifecycle planning, and compact always-on-top HUDs.

Isley's map does not depend on the game server you join. Live Map uses the
attributed public MyIsleMap Gateway feed for current game-file basemap, zone,
resource, road, and water data. The overlay and manual tools work on official,
community, private, passworded, and unlisted servers.

Continuous server telemetry is optional. A participating server can install the
independent Isley Server Bridge and publish only operator-authorized data to an
Isley Relay. Players sign in through Steam and receive a filtered live stream;
Isley never needs another community's map, player panel, private API, or account.

## What's new in 1.4.0

Thirteen quality-of-life additions across planners, voice, survival, and
chrome: nest timer alerts, a schema-versioned planner-state store, server
growth-rate presets, per-peer voice volume memory with a real per-peer
connection-quality surface, named friend squads, a map watchlist, bounded
tactical-log export, timer journaling that reconciles what expired while you
were away, sensor confidence dots with heading confidence decay (held values
never jump), hotkey conflict detection, Lite Mode suggestions that never
auto-enable, named layout profiles, and a redacted diagnostics bundle.

The relay viewer stream speaks v2 on opt-in: delta frames with periodic
keyframes and a validation-first apply, while v1 stays the byte-identical
default. The updater gained a post-update boot-ok marker (an update stays
pending until a healthy boot confirms it), a real beta channel, and delta
downloads that keep the same hash, size, and zip-safety posture as full
packages. Root of trust is unchanged.

The verification culture kept pace with the features: every one of the 84
logic files compiles under a focused verifier (84/84), 71 verifiers run in
the solution, and the mutation harness proves 13/13 deliberate contract
sabotages are caught with zero false passes.

## Live Map

The Live Map application shell runs from `BurntHud/Map/index.html`, which is
packaged with the desktop app and exposed only through the local `isley.local`
WebView host. The shell validates and renders current public MyIsleMap data
instead of navigating to or embedding another map application. If that public
feed cannot be reached, Isley identifies and reveals its bundled schematic
fallback.

The map retains the full Isley tool set:

- follow and recenter;
- fixed-size self circle with a blue facing arrow;
- friend and other-provider-animal markers;
- north-up and heading-up modes;
- smart zoom and look-ahead;
- saved pins, alert zones, no-go polygons, ruler, grid, and recent routes;
- multi-stop, breadcrumb-return, friend, pack-center, and pack-outlier routes;
- road/trail courses around known water, marked dangers, and local obstacles;
- terrain-probe evidence and explicit saved passage/avoidance tools;
- player counts, friend roster, encounter awareness, and optional last-seen
  memory using only positions already authorized by the active provider;
- Streamer Mode, click-through, arbitrary resize, persistent position lock,
  docking, and scrollable workspaces.

## Isley Live Network

The independent live path is:

`authorized plugin or private RCON -> Isley Server Bridge -> Isley Relay -> Steam-authenticated Isley client`

The bridge can continuously provide position, direction, health, growth,
stamina, food, water, conditions, friends, and AI animals when its source
supports those fields. A plugin can provide authoritative facing and conditions
at high cadence. Evrima RCON provides position and core stats; Isley infers
movement heading and does not pretend RCON contains sickness, AI, or stationary
facing that it does not expose.

Each signed-in client is an Isley player node. The relay fans the newest frame
out over authenticated WebSockets, drops superseded frames for slow clients,
reports stream rate, freshness, and node counts, and reconnects automatically.
Clients never connect directly to each other, so player IP addresses and raw
Steam IDs are not revealed.

Operators can choose consent-filtered visibility or explicitly enable
server-wide awareness for every entity their authorized source supplies.
Players can allow verified Steam friends or individual SteamID64 viewers from
the overlay. Server-wide awareness is never silently enabled by a client.

See [docs/ISLEY_LIVE_NETWORK.md](docs/ISLEY_LIVE_NETWORK.md) for deployment,
capabilities, security, and operator setup, and
[docs/PRIVATE_SERVER_QUICKSTART.md](docs/PRIVATE_SERVER_QUICKSTART.md) for the
fastest player and operator connection path (players: copy the server link,
run the **Connect to private server** Quick Command — done). The server kit
includes a guided
`Start-IsleyServerBridge.ps1` launcher that securely prompts for credentials
without writing them to disk.

## Local live-data fallback

Isley can consume a neutral local JSON document:

- portable build:
  `IsleyData/LiveData/positions.json`
- installed/development build:
  `%LOCALAPPDATA%/Isley/LiveData/positions.json`

`distribution/IsleyLiveData.example.json` documents the format. A provider may
supply:

- self X/Y/Z and world yaw;
- opted-in friends;
- other animals the provider is allowed to reveal;
- species, growth, health, food, and water.

Full mode checks for a changed document up to four times per second. Lite Mode
uses a lower cadence. Documents are limited to 256 KB and 512 players, numeric
fields are bounded, labels are sanitized, and `updatedAt` is required. Data
older than ten seconds is removed from the map and excluded from decisions.

Isley does not ship a hidden player-discovery source. Live data must come from
an integration the player and server permit. Isley does not read game
memory, inspect packets, scrape a server website, bypass privacy, or fabricate
players and animals.

## Coordinate fallback

**Auto location** (on by default) watches for The Isle to start. If you already
saved an Isley Live Network link and Steam session, Isley resumes that feed so
your map marker can appear without a manual copy. If no live feed is available,
it turns Player Sync on and coaches `Tab → Asset Location`.

Without a continuous provider, Player Sync uses *The Isle*'s Asset Location copy
control. Isley reads only a new clipboard change while the game or Isley is in
front, accepts only a bounded three-coordinate value, and discards all other
clipboard text. Every accepted copy refreshes the live marker timestamp; movement
between copies is smoothed across the newest valid captures to estimate course
and speed, while a stationary copy keeps the last trustworthy heading.

The newest point places the self marker on the bundled map. Two different
captured points provide a travel-direction estimate for the blue arrow and
terrain evidence. Fully automatic continuous placement still requires a
participating Live Network server or a local `positions.json` provider.

## Vitals and survival

The compact Core Vitals strip supports health, food, water, and stamina.
Fresh provider values can supply exact health/food/water percentages and growth;
manual bands remain available on every server and expire safely.

The opt-in Visible HUD Sensor adds a universal middle path. While The Isle is
the foreground window, it samples only the already-visible bottom-right HUD and
damage-edge colors at the Full/Lite overlay cadence. It never reads game
memory, packets, or input and never stores a screenshot. The resulting
health/food/water/stamina percentages are deliberately marked with `~` because
they are broad visual estimates; authorized provider values always take
priority. Its calibration control adapts the scan geometry to common HUD scales.
An explicit visible-text read can use Windows OCR to import only allowlisted
location and vital fields; the captured image and raw OCR text are discarded.
The sensor pauses when the HUD is hidden, the game is not foreground, or
Streamer Mode is active.

The survival kit includes:

- condition-specific guidance and timers;
- vomit-sickness instructions with a bounded player-confirmed recovery window;
- stop-eating guidance only while the matching reported warning is active;
- bleeding, fracture, dehydration, starvation, bacterial sickness, and low-HP
  guidance;
- Rest & Recovery, Safe Logout, restart watch, shoreline and water-crossing
  checks;
- Trip Check, Fight Check, Next Move, field conditions, and sighting reports.

Isley never diagnoses a condition. The in-game HUD and server rules remain
authoritative.

## Life, species, and planning tools

- Life Run and private life journal;
- Growth Clock with selectable server multiplier;
- Prime, Elder, nesting, spawn, and zone planners;
- mutation planner, build lab, and unlock tracker;
- species field guide, diet coach, combat brief, and resource finder;
- current official patch watch from Steam's public *The Isle* news endpoint.

## Friends and voice

Steam friend watch validates Steam Community targets before opening Steam and
matches only exact provider-authorized map names. It can route to a selected
friend or arm conservative auto-follow without replacing an active route.

Isley Voice is the app's own WebRTC-based push-to-talk proximity voice system.
Proximity mode and Auto proximity are on by default: Isley connects the voice
session automatically, keeps the microphone muted until you hold PTT, and fades
peers by map distance. On an Isley Live Network server, clients join a shared
proximity lobby for that server.

It also includes:

- automatic bundled local signaling host startup and readiness verification;
- encrypted peer data and media channels;
- microphone and output-device selection;
- optional TURN relay configuration;
- invite and room controls for trusted packmates outside Live Network;
- proximity attenuation and position sharing only when an authorized position
  is available;
- manual Start/Stop voice, Auto proximity opt-out, and Streamer Mode boundaries.

The voice signaling server is packaged under `VoiceServer/`.

## Overlay controls

- Drag the top bar to move Isley.
- Drag the resize corner for any window size.
- Select the lock icon to pass every panel click through to the game except the
  unlock button. This also prevents movement and resizing.
- Select minimize to use the small draggable dock.
- Use the mouse wheel in Tools, Quick Commands, onboarding, and universal
  workspaces.
- `RECENTER` resumes follow mode.
- Click-through lets the game receive input while the overlay remains visible.
  Locked mode keeps only the unlock button interactive.
- Lite Mode reduces timer cadence and effects while retaining compatible tools.

## Isley updates

Starting with Isley 1.1.0, automatic update checks are enabled by default. The
app checks the stable release manifest at
`https://isley-download.gmith.chatgpt.site/Isley-release.json` shortly after
startup and every 30 minutes while it remains open. A compact notification
shows the available version and release note without interrupting the game.

`UPDATE & RESTART` downloads the stable ZIP, requires its declared byte count
and SHA-256 fingerprint to match, rejects unsafe archive paths, stages the
release outside the running application, and starts the bundled updater. The
updater waits for Isley to close, preserves `IsleyData`, replaces the app
files, and reopens Isley. `LATER` snoozes the notification for six hours.
Automatic checks can be disabled or run manually from App > Isley Updates.

Installations older than 1.1.0 do not contain the update client and therefore
need this one release installed manually. Later releases can notify and update
1.1.0-or-newer copies through the stable channel.

Default hotkeys are configurable in the app. Common defaults include:

- `Ctrl+Shift+M`: show/hide Isley
- `Ctrl+Shift+C`: toggle click-through
- `Ctrl+Shift+R`: recenter
- `Ctrl+Shift+P`: Quick Commands
- `Ctrl+Shift+V`: push to talk

## Privacy and safety

Isley is external to the game. It does not inject a DLL, hook DirectX, modify
game files, read game memory, inspect network traffic, synthesize game input, or
bypass anti-cheat. Global shortcuts use normal Windows APIs. Streamer Mode hides
sensitive names, positions, pins, routes, logs, vitals, and voice surfaces.

The aim guide is a manually calibrated visual reference. It is not a hitbox
reader and cannot expose live game hitboxes.

Routes and map geometry are planning aids. Verify cliffs, water, weather,
terrain changes, combat state, and server-specific rules in game.

## Build and verify

Build the Windows app:

```powershell
dotnet build BurntHud\BurntHud.csproj -c Release -r win-x64
dotnet build Isley.ServerBridge\Isley.ServerBridge.csproj -c Release
dotnet build Isley.Relay\Isley.Relay.csproj -c Release
```

Run the complete build, security, website, and focused verification suite:

```powershell
.\scripts\verify-all.ps1
```

After dependencies are already restored, use `-SkipRestore` for a faster local
pass. Add `-IncludeRuntime` on Windows to publish a fresh portable copy and
exercise the real lock/unlock window, minimized dock, current online map,
recenter/follow behavior, heading cadence, and self/friend/animal markers:

```powershell
.\scripts\verify-all.ps1 -SkipRestore -IncludeRuntime
```

The desktop project targets .NET 8 WPF and uses WebView2 for the bundled map and
voice surfaces. Portable installs need the .NET 8 Desktop Runtime plus the
ASP.NET Core 8 Runtime for the bundled local VoiceServer host.

## Portable distribution

Keep `Isley.portable` beside `Isley.exe` to store settings and WebView data
under the extracted `IsleyData` directory. The finished package includes the
desktop app, bundled map, voice client, local voice server, live-data example,
and user-facing README.

Support the project at
[ko-fi.com/theoneboundinink](https://ko-fi.com/theoneboundinink).
