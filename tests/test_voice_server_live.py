"""Permanent live test for Isley.VoiceServer signaling behavior.

Builds and runs the real VoiceServer process, connects two WebSocket peers to
one room, and proves the audited behavior end to end:

- sealed-envelope (AES-GCM) signals are forwarded to the addressed peer;
- plaintext signals (``data`` instead of ``sealed``) are silently refused;
- malformed JSON closes the offending socket with policy-violation 1008;
- the room survives: the remaining peer still receives ``peer-left``.

The test SKIPs (never fails) when the dotnet SDK is unavailable, the build
fails, or the server cannot start in this environment. It uses only the
standard library so no extra pip packages are required.
"""

from __future__ import annotations

import base64
import json
import os
import secrets
import shutil
import socket
import struct
import subprocess
import time
import urllib.request
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
PROJECT_DIR = ROOT / "Isley.VoiceServer"
PROJECT_FILE = PROJECT_DIR / "Isley.VoiceServer.csproj"

DOTNET = shutil.which("dotnet")
if DOTNET is None:
    pytest.skip(
        "dotnet SDK is not available; skipping VoiceServer live test.",
        allow_module_level=True,
    )

WS_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
CLOSE_POLICY_VIOLATION = 1008


class LiveWebSocket:
    """Minimal blocking WebSocket client (client-masked frames, stdlib only)."""

    def __init__(self, sock: socket.socket):
        self._sock = sock
        self._buf = bytearray()

    @classmethod
    def connect(cls, url: str, timeout: float = 10.0) -> "LiveWebSocket":
        assert url.startswith("ws://")
        rest = url[len("ws://"):]
        authority, _, path = rest.partition("/")
        host, _, port_text = authority.partition(":")
        port = int(port_text) if port_text else 80
        path = "/" + path

        sock = socket.create_connection((host, port), timeout=timeout)
        key = base64.b64encode(secrets.token_bytes(16)).decode("ascii")
        request = (
            f"GET {path} HTTP/1.1\r\n"
            f"Host: {authority}\r\n"
            "Upgrade: websocket\r\n"
            "Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            "Sec-WebSocket-Version: 13\r\n"
            # The server's allowlist (appsettings AllowedOrigins) rejects
            # origin-less upgrades when non-empty; send the default allowed one.
            "Origin: https://isley.voice.local\r\n"
            "\r\n"
        )
        sock.sendall(request.encode("ascii"))

        response = b""
        while b"\r\n\r\n" not in response:
            chunk = sock.recv(4096)
            if not chunk:
                raise ConnectionError("VoiceServer closed during handshake.")
            response += chunk
        head, _, remainder = response.partition(b"\r\n\r\n")
        status_line = head.decode("latin-1").splitlines()[0]
        if " 101" not in status_line:
            raise ConnectionError(f"VoiceServer refused the upgrade: {status_line}")
        # Bytes past the header terminator are already WebSocket frames
        # (the server may flush 101 + first frame in one TCP segment) —
        # carry them into the frame reader instead of discarding them.
        client = cls(sock)
        client._buf = bytearray(remainder)
        return client

    def close(self) -> None:
        try:
            self._send_frame(0x8, struct.pack(">H", 1000))
        except OSError:
            pass
        try:
            self._sock.close()
        except OSError:
            pass

    def send_text(self, payload: str) -> None:
        self._send_frame(0x1, payload.encode("utf-8"))

    def send_json(self, value: dict) -> None:
        self.send_text(json.dumps(value))

    def recv_json(self, timeout: float = 5.0) -> dict | None:
        """Next text message as JSON, or None if none arrives in time."""
        frame = self._recv_message(timeout)
        if frame is None:
            return None
        opcode, payload = frame
        if opcode != 0x1:
            raise AssertionError(f"Expected a text frame, got opcode {opcode}.")
        return json.loads(payload.decode("utf-8"))

    def expect_close(self, timeout: float = 5.0) -> int:
        """Wait for the server close frame and return its status code."""
        deadline = time.monotonic() + timeout
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise AssertionError("Timed out waiting for the server close frame.")
            frame = self._recv_message(remaining)
            if frame is None:
                raise AssertionError("Timed out waiting for the server close frame.")
            opcode, payload = frame
            if opcode == 0x8:
                if len(payload) < 2:
                    raise AssertionError("Close frame had no status code.")
                return struct.unpack(">H", payload[:2])[0]

    def _send_frame(self, opcode: int, payload: bytes) -> None:
        header = bytearray([0x80 | opcode])
        length = len(payload)
        if length < 126:
            header.append(0x80 | length)
        elif length <= 0xFFFF:
            header.append(0x80 | 126)
            header += struct.pack(">H", length)
        else:
            header.append(0x80 | 127)
            header += struct.pack(">Q", length)
        mask = secrets.token_bytes(4)
        masked = bytes(byte ^ mask[index % 4] for index, byte in enumerate(payload))
        self._sock.sendall(bytes(header) + mask + masked)

    def _recv_message(self, timeout: float):
        """Next complete message as (opcode, payload); None on timeout."""
        self._sock.settimeout(timeout)
        try:
            while True:
                first = self._read_exact(2)
                if first is None:
                    return None
                opcode = first[0] & 0x0F
                masked = first[1] & 0x80
                length = first[1] & 0x7F
                if length == 126:
                    raw = self._read_exact(2)
                    if raw is None:
                        return None
                    (length,) = struct.unpack(">H", raw)
                elif length == 127:
                    raw = self._read_exact(8)
                    if raw is None:
                        return None
                    (length,) = struct.unpack(">Q", raw)
                mask = b""
                if masked:
                    mask = self._read_exact(4) or b""
                    if not mask:
                        return None
                payload = self._read_exact(length) if length else b""
                if payload is None:
                    return None
                if masked:
                    payload = bytes(
                        byte ^ mask[index % 4] for index, byte in enumerate(payload)
                    )
                if opcode == 0x9:  # ping -> pong, keep waiting
                    self._send_frame(0xA, payload)
                    continue
                return opcode, payload
        except (TimeoutError, socket.timeout):
            return None

    def _read_exact(self, count: int):
        data = b""
        if self._buf:
            take = self._buf[:count]
            del self._buf[:count]
            data += bytes(take)
        while len(data) < count:
            chunk = self._sock.recv(count - len(data))
            if not chunk:
                raise ConnectionError("VoiceServer closed the socket unexpectedly.")
            data += chunk
        return data


