(() => {
  'use strict';

  const bridge = window.chrome?.webview;
  const voiceCrypto = window.IsleyVoiceCrypto;
  const stateLabel = document.getElementById('state');
  const audioRoot = document.getElementById('audio');
  const peers = new Map();
  const presences = new Map();
  const peerNames = new Map();
  const signalingPeers = new Set();
  const recentRouteOffers = new Map();
  const peerRouteOfferAt = new Map();
  let socket = null;
  let messageChain = Promise.resolve();
  let localStream = null;
  let localPosition = null;
  let localPeerId = '';
  let displayName = 'Isley Player';
  let roomSecret = '';
  let signalingKey = null;
  let transmitting = false;
  let deafened = false;
  let connectionRevision = 0;
  let deviceSwitchRevision = 0;
  let outputDeviceSwitchRevision = 0;
  let iceServers = [];
  let proximityEnabled = true;
  let proximityMaxDistance = 110;
  let echoCancellation = true;
  let noiseSuppression = true;
  let autoGainControl = true;
  let selectedInputDeviceId = '';
  let selectedOutputDeviceId = '';
  let micMeterEnabled = true;
  let micMeterRevision = 0;
  let micMeterTimer = null;
  let micMeterContext = null;
  let micMeterSource = null;
  let micMeterTrack = null;
  let qualityMonitorEnabled = true;
  let qualityTimer = null;
  let qualitySampleInFlight = false;
  const inboundQualityHistory = new Map();

  const post = (type, detail = {}) => bridge?.postMessage({ type, ...detail });
  const setState = (state, detail = '', participantCount = peers.size) => {
    stateLabel.textContent = state;
    post('voice-status', { state, detail, participantCount });
  };

  const validPeerId = value => /^[a-f0-9]{32}$/.test(String(value || ''));
  const normalizePeerName = (value, fallback = 'Isley Player') => {
    const normalized = String(value || '')
      .replace(/[^\p{L}\p{N} _.'-]/gu, '')
      .replace(/\s+/g, ' ')
      .trim()
      .slice(0, 32);
    return normalized || fallback;
  };
  const normalizeRouteOffer = value => {
    const offerId = String(value?.offerId || '').trim().toLowerCase();
    const routeText = String(value?.routeText || '');
    if (!/^[a-f0-9]{24}$/.test(offerId)
        || routeText.length < 1
        || routeText.length > 1600
        || /[\u0000-\u001f\u007f]/.test(routeText)
        || !/^Isley (?:route|breadcrumb return|road\/trail course) \| /.test(routeText)) {
      return null;
    }
    return { offerId, routeText };
  };
  const acceptRouteOfferFromPeer = (id, peer, message) => {
    const offer = normalizeRouteOffer(message);
    if (!offer) return;
    const now = Date.now();
    const key = `${id}:${offer.offerId}`;
    if (recentRouteOffers.has(key) || now - Number(peerRouteOfferAt.get(id) || 0) < 3000) return;
    recentRouteOffers.set(key, now);
    peerRouteOfferAt.set(id, now);
    for (const [seenKey, seenAt] of recentRouteOffers) {
      if (now - seenAt > 120000 || recentRouteOffers.size > 64) recentRouteOffers.delete(seenKey);
    }
    post('voice-route-offer', {
      peerId: id,
      peerName: normalizePeerName(peer.name || peerNames.get(id)),
      offerId: offer.offerId,
      routeText: offer.routeText
    });
  };
  const rememberPeerIdentity = (id, name) => {
    if (!validPeerId(id)) return '';
    peerNames.set(id, normalizePeerName(name));
    const peer = peers.get(id);
    if (peer) peer.name = peerNames.get(id);
    return id;
  };
  const postParticipants = () => {
    const participants = [...peers.entries()].slice(0, 31).map(([id, peer]) => {
      const remote = proximityEnabled ? presences.get(id) : null;
      const rawDistance = localPosition && remote
        ? Math.hypot(remote.x - localPosition.x, remote.y - localPosition.y)
        : Number.NaN;
      return {
        id,
        name: normalizePeerName(peer.name || peerNames.get(id)),
        muted: Boolean(peer.muted),
        volume: Math.max(0, Math.min(1, Number(peer.manualGain) || 0)),
        state: String(peer.connection?.connectionState || 'new').toUpperCase(),
        talking: Boolean(peer.talking),
        distance: Number.isFinite(rawDistance) ? Math.round(rawDistance / 5) * 5 : null
      };
    });
    post('voice-participants', { participants });
  };

  const validServerUrl = value => {
    try {
      const url = new URL(String(value || ''));
      if (url.protocol === 'http:') url.protocol = 'ws:';
      if (url.protocol === 'https:') url.protocol = 'wss:';
      const local = ['localhost', '127.0.0.1', '::1'].includes(url.hostname);
      if (url.protocol !== 'wss:' && !(url.protocol === 'ws:' && local)) return null;
      return url;
    } catch {
      return null;
    }
  };

  const validTurnConfig = config => {
    if (!config?.turnRelay) return null;
    const url = String(config.turnUrl || '').trim();
    const username = String(config.turnUsername || '').trim();
    const credential = String(config.turnCredential || '');
    const turnPattern = /^turns?:(?:\[[0-9a-f:]+\]|[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?)(?::[0-9]{1,5})?(?:\?transport=(?:udp|tcp))?$/i;
    if (!turnPattern.test(url)
        || username.length < 1 || username.length > 64
        || credential.length < 1 || credential.length > 128
        || /[\u0000-\u001f\u007f]/.test(`${url}${username}${credential}`)) return null;
    return { urls: url, username, credential };
  };

  const sha256 = async value => {
    const data = new TextEncoder().encode(value);
    const digest = await crypto.subtle.digest('SHA-256', data);
    return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
  };

  const stopMicMeter = (state = 'OFF') => {
    micMeterRevision += 1;
    if (micMeterTimer !== null) clearInterval(micMeterTimer);
    micMeterTimer = null;
    try { micMeterSource?.disconnect?.(); } catch {}
    micMeterSource = null;
    try { micMeterTrack?.stop?.(); } catch {}
    micMeterTrack = null;
    const context = micMeterContext;
    micMeterContext = null;
    if (context && context.state !== 'closed') void context.close().catch(() => {});
    post('voice-meter', { active: false, level: 0, clipped: false, state });
  };

  const startMicMeter = async () => {
    stopMicMeter('STARTING');
    const sourceTrack = localStream?.getAudioTracks?.()[0] || null;
    const AudioContext = window.AudioContext || window.webkitAudioContext;
    if (!micMeterEnabled || !sourceTrack || !AudioContext) {
      post('voice-meter', {
        active: false,
        level: 0,
        clipped: false,
        state: micMeterEnabled ? 'UNAVAILABLE' : 'OFF'
      });
      return;
    }

    const revision = micMeterRevision;
    try {
      // PTT disables the transmitted track. A separate enabled clone keeps this
      // local-only meter honest without connecting it to playback or any peer.
      micMeterTrack = sourceTrack.clone();
      micMeterTrack.enabled = true;
      micMeterContext = new AudioContext();
      micMeterSource = micMeterContext.createMediaStreamSource(new MediaStream([micMeterTrack]));
      const analyser = micMeterContext.createAnalyser();
      analyser.fftSize = 256;
      analyser.smoothingTimeConstant = 0.72;
      micMeterSource.connect(analyser);
      if (micMeterContext.state === 'suspended') await micMeterContext.resume();
      if (revision !== micMeterRevision) return;

      const samples = new Uint8Array(analyser.fftSize);
      micMeterTimer = setInterval(() => {
        if (revision !== micMeterRevision) return;
        analyser.getByteTimeDomainData(samples);
        let energy = 0;
        let peak = 0;
        for (const sample of samples) {
          const amplitude = Math.abs((sample - 128) / 128);
          energy += amplitude * amplitude;
          peak = Math.max(peak, amplitude);
        }
        const rms = Math.sqrt(energy / samples.length);
        const decibels = 20 * Math.log10(Math.max(rms, 0.000001));
        const level = Math.round(Math.max(0, Math.min(100, ((decibels + 60) / 54) * 100)));
        post('voice-meter', { active: true, level, clipped: peak >= 0.985, state: 'ACTIVE' });
      }, 125);
    } catch {
      if (revision === micMeterRevision) stopMicMeter('UNAVAILABLE');
    }
  };

  const stopLocalMedia = () => {
    stopMicMeter();
    for (const track of localStream?.getTracks?.() || []) track.stop();
    localStream = null;
  };

  const roomParticipantCount = () => signalingPeers.size + (localPeerId ? 1 : 0);

  const closePeer = id => {
    const peer = peers.get(id);
    if (!peer) return;
    peer.connection.ontrack = null;
    peer.connection.onicecandidate = null;
    peer.connection.close();
    peer.audio.remove();
    peers.delete(id);
    presences.delete(id);
    peerNames.delete(id);
    peerRouteOfferAt.delete(id);
    for (const key of inboundQualityHistory.keys()) {
      if (key.startsWith(`${id}:`)) inboundQualityHistory.delete(key);
    }
    for (const key of recentRouteOffers.keys()) {
      if (key.startsWith(`${id}:`)) recentRouteOffers.delete(key);
    }
    postParticipants();
  };

  const disconnect = (reason = 'DISCONNECTED') => {
    connectionRevision += 1;
    deviceSwitchRevision += 1;
    outputDeviceSwitchRevision += 1;
    const current = socket;
    socket = null;
    stopQualityMonitor('DISCONNECTED');
    if (current && current.readyState < WebSocket.CLOSING) current.close(1000, 'User disconnected');
    for (const id of [...peers.keys()]) closePeer(id);
    signalingPeers.clear();
    messageChain = Promise.resolve();
    stopLocalMedia();
    transmitting = false;
    iceServers = [];
    localPosition = null;
    roomSecret = '';
    signalingKey = null;
    recentRouteOffers.clear();
    peerRouteOfferAt.clear();
    post('voice-devices', {
      state: 'LOCKED',
      devices: [],
      selectedDeviceId: '',
      outputDevices: [],
      selectedOutputDeviceId,
      outputSelectionSupported: supportsOutputSelection()
    });
    postParticipants();
    setState('DISCONNECTED', reason, 0);
  };

  const send = payload => {
    if (socket?.readyState === WebSocket.OPEN) socket.send(JSON.stringify(payload));
  };

  const attenuate = (distance, maxDistance = proximityMaxDistance) => {
    if (!Number.isFinite(distance) || distance >= maxDistance) return 0;
    if (distance <= 12) return 1;
    const normalized = 1 - (distance - 12) / (maxDistance - 12);
    return Math.max(0, Math.min(1, normalized * normalized));
  };

  const updatePeerVolume = id => {
    const peer = peers.get(id);
    if (!peer) return;
    if (deafened) {
      peer.audio.volume = 0;
      return;
    }
    if (peer.muted) {
      peer.audio.volume = 0;
      return;
    }
    const manualGain = Math.max(0, Math.min(1, Number(peer.manualGain) || 0));
    if (!proximityEnabled) {
      peer.audio.volume = manualGain;
      return;
    }
    const remote = presences.get(id);
    if (!localPosition || !remote) {
      peer.audio.volume = 0;
      return;
    }
    const distance = Math.hypot(remote.x - localPosition.x, remote.y - localPosition.y);
    peer.audio.volume = attenuate(distance) * manualGain;
  };

  const applyParticipantSettings = config => {
    const peerId = String(config.peerId || '').trim().toLowerCase();
    const peer = validPeerId(peerId) ? peers.get(peerId) : null;
    if (!peer) return;
    peer.muted = Boolean(config.muted);
    const volume = Number(config.volume);
    if (Number.isFinite(volume)) peer.manualGain = Math.max(0, Math.min(1, volume));
    updatePeerVolume(peerId);
    postParticipants();
  };

  const updatePreferences = config => {
    const qualityWasEnabled = qualityMonitorEnabled;
    proximityEnabled = config.proximityEnabled !== false;
    const requestedDistance = Number(config.proximityMaxDistance);
    proximityMaxDistance = Number.isFinite(requestedDistance)
      ? Math.max(25, Math.min(250, requestedDistance))
      : 110;
    echoCancellation = config.echoCancellation !== false;
    noiseSuppression = config.noiseSuppression !== false;
    autoGainControl = config.autoGainControl !== false;
    micMeterEnabled = config.micMeterEnabled !== false;
    qualityMonitorEnabled = config.qualityMonitorEnabled !== false;
    if (!proximityEnabled) localPosition = null;
    if (qualityWasEnabled !== qualityMonitorEnabled && socket?.readyState === WebSocket.OPEN) {
      if (qualityMonitorEnabled) startQualityMonitor();
      else stopQualityMonitor('OFF');
    }
  };

  const applyAudioProcessing = async () => {
    const tracks = localStream?.getAudioTracks?.() || [];
    if (!tracks.length) return;
    try {
      await Promise.all(tracks.map(track => track.applyConstraints({
        echoCancellation,
        noiseSuppression,
        autoGainControl
      })));
      post('voice-processing', { state: 'ACTIVE' });
    } catch {
      post('voice-processing', { state: 'NEXT CONNECTION' });
    }
  };

  const normalizeDeviceId = value => {
    const normalized = String(value || '').trim();
    return normalized.length > 0 && normalized.length <= 512
      && !/[\u0000-\u001f\u007f]/.test(normalized)
      ? normalized
      : '';
  };

  const supportsOutputSelection = () =>
    typeof HTMLMediaElement !== 'undefined'
    && typeof HTMLMediaElement.prototype.setSinkId === 'function';

  const audioConstraints = deviceId => ({
    echoCancellation,
    noiseSuppression,
    autoGainControl,
    ...(deviceId ? { deviceId: { exact: deviceId } } : {})
  });

  const reportAudioDevices = async (state = 'READY') => {
    try {
      const enumerated = await navigator.mediaDevices.enumerateDevices();
      const available = enumerated
        .filter(device => device.kind === 'audioinput' && normalizeDeviceId(device.deviceId))
        .slice(0, 16)
        .map((device, index) => ({
          id: normalizeDeviceId(device.deviceId),
          label: String(device.label || `Microphone ${index + 1}`).trim().slice(0, 80)
        }));
      const outputDevices = enumerated
        .filter(device => device.kind === 'audiooutput' && normalizeDeviceId(device.deviceId))
        .slice(0, 16)
        .map((device, index) => ({
          id: normalizeDeviceId(device.deviceId),
          label: String(device.label || `Speaker ${index + 1}`).trim().slice(0, 80)
        }));
      const activeId = normalizeDeviceId(
        localStream?.getAudioTracks?.()[0]?.getSettings?.().deviceId || selectedInputDeviceId);
      if (activeId) selectedInputDeviceId = activeId;
      if (outputDevices.length
          && !outputDevices.some(device => device.id === selectedOutputDeviceId)) {
        selectedOutputDeviceId =
          outputDevices.find(device => device.id === 'default')?.id
          || outputDevices[0].id;
      }
      post('voice-devices', {
        state,
        devices: available,
        selectedDeviceId: selectedInputDeviceId,
        outputDevices,
        selectedOutputDeviceId,
        outputSelectionSupported: supportsOutputSelection()
      });
      return { inputDevices: available, outputDevices };
    } catch {
      post('voice-devices', {
        state: 'FAILED',
        devices: [],
        selectedDeviceId: '',
        outputDevices: [],
        selectedOutputDeviceId,
        outputSelectionSupported: supportsOutputSelection()
      });
      return { inputDevices: [], outputDevices: [] };
    }
  };

  const switchInputDevice = async requestedId => {
    if (!localStream) {
      post('voice-device', { state: 'DISCONNECTED' });
      return;
    }
    const deviceId = normalizeDeviceId(requestedId);
    if (!deviceId) {
      post('voice-device', { state: 'NOT FOUND' });
      return;
    }

    const available = await navigator.mediaDevices.enumerateDevices().catch(() => []);
    const target = available.find(device => device.kind === 'audioinput' && device.deviceId === deviceId);
    if (!target) {
      post('voice-device', { state: 'NOT FOUND' });
      await reportAudioDevices();
      return;
    }

    const switchRevision = ++deviceSwitchRevision;
    const activeConnectionRevision = connectionRevision;
    setPtt(false);
    post('voice-device', { state: 'SWITCHING' });
    let nextStream = null;
    const replacedSenders = [];
    const previousTrack = localStream.getAudioTracks()[0] || null;
    try {
      nextStream = await navigator.mediaDevices.getUserMedia({
        audio: audioConstraints(deviceId),
        video: false
      });
      if (switchRevision !== deviceSwitchRevision
          || activeConnectionRevision !== connectionRevision
          || !localStream) {
        for (const track of nextStream.getTracks()) track.stop();
        return;
      }
      const nextTrack = nextStream.getAudioTracks()[0];
      if (!nextTrack) throw new Error('No audio track');
      nextTrack.enabled = false;
      for (const peer of peers.values()) {
        for (const sender of peer.connection.getSenders()) {
          if (sender.track?.kind !== 'audio') continue;
          await sender.replaceTrack(nextTrack);
          replacedSenders.push(sender);
        }
      }
      const previousStream = localStream;
      localStream = nextStream;
      nextStream = null;
      selectedInputDeviceId = normalizeDeviceId(nextTrack.getSettings?.().deviceId) || deviceId;
      if (micMeterEnabled) void startMicMeter();
      else stopMicMeter();
      for (const track of previousStream.getTracks()) track.stop();
      await reportAudioDevices();
      post('voice-device', {
        state: 'ACTIVE',
        label: String(target.label || 'Selected microphone').trim().slice(0, 80)
      });
    } catch {
      if (previousTrack) {
        for (const sender of replacedSenders) {
          await sender.replaceTrack(previousTrack).catch(() => {});
        }
      }
      for (const track of nextStream?.getTracks?.() || []) track.stop();
      post('voice-device', { state: 'FAILED' });
      await reportAudioDevices();
    }
  };

  const applyOutputDevice = async (audio, deviceId) => {
    if (!supportsOutputSelection()) throw new Error('Output selection unsupported');
    const normalized = normalizeDeviceId(deviceId);
    if (!normalized) throw new Error('Output device invalid');
    await audio.setSinkId(normalized);
  };

  const switchOutputDevice = async requestedId => {
    if (!localStream) {
      post('voice-output-device', { state: 'DISCONNECTED' });
      return;
    }
    if (!supportsOutputSelection()) {
      post('voice-output-device', { state: 'UNSUPPORTED' });
      return;
    }
    const deviceId = normalizeDeviceId(requestedId);
    if (!deviceId) {
      post('voice-output-device', { state: 'NOT FOUND' });
      return;
    }

    const available = await navigator.mediaDevices.enumerateDevices().catch(() => []);
    const target = available.find(device =>
      device.kind === 'audiooutput' && device.deviceId === deviceId);
    if (!target) {
      post('voice-output-device', { state: 'NOT FOUND' });
      await reportAudioDevices();
      return;
    }

    const switchRevision = ++outputDeviceSwitchRevision;
    const activeConnectionRevision = connectionRevision;
    const previousDeviceId = selectedOutputDeviceId;
    const changedAudio = [];
    selectedOutputDeviceId = deviceId;
    post('voice-output-device', { state: 'SWITCHING' });
    try {
      for (const peer of peers.values()) {
        if (switchRevision !== outputDeviceSwitchRevision
            || activeConnectionRevision !== connectionRevision
            || !localStream) return;
        await applyOutputDevice(peer.audio, deviceId);
        changedAudio.push(peer.audio);
        if (switchRevision !== outputDeviceSwitchRevision) {
          await applyOutputDevice(peer.audio, selectedOutputDeviceId).catch(() => {});
          return;
        }
      }
      if (switchRevision !== outputDeviceSwitchRevision
          || activeConnectionRevision !== connectionRevision
          || !localStream) return;
      await reportAudioDevices();
      post('voice-output-device', {
        state: 'ACTIVE',
        label: String(target.label || 'Selected speaker').trim().slice(0, 80)
      });
    } catch {
      if (switchRevision !== outputDeviceSwitchRevision
          || activeConnectionRevision !== connectionRevision
          || !localStream) return;
      selectedOutputDeviceId = previousDeviceId;
      if (previousDeviceId) {
        for (const audio of changedAudio) {
          await applyOutputDevice(audio, previousDeviceId).catch(() => {});
        }
      }
      post('voice-output-device', { state: 'FAILED' });
      await reportAudioDevices();
    }
  };

  const attachPresenceChannel = (id, peer, channel) => {
    peer.channel = channel;
    channel.onopen = () => {
      channel.send(JSON.stringify({ type: 'profile', name: displayName }));
      const sharedPosition = proximityEnabled ? localPosition : null;
      channel.send(JSON.stringify(sharedPosition
        ? { type: 'position', ...sharedPosition }
        : { type: 'position', x: null, y: null }));
      channel.send(JSON.stringify({ type: 'ptt', transmitting }));
    };
    channel.onmessage = event => {
      try {
        if (typeof event.data !== 'string' || event.data.length > 2048) return;
        const message = JSON.parse(event.data);
        if (message.type === 'ptt') {
          peer.talking = Boolean(message.transmitting);
          postParticipants();
          return;
        }
        if (message.type === 'profile') {
          peer.name = normalizePeerName(message.name);
          peerNames.set(id, peer.name);
          postParticipants();
          return;
        }
        if (message.type === 'route-offer') {
          acceptRouteOfferFromPeer(id, peer, message);
          return;
        }
        const x = Number(message.x);
        const y = Number(message.y);
        if (proximityEnabled && message.type === 'position' && Number.isFinite(x) && Number.isFinite(y)) {
          presences.set(id, { x, y });
        } else {
          presences.delete(id);
        }
        updatePeerVolume(id);
        postParticipants();
      } catch {
        presences.delete(id);
        updatePeerVolume(id);
        postParticipants();
      }
    };
    channel.onclose = () => {
      presences.delete(id);
      peer.talking = false;
      postParticipants();
    };
  };

  const broadcastPosition = () => {
    const sharedPosition = proximityEnabled ? localPosition : null;
    const payload = JSON.stringify(sharedPosition
      ? { type: 'position', ...sharedPosition }
      : { type: 'position', x: null, y: null });
    for (const peer of peers.values()) {
      if (peer.channel?.readyState === 'open') peer.channel.send(payload);
    }
  };

  const broadcastPtt = () => {
    const payload = JSON.stringify({ type: 'ptt', transmitting });
    for (const peer of peers.values()) {
      if (peer.channel?.readyState === 'open') peer.channel.send(payload);
    }
  };

  const broadcastRouteOffer = config => {
    const offer = normalizeRouteOffer(config);
    if (!offer) {
      post('voice-route-sent', { offerId: '', recipientCount: 0, state: 'INVALID' });
      return;
    }
    const payload = JSON.stringify({ type: 'route-offer', ...offer });
    let recipientCount = 0;
    for (const peer of peers.values()) {
      if (peer.channel?.readyState !== 'open') continue;
      peer.channel.send(payload);
      recipientCount += 1;
    }
    post('voice-route-sent', {
      offerId: offer.offerId,
      recipientCount,
      state: recipientCount > 0 ? 'SENT' : 'NO PEERS'
    });
  };

  const reportPeerNetwork = async (id, connection) => {
    let route = '';
    try {
      const stats = await connection.getStats();
      let selectedPair = null;
      for (const report of stats.values()) {
        if (report.type === 'transport' && report.selectedCandidatePairId) {
          selectedPair = stats.get(report.selectedCandidatePairId) || selectedPair;
        } else if (report.type === 'candidate-pair' && report.state === 'succeeded'
            && (report.selected || report.nominated)) {
          selectedPair ||= report;
        }
      }
      const local = selectedPair ? stats.get(selectedPair.localCandidateId) : null;
      const remote = selectedPair ? stats.get(selectedPair.remoteCandidateId) : null;
      const types = [local?.candidateType, remote?.candidateType]
        .filter(type => ['host', 'srflx', 'prflx', 'relay'].includes(type));
      route = types.includes('relay')
        ? 'TURN RELAY'
        : types.some(type => type === 'srflx' || type === 'prflx')
          ? 'DIRECT · NAT'
          : types.length ? 'DIRECT · LOCAL' : '';
    } catch {
      route = '';
    }
    post('voice-network', {
      peer: id,
      state: String(connection.iceConnectionState || connection.connectionState || 'new').toUpperCase(),
      route
    });
    postParticipants();
  };

  const boundedQualityMetric = (value, maximum) => {
    const number = Number(value);
    return Number.isFinite(number) && number >= 0
      ? Math.min(maximum, number)
      : null;
  };

  const reportVoiceQuality = async () => {
    if (!qualityMonitorEnabled || socket?.readyState !== WebSocket.OPEN || qualitySampleInFlight) return;
    qualitySampleInFlight = true;
    const revision = connectionRevision;
    let peerCount = 0;
    let sampleCount = 0;
    const roundTrips = [];
    const jitters = [];
    const losses = [];
    try {
      for (const [id, peer] of peers) {
        const connection = peer.connection;
        if (!connection || connection.connectionState !== 'connected') continue;
        peerCount += 1;
        try {
          const stats = await connection.getStats();
          let selectedPair = null;
          let peerHasSample = false;
          for (const report of stats.values()) {
            if (report.type === 'transport' && report.selectedCandidatePairId) {
              selectedPair = stats.get(report.selectedCandidatePairId) || selectedPair;
            } else if (report.type === 'candidate-pair' && report.state === 'succeeded'
                && (report.selected || report.nominated)) {
              selectedPair ||= report;
            }
            if (report.type === 'remote-inbound-rtp'
                && (report.kind === 'audio' || report.mediaType === 'audio')) {
              const roundTrip = boundedQualityMetric(Number(report.roundTripTime) * 1000, 5000);
              if (roundTrip !== null) {
                roundTrips.push(roundTrip);
                peerHasSample = true;
              }
            }
            if (report.type !== 'inbound-rtp'
                || report.isRemote
                || !((report.kind === 'audio') || (report.mediaType === 'audio'))) continue;
            const jitter = boundedQualityMetric(Number(report.jitter) * 1000, 1000);
            if (jitter !== null) {
              jitters.push(jitter);
              peerHasSample = true;
            }
            const packetsLost = boundedQualityMetric(report.packetsLost, Number.MAX_SAFE_INTEGER);
            const packetsReceived = boundedQualityMetric(report.packetsReceived, Number.MAX_SAFE_INTEGER);
            if (packetsLost === null || packetsReceived === null) continue;
            const historyKey = `${id}:${report.id}`;
            const previous = inboundQualityHistory.get(historyKey);
            inboundQualityHistory.set(historyKey, { lost: packetsLost, received: packetsReceived });
            const intervalLost = previous ? Math.max(0, packetsLost - previous.lost) : packetsLost;
            const intervalReceived = previous
              ? Math.max(0, packetsReceived - previous.received)
              : packetsReceived;
            const intervalPackets = intervalLost + intervalReceived;
            if ((previous && intervalPackets >= 20) || (!previous && intervalPackets >= 100)) {
              losses.push(Math.min(100, intervalLost / intervalPackets * 100));
              peerHasSample = true;
            }
          }
          const candidateRoundTrip = boundedQualityMetric(
            Number(selectedPair?.currentRoundTripTime) * 1000,
            5000);
          if (candidateRoundTrip !== null) {
            roundTrips.push(candidateRoundTrip);
            peerHasSample = true;
          }
          if (peerHasSample) sampleCount += 1;
        } catch {}
      }
      if (revision !== connectionRevision || !qualityMonitorEnabled) return;
      post('voice-quality', {
        peerCount,
        sampleCount,
        roundTripMilliseconds: roundTrips.length ? Math.max(...roundTrips) : null,
        jitterMilliseconds: jitters.length ? Math.max(...jitters) : null,
        packetLossPercent: losses.length ? Math.max(...losses) : null
      });
    } finally {
      qualitySampleInFlight = false;
    }
  };

  const stopQualityMonitor = (state = 'OFF') => {
    if (qualityTimer !== null) clearInterval(qualityTimer);
    qualityTimer = null;
    inboundQualityHistory.clear();
    post('voice-quality', {
      peerCount: 0,
      sampleCount: 0,
      roundTripMilliseconds: null,
      jitterMilliseconds: null,
      packetLossPercent: null,
      state
    });
  };

  const startQualityMonitor = () => {
    stopQualityMonitor('CALIBRATING');
    if (!qualityMonitorEnabled || socket?.readyState !== WebSocket.OPEN) return;
    void reportVoiceQuality();
    qualityTimer = setInterval(() => void reportVoiceQuality(), 3000);
  };

  const sendEncryptedSignal = async (to, data) => {
    if (!validPeerId(to) || !signalingKey || !voiceCrypto) return;
    const revision = connectionRevision;
    const key = signalingKey;
    try {
      const sealed = await voiceCrypto.sealSignal(key, data);
      if (revision !== connectionRevision || key !== signalingKey) return;
      send({ type: 'signal', to, sealed });
    } catch {
      if (revision === connectionRevision) setState('ERROR', 'SIGNAL ENCRYPTION FAILED');
    }
  };

  const createPeer = id => {
    if (peers.has(id)) return peers.get(id);
    const connection = new RTCPeerConnection({ iceServers });
    const audio = document.createElement('audio');
    audio.autoplay = true;
    audio.playsInline = true;
    audio.volume = 0;
    if (selectedOutputDeviceId) {
      void applyOutputDevice(audio, selectedOutputDeviceId).catch(() => {});
    }
    audioRoot.appendChild(audio);
    for (const track of localStream?.getAudioTracks?.() || []) connection.addTrack(track, localStream);
    connection.ondatachannel = event => attachPresenceChannel(id, peer, event.channel);
    connection.onicecandidate = event => {
      if (event.candidate) {
        void sendEncryptedSignal(id, { candidate: event.candidate });
      }
    };
    connection.ontrack = event => {
      audio.srcObject = event.streams[0];
      void audio.play().catch(() => {});
      updatePeerVolume(id);
    };
    connection.onconnectionstatechange = () => {
      if (['failed', 'closed'].includes(connection.connectionState)) closePeer(id);
      else postParticipants();
      const count = Math.max(1, roomParticipantCount());
      setState('CONNECTED', `${count} IN ROOM`, count);
    };
    connection.oniceconnectionstatechange = () => {
      void reportPeerNetwork(id, connection);
    };
    const peer = {
      connection,
      audio,
      channel: null,
      pendingCandidates: [],
      name: peerNames.get(id) || 'Isley Player',
      muted: false,
      manualGain: 1,
      talking: false
    };
    peers.set(id, peer);
    postParticipants();
    return peer;
  };

  const offerPeer = async id => {
    const peer = createPeer(id);
    if (!peer.channel) {
      attachPresenceChannel(id, peer, peer.connection.createDataChannel('isley-position', { ordered: false }));
    }
    const offer = await peer.connection.createOffer({ offerToReceiveAudio: true });
    await peer.connection.setLocalDescription(offer);
    await sendEncryptedSignal(id, { description: peer.connection.localDescription });
  };

  const addIceCandidateSafely = async (peer, candidate) => {
    try {
      await peer.connection.addIceCandidate(candidate);
    } catch {
      // Ignore superseded or malformed ICE candidates so one bad candidate
      // cannot tear down an otherwise healthy private room.
    }
  };

  const handleSignal = async (from, data) => {
    if (!validPeerId(from) || !data) return;
    const peer = createPeer(from);
    if (data.description) {
      await peer.connection.setRemoteDescription(data.description);
      for (const candidate of peer.pendingCandidates.splice(0)) {
        await addIceCandidateSafely(peer, candidate);
      }
      if (data.description.type === 'offer') {
        const answer = await peer.connection.createAnswer();
        await peer.connection.setLocalDescription(answer);
        await sendEncryptedSignal(from, { description: peer.connection.localDescription });
      }
    } else if (data.candidate) {
      if (peer.connection.remoteDescription) {
        await addIceCandidateSafely(peer, data.candidate);
      } else {
        peer.pendingCandidates.push(data.candidate);
      }
    }
  };

  const handleEncryptedSignal = async (from, sealed) => {
    if (!validPeerId(from) || !signalingKey || !voiceCrypto) {
      throw new Error('SIGNALING KEY UNAVAILABLE');
    }
    const revision = connectionRevision;
    const key = signalingKey;
    const data = await voiceCrypto.openSignal(key, sealed);
    if (revision !== connectionRevision || key !== signalingKey) return;
    await handleSignal(from, data);
  };

  const handleMessage = async event => {
    let payload;
    try { payload = JSON.parse(event.data); } catch { return; }
    if (payload.type === 'welcome') {
      signalingPeers.clear();
      for (const remote of payload.peers || []) {
        const remoteId = rememberPeerIdentity(remote.id, 'Isley Player');
        if (remoteId) signalingPeers.add(remoteId);
        if (remoteId && localPeerId.localeCompare(remoteId) < 0) await offerPeer(remoteId);
      }
      postParticipants();
      const count = roomParticipantCount();
      setState('CONNECTED', `${count} IN ROOM`, count);
      return;
    }
    const from = payload.from;
    const message = payload.message;
    if (!message || !from) return;
    if (message.type === 'peer-joined') {
      const remoteId = rememberPeerIdentity(from, 'Isley Player');
      if (remoteId) signalingPeers.add(remoteId);
      if (remoteId && localPeerId.localeCompare(remoteId) < 0) await offerPeer(remoteId);
      postParticipants();
      const count = roomParticipantCount();
      setState('CONNECTED', `${count} IN ROOM`, count);
    } else if (message.type === 'peer-left') {
      signalingPeers.delete(from);
      closePeer(from);
      const count = Math.max(1, roomParticipantCount());
      setState('CONNECTED', `${count} IN ROOM`, count);
    } else if (message.type === 'signal') {
      await handleEncryptedSignal(from, message.sealed);
    }
  };

  const setPtt = held => {
    const next = Boolean(held) && socket?.readyState === WebSocket.OPEN;
    const changed = next !== transmitting;
    transmitting = next;
    for (const track of localStream?.getAudioTracks?.() || []) track.enabled = transmitting;
    if (changed) broadcastPtt();
    post('voice-ptt', { transmitting });
  };

  const connect = async config => {
    disconnect('RECONNECTING');
    const revision = connectionRevision;
    const parsed = validServerUrl(config.serverUrl);
    if (!parsed) {
      setState('ERROR', 'USE WSS, OR WS ON LOCALHOST');
      return;
    }
    if (!/^[a-f0-9]{32}$/.test(config.peerId || '') || String(config.roomSecret || '').length < 16) {
      setState('ERROR', 'ROOM IDENTITY INVALID');
      return;
    }
    const turnRelay = validTurnConfig(config);
    if (config.turnRelay && !turnRelay) {
      setState('ERROR', 'TURN RELAY CONFIG INVALID');
      return;
    }
    if (!voiceCrypto?.deriveSignalKey
        || !voiceCrypto?.sealSignal
        || !voiceCrypto?.openSignal) {
      setState('ERROR', 'ROOM ENCRYPTION UNAVAILABLE');
      return;
    }

    localPeerId = config.peerId;
    displayName = normalizePeerName(config.displayName);
    roomSecret = config.roomSecret;
    selectedInputDeviceId = normalizeDeviceId(config.inputDeviceId);
    selectedOutputDeviceId = normalizeDeviceId(config.outputDeviceId) || selectedOutputDeviceId;
    updatePreferences(config);
    iceServers = config.natAssist
      ? [{ urls: 'stun:stun.cloudflare.com:3478' }]
      : [];
    if (turnRelay) iceServers.push(turnRelay);
    setState('CONNECTING', 'SECURING PRIVATE ROOM');
    try {
      signalingKey = await voiceCrypto.deriveSignalKey(roomSecret);
    } catch {
      signalingKey = null;
      setState('ERROR', 'ROOM ENCRYPTION UNAVAILABLE');
      return;
    }
    if (revision !== connectionRevision) {
      signalingKey = null;
      return;
    }
    setState('CONNECTING', 'REQUESTING MICROPHONE');
    try {
      localStream = await navigator.mediaDevices.getUserMedia({
        audio: audioConstraints(selectedInputDeviceId),
        video: false
      });
      if (revision !== connectionRevision) return stopLocalMedia();
      for (const track of localStream.getAudioTracks()) track.enabled = false;
      selectedInputDeviceId = normalizeDeviceId(
        localStream.getAudioTracks()[0]?.getSettings?.().deviceId) || selectedInputDeviceId;
      if (micMeterEnabled) void startMicMeter();
      await reportAudioDevices();
      const room = await sha256(`isley-voice-v1:${roomSecret}`);
      parsed.searchParams.set('room', room);
      parsed.searchParams.set('peer', localPeerId);
      socket = new WebSocket(parsed);
      socket.onopen = () => {
        setState('CONNECTED', 'HOLD PTT TO TALK', 1);
        startQualityMonitor();
      };
      messageChain = Promise.resolve();
      socket.onmessage = event => {
        // Serialize signaling so concurrent SDP/ICE handling cannot race the
        // WebRTC state machine into InvalidStateError and drop the room.
        messageChain = messageChain
          .then(() => handleMessage(event))
          .catch(() => disconnect('SIGNALING MESSAGE FAILED'));
      };
      socket.onerror = () => setState('ERROR', 'SIGNALING CONNECTION FAILED');
      socket.onclose = event => {
        if (socket) disconnect(event.reason || 'VOICE SERVER CLOSED');
      };
    } catch (error) {
      stopLocalMedia();
      setState('ERROR', error?.name === 'NotAllowedError'
        ? 'MICROPHONE PERMISSION DENIED'
        : selectedInputDeviceId ? 'SELECTED MICROPHONE UNAVAILABLE' : 'MICROPHONE UNAVAILABLE');
    }
  };

  bridge?.addEventListener('message', event => {
    const command = event.data || {};
    if (command.type === 'connect') void connect(command);
    else if (command.type === 'disconnect') disconnect('USER DISCONNECTED');
    else if (command.type === 'ptt') setPtt(command.held);
    else if (command.type === 'deafen') {
      deafened = Boolean(command.enabled);
      for (const id of peers.keys()) updatePeerVolume(id);
      post('voice-deafen', { deafened });
    } else if (command.type === 'position') {
      const hasPosition = command.x !== null && command.x !== undefined
        && command.y !== null && command.y !== undefined;
      const x = hasPosition ? Number(command.x) : Number.NaN;
      const y = hasPosition ? Number(command.y) : Number.NaN;
      localPosition = proximityEnabled && Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
      broadcastPosition();
      for (const id of peers.keys()) updatePeerVolume(id);
      postParticipants();
    } else if (command.type === 'preferences') {
      const meterWasEnabled = micMeterEnabled;
      updatePreferences(command);
      if (meterWasEnabled !== micMeterEnabled) {
        if (micMeterEnabled && localStream) void startMicMeter();
        else stopMicMeter();
      }
      if (!proximityEnabled) presences.clear();
      broadcastPosition();
      for (const id of peers.keys()) updatePeerVolume(id);
      postParticipants();
      void applyAudioProcessing();
    } else if (command.type === 'enumerate-devices') {
      if (localStream) void reportAudioDevices();
      else post('voice-devices', {
        state: 'LOCKED',
        devices: [],
        selectedDeviceId: '',
        outputDevices: [],
        selectedOutputDeviceId,
        outputSelectionSupported: supportsOutputSelection()
      });
    } else if (command.type === 'switch-input') {
      void switchInputDevice(command.deviceId);
    } else if (command.type === 'switch-output') {
      void switchOutputDevice(command.deviceId);
    } else if (command.type === 'participant-settings') {
      applyParticipantSettings(command);
    } else if (command.type === 'send-route-offer') {
      broadcastRouteOffer(command);
    }
  });

  navigator.mediaDevices?.addEventListener?.('devicechange', () => {
    if (localStream) void reportAudioDevices();
  });
  window.addEventListener('blur', () => setPtt(false));
  window.addEventListener('beforeunload', () => disconnect('PAGE CLOSED'));
  setState('READY', 'MICROPHONE OFF UNTIL CONNECT');
})();
