// Syntax and behavior-contract checks for the browser overlay scripts that
// ship inside the Isley package. Fast enough to run on every push; the deep
// map-controller contracts live in verify-controller.cjs.
const fs = require("node:fs");
const path = require("node:path");

const root = path.join(__dirname, "..");
const read = relative => fs.readFileSync(path.join(root, relative), "utf8");

const scripts = [
  "BurntHud/Map/isley-map-controller.js",
  "BurntHud/Voice/voice.js",
  "BurntHud/Voice/voice-crypto.js"
];

for (const relative of scripts) {
  const source = read(relative);
  if (source.length < 1000) {
    throw new Error(`${relative} is unexpectedly small (${source.length} bytes).`);
  }
  try {
    new Function(source);
  } catch (error) {
    throw new Error(`${relative} failed the JavaScript syntax check: ${error.message}`);
  }
}

const controller = read(scripts[0]);
const voice = read(scripts[1]);
const voiceCrypto = read(scripts[2]);

const requiredControllerContracts = [
  ["installed controller identity", "window.__isley = api"],
  ["legacy bridge alias", "window.__theIsleMapper = api"],
  ["host snapshot bridge", "window.chrome?.webview?.postMessage"]
];
for (const [label, contract] of requiredControllerContracts) {
  if (!controller.includes(contract)) {
    throw new Error(`Map controller is missing ${label}: ${contract}`);
  }
}

const requiredVoiceContracts = [
  ["strict mode", "'use strict'"],
  ["fail-closed transmit gate", "track.enabled = transmitting"],
  ["microphone muted on teardown", "track.enabled = false"],
  ["glare-free offer ordering", "localPeerId.localeCompare(remoteId) < 0"]
];
for (const [label, contract] of requiredVoiceContracts) {
  if (!voice.includes(contract)) {
    throw new Error(`voice.js is missing ${label}: ${contract}`);
  }
}

// Sealed signaling must fail closed in BOTH branches: the crypto capability
// guard and the room-key derivation failure path. Each branch is asserted
// structurally so a regression in one cannot hide behind the duplicate
// literal in the other.
const sealedCapabilityGuard =
  /if \(!voiceCrypto\?\.deriveSignalKey\s*\|\|\s*!voiceCrypto\?\.sealSignal\s*\|\|\s*!voiceCrypto\?\.openSignal\)\s*\{\s*setState\('ERROR', 'ROOM ENCRYPTION UNAVAILABLE'\);/;
if (!sealedCapabilityGuard.test(voice)) {
  throw new Error(
    "voice.js is missing the sealed-signaling capability guard that fails closed with ROOM ENCRYPTION UNAVAILABLE.");
}
const sealedDerivationGuard =
  /signalingKey = await voiceCrypto\.deriveSignalKey\(roomSecret\);\s*\}\s*catch\s*\{\s*signalingKey = null;\s*setState\('ERROR', 'ROOM ENCRYPTION UNAVAILABLE'\);/;
if (!sealedDerivationGuard.test(voice)) {
  throw new Error(
    "voice.js is missing the room-key derivation failure path that fails closed with ROOM ENCRYPTION UNAVAILABLE.");
}

// Glare-free offer ordering guards TWO call sites (the welcome roster and the
// peer-joined branch). The exact occurrence count is pinned and each site is
// asserted structurally, so inverting one site cannot hide behind the
// duplicated literal in the other.
const glareOrderingNeedle = "localPeerId.localeCompare(remoteId) < 0";
if (voice.split(glareOrderingNeedle).length - 1 !== 2) {
  throw new Error(
    "voice.js must carry the glare-free offer ordering guard at exactly two call sites.");
}
const glareWelcomeGuard =
  /for \(const remote of payload\.peers \|\| \[\]\) \{\s*const remoteId = rememberPeerIdentity\(remote\.id, 'Isley Player'\);\s*if \(remoteId\) signalingPeers\.add\(remoteId\);\s*if \(remoteId && localPeerId\.localeCompare\(remoteId\) < 0\) await offerPeer\(remoteId\);/;
if (!glareWelcomeGuard.test(voice)) {
  throw new Error(
    "voice.js is missing the glare-free offer ordering guard in the welcome-roster branch.");
}
const glarePeerJoinedGuard =
  /if \(message\.type === 'peer-joined'\) \{\s*const remoteId = rememberPeerIdentity\(from, 'Isley Player'\);\s*if \(remoteId\) signalingPeers\.add\(remoteId\);\s*if \(remoteId && localPeerId\.localeCompare\(remoteId\) < 0\) await offerPeer\(remoteId\);/;
if (!glarePeerJoinedGuard.test(voice)) {
  throw new Error(
    "voice.js is missing the glare-free offer ordering guard in the peer-joined branch.");
}

const requiredVoiceCryptoContracts = [
  ["AES-GCM sealing", "AES-GCM"],
  ["seal API", "sealSignal"],
  ["open API", "openSignal"]
];
for (const [label, contract] of requiredVoiceCryptoContracts) {
  if (!voiceCrypto.includes(contract)) {
    throw new Error(`voice-crypto.js is missing ${label}: ${contract}`);
  }
}

console.log(
  "Overlay script verification passed (syntax + contracts for "
  + `${scripts.length} shipped scripts).`);