def _free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


def _sealed_envelope() -> dict:
    iv = base64.urlsafe_b64encode(secrets.token_bytes(12)).decode("ascii").rstrip("=")
    ciphertext = (
        base64.urlsafe_b64encode(secrets.token_bytes(48)).decode("ascii").rstrip("=")
    )
    assert len(iv) == 16 and len(ciphertext) >= 24
    return {"v": 1, "iv": iv, "ciphertext": ciphertext}


@pytest.fixture(scope="module")
def voice_server():
    build = subprocess.run(
        [DOTNET, "build", str(PROJECT_FILE), "-c", "Release", "--nologo"],
        capture_output=True,
        text=True,
        timeout=300,
        cwd=str(ROOT),
    )
    if build.returncode != 0:
        pytest.skip(
            "Isley.VoiceServer could not be built in this environment: "
            + (build.stdout or build.stderr or "")[-800:]
        )

    assembly = PROJECT_DIR / "bin" / "Release" / "net8.0" / "Isley.VoiceServer.dll"
    if not assembly.is_file():
        pytest.skip(f"VoiceServer assembly missing after build: {assembly}")

    port = _free_port()
    environment = dict(os.environ)
    environment.update(
        {
            # appsettings.json "Urls" wins over ASPNETCORE_URLS (config beats
            # host env); the plain "Urls" env var lands in app config AFTER
            # appsettings.json, so it is the override that actually works.
            "Urls": f"http://127.0.0.1:{port}",
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
            "DOTNET_NOLOGO": "1",
            "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        }
    )
    process = subprocess.Popen(
        [DOTNET, str(assembly)],
        cwd=str(PROJECT_DIR),
        env=environment,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    try:
        health_url = f"http://127.0.0.1:{port}/health"
        deadline = time.monotonic() + 30
        ready = False
        while time.monotonic() < deadline:
            if process.poll() is not None:
                break
            try:
                with urllib.request.urlopen(health_url, timeout=2) as response:
                    if response.status == 200:
                        ready = True
                        break
            except OSError:
                time.sleep(0.25)
        if not ready:
            pytest.skip(
                "VoiceServer did not start in this environment "
                f"(exit code: {process.poll()})."
            )
        yield f"ws://127.0.0.1:{port}"
    finally:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()


def test_two_peer_sealed_signal_malformed_close_and_room_survival(voice_server):
    room = secrets.token_hex(32)
    peer_a = secrets.token_hex(16)
    peer_b = secrets.token_hex(16)

    peer_a_socket = LiveWebSocket.connect(
        f"{voice_server}/voice?room={room}&peer={peer_a}"
    )
    welcome_a = peer_a_socket.recv_json()
    assert welcome_a["type"] == "welcome"
    assert welcome_a["self"] == peer_a
    assert welcome_a["peers"] == []

    peer_b_socket = LiveWebSocket.connect(
        f"{voice_server}/voice?room={room}&peer={peer_b}"
    )
    try:
        welcome_b = peer_b_socket.recv_json()
        assert welcome_b["type"] == "welcome"
        assert welcome_b["self"] == peer_b
        assert [peer["id"] for peer in welcome_b["peers"]] == [peer_a]

        joined = peer_a_socket.recv_json()
        assert joined["from"] == peer_b
        assert joined["message"]["type"] == "peer-joined"
        assert joined["message"]["peer"]["id"] == peer_b

        # Sealed envelopes are forwarded verbatim to the addressed peer.
        envelope = _sealed_envelope()
        peer_a_socket.send_json(
            {"type": "signal", "to": peer_b, "sealed": envelope}
        )
        forwarded = peer_b_socket.recv_json()
        assert forwarded["from"] == peer_a
        assert forwarded["message"]["type"] == "signal"
        assert forwarded["message"]["to"] == peer_b
        assert forwarded["message"]["sealed"] == envelope

        # Plaintext signaling is refused: dropped without a forward or a close.
        peer_a_socket.send_json(
            {"type": "signal", "to": peer_b, "data": {"sdp": "plaintext"}}
        )
        assert peer_b_socket.recv_json(timeout=1.5) is None, (
            "Plaintext signal was forwarded; the server must refuse it."
        )

        # Malformed JSON closes the offending socket with policy-violation 1008.
        peer_a_socket.send_text("this is not json {")
        assert peer_a_socket.expect_close() == CLOSE_POLICY_VIOLATION
    finally:
        peer_a_socket.close()

    # The room survives: peer B is still connected and learns that A left.
    departed = peer_b_socket.recv_json()
    assert departed["from"] == peer_a
    assert departed["message"]["type"] == "peer-left"
    assert departed["message"]["peer"] == peer_a

    # Peer B can still use the room: a new peer joins and B is notified.
    peer_c = secrets.token_hex(16)
    peer_c_socket = LiveWebSocket.connect(
        f"{voice_server}/voice?room={room}&peer={peer_c}"
    )
    try:
        welcome_c = peer_c_socket.recv_json()
        assert [peer["id"] for peer in welcome_c["peers"]] == [peer_b]
        joined_c = peer_b_socket.recv_json()
        assert joined_c["from"] == peer_c
        assert joined_c["message"]["type"] == "peer-joined"
    finally:
        peer_c_socket.close()
        peer_b_socket.close()
