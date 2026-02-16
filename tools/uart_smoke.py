#!/usr/bin/env python3
"""UART protocol smoke test for MCU scope firmware.

Usage examples:
  python tools/uart_smoke.py --dry-run
  python tools/uart_smoke.py --port COM5 --baud 1000000
  python tools/uart_smoke.py --port COM5 --read-var com_u1_system_mode:0x00001829
  python tools/uart_smoke.py --port COM5 --scope --scope-channels 0,1,2
"""

from __future__ import annotations

import argparse
import struct
import sys
import time
from dataclasses import dataclass
from typing import Dict, Iterable, List, Optional, Sequence, Tuple


SYNC = b"\xAA\x55"

CMD_GET_INFO = 0x02
CMD_READ_VARIABLE = 0x10
CMD_WRITE_VARIABLE = 0x11
CMD_START_SCOPE = 0x20
CMD_STOP_SCOPE = 0x21

CMD_ACK = 0x80
CMD_NACK = 0x81
CMD_INFO_RESPONSE = 0x82
CMD_VARIABLE_DATA = 0x83
CMD_WAVEFORM_DATA = 0x84

VAR_TYPE_MAP: Dict[str, int] = {
    "u8": 0,
    "i8": 1,
    "u16": 2,
    "i16": 3,
    "u32": 4,
    "i32": 5,
    "f32": 6,
    "bool": 7,
    "logic": 8,
}

VAR_TYPE_NAME = {
    0: "u8",
    1: "i8",
    2: "u16",
    3: "i16",
    4: "u32",
    5: "i32",
    6: "f32",
    7: "bool",
    8: "logic",
}


class ProtocolError(RuntimeError):
    """Raised when the protocol exchange fails."""


@dataclass
class ReadVarSpec:
    name: str
    address: int


@dataclass
class WriteVarSpec:
    name: str
    address: int
    var_type: int
    value: float


def checksum(body: bytes) -> int:
    return sum(body) & 0xFF


def build_packet(command: int, payload: bytes) -> bytes:
    body = bytes((command,)) + len(payload).to_bytes(2, "little") + payload
    return SYNC + body + bytes((checksum(body),))


def parse_next_packet(buffer: bytearray) -> Optional[Tuple[int, bytes]]:
    while True:
        if len(buffer) < 2:
            return None
        if buffer[0] == SYNC[0] and buffer[1] == SYNC[1]:
            break
        del buffer[0]

    if len(buffer) < 6:
        return None

    command = buffer[2]
    payload_len = int.from_bytes(buffer[3:5], "little")
    total_len = 6 + payload_len
    if len(buffer) < total_len:
        return None

    frame = bytes(buffer[:total_len])
    del buffer[:total_len]

    body = frame[2:-1]
    if frame[-1] != checksum(body):
        # Drop frame and continue parsing later packets.
        return None

    payload = frame[5:-1]
    return command, payload


def encode_var_value(var_type: int, value: float) -> bytes:
    if var_type == 0:
        return struct.pack("<B", int(max(0, min(255, value))))
    if var_type == 1:
        return struct.pack("<b", int(max(-128, min(127, value))))
    if var_type == 2:
        return struct.pack("<H", int(max(0, min(65535, value))))
    if var_type == 3:
        return struct.pack("<h", int(max(-32768, min(32767, value))))
    if var_type == 4:
        return struct.pack("<I", int(max(0, min(0xFFFFFFFF, value))))
    if var_type == 5:
        return struct.pack("<i", int(max(-2147483648, min(2147483647, value))))
    if var_type == 6:
        return struct.pack("<f", float(value))
    if var_type in (7, 8):
        return struct.pack("<B", 1 if value != 0 else 0)
    raise ValueError(f"Unsupported variable type: {var_type}")


def decode_var_value(var_type: int, raw: bytes) -> float:
    if var_type == 0 and len(raw) >= 1:
        return float(struct.unpack("<B", raw[:1])[0])
    if var_type == 1 and len(raw) >= 1:
        return float(struct.unpack("<b", raw[:1])[0])
    if var_type == 2 and len(raw) >= 2:
        return float(struct.unpack("<H", raw[:2])[0])
    if var_type == 3 and len(raw) >= 2:
        return float(struct.unpack("<h", raw[:2])[0])
    if var_type == 4 and len(raw) >= 4:
        return float(struct.unpack("<I", raw[:4])[0])
    if var_type == 5 and len(raw) >= 4:
        return float(struct.unpack("<i", raw[:4])[0])
    if var_type == 6 and len(raw) >= 4:
        return float(struct.unpack("<f", raw[:4])[0])
    if var_type in (7, 8) and len(raw) >= 1:
        return 1.0 if raw[0] != 0 else 0.0
    raise ValueError(f"Payload too short for variable type {var_type}")


