# Windows Smoke Test Checklist

Run after pulling a new build onto a Windows machine (the Linux verification
environment compiles and contract-checks everything, but cannot execute the
WPF overlay). About 15 minutes.

## Previous batch (private servers + fixes)

1. **Private server connect** — copy a valid `https://<relay>/join/<server>`
   link, run Quick Command *Connect to private server* (`Ctrl+Shift+P` →
   "connect"). Expect: input auto-filled, CONNECTING toast, Steam sign-in tab,
   LINKED toast. Repeat with junk on the clipboard → expect the "copy your
   server's Isley link" toast and the Tools panel opening.
2. **Palette fuzzy search** — palette query `gc` should surface *Open Growth
   Clock*; `ssl` should surface *Start Safe Logout Guard*.
3. **Voice reconnect backoff** — connect voice, kill the bundled
   `Isley.VoiceServer` process. Expect reconnect attempts at ~5s then
   doubling gaps (watch status pill), and instant recovery once the host is
   back. Press Disconnect manually → no auto-reconnect.
4. **Crash reporter** — temporarily throw from a button handler (debug build)
   or kill via an induced error; expect `crash-*.txt` under
   `IsleyData\Logs` (portable) or `%LocalAppData%\Isley\Logs`.
5. **Multi-monitor restore** — move Isley to a secondary monitor, close,
   reopen → it must reopen on the secondary monitor. Disconnect that monitor,
   reopen → it must clamp back onto a visible screen.
6. **Tile retry** — block `myislemap.com` in hosts file mid-session; failed
   tiles disappear, then retry twice after ~4s/8s once unblocked.

## This batch (P1 features)

7. **Marker interpolation** — on a live server (or local fallback map with a
   moving position feed), other-player markers should glide between updates
   instead of teleporting; with Windows "reduce motion" enabled they snap
   (no transition).
8. **Pin share codes** — save a few pins, run *Copy pin share code*, paste the
   `ISLEYPINS1.` code into notepad. On a second machine/profile run *Import
   shared pins* with the code on the clipboard → pins appear; re-import →
   "NO NEW PINS". Corrupt a character → "SHARE CODE NOT VALID".
9. **Vitals low-boundary warnings** — with live vitals below 35% and falling,
   the trend warning should now read `WATER CRITICAL IN ABOUT Xm` (previously
   silent below 35%). Above 35% the wording is unchanged (`… LOW IN ABOUT …`).
10. **Encounter history** — trigger two encounter alerts (another player
    entering the alert radius), run *Copy encounter history* → clipboard has
    both timestamped entries; run it in a fresh session → "NO ENCOUNTERS
    RECORDED THIS SESSION".

## Results — 2026-07-29 live session (v1.4.0)

Ran on Windows against the packaged v1.4.0 portable build. Items checked
below are marked; anything not listed as checked remains unverified on this
build.

- [x] **Launch + Live Map render** — the map renders against the public feed
  and the status pill reads `GAME LIVE` (with the session-minute counter)
  once The Isle is running.
- [x] **Palette keystroke** — `Ctrl+Shift+P` opens Quick Commands (the fuzzy
  matches in item 2 were not re-checked this session).
- [x] **Live map diff (item 7, partial)** — a live-feed diff measured 2,144
  changed pixels over 45 s, consistent with continuous interpolation rather
  than a frozen view.
- [x] **Voice bundled-host auto-relaunch (item 3)** — killing the bundled
  `Isley.VoiceServer` process was followed by an automatic host relaunch and
  voice-session recovery (reconnect backoff timing not separately measured).
- [x] **Planner-state persistence** — `planner-state.json` under the portable
  `IsleyData` directory is written live as planner state changes.
- [x] **Updater exe drill 4/4** — full/orphan sweep, delta delete-list,
  traversal refusal, and source==target refusal all pass.
- [x] **VoiceServer live test 4/4** — `tests/test_voice_server_live.py` passes
  on the real Windows host.

Still unchecked (interactive items needing live gameplay or extra hardware):

- [ ] In-game marker glide smoothness and reduced-motion snap (item 7) with
  real movement.
- [ ] Route auto-replan on actual in-game deviation, including its toasts.
- [ ] Toast surfaces in play: nest timer alerts, Lite Mode suggestion, and
  the update notification.
- [ ] Two-peer live voice: PTT audibility, proximity fade, and per-peer
  volume/quality with real peers.
- [ ] Multi-monitor restore and clamp-back (item 5).
