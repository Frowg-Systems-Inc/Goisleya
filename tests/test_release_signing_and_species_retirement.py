"""Regression checks: retired species parsing, Azure removal, and cert signing."""

from pathlib import Path
import subprocess

import yaml

ROOT = Path("/app")
CONTROLLER = ROOT / "BurntHud/Map/isley-map-controller.js"
WORKFLOW = ROOT / ".github/workflows/release-package.yml"
PACKAGE_SCRIPT = ROOT / "scripts/package-isley-1.3.ps1"

RETIRED_IDENTIFIERS = (
    "parsePlayerSnapshotDocument",
    "snapshotSpeciesCatalog",
    "normalizeSnapshotSpeciesToken",
    "parseSnapshotSpeciesId",
    "findExactSnapshotLeaf",
    "lastKnownPlayerSnapshotIntervalMs",
)
AZURE_MARKERS = (
    "AZURE_",
    "Trusted Signing",
    "Trusted.Signing",
    "ISLEY_CODE_SIGN_DLIB",
    "ISLEY_CODE_SIGN_METADATA",
    "azure/login",
    "codesigning.azure.net",
    "timestamp.acs.microsoft.com",
)


def load_workflow():
    # BaseLoader prevents YAML 1.1 from coercing GitHub's `on` key to True.
    return yaml.load(WORKFLOW.read_text(encoding="utf-8"), Loader=yaml.BaseLoader)


def package_steps():
    return load_workflow()["jobs"]["package"]["steps"]


def named_step(name):
    return next(step for step in package_steps() if step.get("name") == name)


def test_retired_species_dom_chain_is_absent():
    matches = []
    for directory in ("BurntHud", "scripts", "Verification"):
        for path in (ROOT / directory).rglob("*"):
            if not path.is_file():
                continue
            try:
                source = path.read_text(encoding="utf-8")
            except UnicodeDecodeError:
                continue
            for identifier in RETIRED_IDENTIFIERS:
                if identifier in source:
                    matches.append(f"{path.relative_to(ROOT)}: {identifier}")
    assert not matches, "Retired identifiers remain: " + ", ".join(matches)


def test_live_species_snapshot_bridge_is_intact():
    source = CONTROLLER.read_text(encoding="utf-8")
    assert "const fetchPlayerSnapshot = async force =>" in source
    assert "const vitals = window.__isleyLocalMap?.getVitals?.();" in source
    assert "speciesId: vitals.speciesId ?? null" in source
    snapshot_block = source[source.index("const fetchPlayerSnapshot = async force =>"):]
    snapshot_block = snapshot_block[:snapshot_block.index("const nextPlayerSnapshotIntervalMs")]
    assert "window.chrome?.webview?.postMessage(snapshot);" in snapshot_block


def test_controller_javascript_parses():
    result = subprocess.run(
        [
            "node",
            "-e",
            "const fs=require('fs'); new Function(fs.readFileSync(process.argv[1],'utf8'));",
            str(CONTROLLER),
        ],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == 0, result.stderr


def test_azure_trusted_signing_is_fully_removed():
    leftovers = []
    targets = [
        WORKFLOW,
        PACKAGE_SCRIPT,
        ROOT / ".github/workflows/verify.yml",
        ROOT / "docs/WINDOWS_DEFENDER.md",
        ROOT / "scripts/verify-all.ps1",
    ]
    for path in targets:
        source = path.read_text(encoding="utf-8")
        for marker in AZURE_MARKERS:
            if marker in source:
                leftovers.append(f"{path.relative_to(ROOT)}: {marker}")
    assert not leftovers, "Azure references remain: " + ", ".join(leftovers)
    assert not (ROOT / "docs/AZURE_TRUSTED_SIGNING.md").exists()


def test_release_workflow_yaml_and_permissions():
    workflow = load_workflow()
    assert workflow["permissions"] == {"contents": "write"}
    names = [step.get("name") for step in package_steps()]
    assert names == [
        "Check out Isley",
        "Set up .NET 8",
        "Set up Node.js",
        "Package Windows archives",
        "Stage download site release",
        "Upload release archives",
    ]


def test_certificate_signing_passthrough_is_intact():
    env = named_step("Package Windows archives").get("env", {})
    for name in (
        "ISLEY_CODE_SIGN_PFX",
        "ISLEY_CODE_SIGN_PFX_BASE64",
        "ISLEY_CODE_SIGN_PASSWORD",
        "ISLEY_CODE_SIGN_THUMBPRINT",
        "ISLEY_CODE_SIGN_TIMESTAMP_URL",
    ):
        assert name in env, f"Package step no longer passes {name}"


def test_packaging_script_parses_and_keeps_certificate_route():
    command = (
        '$errors = $null; $tokens = $null; '
        f'[void][System.Management.Automation.Language.Parser]::ParseFile("{PACKAGE_SCRIPT}", '
        '[ref]$tokens, [ref]$errors); if ($errors.Count) { exit 1 }'
    )
    result = subprocess.run(
        ["/opt/pwsh/pwsh", "-NoProfile", "-NonInteractive", "-Command", command],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == 0, result.stderr

    source = PACKAGE_SCRIPT.read_text(encoding="utf-8")
    assert 'GetEnvironmentVariable("ISLEY_CODE_SIGN_PFX_BASE64")' in source
    assert '$signArguments += @("/sha1", $thumbprint)' in source
    assert "& $signtoolPath verify /pa /q $target.FullName" in source
    assert '$timestampUrl = "http://timestamp.digicert.com"' in source


def test_delta_manifest_emission_is_flat_and_writer_validates_inner_manifest():
    """Regression: the 1.4.1 delta shipped "deletedFiles": [ [] ] (unary-comma
    nesting of an empty list). Clients correctly rejected it and fell back to
    the full package; producer + writer validation are now fixed."""
    package_source = PACKAGE_SCRIPT.read_text(encoding="utf-8")
    # Flat array emission for any entry count incl. zero (no unary comma).
    assert "deletedFiles = @($deletedNormalized)" in package_source
    assert "deletedFiles = , $deletedNormalized" not in package_source

    writer_source = (ROOT / "download-site/scripts/update-isley-download.ps1").read_text(
        encoding="utf-8"
    )
    # The manifest writer must open the delta and enforce the inner contract:
    # format 1, matching versions, and deletedFiles a flat string array.
    assert 'isley-delta-manifest.json' in writer_source
    assert "$deltaManifest.format -ne 1" in writer_source
    assert "$deltaPath -isnot [string]" in writer_source
    assert "The delta inner manifest contains an invalid deletedFiles entry" in writer_source