def parse_info_payload(payload: bytes) -> Tuple[str, str, float]:
    offset = 0
    if len(payload) < 2:
        raise ProtocolError("InfoResponse payload too short")

    name_len = payload[offset]
    offset += 1
    if len(payload) < offset + name_len + 1:
        raise ProtocolError("InfoResponse payload missing CPU name")
    cpu_name = payload[offset: offset + name_len].decode("ascii", errors="replace")
    offset += name_len

    ver_len = payload[offset]
    offset += 1
    if len(payload) < offset + ver_len + 4:
        raise ProtocolError("InfoResponse payload missing version/clock")
    lib_version = payload[offset: offset + ver_len].decode("ascii", errors="replace")
    offset += ver_len

    clock_mhz = struct.unpack("<f", payload[offset: offset + 4])[0]
    return cpu_name, lib_version, clock_mhz


def parse_variable_payload(payload: bytes) -> Tuple[str, int, float]:
    if len(payload) < 2:
        raise ProtocolError("VariableData payload too short")
    name_len = payload[0]
    if len(payload) < 1 + name_len + 1:
        raise ProtocolError("VariableData payload missing name/type")

    name = payload[1: 1 + name_len].decode("ascii", errors="replace")
    var_type = payload[1 + name_len]
    value_raw = payload[2 + name_len:]
    value = decode_var_value(var_type, value_raw)
    return name, var_type, value


def parse_waveform_payload(payload: bytes) -> Tuple[int, float, int]:
    if len(payload) < 7:
        raise ProtocolError("WaveformData payload too short")
    channel = payload[0]
    sample_period = struct.unpack("<f", payload[1:5])[0]
    count = int.from_bytes(payload[5:7], "little")
    expected = 7 + (count * 4)
    if len(payload) < expected:
        raise ProtocolError("WaveformData payload missing sample data")
    return channel, sample_period, count


def parse_read_var_spec(token: str) -> ReadVarSpec:
    parts = token.split(":", 1)
    if len(parts) != 2:
        raise ValueError("read-var format must be NAME:ADDRESS")
    return ReadVarSpec(name=parts[0], address=int(parts[1], 0))


def parse_write_var_spec(token: str) -> WriteVarSpec:
    parts = token.split(":", 3)
    if len(parts) != 4:
        raise ValueError("write-var format must be NAME:ADDRESS:TYPE:VALUE")
    var_type_token = parts[2].strip().lower()
    if var_type_token.isdigit():
        var_type = int(var_type_token)
    else:
        if var_type_token not in VAR_TYPE_MAP:
            raise ValueError(f"Unknown variable type: {parts[2]}")
        var_type = VAR_TYPE_MAP[var_type_token]

    return WriteVarSpec(
        name=parts[0],
        address=int(parts[1], 0),
        var_type=var_type,
        value=float(parts[3]),
    )


class ProtocolClient:
    def __init__(self, serial_port, verbose: bool) -> None:
        self.serial = serial_port
        self.verbose = verbose
        self.rx_buffer = bytearray()

    def send(self, command: int, payload: bytes) -> None:
        packet = build_packet(command, payload)
        self.serial.write(packet)
        if self.verbose:
            print(f"[TX] cmd=0x{command:02X} len={len(payload)} data={packet.hex(' ')}")

    def wait_for(self, commands: Iterable[int], timeout_s: float) -> Tuple[int, bytes]:
        command_set = set(commands)
        deadline = time.monotonic() + timeout_s

        while time.monotonic() < deadline:
            packet = parse_next_packet(self.rx_buffer)
            if packet is not None:
                command, payload = packet
                if self.verbose:
                    print(f"[RX] cmd=0x{command:02X} len={len(payload)}")
                if command in command_set:
                    return command, payload
                continue

            chunk = self.serial.read(512)
            if chunk:
                self.rx_buffer.extend(chunk)
            else:
                time.sleep(0.01)

        readable = ", ".join(f"0x{cmd:02X}" for cmd in command_set)
        raise ProtocolError(f"Timeout waiting for command(s): {readable}")


