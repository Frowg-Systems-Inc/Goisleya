const fs = require('fs');
const path = require('path');
const vm = require('vm');
const { webcrypto } = require('crypto');
const { TextEncoder, TextDecoder } = require('util');

const source = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'Voice', 'voice.js'),
  'utf8');

const check = (condition, message) => {
  if (!condition) throw new Error(message);
};

const flush = async (turns = 12) => {
  for (let index = 0; index < turns; index += 1) {
    await new Promise(resolve => setTimeout(resolve, 0));
  }
};

async function createHarness(outputSelectionSupported) {
  const posts = [];
  const audioElements = [];
  const socketInstances = [];
  let bridgeMessageHandler = null;
  let deviceChangeHandler = null;
  let availableDevices = [
    { kind: 'audioinput', deviceId: 'mic-1', label: 'USB Microphone' },
    { kind: 'audiooutput', deviceId: 'default', label: 'Windows Default' },
    { kind: 'audiooutput', deviceId: 'headset-2', label: 'USB Headset' },
    ...Array.from({ length: 16 }, (_, index) => ({
      kind: 'audiooutput',
      deviceId: `extra-${index + 1}`,
      label: `Extra Output ${index + 1}`
    }))
  ];

  function MockHtmlMediaElement() {}
  if (outputSelectionSupported) {
    MockHtmlMediaElement.prototype.setSinkId = async function setSinkId(deviceId) {
      if (deviceId === 'fail-output') throw new Error('simulated output failure');
      this.sinkId = deviceId;
      this.sinkHistory.push(deviceId);
    };
  }

  const audioRoot = {
    appendChild(audio) {
      audioElements.push(audio);
    }
  };
  const document = {
    getElementById(id) {
      if (id === 'state') return { textContent: '' };
      if (id === 'audio') return audioRoot;
      throw new Error(`Unexpected element lookup: ${id}`);
    },
    createElement(tag) {
      check(tag === 'audio', `Unexpected element creation: ${tag}`);
      const audio = Object.create(MockHtmlMediaElement.prototype);
      Object.assign(audio, {
        autoplay: false,
        playsInline: false,
        volume: 0,
        srcObject: null,
        sinkId: '',
        sinkHistory: [],
        play: async () => {},
        remove: () => {}
      });
      return audio;
    }
  };

  const track = {
    kind: 'audio',
    enabled: false,
    stop() {},
    clone() { return { ...this, stop() {} }; },
    getSettings() { return { deviceId: 'mic-1' }; },
    async applyConstraints() {}
  };
  const stream = {
    getAudioTracks: () => [track],
    getTracks: () => [track]
  };
  const mediaDevices = {
    async enumerateDevices() { return availableDevices.map(device => ({ ...device })); },
    async getUserMedia() { return stream; },
    addEventListener(type, handler) {
      if (type === 'devicechange') deviceChangeHandler = handler;
    }
  };

  class MockWebSocket {
    static OPEN = 1;
    static CLOSING = 2;

    constructor(url) {
      this.url = String(url);
      this.readyState = MockWebSocket.OPEN;
      this.sent = [];
      socketInstances.push(this);
    }

    send(payload) { this.sent.push(String(payload)); }
    close() { this.readyState = 3; }
  }

  class MockPeerConnection {
    constructor() {
      this.connectionState = 'new';
      this.iceConnectionState = 'new';
      this.localDescription = null;
      this.remoteDescription = null;
      this.senders = [];
    }

    addTrack(addedTrack) {
      const sender = {
        track: addedTrack,
        async replaceTrack(nextTrack) { this.track = nextTrack; }
      };
      this.senders.push(sender);
      return sender;
    }

    getSenders() { return this.senders; }
    createDataChannel() {
      return {
        readyState: 'open',
        send() {},
        close() {},
        onopen: null,
        onmessage: null
      };
    }
    async createOffer() { return { type: 'offer', sdp: 'v=0\r\n' }; }
    async createAnswer() { return { type: 'answer', sdp: 'v=0\r\n' }; }
    async setLocalDescription(description) { this.localDescription = description; }
    async setRemoteDescription(description) { this.remoteDescription = description; }
    async addIceCandidate() {}
    async getStats() { return new Map(); }
    close() { this.connectionState = 'closed'; }
  }

  const webview = {
    postMessage(message) { posts.push(structuredClone(message)); },
    addEventListener(type, handler) {
      if (type === 'message') bridgeMessageHandler = handler;
    }
  };
  const windowListeners = new Map();
  const windowObject = {
    chrome: { webview },
    IsleyVoiceCrypto: {
      async deriveSignalKey() { return { private: true }; },
      async sealSignal() {
        return { v: 1, iv: 'AAAAAAAAAAAAAAAA', ciphertext: 'BBBBBBBBBBBBBBBBBBBBBBBB' };
      },
      async openSignal() { return { description: { type: 'answer', sdp: 'v=0\r\n' } }; }
    },
    addEventListener(type, handler) { windowListeners.set(type, handler); }
  };

  const context = {
    window: windowObject,
    document,
    navigator: { mediaDevices },
    HTMLMediaElement: MockHtmlMediaElement,
    WebSocket: MockWebSocket,
    RTCPeerConnection: MockPeerConnection,
    crypto: webcrypto,
    TextEncoder,
    TextDecoder,
    URL,
    console,
    setTimeout,
    clearTimeout,
    setInterval,
    clearInterval,
    structuredClone,
    Math,
    Date,
    JSON,
    Map,
    Set,
    Array,
    Object,
    String,
    Number,
    Boolean,
    RegExp,
    Error,
    Promise
  };
  windowObject.window = windowObject;
  windowObject.document = document;
  windowObject.navigator = context.navigator;
  windowObject.crypto = webcrypto;
  windowObject.setTimeout = setTimeout;
  windowObject.clearTimeout = clearTimeout;
  windowObject.setInterval = setInterval;
  windowObject.clearInterval = clearInterval;

  vm.runInNewContext(source, context, { filename: 'voice.js' });
  check(typeof bridgeMessageHandler === 'function', 'voice bridge message handler was not registered');

  const sendCommand = data => bridgeMessageHandler({ data });
  sendCommand({
    type: 'connect',
    serverUrl: 'wss://voice.example.test/voice',
    peerId: '0'.repeat(32),
    roomSecret: 'a'.repeat(24),
    displayName: 'Output Tester',
    natAssist: false,
    proximityEnabled: false,
    micMeterEnabled: false,
    qualityMonitorEnabled: false,
    inputDeviceId: 'mic-1',
    outputDeviceId: 'headset-2'
  });
  await flush();
  check(socketInstances.length === 1, 'voice connection did not create its signaling socket');

  return {
    posts,
    audioElements,
    socket: socketInstances[0],
    sendCommand,
    setAvailableDevices(devices) { availableDevices = devices; },
    fireDeviceChange() { deviceChangeHandler?.(); }
  };
}

