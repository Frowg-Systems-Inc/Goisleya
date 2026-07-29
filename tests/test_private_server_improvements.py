"""Regression contracts for private-server and reliability improvements."""

from pathlib import Path
import re
import subprocess


ROOT = Path("/app")
BURNTHUD = ROOT / "BurntHud"
MAIN = (BURNTHUD / "MainWindow.xaml.cs").read_text(encoding="utf-8")
COMMANDS = (BURNTHUD / "MainWindow.Commands.cs").read_text(encoding="utf-8")
LIVE_NETWORK = (BURNTHUD / "MainWindow.LiveNetwork.cs").read_text(encoding="utf-8")
VOICE = (BURNTHUD / "MainWindow.Voice.cs").read_text(encoding="utf-8")
APP = (BURNTHUD / "App.xaml.cs").read_text(encoding="utf-8")
SETTINGS = (BURNTHUD / "MainWindow.Settings.cs").read_text(encoding="utf-8")
MAP_HTML = (BURNTHUD / "Map/index.html").read_text(encoding="utf-8")


def method_body(source: str, signature: str) -> str:
    """Extract a C# method body using balanced braces."""
    start = source.index(signature)
    brace = source.index("{", start)
    depth = 0
    for index in range(brace, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:index]
    raise AssertionError(f"Unclosed method: {signature}")


def test_private_server_command_catalog_and_dispatch_are_consistent():
    command_start = MAIN.index("CommandPaletteActions =")
    command_end = MAIN.index("] ;".replace(" ", ""), command_start)
    command_block = MAIN[command_start:command_end]
    assert command_block.count('new("') == 121
    assert command_block.count('new("private-server-connect"') == 1
    dispatch = method_body(
        COMMANDS,
        "private async Task ExecuteCommandPaletteActionAsync(string actionId)",
    )
    assert dispatch.count('case "private-server-connect":') == 1
    assert "await ConnectPrivateServerFromClipboardAsync();" in dispatch
    assert "commandCatalogCount !== 121" in (
        ROOT / "scripts/verify-controller.cjs"
    ).read_text(encoding="utf-8")


def test_relay_connect_handler_is_thin_and_refactored_flow_is_complete():
    handler = method_body(LIVE_NETWORK, "IsleyRelayConnectButton_Click")
    assert handler.strip() == (
        "await ConnectIsleyRelayAsync(IsleyRelayJoinLinkInputBox.Text.Trim());"
    )
    connect = method_body(
        LIVE_NETWORK,
        "private async Task ConnectIsleyRelayAsync(string input)",
    )
    ordered_contracts = [
        "IsleyRelayJoinLogic.TryParse(input, out var join)",
        "_isleyRelayJoinLink = input;",
        "_isleyRelayJoin = join;",
        "SyncCurrentCommunityServerProfile();",
        "SaveSettings();",
        "_isleyRelaySignInCancellation?.Cancel();",
        "_isleyRelaySignInCancellation?.Dispose();",
        "StartDeviceAuthorizationAsync",
        "CompleteDeviceAuthorizationAsync",
        "ConnectAsync(join, accessToken, cancellationToken)",
        "RefreshIsleyRelayPrivacyAsync(join, accessToken, cancellationToken)",
        "catch (OperationCanceledException)",
        "or WebSocketException",
        "UpdateIsleyRelayPresentation();",
    ]
    for contract in ordered_contracts:
        assert contract in connect
    assert connect.index("_isleyRelayJoinLink = input;") < connect.index(
        "SyncCurrentCommunityServerProfile();"
    ) < connect.index("SaveSettings();")
    assert connect.index("StartDeviceAuthorizationAsync") < connect.index(
        "CompleteDeviceAuthorizationAsync"
    ) < connect.index("ConnectAsync(join, accessToken, cancellationToken)")
    assert connect.rindex("UpdateIsleyRelayPresentation();") > connect.index(
        "catch (Exception ex) when ("
    )


def test_clipboard_connect_validates_fills_and_delegates():
    body = method_body(
        LIVE_NETWORK,
        "private async Task ConnectPrivateServerFromClipboardAsync()",
    )
    assert "Clipboard.ContainsText()" in body and "Clipboard.GetText().Trim()" in body
    assert "IsleyRelayJoinLogic.TryParse(clipboard, out var join)" in body
    assert "IsleyRelayJoinLinkInputBox.Text = clipboard;" in body
    assert "await ConnectIsleyRelayAsync(clipboard);" in body
    assert "COPY YOUR SERVER'S ISLEY LINK" in body
    assert "PRIVATE SERVER LINKED" in body and "COULD NOT CONNECT" in body