def run_dry_run(
    read_specs: Sequence[ReadVarSpec],
    write_specs: Sequence[WriteVarSpec],
    scope_enabled: bool,
    scope_channels: Sequence[int],
    scope_sample_period: float,
    scope_record_length: int,
) -> int:
    print("Dry-run enabled: no serial port will be opened.")

    packets = [("GetInfo", build_packet(CMD_GET_INFO, b""))]

    for spec in read_specs:
        name = spec.name.encode("ascii")
        payload = bytes((len(name),)) + name + struct.pack("<I", spec.address)
        packets.append((f"ReadVariable:{spec.name}", build_packet(CMD_READ_VARIABLE, payload)))

    for spec in write_specs:
        name = spec.name.encode("ascii")
        value_bytes = encode_var_value(spec.var_type, spec.value)
        payload = (
            bytes((len(name),))
            + name
            + struct.pack("<I", spec.address)
            + bytes((spec.var_type,))
            + value_bytes
        )
        packets.append((f"WriteVariable:{spec.name}", build_packet(CMD_WRITE_VARIABLE, payload)))

    if scope_enabled:
        channels = bytes(scope_channels)
        start_payload = (
            struct.pack("<f", scope_sample_period)
            + struct.pack("<i", scope_record_length)
            + bytes((len(channels),))
            + channels
        )
        packets.append(("StartScope", build_packet(CMD_START_SCOPE, start_payload)))
        packets.append(("StopScope", build_packet(CMD_STOP_SCOPE, b"")))

    for label, packet in packets:
        print(f"{label:<24} {packet.hex(' ')}")

    # Decode demo payloads so parser paths are also sanity checked.
    demo_info = bytes((3,)) + b"RX2" + bytes((5,)) + b"1.0.0" + struct.pack("<f", 40.0)
    cpu_name, lib_version, clock = parse_info_payload(demo_info)
    print(f"Decode demo InfoResponse: cpu={cpu_name} lib={lib_version} clock={clock:.2f}MHz")
    return 0


