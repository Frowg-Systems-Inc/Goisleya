const DOWNLOAD_PATH = "/Isley-Windows-x64.zip";
const SERVER_KIT_PATH = "/Isley-Server-Network.zip";
const KOFI_PATH = "https://ko-fi.com/theoneboundinink";
const ARCHIVE_SIZE = "8.55 MB";
const RELEASE_VERSION = "1.3.6";
const RELEASE_DATE = "July 26, 2026";
const SHA256 =
  "2E335F99A7FF4BC0D0D20EDE34F8CC271F724502AB885D5A8365EB3DC7D23EC9";

export default function Home() {
  return (
    <main>
      <nav className="nav" aria-label="Primary navigation">
        <a className="brand" href="#top" aria-label="Isley download home">
          <img src="/isley-triceratops-teeth-clean.png" alt="" width="42" height="42" />
          <span>ISLEY</span>
        </a>
        <div className="navActions">
          <a href="#network">Live Network</a>
          <a href="#install">Install help</a>
          <a href="#doctor">Install Doctor</a>
          <a href="#join">Join links</a>
          <a href={KOFI_PATH} target="_blank" rel="noreferrer">
            Ko-fi
          </a>
        </div>
      </nav>

      <section className="hero" id="top">
        <div className="heroCopy">
          <p className="eyebrow">THE ISLE COMPANION OVERLAY</p>
          <h1>Your map.<br />Your pack.<br /><span>Always in view.</span></h1>
          <p className="lede">
            Download the current Isley portable build for Windows. Live Map,
            universal server tools, Core Vitals, routes, survival guidance,
            private push-to-talk voice, wheel-friendly pages, and a persistent
            unlock-only click-through lock are included. Version 1.3 adds the
            opt-in Visible HUD Sensor with automatic HUD calibration and an
            explicit allowlisted visible-text reader for location and vitals.
            Captured pixels and raw text are discarded. Player Sync now smooths
            recent coordinate copies into course and speed. The
            independent Isley Live Network lets participating servers provide
            continuous positions, facing, vitals, sickness, friends, and AI
            through an authorized bridge and Steam-authenticated relay. The
            overlay shows latency, update rate, connected nodes, and whether
            awareness is consent-filtered or server-wide. Live Map uses the
            current MyIsleMap Gateway game-file basemap and one matching
            coordinate system for zones, 953 resources, roads, water, labels,
            routes, and player markers. It loads only nearby high-resolution
            tiles and identifies its bundled terrain honestly as a schematic
            offline fallback. Isley checks its trusted stable release channel
            automatically and shows an in-app Update &amp; Restart notification
            for verified releases. The server kit includes a secure guided
            bridge launcher that never writes credentials to disk.
          </p>
          <div className="ctaRow">
            <a
              className="downloadButton"
              href={DOWNLOAD_PATH}
              download="Isley-Windows-x64.zip"
            >
              <span>Download Isley</span>
              <small>Windows x64 · ZIP · {ARCHIVE_SIZE}</small>
            </a>
            <a className="secondaryButton" href="#install">
              How to install
            </a>
            <a
              className="secondaryButton"
              href={SERVER_KIT_PATH}
              download="Isley-Server-Network.zip"
            >
              Server operator kit
            </a>
          </div>
          <p className="releaseLine">
            Isley v{RELEASE_VERSION} · Updated {RELEASE_DATE} · Isley Live Network · Automatic update notifications · Stable latest-release link
          </p>
        </div>

        <div className="heroVisual" aria-label="Isley red Triceratops emblem">
          <div className="orbit orbitOne" />
          <div className="orbit orbitTwo" />
          <div className="logoGlow" />
          <img
            src="/isley-triceratops-teeth-clean.png"
            alt="Red Triceratops head, the Isley emblem"
            width="360"
            height="360"
          />
          <div className="statusChip statusLive">
            <span />
            LIVE MAP
          </div>
          <div className="statusChip statusVoice">
            <span />
            PTT VOICE
          </div>
          <div className="statusChip statusVitals">
            <span />
            CORE VITALS
          </div>
        </div>
      </section>

      <section className="trustBar" aria-label="Release details">
        <div><strong>EXTERNAL</strong><span>No injection or game modification</span></div>
        <div><strong>AUTO-UPDATE</strong><span>Verified in-app updates preserve your settings</span></div>
        <div><strong>LIVE NETWORK</strong><span>Authorized bridge, Steam sign-in, newest-frame delivery</span></div>
      </section>

      <section className="networkSection" id="network">
        <div className="sectionHeading">
          <p className="eyebrow">ISLEY 1.3</p>
          <h2>A live map network Isley controls.</h2>
          <p>
            Participating servers connect their own authorized plugin or private
            RCON to the Isley Server Bridge. The relay authenticates players
            through Steam and sends each client only the positions that server
            policy and player sharing permit.
          </p>
        </div>
        <div className="networkGrid">
          <article>
            <strong>FAST BY DESIGN</strong>
            <p>High-cadence plugin updates, automatic reconnect, and a newest-frame queue keep turns and movement current instead of replaying old positions.</p>
          </article>
          <article>
            <strong>VISIBLE HEALTH</strong>
            <p>See relay age, measured update rate, connected Isley nodes, visible entities, source capabilities, and server-wide or consent-filtered coverage.</p>
          </article>
          <article>
            <strong>PLAYER CONTROL</strong>
            <p>Steam friend sharing is optional. Add or remove one trusted SteamID64, keep raw IDs private, and never expose player IP addresses to other clients.</p>
          </article>
        </div>
        <div className="operatorPanel">
          <div>
            <strong>Run an Isle server?</strong>
            <p>The separate operator kit includes Isley Relay, Isley Server Bridge, a secure credential-prompting launcher, the sanctioned-interface request, and an example plugin payload.</p>
          </div>
          <a href={SERVER_KIT_PATH} download="Isley-Server-Network.zip">
            Download server kit
          </a>
        </div>
      </section>

      <section className="installSection" id="install">
        <div className="sectionHeading">
          <p className="eyebrow">QUICK START</p>
          <h2>Three steps. Then play.</h2>
          <p>Keep the extracted files together so the map, voice, and local settings work correctly.</p>
        </div>
        <ol className="steps">
          <li>
            <span>01</span>
            <div><strong>Download the ZIP</strong><p>Use the red download button above.</p></div>
          </li>
          <li>
            <span>02</span>
            <div><strong>Extract everything</strong><p>Right-click the ZIP and choose Extract All.</p></div>
          </li>
          <li>
            <span>03</span>
            <div><strong>Open Isley.exe</strong><p>Choose Live Map, Official, or Any Server. Future updates can install from Isley.</p></div>
          </li>
        </ol>
        <div className="requirements">
          <h3>Windows requirements</h3>
          <p>
            Windows 10 version 2004 or newer, or Windows 11. If prompted,
            install the official{" "}
            <a href="https://dotnet.microsoft.com/en-us/download/dotnet/8.0" target="_blank" rel="noreferrer">
              .NET 8 Desktop Runtime
            </a>{" "}
            and{" "}
            <a href="https://developer.microsoft.com/en-us/microsoft-edge/webview2/" target="_blank" rel="noreferrer">
              WebView2 Runtime
            </a>.
          </p>
        </div>
      </section>

      <section className="doctorSection" id="doctor">
        <div className="sectionHeading">
          <p className="eyebrow">INSTALL DOCTOR</p>
          <h2>Fix the usual blockers fast.</h2>
          <p>
            Isley is a portable Windows overlay. These checks cover the cases
            that look like a broken download but are usually runtime or folder
            layout issues.
          </p>
        </div>
        <div className="doctorGrid">
          <article>
            <strong>Isley will not start</strong>
            <p>
              Install the .NET 8 Desktop Runtime and WebView2 Runtime linked
              above, then relaunch <code>Isley.exe</code> from the extracted
              folder.
            </p>
          </article>
          <article>
            <strong>Blank Live Map</strong>
            <p>
              Keep <code>Map</code>, <code>Voice</code>, and{" "}
              <code>Updater</code> beside <code>Isley.exe</code>. Moving only
              the EXE breaks local map hosting.
            </p>
          </article>
          <article>
            <strong>No player marker</strong>
            <p>
              Use Asset Location copy from The Isle when your server is not on
              Isley Live Network. For participating servers, paste the join
              link and connect with Steam.
            </p>
          </article>
          <article>
            <strong>Voice cannot connect</strong>
            <p>
              Start with Local Host or a trusted voice URL, keep Streamer Mode
              and mic muted until connect, then enable NAT assist or your own
              TURN relay only if peers fail.
            </p>
          </article>
          <article>
            <strong>Windows Defender flags Isley</strong>
            <p>
              <code>Trojan:Win32/Wacatac.B!ml</code> is a machine-learning
              heuristic that often misfires on new unsigned portable apps.
              Verify the SHA-256 below, prefer this official download site, then
              allow the file in Protection history if the hash matches. Isley
              does not inject into The Isle or read game memory.
            </p>
          </article>
        </div>
      </section>

      <section className="joinSection" id="join">
        <div className="sectionHeading">
          <p className="eyebrow">LIVE NETWORK JOIN</p>
          <h2>Paste a join link. Connect with Steam.</h2>
          <p>
            Participating servers publish links shaped like{" "}
            <code>https://relay.example/join/your-server-id</code>. Open Isley
            → Tools → Isley Live Network, paste the link, then connect. Isley
            never asks for a Steam password or RCON secret.
          </p>
        </div>
        <div className="joinPanel">
          <div>
            <strong>Players</strong>
            <p>
              After connect, Live Health shows map freshness, relay age, and
              voice state. Friend visibility follows consent-filtered or
              server-wide policy set by the operator.
            </p>
          </div>
          <div>
            <strong>Operators</strong>
            <p>
              The server kit includes Relay, Bridge, setup scripts,{" "}
              <code>operator-console.html</code>, and{" "}
              <code>RCON_TO_PLUGIN.md</code> when you need facing, conditions,
              or AI beyond RCON limits.
            </p>
          </div>
        </div>
      </section>

      <section className="integrity">
        <div>
          <p className="eyebrow">FILE INTEGRITY</p>
          <h2>Verify your download</h2>
        </div>
        <code>{SHA256}</code>
        <p>SHA-256 · Isley-Windows-x64.zip</p>
      </section>

      <footer>
        <div className="footerBrand">
          <img src="/isley-triceratops-teeth-clean.png" alt="" width="34" height="34" />
          <strong>ISLEY</strong>
        </div>
        <p>
          Isley is an independent external companion. Its map remains separate
          from the server you join. Participating servers may optionally publish
          their own authorized live telemetry through Isley Server Bridge and
          Relay. When online, Live Map uses the attributed public{" "}
          <a href="https://myislemap.com/" target="_blank" rel="noreferrer">
            MyIsleMap Gateway game-file map
          </a>
          . Its schematic terrain remains available offline. Isley does not
          depend on another community&apos;s account, map, player panel, private
          API, or service.
        </p>
        <a href={KOFI_PATH} target="_blank" rel="noreferrer">
          Support theoneboundinink on Ko-fi
        </a>
      </footer>
    </main>
  );
}
