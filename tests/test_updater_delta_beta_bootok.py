"""Regression checks: updater delta mode, beta channel, and boot-ok marker."""

from pathlib import Path

ROOT = Path("/app")
CLIENT = ROOT / "BurntHud/IsleyUpdateClient.cs"
LOGIC = ROOT / "BurntHud/IsleyReleaseLogic.cs"
UPDATES = ROOT / "BurntHud/MainWindow.Updates.cs"
UPDATER = ROOT / "Isley.Updater/Program.cs"
PACKAGING = ROOT / "scripts/package-isley-1.3.ps1"
DESIGN_DOC = ROOT / "docs/ISLEY_UPDATER_DELTA.md"
SOLUTION = ROOT / "Isley.sln"
VERIFIER_CSPROJ = (
    ROOT / "Verification/IsleyReleaseUpdateVerifier"
    / "IsleyReleaseUpdateVerifier.csproj"
)


def read(path):
    return path.read_text(encoding="utf-8")


def test_beta_channel_is_pinned_and_honest():
    logic = read(LOGIC)
    assert 'BetaReleaseEndpoint =' in logic
    assert "Isley-release-beta.json" in logic
    assert "Isley-Windows-x64-beta.zip" in logic
    assert 'TrustedDownloadHost = "isley-download.gmith.chatgpt.site"' in logic
    client = read(CLIENT)
    assert "FetchReleaseAsync(" in client
    assert "BetaFallback" in client
    assert "catch (OperationCanceledException)" in client
    updates = read(UPDATES)
    assert "BETA CHANNEL UNAVAILABLE" in updates
    assert "BETA RELEASES PREFERRED WHEN PUBLISHED" in updates
    assert "STABLE STILL USED UNTIL BETA PUBLISHES" not in updates


def test_boot_ok_marker_flow():
    client = read(CLIENT)
    assert "WriteBootOkMarker" in client
    assert "TryReadBootOkMarker" in client
    assert "MaxBootOkMarkerBytes" in client
    updates = read(UPDATES)
    assert "ConfirmUpdatedBootAsync" in updates
    assert "BOOT CONFIRMED" in updates
    assert "BOOT NOT CONFIRMED" in updates
    assert "last-boot-ok.json" in updates
    assert "_survivalTimerTick.IsEnabled" in updates


def test_delta_posture_matches_full_package():
    client = read(CLIENT)
    # Same verification primitives must gate the delta path.
    assert "StageDeltaAsync" in client
    assert "ValidateDeltaPackage" in client
    assert client.count("ValidateArchiveHash") >= 2
    assert client.count("FixedTimeEquals") >= 2
    assert "never bricks an update" in client
    logic = read(LOGIC)
    assert "ParseDeltaOffer" in logic
    assert "ParseDeltaManifest" in logic
    assert "MaximumDeltaDeleteEntries = 2000" in logic
    assert "IsSameVersion" in logic


def test_updater_delta_mode_is_validated_and_isleydata_safe():
    updater = read(UPDATER)
    assert '"--mode"' in updater
    assert "ApplyDeltaDeleteList" in updater
    assert "isley-delta-manifest.json" in updater
    assert "escaped the install folder" in updater
    assert "must not run here" in updater
    # Full-mode orphan sweep must remain for full packages.
    assert "RemoveOrphanedPackageFiles" in updater
    # The delete list must never reach user data.
    delete_section = updater[updater.index("ApplyDeltaDeleteList"):]
    assert "IsleyData" in delete_section


def test_packaging_emits_delta_package():
    packaging = read(PACKAGING)
    assert "PreviousClientArchive" in packaging
    assert "isley-delta-manifest.json" in packaging
    assert "Isley-delta-" in packaging
    assert "deletedFiles" in packaging
    assert r"Updater\Isley.Updater.exe" in packaging
    assert "DeltaSha256" in packaging
    assert "saves nothing must not be published" in packaging


def test_verifier_covers_new_logic_and_stays_registered():
    csproj = read(VERIFIER_CSPROJ)
    assert "IsleyUpdateClient.cs" in csproj
    assert "IsleyReleaseLogic.cs" in csproj
    solution = read(SOLUTION)
    assert "IsleyReleaseUpdateVerifier" in solution
    verifier = read(
        ROOT / "Verification/IsleyReleaseUpdateVerifier/Program.cs")
    assert "BetaChannel" in verifier
    assert "ParseDeltaManifest" in verifier
    assert "WriteBootOkMarker" in verifier


def test_design_doc_exists():
    assert DESIGN_DOC.is_file()
    text = read(DESIGN_DOC)
    assert "isley-delta-manifest.json" in text
    assert "never bricks" in text
    assert "BOOT CONFIRMED" in text
