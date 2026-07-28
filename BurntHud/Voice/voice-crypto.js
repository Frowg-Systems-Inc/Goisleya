((root, factory) => {
  const api = factory(
    root.crypto,
    root.TextEncoder,
    root.TextDecoder,
    root.btoa,
    root.atob);
  root.IsleyVoiceCrypto = api;
  if (typeof module === 'object' && module.exports) module.exports = api;
})(typeof globalThis === 'object' ? globalThis : window, (
  webCrypto,
  TextEncoderType,
  TextDecoderType,
  encodeBase64,
  decodeBase64
) => {
  'use strict';

  const EnvelopeVersion = 1;
  const MaximumPlaintextBytes = 32 * 1024;
  const MaximumCiphertextBytes = MaximumPlaintextBytes + 16;
  const MaximumCiphertextCharacters = 44 * 1024;
  const encoder = new TextEncoderType();
  const decoder = new TextDecoderType('utf-8', { fatal: true });
  const additionalData = encoder.encode('isley-voice-signal-envelope-v1');

  const bytesToBase64Url = bytes => {
    let binary = '';
    for (let offset = 0; offset < bytes.length; offset += 4096) {
      binary += String.fromCharCode(...bytes.subarray(offset, offset + 4096));
    }
    return encodeBase64(binary)
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/g, '');
  };

  const base64UrlToBytes = (value, maximumCharacters) => {
    const encoded = String(value || '');
    if (!encoded
        || encoded.length > maximumCharacters
        || !/^[A-Za-z0-9_-]+$/.test(encoded)) {
      throw new Error('INVALID_ENCRYPTED_SIGNAL');
    }
    const padding = '='.repeat((4 - encoded.length % 4) % 4);
    const binary = decodeBase64(encoded.replace(/-/g, '+').replace(/_/g, '/') + padding);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index += 1) {
      bytes[index] = binary.charCodeAt(index);
    }
    return bytes;
  };

  const normalizeSignalPayload = value => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
    const hasDescription = Object.prototype.hasOwnProperty.call(value, 'description');
    const hasCandidate = Object.prototype.hasOwnProperty.call(value, 'candidate');
    if (hasDescription === hasCandidate) return null;

    if (hasDescription) {
      const description = value.description;
      const type = String(description?.type || '');
      const sdp = typeof description?.sdp === 'string' ? description.sdp : '';
      if (!['offer', 'answer'].includes(type)
          || sdp.length < 1
          || sdp.length > 30 * 1024
          || sdp.includes('\0')
          || encoder.encode(sdp).length > MaximumPlaintextBytes) {
        return null;
      }
      return { description: { type, sdp } };
    }

    const candidate = value.candidate;
    const candidateText = typeof candidate?.candidate === 'string'
      ? candidate.candidate
      : '';
    const sdpMid = candidate?.sdpMid === null || candidate?.sdpMid === undefined
      ? null
      : String(candidate.sdpMid);
    const sdpMLineIndex = candidate?.sdpMLineIndex === null
        || candidate?.sdpMLineIndex === undefined
      ? null
      : Number(candidate.sdpMLineIndex);
    const usernameFragment = candidate?.usernameFragment === null
        || candidate?.usernameFragment === undefined
      ? null
      : String(candidate.usernameFragment);
    if (candidateText.length < 1
        || candidateText.length > 4096
        || candidateText.includes('\0')
        || (sdpMid !== null && (sdpMid.length > 128 || /[\u0000-\u001f\u007f]/.test(sdpMid)))
        || (sdpMLineIndex !== null
          && (!Number.isInteger(sdpMLineIndex) || sdpMLineIndex < 0 || sdpMLineIndex > 65535))
        || (usernameFragment !== null
          && (usernameFragment.length > 256
            || /[\u0000-\u001f\u007f]/.test(usernameFragment)))) {
      return null;
    }
    return {
      candidate: {
        candidate: candidateText,
        sdpMid,
        sdpMLineIndex,
        usernameFragment
      }
    };
  };

  const deriveSignalKey = async roomSecret => {
    const normalized = String(roomSecret || '').trim().toLowerCase();
    if (!/^[a-f0-9]{16,128}$/.test(normalized) || !webCrypto?.subtle) {
      throw new Error('INVALID_ROOM_KEY');
    }
    const digest = await webCrypto.subtle.digest(
      'SHA-256',
      encoder.encode(`isley-voice-signal-key-v1:${normalized}`));
    return webCrypto.subtle.importKey(
      'raw',
      digest,
      { name: 'AES-GCM' },
      false,
      ['encrypt', 'decrypt']);
  };

  const sealSignal = async (key, value) => {
    const normalized = normalizeSignalPayload(value);
    if (!key || !normalized) throw new Error('INVALID_SIGNAL_PAYLOAD');
    const plaintext = encoder.encode(JSON.stringify(normalized));
    if (plaintext.length < 2 || plaintext.length > MaximumPlaintextBytes) {
      throw new Error('INVALID_SIGNAL_PAYLOAD');
    }
    const iv = webCrypto.getRandomValues(new Uint8Array(12));
    const ciphertext = new Uint8Array(await webCrypto.subtle.encrypt(
      {
        name: 'AES-GCM',
        iv,
        additionalData,
        tagLength: 128
      },
      key,
      plaintext));
    return {
      v: EnvelopeVersion,
      iv: bytesToBase64Url(iv),
      ciphertext: bytesToBase64Url(ciphertext)
    };
  };

  const openSignal = async (key, envelope) => {
    if (!key
        || !envelope
        || typeof envelope !== 'object'
        || Array.isArray(envelope)
        || envelope.v !== EnvelopeVersion
        || Object.keys(envelope).sort().join(',') !== 'ciphertext,iv,v') {
      throw new Error('INVALID_ENCRYPTED_SIGNAL');
    }
    try {
      const iv = base64UrlToBytes(envelope.iv, 16);
      const ciphertext = base64UrlToBytes(
        envelope.ciphertext,
        MaximumCiphertextCharacters);
      if (iv.length !== 12
          || ciphertext.length < 17
          || ciphertext.length > MaximumCiphertextBytes) {
        throw new Error('INVALID_ENCRYPTED_SIGNAL');
      }
      const plaintext = new Uint8Array(await webCrypto.subtle.decrypt(
        {
          name: 'AES-GCM',
          iv,
          additionalData,
          tagLength: 128
        },
        key,
        ciphertext));
      if (plaintext.length < 2 || plaintext.length > MaximumPlaintextBytes) {
        throw new Error('INVALID_ENCRYPTED_SIGNAL');
      }
      const normalized = normalizeSignalPayload(JSON.parse(decoder.decode(plaintext)));
      if (!normalized) throw new Error('INVALID_ENCRYPTED_SIGNAL');
      return normalized;
    } catch {
      throw new Error('INVALID_ENCRYPTED_SIGNAL');
    }
  };

  return Object.freeze({
    EnvelopeVersion,
    MaximumPlaintextBytes,
    deriveSignalKey,
    normalizeSignalPayload,
    sealSignal,
    openSignal
  });
});
