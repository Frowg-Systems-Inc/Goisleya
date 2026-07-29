// Mutation-testing harness for the overlay contract suite.
//
// Copies the files each contract verifier reads into a temporary tree,
// applies ONE targeted in-memory mutation to a shipped overlay script, then
// spawns the unmodified contract verifiers against the mutated copy.
//
// Every mutation with `expect: "fail"` MUST make its verifier exit non-zero;
// a pass there means the contract suite has a hole. Probes with
// `expect: "pass"` document known false-pass weaknesses: they must keep
// passing (if a future contract improvement catches one, reclassify it as
// "fail"). Any mismatch fails this script.
//
// This script never modifies the shipped sources or the contract verifiers;
// all mutation happens on temp copies.
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const root = path.join(__dirname, '..');

const OVERLAY_SCRIPTS = [
  'BurntHud/Map/isley-map-controller.js',
  'BurntHud/Voice/voice.js',
  'BurntHud/Voice/voice-crypto.js'
];

const mutations = [
  {
    id: 'voice-transmit-gate-fail-open',
    file: 'BurntHud/Voice/voice.js',
    description: 'fail-closed transmit gate flipped to always-on',
    replace: [{ from: 'track.enabled = transmitting', to: 'track.enabled = true', occurrences: 1 }],
    verifiers: ['verify-overlay-scripts.cjs'],
    expect: 'fail'
  },
  {
    id: 'voice-glare-order-flipped-both-sites',
    file: 'BurntHud/Voice/voice.js',
    description: 'glare-free offer ordering inverted at both call sites',
    replace: [{
      from: 'localPeerId.localeCompare(remoteId) < 0',
      to: 'localPeerId.localeCompare(remoteId) > 0',
      occurrences: 2
    }],
    verifiers: ['verify-overlay-scripts.cjs'],
    expect: 'fail'
  },
  {
    id: 'voice-sealed-capability-guard-weakened',
    file: 'BurntHud/Voice/voice.js',
    description: 'sealed-signaling capability guard no longer fails closed',
    replace: [{
      from: 'if (!voiceCrypto?.deriveSignalKey',
      to: 'if (false && !voiceCrypto?.deriveSignalKey',
      occurrences: 1
    }],
    verifiers: ['verify-overlay-scripts.cjs'],
    expect: 'fail'
  },
  {
    id: 'controller-identity-removed',
    file: 'BurntHud/Map/isley-map-controller.js',
    description: 'installed controller identity removed',
    replace: [{ from: 'window.__isley = api', to: 'window.__isley_missing = api', occurrences: 1 }],
    verifiers: ['verify-overlay-scripts.cjs'],
    expect: 'fail'
  },
  {
    id: 'crypto-ice-candidate-cap-raised',
    file: 'BurntHud/Voice/voice-crypto.js',
    description: 'ICE candidate size cap raised from 4096 to 8192',
    replace: [{ from: 'candidateText.length > 4096', to: 'candidateText.length > 8192', occurrences: 1 }],
    verifiers: ['verify-voice-crypto.cjs'],
    expect: 'fail'
  },
  {
    id: 'crypto-envelope-extra-fields-allowed',
    file: 'BurntHud/Voice/voice-crypto.js',
    description: 'strict envelope field check relaxed to a length bound',
    replace: [{
      from: "Object.keys(envelope).sort().join(',') !== 'ciphertext,iv,v'",
      to: 'Object.keys(envelope).length > 8',
      occurrences: 1
    }],
    verifiers: ['verify-voice-crypto.cjs'],
    expect: 'fail'
  },
  {
    id: 'controller-reuse-gate-flipped',
    file: 'BurntHud/Map/isley-map-controller.js',
    description: 'controller reuse gate inverted so stale controllers win',
    replace: [{ from: 'existing?.version === 78', to: 'existing?.version !== 78', occurrences: 1 }],
    verifiers: ['verify-controller.cjs'],
    expect: 'fail'
  },
  {
    id: 'controller-nogo-vertex-cap-lowered',
    file: 'BurntHud/Map/isley-map-controller.js',
    description: 'no-go polygon vertex cap lowered from 12 to 8',
    replace: [{ from: 'noGoAreaMaximumVertices = 12', to: 'noGoAreaMaximumVertices = 8', occurrences: 1 }],
    verifiers: ['verify-controller.cjs'],
    expect: 'fail'
  },
  {
    id: 'controller-interaction-token-window-raised',
    file: 'BurntHud/Map/isley-map-controller.js',
    description: 'bounded map interaction token raised from 5s to 60s',
    replace: [{ from: 'elapsed <= 5000', to: 'elapsed <= 60000', occurrences: 1 }],
    verifiers: ['verify-controller.cjs'],
    expect: 'fail'
  },
  {
    id: 'crypto-kdf-prefix-rekeyed',
    file: 'BurntHud/Voice/voice-crypto.js',
    description: 'room-key KDF domain-separation prefix rekeyed (self-consistent)',
    replace: [{
      from: 'isley-voice-signal-key-v1:',
      to: 'isley-voice-signal-key-v9:',
      occurrences: 1
    }],
    verifiers: ['verify-voice-crypto.cjs', 'verify-overlay-scripts.cjs'],
    expect: 'fail'
  },
  {
    id: 'crypto-gcm-tag-shortened',
    file: 'BurntHud/Voice/voice-crypto.js',
    description: 'AES-GCM authentication tag shortened from 128 to 32 bits',
    replace: [{ from: 'tagLength: 128', to: 'tagLength: 32', occurrences: 2 }],
    verifiers: ['verify-voice-crypto.cjs', 'verify-overlay-scripts.cjs'],
    expect: 'fail'
  },
  {
    id: 'voice-glare-order-single-site',
    file: 'BurntHud/Voice/voice.js',
    description: 'glare ordering inverted at only one of two duplicated call sites',
    replace: [{
      from: 'localPeerId.localeCompare(remoteId) < 0',
      to: 'localPeerId.localeCompare(remoteId) > 0',
      occurrences: 2,
      apply: 'first'
    }],
    verifiers: ['verify-overlay-scripts.cjs'],
    expect: 'fail'
  },
  {
    id: 'controller-nogo-vertex-cap-raised',
    file: 'BurntHud/Map/isley-map-controller.js',
    description: 'no-go vertex cap raised 12 -> 120 (boundary-aware assertion must not prefix-match)',
    replace: [{ from: 'noGoAreaMaximumVertices = 12', to: 'noGoAreaMaximumVertices = 120', occurrences: 1 }],
    verifiers: ['verify-controller.cjs'],
    expect: 'fail'
  }
  // --- False-pass probes: document known contract-suite weaknesses. ---
  // A "pass" result here is EXPECTED and must be documented in
  // docs/VERIFIER-COVERAGE.md. If a probe starts failing, the suite got
  // stronger: move it above and expect "fail". All four probes documented in
  // the first mutation audit were reclassified as hard mutations above once
  // the contracts learned to catch them; none remain today.
];

