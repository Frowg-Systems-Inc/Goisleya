# Isley product options

## Product boundary

Isley is an independent external companion for *The Isle*. Its default Live Map
is bundled with the app and does not load or parse any server operator's
website, player account, private API, or private services.

The universal feature set remains available across official, community,
private, passworded, and unlisted servers:

- minimap, pins, routes, road/trail course planning, obstacle avoidance, and
  Terrain Probe;
- Core Vitals, survival guidance, sickness timers, recovery, Safe Logout, and
  restart watch;
- species guide, Diet Coach, Fight Check, Life Run, Growth Clock, mutations,
  nesting, Prime, and Elder planning;
- Steam friend watch and provider-authorized friend routing;
- Isley's own push-to-talk proximity voice;
- arbitrary resizing, docking, persistent lock, click-through, scrolling,
  Streamer Mode, and Lite Mode.

## Live-data options

### 1. Player-triggered coordinate capture

The player opts into Terrain Probe and uses the in-game Asset Location copy
control. Isley accepts only a bounded three-coordinate value while the game or
overlay is active. Two captures can estimate travel direction and terrain
change. This needs no server integration but is not continuous.

### 2. Local Isley provider

A permitted integration writes a fresh `positions.json` document under Isley's
local data directory. The neutral contract supports self position/yaw,
opted-in friends, other permitted animals, and bounded vitals.

The app checks the changed file at low latency, rejects documents over 256 KB,
caps the roster at 128, sanitizes labels, and removes data older than ten
seconds. This is the preferred path for continuous direction and vitals when a
server or community chooses to integrate.

### 3. Manual-only universal mode

Official and Any Server modes keep voice, manual vitals, survival, guides,
timers, lifecycle tools, and Terrain Probe without requiring any live-data
provider.

## Server integration contract

A future provider should:

- obtain explicit server and player permission;
- expose only data each player is authorized to see;
- use opaque stable IDs and bounded display labels;
- include a current UTC `updatedAt`;
- publish world X/Y/Z and yaw in a documented coordinate system;
- classify self, friend, and other permitted animals explicitly;
- omit hidden identities and any data the provider cannot lawfully share;
- fail closed on logout, privacy changes, stale state, or provider errors.

Isley will not ship credentials, scrape authenticated pages, inspect game
memory or packets, bypass anti-cheat, or manufacture a hidden-player database.

## Packaging

The portable package contains:

- `Isley.exe` and desktop dependencies;
- the bundled `Map/` surface;
- `Voice/` and the local `VoiceServer/`;
- `LiveData/IsleyLiveData.example.json`;
- `Isley.portable`;
- the user-facing `README.txt`.

The public download should be updated only after the exact packaged ZIP passes
the independence scan, controller checks, provider verifier, clean-extraction
launch, and SHA-256 verification.
