# Request for a sanctioned read-only telemetry interface for The Isle

**To:** The Isle / Afterthought developer team  
**From:** Frowg Systems, Inc., developer of Isley  
**Subject:** Request for a sanctioned, player-consented telemetry or mod interface

Hello,

We develop Isley, an independent Windows companion for The Isle. Isley provides
navigation, accessibility-oriented HUD tools, survival reminders, and optional
proximity voice. It intentionally remains external: it does not inject into the
game, read process memory, inspect player traffic, automate input, modify game
files, or bypass anti-cheat.

We would like to support accurate real-time self position, facing, and survival
status through an interface that you explicitly approve. Today Isley can use
only visible-screen estimates, player-triggered Asset Location copies, or data
published by a participating server through its own authorized RCON/plugin
bridge. A sanctioned interface would be more accurate, safer, and clearer for
players and server operators.

## Requested capability

We propose a versioned, read-only, capability-negotiated feed with:

- the local player's position, yaw, map/version ID, species, growth, health,
  stamina, food, water, and active condition identifiers;
- opt-in party or friend positions when the affected players and server policy
  allow them;
- AI or non-player animals only when a server-authorized plugin deliberately
  exposes them;
- monotonic sequence numbers and capture timestamps;
- explicit capability flags so a client never invents an unavailable value;
- 10-20 Hz for position and yaw, with slower fields updated only when changed.

Self data should be the default. Opponent, friend, party, and AI awareness
should remain unavailable unless the server and affected players explicitly
authorize it.

## Suggested transports

Any of these would work:

- a documented local named pipe or authenticated loopback endpoint owned by the
  game;
- a signed local WebSocket or shared-memory contract intended for companions;
- a supported server plugin callback/API that can publish policy-filtered data.

We would follow your required rate limits, permission model, branding rules,
anti-cheat guidance, and telemetry field restrictions. Isley can expose the
active source and capabilities directly to users so estimates are never
presented as authoritative telemetry.

## What we are asking from you

Could you let us know whether an approved interface already exists, whether
planned mod support will include read-only player telemetry, or whether you
would be open to reviewing a narrow protocol proposal and reference client?
We would also appreciate written guidance on any integration patterns you want
companion developers to avoid.

Project repository: https://github.com/Frowg-Systems-Inc/Isley  
Current download: https://isley-download.gmith.chatgpt.site/

Thank you for considering a safe, consent-based integration path.

Frowg Systems, Inc.  
Isley project