const copyTree = (relativeDir, targetDir, options = {}) => {
  const sourceDir = path.join(root, relativeDir);
  fs.mkdirSync(targetDir, { recursive: true });
  for (const entry of fs.readdirSync(sourceDir, { withFileTypes: true })) {
    if (options.skip && options.skip(entry.name)) continue;
    const from = path.join(sourceDir, entry.name);
    const to = path.join(targetDir, entry.name);
    if (entry.isDirectory()) {
      copyTree(path.join(relativeDir, entry.name), to, options);
    } else {
      fs.copyFileSync(from, to);
    }
  }
};

const buildTree = (needsFullTree, mutation) => {
  const tree = fs.mkdtempSync(path.join(os.tmpdir(), 'isley-mutation-'));
  fs.mkdirSync(path.join(tree, 'scripts'), { recursive: true });
  fs.mkdirSync(path.join(tree, 'BurntHud', 'Map'), { recursive: true });
  fs.mkdirSync(path.join(tree, 'BurntHud', 'Voice'), { recursive: true });

  if (needsFullTree) {
    // verify-controller.cjs inspects most of BurntHud, several verifier
    // Program.cs files, the voice server, and Directory.Build.props, and it
    // spawns the voice verifiers from its own __dirname.
    copyTree('BurntHud', path.join(tree, 'BurntHud'),
      { skip: name => name.endsWith('.png') || name.endsWith('.ico') });
    copyTree('Verification', path.join(tree, 'Verification'),
      { skip: name => name === 'bin' || name === 'obj' });
    copyTree('Isley.VoiceServer', path.join(tree, 'Isley.VoiceServer'));
    fs.copyFileSync(
      path.join(root, 'Directory.Build.props'),
      path.join(tree, 'Directory.Build.props'));
    for (const script of [
      'verify-controller.cjs',
      'verify-voice-crypto.cjs',
      'verify-voice-audio-output.cjs'
    ]) {
      fs.copyFileSync(path.join(root, 'scripts', script), path.join(tree, 'scripts', script));
    }
  } else {
    for (const relative of OVERLAY_SCRIPTS) {
      fs.copyFileSync(path.join(root, relative), path.join(tree, relative));
    }
    for (const script of ['verify-overlay-scripts.cjs', 'verify-voice-crypto.cjs']) {
      fs.copyFileSync(path.join(root, 'scripts', script), path.join(tree, 'scripts', script));
    }
  }

  if (mutation) {
    const target = path.join(tree, mutation.file);
    let source = fs.readFileSync(target, 'utf8').replace(/\r\n?/g, '\n');
    for (const rule of mutation.replace) {
      const found = source.split(rule.from).length - 1;
      if (found !== rule.occurrences) {
        throw new Error(
          `Mutation ${mutation.id} drifted: expected ${rule.occurrences} occurrence(s) of ` +
          `${JSON.stringify(rule.from)} in ${mutation.file}, found ${found}.`);
      }
      if (rule.apply === 'first') {
        source = source.replace(rule.from, rule.to);
      } else {
        source = source.split(rule.from).join(rule.to);
      }
    }
    fs.writeFileSync(target, source);
  }
  return tree;
};

