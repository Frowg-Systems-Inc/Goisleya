import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile, stat } from "node:fs/promises";
import test from "node:test";

const EXPECTED_CLIENT_SHA256 =
  "2E335F99A7FF4BC0D0D20EDE34F8CC271F724502AB885D5A8365EB3DC7D23EC9";
const EXPECTED_SERVER_SHA256 =
  "8CB26C6AE03288A491AB7A45584F61F1C8C82FDE8497B8D377718D1612573FE2";

async function render() {
  const workerUrl = new URL("../dist/server/index.js", import.meta.url);
  workerUrl.searchParams.set("test", `${process.pid}-${Date.now()}`);
  const { default: worker } = await import(workerUrl.href);

  return worker.fetch(
    new Request("http://localhost/", {
      headers: { accept: "text/html" },
    }),
    {
      ASSETS: {
        fetch: async () => new Response("Not found", { status: 404 }),
      },
    },
    {
      waitUntil() {},
      passThroughOnException() {},
    },
  );
}

test("server-renders the Isley download page", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);

  const html = await response.text();
  assert.match(html, /<title>Download Isley for Windows<\/title>/i);
  assert.match(html, /Download Isley/);
  assert.match(html, /href="\/Isley-Windows-x64\.zip"/);
  assert.match(html, /download="Isley-Windows-x64\.zip"/);
  assert.match(html, /href="\/Isley-Server-Network\.zip"/);
  assert.match(html, /download="Isley-Server-Network\.zip"/);
  assert.match(html, /Support theoneboundinink on Ko-fi/);
  assert.match(html, /current MyIsleMap Gateway game-file basemap/);
  assert.match(html, /953 resources/);
  assert.match(html, /schematic offline fallback/);
  assert.match(html, /MyIsleMap Gateway game-file map/);
  assert.match(html, /map remains separate from the server you join/);
  assert.match(html, /Isley Live Network/);
  assert.match(html, /continuous positions, facing, vitals, sickness, friends, and AI/);
  assert.match(html, /Steam-authenticated relay/);
  assert.match(html, /server-wide/);
  assert.match(html, /Player Control/i);
  assert.match(html, /automatic HUD calibration/i);
  assert.match(html, /allowlisted visible-text reader/i);
  assert.match(html, /course and speed/i);
  assert.match(html, /secure credential-prompting launcher/i);
  assert.match(html, /Stable latest-release link/);
  assert.match(html, /Automatic update notifications/);
  assert.match(html, /Update &amp; Restart notification/);
  assert.match(html, /Verified in-app updates preserve your settings/);
  assert.match(html, /Windows Defender flags Isley/);
  assert.match(html, /Trojan:Win32\/Wacatac\.B!ml/);
  assert.doesNotMatch(html, /codex-preview|react-loading-skeleton/i);
});

test("meets the accessibility and SEO baseline", async () => {
  const response = await render();
  const html = await response.text();

  // Document language and a single top-level heading.
  assert.match(html, /<html lang="en"/);
  assert.equal(html.match(/<h1[\s>]/g)?.length, 1);

  // Semantic landmarks: banner header with nav, one main, and a contentinfo
  // footer that is a body-level element rather than nested inside main.
  assert.equal(html.match(/<header[\s>]/g)?.length, 1);
  assert.equal(html.match(/<main[\s>]/g)?.length, 1);
  assert.equal(html.match(/<nav[\s>]/g)?.length, 1);
  assert.equal(html.match(/<footer[\s>]/g)?.length, 1);
  assert.ok(html.indexOf("</main>") < html.indexOf("<footer>"));
  assert.match(html, /<nav class="nav" aria-label="Primary navigation">/);

  // Skip link targets the main landmark for keyboard users.
  assert.match(html, /<a class="skipLink" href="#content">/);
  assert.match(html, /<main id="content">/);

  // Named content sections double as region landmarks.
  for (const label of [
    "Isley download overview",
    "Release details",
    "Isley Live Network",
    "Quick start installation",
    "Install Doctor troubleshooting",
    "Join the Isley Live Network",
    "Download file integrity",
  ]) {
    assert.match(html, new RegExp(`aria-label="${label}"`));
  }

  // Every rendered image carries an alt attribute (decorative images use "").
  const images = html.match(/<img\b[^>]*>/g) ?? [];
  assert.ok(images.length > 0);
  for (const image of images) {
    assert.match(image, /\salt="/);
  }

  // Meta and social cards are emitted for the page.
  assert.match(html, /<meta name="description" content="[^"]+"/);
  assert.match(html, /<meta property="og:title" content="[^"]+"/);
  assert.match(html, /<meta property="og:description" content="[^"]+"/);
  assert.match(html, /<meta property="og:image" content="[^"]+"/);
  assert.match(html, /<meta name="twitter:card" content="[^"]+"/);

  // Keyboard focus stays visible and motion preferences are honored in CSS.
  const cssUrl = new URL("../app/globals.css", import.meta.url);
  const css = await readFile(cssUrl, "utf8");
  assert.match(css, /a:focus-visible\s*\{[^}]*outline:\s*2px solid var\(--cyan\)/);
  assert.match(css, /\.skipLink:focus-visible/);
  assert.match(css, /@media \(prefers-reduced-motion: reduce\)/);
});

test("ships the Isley server network operator kit", async () => {
  const kitUrl = new URL("../public/Isley-Server-Network.zip", import.meta.url);
  const kit = await readFile(kitUrl);
  const kitStat = await stat(kitUrl);
  const sha256 = createHash("sha256").update(kit).digest("hex").toUpperCase();

  assert.ok(kitStat.size > 200_000);
  assert.ok(kitStat.size < 2_000_000);
  assert.equal(
    sha256,
    EXPECTED_SERVER_SHA256,
  );
});

test("ships the exact verified Windows archive", async () => {
  const archiveUrl = new URL("../public/Isley-Windows-x64.zip", import.meta.url);
  const archive = await readFile(archiveUrl);
  const archiveStat = await stat(archiveUrl);
  const sha256 = createHash("sha256").update(archive).digest("hex").toUpperCase();

  assert.ok(archiveStat.size > 7_000_000);
  assert.ok(archiveStat.size < 10_000_000);
  assert.equal(
    sha256,
    EXPECTED_CLIENT_SHA256,
  );

  const manifestUrl = new URL("../public/Isley-release.json", import.meta.url);
  const manifest = JSON.parse(await readFile(manifestUrl, "utf8"));
  assert.deepEqual(
    {
      manifestVersion: manifest.manifestVersion,
      channel: manifest.channel,
      version: manifest.version,
      downloadUrl: manifest.downloadUrl,
      sha256: manifest.sha256,
      bytes: manifest.bytes,
      required: manifest.required,
    },
    {
      manifestVersion: 1,
      channel: "stable",
      version: "1.3.6",
      downloadUrl:
        "https://isley-download.gmith.chatgpt.site/Isley-Windows-x64.zip",
      sha256,
      bytes: archiveStat.size,
      required: false,
    },
  );
  assert.equal(typeof manifest.notes, "string");
  assert.ok(manifest.notes.length >= 20);
  assert.ok(Number.isFinite(Date.parse(manifest.publishedAt)));
});
