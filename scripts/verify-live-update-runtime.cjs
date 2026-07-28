const http = require('http');

const port = Number(process.argv[2] || 9223);
const endpoint = `http://127.0.0.1:${port}/json`;

const fail = message => {
  throw new Error(message);
};

http.get(endpoint, response => {
  let body = '';
  response.on('data', chunk => {
    body += chunk;
  });
  response.on('end', () => {
    const target = JSON.parse(body).find(candidate =>
      candidate.type === 'page' && candidate.url === 'https://isley.local/map/index.html');
    if (!target?.webSocketDebuggerUrl) {
      fail('The bundled Isley map target is not available.');
    }

    const socket = new WebSocket(target.webSocketDebuggerUrl);
    socket.addEventListener('open', () => {
      socket.send(JSON.stringify({
        id: 1,
        method: 'Runtime.evaluate',
        params: {
          expression: `(async () => {
            window.__isleyLocalMap?.setPlayers?.([
              { id: 'runtime-self', label: 'You', x: -49000, y: 51000, yaw: 45, self: true },
              { id: 'runtime-friend', label: 'Friend', x: -1000, y: 100000, yaw: 90, friend: true },
              { id: 'runtime-animal', label: 'Animal', x: -110000, y: 10000, yaw: 180 }
            ]);
            await new Promise(resolve => setTimeout(resolve, 350));
            const recentered = window.__isley?.recenter?.() ?? false;
            const snapshot = window.__isley?.snapshot?.();
            return {
              version: window.__isley?.version ?? null,
              recentered,
              following: snapshot?.following ?? null,
              markerAvailable: snapshot?.markerAvailable ?? null,
              centerErrorPx: snapshot?.centerErrorPx ?? null,
              liteMode: snapshot?.liteMode ?? null,
              markerIntervalMs: snapshot?.fastPollIntervalMs ?? null,
              healthIntervalMs: snapshot?.playerSnapshotIntervalMs ?? null,
              healthInFlight: snapshot?.playerSnapshotInFlight ?? null,
              healthFailures: snapshot?.playerSnapshotFailures ?? null,
              markerResponseStatus: snapshot?.markerResponseStatus ?? null,
              markerResponseOk: snapshot?.markerResponseOk ?? null
              ,localMap: window.__isleyLocalMap?.describe?.() ?? null,
              markerRoles: Array.from(document.querySelectorAll('[data-isley-role]'))
                .map(marker => marker.dataset.isleyRole)
            };
          })()`,
          returnByValue: true,
          awaitPromise: true
        }
      }));
    });
    socket.addEventListener('message', message => {
      const payload = JSON.parse(message.data);
      if (payload.id !== 1) return;
      const result = payload.result?.result?.value;
      if (result?.version !== 77) fail(`Expected controller 77, received ${result?.version}.`);
      const expectedMarkerInterval = result.liteMode ? 1000 : 500;
      const expectedHealthInterval = result.liteMode ? 5000 : 2000;
      if (result.markerIntervalMs !== expectedMarkerInterval) {
        fail(`Expected ${expectedMarkerInterval}ms heading cadence, received ${result.markerIntervalMs}.`);
      }
      if (result.healthIntervalMs !== expectedHealthInterval) {
        fail(`Expected ${expectedHealthInterval}ms health cadence, received ${result.healthIntervalMs}.`);
      }
      if (result.localMap?.source !== 'myislemap-current-gamefiles'
          || !/^[A-Za-z0-9._-]{1,32}$/.test(result.localMap?.mapVersion ?? '')
          || result.localMap?.referenceDate !== '2026-07-18'
          || result.localMap?.resourceCount !== 953) {
        fail(`Expected the current attributed Gateway map, received ${JSON.stringify(result.localMap)}.`);
      }
      if (!result.recentered || !result.following || !result.markerAvailable) {
        fail('Expected the local player marker to be available and follow/recenter to be active.');
      }
      if (result.localMap?.playerCount !== 3 || result.localMap?.hasSelf !== true) {
        fail('Expected the independent provider self, friend, and animal fixtures.');
      }
      const markerRoles = [...(result.markerRoles ?? [])].sort().join(',');
      if (markerRoles !== 'friend,other,self') {
        fail(`Expected self, friend, and other marker roles; received ${markerRoles || 'none'}.`);
      }
      console.log(JSON.stringify(result, null, 2));
      console.log('Live update runtime: PASS');
      socket.close();
    });
  });
}).on('error', error => {
  fail(`Could not reach the Isley diagnostics port: ${error.message}`);
});
