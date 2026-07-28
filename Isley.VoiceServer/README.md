# Isley Voice signaling server

This is Isley's first-party WebRTC signaling service. Version 2 forwards only bounded AES-GCM signaling envelopes between opaque peer identifiers in the same opaque room. Current Isley clients encrypt SDP and ICE details with a room-derived, non-exportable key before transmission and exchange display names only over the encrypted peer data channel. The broker cannot decrypt those envelopes and does not relay microphone audio or receive player positions or display names.

For local testing, launch the bundled host from Isley's Voice workspace and connect to `ws://127.0.0.1:5198/voice`.

`GET /health` is a liveness probe. `GET /ready` is the client compatibility contract: it reports protocol version, configured limits, anonymous active-room and active-peer counts, and explicit privacy declarations. It never returns room identifiers, peer identifiers, display names, positions, signaling messages, candidate addresses, or audio data. Isley will not request microphone permission until this response passes its bounded, redirect-free validation.

Configuration is fail-closed at startup. `Voice:AllowedOrigins` must contain one to sixteen unique exact HTTPS origins; wildcards, HTTP origins, paths, queries, fragments, credentials, duplicates, and an empty list are rejected. Room and global capacity must also remain inside the validated bounds. The bundled configuration allows only `https://isley.voice.local`, the isolated origin used by Isley's embedded voice client.

For real remote rooms:

1. Publish this project behind an HTTPS reverse proxy and expose `/voice` as WSS.
2. Set each exact approved client origin with `Voice__AllowedOrigins__0=...`; do not use a wildcard origin.
3. Keep request-size, per-room, global-room, global-peer, and rate limits enabled. The defaults are 12 peers per room, 1,024 rooms, and 4,096 total peers.
4. Issue short-lived TURN credentials from a trusted relay provider, then enter them in Isley's session-only TURN controls. Never place a long-lived relay secret in this server's client-facing files or logs; STUN alone cannot relay media.
5. Monitor both `/health` and `/ready`, and alert before anonymous capacity reaches the configured limits.
6. Terminate TLS at the proxy or configure Kestrel with a trusted certificate.

Room secrets never leave the client. The browser hashes them before connecting, and the server accepts only the resulting opaque room identifier.

Proximity range, Room Radio privacy mode, PTT activity, coarse peer distance, per-participant mute and volume, microphone selection, audio processing, and aggregate call-quality checks are peer/client-side state. Room Radio sends a null position over the encrypted peer data channel, microphone identifiers and participant preferences never leave the local Isley client, and this signaling service remains unaware of every player's display name, location, PTT activity, audio device, volume, processing settings, WebRTC candidate details, jitter, round-trip time, and packet loss. Like any internet service, the reverse proxy and server can still observe the source network address of the WebSocket connection itself.
