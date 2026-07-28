const fs = require('fs');
const http = require('http');
const https = require('https');
const path = require('path');

const port = Number(process.argv[2] || 9333);
const screenshotPath = process.argv[3] ? path.resolve(process.argv[3]) : '';
const endpoint = `http://127.0.0.1:${port}/json`;
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

const getCurrentGatewayMapVersion = () => new Promise((resolve, reject) => {
  const request = https.get('https://myislemap.com/', response => {
    if (response.statusCode !== 200) {
      reject(new Error(`MyIsleMap returned HTTP ${response.statusCode}.`));
      response.resume();
      return;
    }
    let body = '';
    response.setEncoding('utf8');
    response.on('data', chunk => { body += chunk; });
    response.on('end', () => {
      const match = body.match(
        /src=["'](?:https:\/\/myislemap\.com)?\/?map-data\.js\?v=([^"'&]+)[^"']*["']/i);
      const version = match?.[1] ?? '';
      if (!/^[A-Za-z0-9._-]{1,32}$/.test(version)) {
        reject(new Error('MyIsleMap did not advertise a valid current map-data version.'));
        return;
      }
      resolve(version);
    });
  });
  request.setTimeout(10000, () => request.destroy(
    new Error('Timed out while reading the current MyIsleMap version.')));
  request.on('error', reject);
});

const getTargets = () => new Promise((resolve, reject) => {
  http.get(endpoint, response => {
    let body = '';
    response.on('data', chunk => { body += chunk; });
    response.on('end', () => {
      try {
        resolve(JSON.parse(body));
      } catch (error) {
        reject(error);
      }
    });
  }).on('error', reject);
});

const connect = url => new Promise((resolve, reject) => {
  const socket = new WebSocket(url);
  socket.addEventListener('open', () => resolve(socket), { once: true });
  socket.addEventListener('error', reject, { once: true });
});

(async () => {
  const advertisedMapVersion = await getCurrentGatewayMapVersion();
  let target = null;
  for (let attempt = 0; attempt < 20 && !target; attempt += 1) {
    try {
      const targets = await getTargets();
      target = targets.find(candidate =>
        candidate.type === 'page' && candidate.url === 'https://isley.local/map/index.html');
    } catch {
      // The WebView diagnostics endpoint may still be starting.
    }
    if (!target) await delay(750);
  }
  if (!target?.webSocketDebuggerUrl) {
    throw new Error('The bundled Isley map target is not available.');
  }

  const socket = await connect(target.webSocketDebuggerUrl);
  const pending = new Map();
  const events = [];
  let requestId = 0;
  socket.addEventListener('message', message => {
    const payload = JSON.parse(message.data);
    if (payload.id) {
      pending.get(payload.id)?.(payload);
      pending.delete(payload.id);
    } else if (payload.method === 'Runtime.exceptionThrown'
        || payload.method === 'Log.entryAdded') {
      events.push(payload);
    }
  });
  const send = (method, params = {}) => new Promise(resolve => {
    const id = ++requestId;
    pending.set(id, resolve);
    socket.send(JSON.stringify({ id, method, params }));
  });
  await send('Runtime.enable');
  await send('Log.enable');
  await send('Page.enable');

  const inspect = async () => {
    const response = await send('Runtime.evaluate', {
      expression: `(() => {
        const localMap = window.__isleyLocalMap;
        localMap?.setSelf?.({ id: 'map-axis-check', x: -49000, y: 51000, yaw: 0 });
        const self = document.querySelector('[data-isley-role="self"] circle');
        const controller = window.__isley?.snapshot?.() ?? null;
        const roads = document.querySelector('[data-isley-current-terrain-network="true"]');
        const water = document.querySelector('[data-isley-current-water-mask="true"]');
        const currentBase = document.querySelector('#currentGatewayBaseMap');
        const currentLabels = document.querySelector('#currentMapLabels');
        return {
          readyState: document.readyState,
          localMap: localMap?.describe?.() ?? null,
          mapSource: {
            status: document.querySelector('#map')?.dataset?.mapSourceStatus ?? '',
            dataStatus: document.querySelector('#map')?.dataset?.mapDataStatus ?? '',
            labelsStatus: document.querySelector('#map')?.dataset?.currentLabelsStatus ?? '',
            baseHref: currentBase?.getAttribute('href') ?? '',
            labelCount: currentLabels?.childElementCount ?? 0,
            attributionHref: document.querySelector('#mapSourceLink')?.getAttribute('href') ?? '',
            highResolutionTileCount:
              document.querySelector('#currentGatewayTiles')?.childElementCount ?? 0
          },
          clarity: {
            mapKeyItems: document.querySelectorAll('#mapKey .map-key-item').length,
            mapKeyText: document.querySelector('#mapKey')?.textContent
              ?.replace(/\s+/g, ' ').trim() ?? '',
            hasReadabilityWash: Boolean(document.querySelector('#terrainReadabilityWash')),
            baseFilter: currentBase ? getComputedStyle(currentBase).filter : '',
            statusText: document.querySelector('#mapStatus')?.textContent ?? ''
          },
          selfMarker: self ? {
            x: Number(self.getAttribute('cx')),
            y: Number(self.getAttribute('cy')),
            fill: self.getAttribute('fill'),
            stroke: self.getAttribute('stroke')
          } : null,
          labels: {
            total: document.querySelectorAll('#currentMapLabels text').length,
            minor: document.querySelectorAll('#currentMapLabels .label-minor').length,
            water: document.querySelectorAll('#currentMapLabels .water-label').length
          },
          layers: {
            sanctuaries: document.querySelector('#layer-sanctuaries')?.childElementCount ?? 0,
            migrationCandidates:
              document.querySelector('#layer-migration')?.childElementCount ?? 0,
            patrolCandidates: document.querySelector('#layer-patrol')?.childElementCount ?? 0,
            resources: document.querySelector('#layer-food')?.childElementCount ?? 0,
            liveHeat: document.querySelector('#layer-heatmap')?.childElementCount ?? 0
          },
          terrain: {
            networkReady: controller?.terrainNetworkReady ?? false,
            pathCount: controller?.terrainNetworkPathCount ?? 0,
            pointCount: controller?.terrainNetworkPointCount ?? 0,
            sourceVersion: controller?.terrainNetworkSourceVersion ?? '',
            renderedSegments: roads?.childElementCount ?? 0,
            renderedRoads: roads?.querySelectorAll('[data-terrain-path-type="road"]').length ?? 0,
            renderedTrails: roads?.querySelectorAll('[data-terrain-path-type="trail"]').length ?? 0,
            fallbackOpacity: document.querySelector('#roads')?.style.opacity ?? '',
            waterStatus: controller?.terrainWaterMaskStatus ?? '',
            waterVersion: controller?.terrainWaterMaskSourceVersion ?? '',
            waterRendered: Boolean(water?.getAttribute('href')?.startsWith('data:image/webp;base64,'))
          }
        };
      })()`,
      returnByValue: true
    });
    if (response.result?.exceptionDetails) {
      throw new Error(`Runtime evaluation failed: ${JSON.stringify(response.result.exceptionDetails)}`);
    }
    return response.result?.result?.value ?? null;
  };

  let state = null;
  for (let attempt = 0; attempt < 20; attempt += 1) {
    state = await inspect();
    if (state?.terrain?.networkReady
        && state?.terrain?.renderedSegments > 0
        && state?.terrain?.waterStatus === 'ready'
        && state?.terrain?.waterRendered
        && state?.mapSource?.status === 'online'
        && state?.mapSource?.dataStatus === 'ready'
        && state?.mapSource?.labelsStatus === 'ready') break;
    await delay(750);
  }

  if (state?.localMap?.mapVersion !== advertisedMapVersion
      || state?.localMap?.swapAxes !== true
      || state?.localMap?.coordinateSpace !== 'gateway-current-gamefiles-2026-07-18'
      || state?.localMap?.referenceDate !== '2026-07-18'
      || state?.localMap?.source !== 'myislemap-current-gamefiles'
      || state?.localMap?.zoneCounts?.sanctuaries !== 7
      || state?.localMap?.zoneCounts?.migrationCandidates !== 12
      || state?.localMap?.zoneCounts?.patrolCandidates !== 61
      || state?.localMap?.resourceCount !== 953) {
    throw new Error(
      `Unexpected map description for advertised version ${advertisedMapVersion}: `
      + JSON.stringify(state?.localMap));
  }
  if (Math.abs(Number(state?.selfMarker?.x) - 500) > 0.001
      || Math.abs(Number(state?.selfMarker?.y) - 500) > 0.001
      || state?.selfMarker?.stroke !== '#38bdf8') {
    throw new Error(`Gateway center/marker calibration failed: ${JSON.stringify(state?.selfMarker)}`);
  }
  if (state?.labels?.total < 50 || state?.labels?.water < 20) {
    throw new Error(`The current Gateway labels are incomplete: ${JSON.stringify(state?.labels)}`);
  }
  if (state?.mapSource?.status !== 'online'
      || state?.mapSource?.dataStatus !== 'ready'
      || state?.mapSource?.labelsStatus !== 'ready'
      || state?.mapSource?.labelCount < 50
      || !state?.mapSource?.baseHref
        ?.startsWith('https://myislemap.com/assets/gateway-preview.webp')
      || state?.mapSource?.attributionHref !== 'https://myislemap.com/') {
    throw new Error(`The current online Gateway basemap failed: ${JSON.stringify(state?.mapSource)}`);
  }
  if (state?.clarity?.mapKeyItems !== 12
      || !state?.clarity?.mapKeyText?.includes('You + facing')
      || !state?.clarity?.mapKeyText?.includes('Drinkable water')
      || !state?.clarity?.mapKeyText?.includes('Sanctuary')
      || !state?.clarity?.mapKeyText?.includes('Migration')
      || !state?.clarity?.mapKeyText?.includes('Patrol')
      || !state?.clarity?.mapKeyText?.includes('Animals')
      || !state?.clarity?.mapKeyText?.includes('safe')
      || !state?.clarity?.mapKeyText?.includes('Other player')
      || !state?.clarity?.hasReadabilityWash
      || !state?.clarity?.baseFilter?.includes('saturate')
      || !state?.clarity?.statusText?.includes('FOLLOWING YOU')) {
    throw new Error(`Map clarity controls failed: ${JSON.stringify(state?.clarity)}`);
  }
  if (state?.layers?.sanctuaries !== 7
      || state?.layers?.migrationCandidates !== 12
      || state?.layers?.patrolCandidates !== 61
      || state?.layers?.resources !== 953
      || state?.layers?.liveHeat !== 0) {
    throw new Error(`Current map layers failed: ${JSON.stringify(state?.layers)}`);
  }
  if (!state?.terrain?.networkReady
      || state?.terrain?.pathCount < 1
      || state?.terrain?.pointCount < state.terrain.pathCount
      || state?.terrain?.renderedSegments < state.terrain.pathCount * 2
      || state?.terrain?.renderedRoads < 1
      || state?.terrain?.renderedTrails < 1
      || state?.terrain?.fallbackOpacity !== '0.28') {
    throw new Error(`Current road/trail rendering failed: ${JSON.stringify(state?.terrain)}`);
  }
  if (state?.terrain?.waterStatus !== 'ready' || !state?.terrain?.waterRendered) {
    throw new Error(`Current water rendering failed: ${JSON.stringify(state?.terrain)}`);
  }
  if (events.some(event => event.method === 'Runtime.exceptionThrown')) {
    throw new Error(`Runtime exception detected: ${JSON.stringify(events)}`);
  }

  const interactionResponse = await send('Runtime.evaluate', {
    expression: `(async () => {
      window.__isleyLocalMap?.setPlayers?.([
        { id: 'self', x: -49000, y: 51000, yaw: 0, self: true },
        { id: 'friend', x: -25000, y: 75000, yaw: 90, friend: true },
        { id: 'animal', x: -90000, y: 20000, yaw: 180 }
      ]);
      window.__isley?.setZoom?.(2.2);
      await new Promise(resolve => setTimeout(resolve, 450));
      const local = {
        tiles: document.querySelector('#currentGatewayTiles')?.childElementCount ?? 0,
        heat: document.querySelector('#layer-heatmap')?.childElementCount ?? 0
      };
      const cleanApplied = window.__isley?.applyLayerPreset?.('clean') ?? false;
      await new Promise(resolve => requestAnimationFrame(resolve));
      const cleanVisibility = [
        '#currentMapLabels', '#layer-sanctuaries', '#layer-migration',
        '#layer-patrol', '#layer-food', '#layer-heatmap'
      ].map(selector => document.querySelector(selector)?.getAttribute('visibility'));
      const allApplied = window.__isley?.applyLayerPreset?.('all') ?? false;
      await new Promise(resolve => requestAnimationFrame(resolve));
      const allVisibility = [
        '#currentMapLabels', '#layer-sanctuaries', '#layer-migration',
        '#layer-patrol', '#layer-food', '#layer-heatmap'
      ].map(selector => document.querySelector(selector)?.getAttribute('visibility'));
      window.__isley?.configure?.({ liteMode: true });
      window.__isley?.setZoom?.(2.2);
      await new Promise(resolve => setTimeout(resolve, 450));
      const liteTiles = document.querySelector('#currentGatewayTiles')?.childElementCount ?? 0;
      window.__isley?.configure?.({ liteMode: false });
      window.__isley?.applyLayerPreset?.('navigation');
      window.__isley?.setZoom?.(1);
      return {
        ...local,
        liteTiles,
        cleanApplied,
        allApplied,
        cleanVisibility,
        allVisibility
      };
    })()`,
    returnByValue: true,
    awaitPromise: true
  });
  const interaction = interactionResponse.result?.result?.value;
  if (!interaction?.cleanApplied || !interaction?.allApplied
      || interaction?.heat !== 2
      || interaction?.tiles < 1 || interaction?.tiles > 25
      || interaction?.liteTiles < 1 || interaction?.liteTiles > 12
      || interaction?.cleanVisibility?.some(value => value !== 'hidden')
      || interaction?.allVisibility?.some(value => value !== 'visible')) {
    throw new Error(`Map layer controls or tile LOD failed: ${JSON.stringify(interaction)}`);
  }

  if (screenshotPath) {
    fs.mkdirSync(path.dirname(screenshotPath), { recursive: true });
    const capture = await send('Page.captureScreenshot', {
      format: 'png',
      fromSurface: true,
      captureBeyondViewport: false
    });
    const encoded = capture.result?.data;
    if (!encoded) throw new Error('The runtime map screenshot was not captured.');
    fs.writeFileSync(screenshotPath, Buffer.from(encoded, 'base64'));
  }

  console.log(JSON.stringify({ state, events, screenshotPath: screenshotPath || null }, null, 2));
  console.log('Realistic Gateway map runtime: PASS');
  socket.close();
})().catch(error => {
  console.error(error.stack || error.message);
  process.exitCode = 1;
});