const runVerifier = (tree, script) => {
  const result = spawnSync(
    process.execPath,
    [path.join(tree, 'scripts', script)],
    { encoding: 'utf8' });
  return {
    ok: result.status === 0,
    output: `${result.stdout || ''}${result.stderr || ''}`.trim()
  };
};

const failures = [];
const results = [];

// Baseline control: unmutated copies of both tree shapes must pass every
// verifier, proving the harness itself is not the cause of any failure.
for (const needsFullTree of [false, true]) {
  const tree = buildTree(needsFullTree, null);
  const scripts = needsFullTree
    ? ['verify-controller.cjs']
    : ['verify-overlay-scripts.cjs', 'verify-voice-crypto.cjs'];
  for (const script of scripts) {
    const run = runVerifier(tree, script);
    if (!run.ok) {
      failures.push(`BASELINE ${script} failed on an unmutated temp copy:\n${run.output}`);
    }
  }
  fs.rmSync(tree, { recursive: true, force: true });
  results.push(`baseline ${needsFullTree ? 'verify-controller.cjs' : 'overlay+crypto'}: PASS`);
}

for (const mutation of mutations) {
  const needsFullTree = mutation.verifiers.includes('verify-controller.cjs');
  const tree = buildTree(needsFullTree, mutation);
  try {
    let caught = false;
    let detail = '';
    for (const script of mutation.verifiers) {
      const run = runVerifier(tree, script);
      const firstLine = run.output.split('\n').pop() || '';
      detail += ` [${script}: exit ${run.ok ? 0 : 1}]`;
      if (!run.ok) {
        caught = true;
        detail += ` — ${firstLine.slice(0, 160)}`;
        break;
      }
    }
    if (mutation.expect === 'fail' && !caught) {
      failures.push(
        `FALSE PASS ${mutation.id}: ${mutation.description} was NOT caught by ` +
        `${mutation.verifiers.join(' + ')}.`);
      results.push(`${mutation.id}: FALSE PASS (unexpected!)${detail}`);
    } else if (mutation.expect === 'pass' && caught) {
      failures.push(
        `PROBE NOW CAUGHT ${mutation.id}: ${mutation.description} now fails the suite. ` +
        `The contract suite improved; reclassify this mutation as expect:"fail".`);
      results.push(`${mutation.id}: caught (reclassify!)${detail}`);
    } else {
      results.push(
        `${mutation.id}: ${mutation.expect === 'fail' ? 'caught' : 'false pass (documented)'}${detail}`);
    }
  } finally {
    fs.rmSync(tree, { recursive: true, force: true });
  }
}

console.log('Mutation-check results:');
for (const line of results) {
  console.log(`  ${line}`);
}

if (failures.length > 0) {
  console.error('\nMutation contract check FAILED:');
  for (const failure of failures) {
    console.error(`  - ${failure}`);
  }
  process.exitCode = 1;
} else {
  const hard = mutations.filter(mutation => mutation.expect === 'fail').length;
  const probes = mutations.length - hard;
  console.log(
    `\nMutation contract check passed (${hard} mutations all caught, ` +
    `${probes} documented false-pass probes behaved as expected, baselines green).`);
}