(async () => {
  const supported = await createHarness(true);
  const deviceReport = [...supported.posts]
    .reverse()
    .find(message => message.type === 'voice-devices' && message.state === 'READY');
  check(deviceReport, 'ready audio-device report was not published');
  check(deviceReport.outputSelectionSupported === true, 'supported output selection was not declared');
  check(deviceReport.outputDevices.length === 16, 'speaker output list was not bounded to sixteen');
  check(deviceReport.selectedOutputDeviceId === 'headset-2', 'requested speaker output was not selected');

  supported.socket.onopen?.();
  supported.socket.onmessage?.({
    data: JSON.stringify({
      type: 'welcome',
      peers: [{ id: 'f'.repeat(32) }]
    })
  });
  await flush();
  check(supported.audioElements.length === 1, 'remote peer audio element was not created');
  check(
    supported.audioElements[0].sinkHistory.includes('headset-2'),
    'new peer audio did not inherit the selected speaker output');
  check(
    supported.posts.some(message =>
      message.type === 'voice-status'
      && message.detail === '2 IN ROOM'
      && message.participantCount === 2),
    'welcome room count did not include the signaling peer');

  // Answerer path: peer-joined must count the remote peer before WebRTC is up.
  supported.socket.onmessage?.({
    data: JSON.stringify({
      from: 'a'.repeat(32),
      message: { type: 'peer-joined' }
    })
  });
  await flush();
  check(
    supported.posts.some(message =>
      message.type === 'voice-status'
      && message.detail === '3 IN ROOM'
      && message.participantCount === 3),
    'peer-joined room count undercounted before WebRTC peer creation');

  supported.sendCommand({ type: 'switch-output', deviceId: 'default' });
  await flush();
  check(
    supported.audioElements[0].sinkId === 'default',
    'existing peer audio did not move to the selected speaker output');
  check(
    supported.posts.some(message =>
      message.type === 'voice-output-device'
      && message.state === 'ACTIVE'
      && message.label === 'Windows Default'),
    'successful speaker switch was not reported to the native UI');

  supported.setAvailableDevices([
    { kind: 'audioinput', deviceId: 'mic-1', label: 'USB Microphone' },
    { kind: 'audiooutput', deviceId: 'default', label: 'Windows Default' }
  ]);
  supported.fireDeviceChange();
  await flush();
  supported.sendCommand({ type: 'switch-output', deviceId: 'headset-2' });
  await flush();
  check(
    supported.posts.some(message =>
      message.type === 'voice-output-device' && message.state === 'NOT FOUND'),
    'removed speaker output was not rejected');
  check(supported.audioElements[0].sinkId === 'default', 'removed output disturbed current playback');

  const signalingText = `${supported.socket.url}\n${supported.socket.sent.join('\n')}`;
  check(
    !signalingText.includes('headset-2')
    && !signalingText.includes('USB Headset')
    && !signalingText.includes('mic-1'),
    'audio hardware identity leaked to the signaling server');

  const unsupported = await createHarness(false);
  const unsupportedReport = [...unsupported.posts]
    .reverse()
    .find(message => message.type === 'voice-devices' && message.state === 'READY');
  check(
    unsupportedReport?.outputSelectionSupported === false,
    'unsupported output selection was not declared truthfully');
  unsupported.sendCommand({ type: 'switch-output', deviceId: 'headset-2' });
  await flush();
  check(
    unsupported.posts.some(message =>
      message.type === 'voice-output-device' && message.state === 'UNSUPPORTED'),
    'unsupported speaker switching did not fail closed');

  console.log(
    'Voice audio output: PASS (16-device bound, selected-output inheritance, live peer reroute, hot-plug refusal, unsupported fallback, and broker privacy)');
})().catch(error => {
  console.error(error.stack || error);
  process.exitCode = 1;
});