def test_fuzzy_palette_logic_has_required_boundaries_and_scoring():
    body = method_body(
        COMMANDS,
        "private static int FuzzyCommandPaletteScore(string title, string normalizedQuery)",
    )
    assert "normalizedQuery.Length < 2" in body
    assert "normalizedQuery.Length > 12" in body
    assert "normalizedQuery.Contains(' ', StringComparison.Ordinal)" in body
    assert "initials.StartsWith(normalizedQuery, StringComparison.Ordinal)" in body
    assert "return 60 +" in body
    assert "if (found < 0)" in body and "return -1;" in body

    # Behavioral mirror of the small pure function for its requested fixtures.
    def score(title: str, query: str) -> int:
        if len(query) < 2 or len(query) > 12 or " " in query:
            return -1
        initials = "".join(word[0] for word in title.split())
        if initials.startswith(query):
            return 60 + (25 if len(initials) == len(query) else 0)
        position = gaps = 0
        for character in query:
            found = title.find(character, position)
            if found < 0:
                return -1
            gaps += found - position
            position = found + 1
        return max(1, 25 - min(20, gaps // 2))

    assert score("growth clock", "gc") >= 60
    assert score("growth clock", "gz") == -1
    assert score("growth clock", "g c") == -1
    assert score("growth clock", "g") == -1
    assert score("growth clock", "abcdefghijklmn") == -1


def test_voice_reconnect_backoff_and_existing_gates():
    assert "_voiceAutoReconnectDelaySeconds = 5" in VOICE
    refresh = method_body(VOICE, "private void RefreshVoiceStatus()")
    for gate in (
        "_voiceEnabled",
        "_voiceAutoOpen",
        "!_streamerMode",
        "!_voiceUserDisconnectedThisSession",
        "_voiceSessionConnectedThisSession",
        "!_voiceBridgeRunning",
        "!_voiceConnecting",
        "!_voiceAutoConnectInFlight",
        '_voiceEngineState is "DISCONNECTED" or "ERROR"',
    ):
        assert gate in refresh
    assert "AddSeconds(_voiceAutoReconnectDelaySeconds)" in refresh
    assert "Math.Min(60, _voiceAutoReconnectDelaySeconds * 2)" in refresh
    assert re.search(
        r"if \(_voiceBridgeRunning\)[\s\S]*?_voiceAutoReconnectDelaySeconds = 5;",
        VOICE,
    )


def test_crash_reporter_handlers_and_retention_contracts():
    assert "DispatcherUnhandledException += OnDispatcherUnhandledException" in APP
    assert "AppDomain.CurrentDomain.UnhandledException" in APP
    assert "TaskScheduler.UnobservedTaskException" in APP
    dispatcher = method_body(
        APP,
        "private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)",
    )
    assert "args.Handled" not in dispatcher
    reporter = method_body(
        APP,
        "private static void WriteCrashReport(string origin, Exception? exception)",
    )
    assert "try" in reporter and "catch" in reporter
    assert 'GetFiles("crash-*.txt")' in APP
    assert ".OrderByDescending(file => file.CreationTimeUtc)" in APP
    assert ".Skip(MaxCrashReports)" in APP
    assert "MaxCrashReports = 10" in APP


def test_settings_schema_and_virtual_screen_restore_contracts():
    assert "CurrentSchemaVersion = 1" in MAIN
    assert "public int SchemaVersion { get; set; }" in MAIN
    assert "SchemaVersion = MapperSettings.CurrentSchemaVersion" in SETTINGS
    load = method_body(SETTINGS, "private void LoadSettings()")
    for property_name in (
        "VirtualScreenLeft",
        "VirtualScreenTop",
        "VirtualScreenWidth",
        "VirtualScreenHeight",
    ):
        assert property_name in load
    assert "Width = Math.Clamp(settings.Width" in load and "workArea.Width - 16" in load
    assert "Height = Math.Clamp(settings.Height" in load and "workArea.Height - 16" in load


def test_map_script_parses_and_tile_retry_contracts_are_present():
    scripts = re.findall(r"<script>([\s\S]*?)</script>", MAP_HTML)
    assert len(scripts) == 1
    result = subprocess.run(
        ["node", "--check", "-"],
        input=scripts[0],
        text=True,
        capture_output=True,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert "const failedGatewayTiles = new Map();" in scripts[0]
    assert "failure.attempts >= 3" in scripts[0]
    assert "4000 * failure.attempts" in scripts[0]
    assert "failedGatewayTiles.delete(key)" in scripts[0]
    assert "failedGatewayTiles.size > 128" in scripts[0]


def test_failed_tiles_schedule_an_automatic_retry_after_error():
    error_handler = MAP_HTML[
        MAP_HTML.index('image.addEventListener("error"'):
        MAP_HTML.index('image.addEventListener("load"')
    ]
    assert "setTimeout" in error_handler and "updateHighResolutionTiles" in error_handler, (
        "A failed tile is only eligible on a later unrelated map update; the error "
        "handler does not schedule the promised 4s/8s retry."
    )


def test_quickstart_documentation_exists_and_is_linked():
    quickstart = ROOT / "docs/PRIVATE_SERVER_QUICKSTART.md"
    assert quickstart.is_file()
    assert "Connect to private server" in quickstart.read_text(encoding="utf-8")
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    assert "docs/PRIVATE_SERVER_QUICKSTART.md" in readme
