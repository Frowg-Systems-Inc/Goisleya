ISLEY - WINDOWS PORTABLE
========================

QUICK START

1. Extract the entire ZIP to a normal folder.
2. Open that folder and double-click Isley.exe.
3. Choose Live Map, Official, or Any Server during the short setup.

Do not run Isley.exe from inside the ZIP. Keep every included file together.

WINDOWS SECURITY / DEFENDER

Microsoft Defender may flag a brand-new unsigned portable build with a
machine-learning label such as Trojan:Win32/Wacatac.B!ml. That !ml suffix is a
heuristic guess, not proof of known malware. Isley does not inject into The
Isle, read game memory, or inspect packets.

Before allowing the file:
1. Confirm the ZIP SHA-256 matches the official download page.
2. Prefer downloads from https://isley-download.gmith.chatgpt.site/
3. In Windows Security > Protection history, allow the detection only if the
   hash matches.

Maintainers: Authenticode signing and Microsoft false-positive submission are
documented in the Isley repository at docs/WINDOWS_DEFENDER.md.

OVERLAY CONTROLS

- The map key explains your facing arrow, friends, other visible animals,
  routes, roads/trails, and drinkable water at a glance.
- Open TOOLS for full-word categories, a short explanation of each section,
  quick map presets, and individually labeled map layers.
- Use the mouse wheel anywhere inside Tools, Quick Commands, onboarding, or
  universal-session pages to scroll.
- Select the lock icon in the title bar to make every part of Isley pass pointer
  input through to the game except the unlock button. This also prevents moving
  or resizing. Select it again to unlock; the setting is remembered.
- The minimized Isley dock has the same unlock-only behavior.

REQUIREMENTS

- Windows 10 version 2004 or newer, or Windows 11
- Microsoft .NET 8 Desktop Runtime (x64):
  https://dotnet.microsoft.com/en-us/download/dotnet/8.0
- Microsoft ASP.NET Core 8 Runtime (x64), required for the bundled Isley Voice
  host:
  https://dotnet.microsoft.com/en-us/download/dotnet/8.0
- Microsoft Edge WebView2 Runtime:
  https://developer.microsoft.com/en-us/microsoft-edge/webview2/

AUTOMATIC ISLEY UPDATES

Isley checks its trusted stable release channel shortly after startup and every
30 minutes. When a newer version is ready, a small notification offers UPDATE
& RESTART or LATER. The update is downloaded from the permanent Isley site,
checked against its exact size and SHA-256 fingerprint, staged safely, and
installed only after you choose to restart.

Your IsleyData folder and saved preferences remain in place. LATER snoozes the
notification for six hours. Automatic checks and a manual Check for updates
button are available under App > Isley Updates.

This is the first updater-enabled release. Copies older than 1.1.0 require one
manual download; releases after that can be installed from the notification.

PRIVACY AND INDEPENDENCE

Isley is an independent external companion. It does not inject into The Isle,
read game memory, inspect packets, automate game input, or bypass anti-cheat.
Its map and manual tools do not depend on the server you join. Continuous
telemetry is available only when that server deliberately runs the independent
Isley Server Bridge; it never depends on another community's account, player
panel, private API, map, or service.

VISIBLE HUD SENSOR

Core Vitals includes an opt-in Visible HUD Sensor for ordinary servers. While
The Isle is the foreground window, it samples only the health effect and
food/water/stamina icons already visible on screen. It stores no screenshots
and reads no game memory, packets, or input. Values prefixed with ~ are broad
estimates, not exact game telemetry. Keep the bottom-right HUD visible and use
CALIBRATE HUD when changing HUD scale or resolution. READ VISIBLE TEXT performs
one explicit Windows OCR pass and imports only supported location/vital labels;
the image and raw text are discarded. Use borderless or windowed fullscreen if
exclusive fullscreen blocks the reading. The sensor pauses outside the game and
in Streamer Mode. A signed server provider always takes priority.

MAP DATA

With an internet connection, Live Map uses the current Gateway game-file
basemap, validated zones and resources, roads, water, and water labels from:

https://myislemap.com/

This release resolves the current public map feed to its July 18, 2026
basemap revision and map-data version 52. Every live layer uses the same
current Gateway world-coordinate transform. The map screen keeps visible
attribution; game imagery remains the property of Afterthought Studios and the
online source is not presented as Isley artwork.

If the current map feed is unavailable, Isley clearly switches to its bundled
SCHEMATIC OFFLINE FALLBACK. The fallback keeps player markers, routes,
road/trail guidance, drinking-water checks, terrain danger, and the A1-T20 grid
usable without presenting the schematic terrain as the current game map.

At local zoom, Isley loads only the visible high-resolution map tiles (up to
25 in full mode or 12 in Lite Mode) instead of decoding the full large map.
Patrol and migration overlays are labeled as candidates because the game
selects the active zone per player.

Community Gateway references can still be work in progress immediately after a
game patch, so use Terrain Probe to confirm uncertain passages in the field.

ISLEY LIVE NETWORK

A participating server can connect its authorized plugin or private RCON to an
Isley Server Bridge and Relay. Paste that server's Isley join link into Tools >
Isley Live Network, then sign in through Steam. Isley receives a continuous
authenticated stream with the fields that server supports: position, facing,
health, growth, stamina, food, water, sickness, friends, and AI animals.

The status shows relay age, measured update rate, connected player nodes,
visible entities, and whether the server chose consent-filtered or server-wide
awareness. Slow clients keep only the newest frame so old movement cannot build
up. The connection retries automatically.

Players never connect directly to each other. Raw Steam IDs and other players'
private vitals are removed by the relay. Use FRIEND SHARING to let verified
Steam friends see your node, or paste a trusted SteamID64 to allow or remove one
specific viewer. A client cannot turn on server-wide visibility.

LOCAL LIVE-DATA FALLBACK

The portable app can also check IsleyData\LiveData\positions.json up to four times per
second in full mode (about once per second in Lite Mode). A fresh file can
provide self position and facing, opted-in friends or other animals, and
health/food/water/growth. Copy IsleyLiveData.example.json to that location and
rename it positions.json to see the documented format.

Each update must carry a current updatedAt timestamp. Data older than 10 seconds
is removed from the map and excluded from decisions. Isley does not discover
players, read game memory, inspect network traffic, or bypass server privacy.
The data must come from a source the player and server permit.

Without a provider, Player Sync is ready by default on every server. Press Tab
in The Isle and click Asset Location. Isley will place the newest point on the
bundled map, refresh its live timestamp even at the same point, and preserve or
infer a smoothed travel course and speed from recent captured points. Repeat the
copy after moving or turning; Isley never injects into the game to obtain it.

BUILT-IN VOICE

Start Voice automatically launches and verifies Isley's bundled local signaling
host before the microphone is requested. Hold the selected PTT key to test the
live microphone meter after connecting. A localhost room works on one computer;
players on different networks must use a trusted public WSS signaling server
and may need TURN relay settings.

The included Isley.portable marker keeps this copy's settings and browser data
inside its own IsleyData folder.

SUPPORT

https://ko-fi.com/theoneboundinink