def run_serial(args: argparse.Namespace) -> int:
    try:
        import serial  # type: ignore
    except Exception as exc:
        print("ERROR: pyserial is required for non-dry-run mode. Install with: pip install pyserial")
        print(f"Import failure: {exc}")
        return 2

    serial_port = serial.Serial(
        port=args.port,
        baudrate=args.baud,
        bytesize=8,
        parity=serial.PARITY_NONE,
        stopbits=serial.STOPBITS_ONE,
        timeout=0.05,
        write_timeout=1.0,
    )

    client = ProtocolClient(serial_port, verbose=args.verbose)
    try:
        print(f"[STEP] GetInfo on {args.port} @ {args.baud} bps")
        client.send(CMD_GET_INFO, b"")
        command, payload = client.wait_for((CMD_INFO_RESPONSE, CMD_NACK), args.timeout)
        if command == CMD_NACK:
            raise ProtocolError("MCU returned NACK for GetInfo")

        cpu_name, lib_version, clock = parse_info_payload(payload)
        print(f"[PASS] InfoResponse cpu={cpu_name} lib={lib_version} clock={clock:.3f}MHz")

        for spec in args.read_vars:
            print(f"[STEP] ReadVariable {spec.name} @ 0x{spec.address:08X}")
            name = spec.name.encode("ascii")
            read_payload = bytes((len(name),)) + name + struct.pack("<I", spec.address)
            client.send(CMD_READ_VARIABLE, read_payload)
            command, payload = client.wait_for((CMD_VARIABLE_DATA, CMD_NACK), args.timeout)
            if command == CMD_NACK:
                raise ProtocolError(f"MCU returned NACK for ReadVariable {spec.name}")
            var_name, var_type, value = parse_variable_payload(payload)
            type_name = VAR_TYPE_NAME.get(var_type, str(var_type))
            print(f"[PASS] VariableData {var_name} type={type_name} value={value}")

        for spec in args.write_vars:
            print(
                f"[STEP] WriteVariable {spec.name} @ 0x{spec.address:08X} "
                f"type={VAR_TYPE_NAME.get(spec.var_type, spec.var_type)} value={spec.value}"
            )
            name = spec.name.encode("ascii")
            value_bytes = encode_var_value(spec.var_type, spec.value)
            write_payload = (
                bytes((len(name),))
                + name
                + struct.pack("<I", spec.address)
                + bytes((spec.var_type,))
                + value_bytes
            )
            client.send(CMD_WRITE_VARIABLE, write_payload)
            command, _ = client.wait_for((CMD_ACK, CMD_NACK), args.timeout)
            if command == CMD_NACK:
                raise ProtocolError(f"MCU returned NACK for WriteVariable {spec.name}")
            print("[PASS] Write ACK received")

        if args.scope:
            channels = bytes(args.scope_channels)
            print(
                f"[STEP] StartScope channels={list(channels)} "
                f"period={args.scope_sample_period} record={args.scope_record_length}"
            )
            start_payload = (
                struct.pack("<f", args.scope_sample_period)
                + struct.pack("<i", args.scope_record_length)
                + bytes((len(channels),))
                + channels
            )
            client.send(CMD_START_SCOPE, start_payload)
            command, _ = client.wait_for((CMD_ACK, CMD_NACK), args.timeout)
            if command == CMD_NACK:
                raise ProtocolError("MCU returned NACK for StartScope")

            command, payload = client.wait_for((CMD_WAVEFORM_DATA, CMD_NACK), args.scope_timeout)
            if command == CMD_NACK:
                raise ProtocolError("MCU returned NACK while waiting for WaveformData")
            ch, period, count = parse_waveform_payload(payload)
            print(f"[PASS] WaveformData channel={ch} sample_period={period} samples={count}")

            print("[STEP] StopScope")
            client.send(CMD_STOP_SCOPE, b"")
            command, _ = client.wait_for((CMD_ACK, CMD_NACK), args.timeout)
            if command == CMD_NACK:
                raise ProtocolError("MCU returned NACK for StopScope")
            print("[PASS] Stop ACK received")

        print("Smoke test PASSED")
        return 0
    finally:
        serial_port.close()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="UART smoke test for MCU scope protocol.")
    parser.add_argument("--port", help="Serial port name (example: COM5). Required unless --dry-run.")
    parser.add_argument("--baud", type=int, default=1_000_000, help="UART baud rate (default: 1000000).")
    parser.add_argument("--timeout", type=float, default=1.5, help="Per-command timeout in seconds.")
    parser.add_argument("--scope-timeout", type=float, default=2.0, help="Waveform wait timeout in seconds.")
    parser.add_argument("--dry-run", action="store_true", help="Build packets only, no serial communication.")
    parser.add_argument("--verbose", action="store_true", help="Print raw TX/RX protocol traffic.")
    parser.add_argument(
        "--read-var",
        dest="read_var_tokens",
        action="append",
        default=[],
        help="Read variable spec: NAME:ADDRESS (hex or decimal). Repeatable.",
    )
    parser.add_argument(
        "--write-var",
        dest="write_var_tokens",
        action="append",
        default=[],
        help="Write variable spec: NAME:ADDRESS:TYPE:VALUE. TYPE: u8/i8/u16/i16/u32/i32/f32/bool/logic or numeric.",
    )
    parser.add_argument("--scope", action="store_true", help="Enable StartScope/StopScope test.")
    parser.add_argument(
        "--scope-channels",
        default="0",
        help="Comma-separated scope channel indexes (default: 0).",
    )
    parser.add_argument(
        "--scope-sample-period",
        type=float,
        default=0.0005,
        help="Scope sample period in seconds (float32, default: 0.0005).",
    )
    parser.add_argument(
        "--scope-record-length",
        type=int,
        default=32,
        help="Scope record length (default: 32).",
    )
    args = parser.parse_args()

    try:
        args.read_vars = [parse_read_var_spec(token) for token in args.read_var_tokens]
        args.write_vars = [parse_write_var_spec(token) for token in args.write_var_tokens]
        args.scope_channels = [
            int(part.strip(), 0)
            for part in args.scope_channels.split(",")
            if part.strip() != ""
        ]
    except Exception as exc:
        raise SystemExit(f"Argument parse error: {exc}") from exc

    if not args.dry_run and not args.port:
        raise SystemExit("--port is required unless --dry-run is used.")

    if args.scope and not args.scope_channels:
        raise SystemExit("--scope requires at least one channel index.")
    if args.scope and len(args.scope_channels) > 12:
        raise SystemExit("--scope supports up to 12 channels.")
    if args.scope_record_length <= 0:
        raise SystemExit("--scope-record-length must be > 0.")

    return args


def main() -> int:
    args = parse_args()

    if args.dry_run:
        return run_dry_run(
            read_specs=args.read_vars,
            write_specs=args.write_vars,
            scope_enabled=args.scope,
            scope_channels=args.scope_channels,
            scope_sample_period=args.scope_sample_period,
            scope_record_length=args.scope_record_length,
        )

    try:
        return run_serial(args)
    except ProtocolError as exc:
        print(f"Smoke test FAILED: {exc}")
        return 1
    except KeyboardInterrupt:
        print("Smoke test aborted by user.")
        return 130


if __name__ == "__main__":
    sys.exit(main())
