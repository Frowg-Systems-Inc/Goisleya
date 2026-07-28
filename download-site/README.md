# Isley download site

This is the public, reusable Windows download page for Isley.

The download button always targets:

`/Isley-Windows-x64.zip`

That path must remain stable. Every verified Isley release replaces the archive
at `public/Isley-Windows-x64.zip`, updates the displayed archive size and
SHA-256 checksum, and regenerates the trusted app update manifest at
`public/Isley-release.json`. The page, archive, manifest, and updater tests must
all pass before a new version of the existing Sites project is published.

The server-operator download remains stable at
`/Isley-Server-Network.zip`. It is packaged separately from the player app and
contains no relay secrets, RCON passwords, or runtime state.

From the download-site folder, update the site to the newest packaged Isley
release:

```powershell
npm run release:update
```

Or select an exact verified archive:

```powershell
npm run release:update -- -ArchivePath C:\path\to\Isley-Windows-x64.zip
```

Validation:

```powershell
cd download-site
npm test
```
