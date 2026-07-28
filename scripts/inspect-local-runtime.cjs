const http = require('http');

const port = Number(process.argv[2] || 9333);
const endpoint = `http://127.0.0.1:${port}/json`;

const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

const getTargets = () => new Promise((resolve, reject) => {
  http.get(endpoint, response => {
    let body = '';
    response.on('data', chunk => {
      body += chunk;
    });
    response.on('end', () => {
      try {
        resolve(JSON.parse(body));
      } catch (error) {
        reject(error);
      }
    });
  }).on('error', reject);
});

(async () => {
  const targets = await getTargets();
  const target = targets.find(candidate =>
    candidate.type === 'page' && candidate.url === 'https://isley.local/map/index.html');
  if (!target?.webSocketDebuggerUrl) {
    throw new Error('The bundled Isley map target is not available.');
  }

  const socket = new WebSocket(target.webSocketDebuggerUrl);
  const pending = new Map();
  const events = [];
  let requestId = 0;
  socket.addEventListener('message', message => {
    const payload = JSON.parse(message.data);
    if (payload.id) {
      pending.get(payload.id)?.(payload);
      pending.delete(payload.id);
    } else if (payload.method === 'Runtime.exceptionThrown'
        || payload.method === 'Log.entryAdded'
        || payload.method === 'Runtime.consoleAPICalled') {
      events.push(payload);
    }
  });
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });

  const send = (method, params = {}) => new Promise(resolve => {
    const id = ++requestId;
    pending.set(id, resolve);
    socket.send(JSON.stringify({ id, method, params }));
  });
  await send('Runtime.enable');
  await send('Log.enable');
  const result = await send('Runtime.evaluate', {
    expression: `(() => {
      const recentered = window.__isley?.recenter?.() ?? false;
      const snapshot = window.__isley?.snapshot?.() ?? null;
      return {
        href: location.href,
        readyState: document.readyState,
        controllerVersion: window.__isley?.version ?? null,
        controllerInstallCount: window.__isleyControllerInstallCount ?? 0,
        recentered,
        controllerSnapshot: snapshot,
        localMap: window.__isleyLocalMap?.describe?.() ?? null,
        mapSize: {
          width: document.querySelector('#map')?.clientWidth ?? null,
          height: document.querySelector('#map')?.clientHeight ?? null
        },
        markerRoles: Array.from(document.querySelectorAll('[data-isley-role]'))
          .map(marker => marker.dataset.isleyRole),
        bodyText: document.body?.innerText?.slice(0, 500) ?? ''
      };
    })()`,
    returnByValue: true
  });
  await delay(500);
  console.log(JSON.stringify({
    state: result.result?.result?.value ?? null,
    evaluationException: result.result?.exceptionDetails ?? null,
    events
  }, null, 2));
  socket.close();
})().catch(error => {
  console.error(error.stack || error.message);
  process.exitCode = 1;
});
