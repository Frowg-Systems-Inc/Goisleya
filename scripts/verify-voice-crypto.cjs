const assert = require('node:assert/strict');
const fs = require('node:fs');
const nodeCrypto = require('node:crypto');
const path = require('node:path');

const cryptoModulePath = path.join(
  __dirname,
  '..',
  'BurntHud',
  'Voice',
  'voice-crypto.js');
const voiceCrypto = require(cryptoModulePath);
const cryptoSource = fs.readFileSync(cryptoModulePath, 'utf8').replace(/\r\n?/g, '\n');

// Fixed contract pins: the room-key KDF domain-separation prefix and the
// AES-GCM authentication tag length are cross-party compatibility contracts.
// A self-consistent rekey or tag shortening would still round trip, so they
// must be asserted literally AND behaviorally, not just by round trips.
assert.equal(
  cryptoSource.split('isley-voice-signal-key-v1:').length - 1,
  1,
  'room-key KDF must use exactly the isley-voice-signal-key-v1: domain prefix');
assert.equal(
  cryptoSource.split('tagLength: 128').length - 1,
  2,
  'AES-GCM must seal and open with a 128-bit authentication tag at both sites');

async function rejects(action, message) {
  let rejected = false;
  try {
    await action();
  } catch {
    rejected = true;
  }
  assert.equal(rejected, true, message);
}

async function main() {
  const roomSecret = 'abcdef0123456789abcdef01';
  const key = await voiceCrypto.deriveSignalKey(roomSecret);
  const sameKey = await voiceCrypto.deriveSignalKey(roomSecret.toUpperCase());
  const otherKey = await voiceCrypto.deriveSignalKey('111111111111111111111111');
  const offer = {
    description: {
      type: 'offer',
      sdp: 'v=0\r\na=candidate:1 1 UDP 2122260223 203.0.113.9 54321 typ srflx\r\n'
    }
  };

  const first = await voiceCrypto.sealSignal(key, offer);
  const second = await voiceCrypto.sealSignal(key, offer);
  assert.equal(first.v, 1, 'encrypted signaling envelope version');
  assert.match(first.iv, /^[A-Za-z0-9_-]{16}$/, '96-bit base64url IV');
  assert.notEqual(first.iv, second.iv, 'every signaling envelope uses a fresh IV');
  assert.notEqual(first.ciphertext, second.ciphertext, 'fresh IVs randomize ciphertext');
  const serialized = JSON.stringify(first);
  assert.equal(serialized.includes('candidate'), false, 'candidate text leaked into envelope');
  assert.equal(serialized.includes('203.0.113.9'), false, 'candidate address leaked into envelope');
  assert.deepEqual(await voiceCrypto.openSignal(key, first), offer, 'offer round trip');
  assert.deepEqual(await voiceCrypto.openSignal(sameKey, first), offer, 'normalized room-key round trip');
  await rejects(
    () => voiceCrypto.openSignal(otherKey, first),
    'another room key decrypted the signaling envelope');

  // Behavioral pin of the KDF domain prefix: a key derived independently as
  // SHA-256('isley-voice-signal-key-v1:' + normalized secret) must be
  // interchangeable with the module's derived key in both directions.
  const independentDigest = nodeCrypto.createHash('sha256')
    .update(`isley-voice-signal-key-v1:${roomSecret}`)
    .digest();
  const independentKey = await nodeCrypto.webcrypto.subtle.importKey(
    'raw',
    independentDigest,
    { name: 'AES-GCM' },
    false,
    ['encrypt', 'decrypt']);
  assert.deepEqual(
    await voiceCrypto.openSignal(independentKey, first),
    offer,
    'room-key KDF drifted from the pinned isley-voice-signal-key-v1: domain');
  assert.deepEqual(
    await voiceCrypto.openSignal(key, await voiceCrypto.sealSignal(independentKey, offer)),
    offer,
    'independently derived room key could not seal for this module');

  // Behavioral pin of the 128-bit tag: an AES-GCM ciphertext is exactly
  // plaintext + 16 bytes (128-bit tag); a shortened tag would shrink it.
  const offerPlaintextBytes = new TextEncoder().encode(
    JSON.stringify(voiceCrypto.normalizeSignalPayload(offer)));
  const sealedBytes = Buffer.from(
    first.ciphertext.replace(/-/g, '+').replace(/_/g, '/'),
    'base64');
  assert.equal(
    sealedBytes.length,
    offerPlaintextBytes.length + 16,
    'AES-GCM authentication tag is no longer 128 bits');

  const tampered = {
    ...first,
    ciphertext: `${first.ciphertext.slice(0, -1)}${first.ciphertext.endsWith('A') ? 'B' : 'A'}`
  };
  await rejects(
    () => voiceCrypto.openSignal(key, tampered),
    'tampered ciphertext passed AES-GCM authentication');
  await rejects(
    () => voiceCrypto.openSignal(key, { ...first, extra: true }),
    'an envelope with unexpected fields was accepted');

  const candidate = {
    candidate: {
      candidate: 'candidate:2 1 UDP 1686052607 relay.example 3478 typ relay',
      sdpMid: 'audio',
      sdpMLineIndex: 0,
      usernameFragment: 'bounded-fragment'
    }
  };
  assert.deepEqual(
    await voiceCrypto.openSignal(key, await voiceCrypto.sealSignal(key, candidate)),
    candidate,
    'ICE candidate round trip');
  assert.equal(
    voiceCrypto.normalizeSignalPayload({ description: offer.description, candidate: candidate.candidate }),
    null,
    'ambiguous signaling payload accepted');
  assert.equal(
    voiceCrypto.normalizeSignalPayload({
      description: { type: 'rollback', sdp: 'v=0' }
    }),
    null,
    'unsupported session description accepted');
  assert.equal(
    voiceCrypto.normalizeSignalPayload({
      candidate: { candidate: 'x'.repeat(4097) }
    }),
    null,
    'oversized ICE candidate accepted');

  console.log(
    'Voice crypto: PASS (pinned KDF domain and 128-bit tag, room-key derivation, '
    + 'AES-GCM sealing, random IVs, tamper/wrong-key refusal, bounded SDP/ICE, '
    + 'and plaintext-address privacy)');
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
