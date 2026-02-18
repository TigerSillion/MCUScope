#!/usr/bin/env python3
"""Automated STM32 GUI/protocol smoke checks.

Runs:
1) dotnet build for GUI
2) UART smoke (GetInfo/read/write/scope) with addresses auto-resolved from Keil map
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path


SYMBOLS = {
    "com_u1_system_mode": "u8",
    "com_f4_ref_speed_rpm": "f32",
    "g_st_sensorless_vector.f4_vdc_ad": "f32",
    "g_st_sensorless_vector.f4_iu_ad": "f32",
    "g_st_sensorless_vector.st_speed_output.f4_speed_rad_lpf": "f32",
}

DERIVED_FROM_BASE = {
    "g_st_sensorless_vector.f4_vdc_ad": ("g_st_sensorless_vector", 0x00),
    "g_st_sensorless_vector.f4_iu_ad": ("g_st_sensorless_vector", 0x04),
    "g_st_sensorless_vector.st_speed_output.f4_speed_rad_lpf": ("g_st_sensorless_vector", 0x10),
}


def run(cmd: list[str], cwd: Path) -> int:
    print(f"[RUN] {' '.join(cmd)}")
    return subprocess.run(cmd, cwd=str(cwd), check=False).returncode


def parse_map_symbol_addresses(map_path: Path) -> dict[str, int]:
    text = map_path.read_text(encoding="utf-8", errors="ignore")
    addresses: dict[str, int] = {}
    for name in SYMBOLS:
        m = re.search(rf"^\s*{re.escape(name)}\s+0x([0-9A-Fa-f]{{8}})\b", text, re.MULTILINE)
        if m:
            addresses[name] = int(m.group(1), 16)
    # Parse base symbols used for derived members (Keil map often omits struct members).
    for base_name, _ in DERIVED_FROM_BASE.values():
        if base_name in addresses:
            continue
        m = re.search(rf"^\s*{re.escape(base_name)}\s+0x([0-9A-Fa-f]{{8}})\b", text, re.MULTILINE)
        if m:
            addresses[base_name] = int(m.group(1), 16)

    for name, (base_name, offset) in DERIVED_FROM_BASE.items():
        if name not in addresses and base_name in addresses:
            addresses[name] = addresses[base_name] + offset
    return addresses


def main() -> int:
    parser = argparse.ArgumentParser(description="Auto debug checks for STM32 + MCUScope.")
    parser.add_argument("--port", required=True, help="Serial port, e.g. COM7")
    parser.add_argument("--baud", type=int, default=2_000_000, help="Baud rate")
    parser.add_argument("--skip-build", action="store_true", help="Skip dotnet build step")
    parser.add_argument(
        "--map",
        default="Reference/G431_KEIL_Sample/MDK-ARM/G431_KEIL_Sample/G431_KEIL_Sample.map",
        help="Path to Keil map file",
    )
    args = parser.parse_args()

    root = Path(__file__).resolve().parent.parent
    map_path = (root / args.map).resolve()
    if not map_path.exists():
        print(f"[FAIL] map not found: {map_path}")
        return 2

    if not args.skip_build:
        if run(["dotnet", "build", "MCUScope.sln"], root) != 0:
            print("[FAIL] dotnet build failed")
            return 3

    symbol_addr = parse_map_symbol_addresses(map_path)
    missing = [k for k in SYMBOLS if k not in symbol_addr]
    if missing:
        print("[FAIL] missing required symbols in map:")
        for name in missing:
            print(f"  - {name}")
        return 4

    read_args = []
    for name, vtype in SYMBOLS.items():
        read_args += ["--read-var", f"{name}:0x{symbol_addr[name]:08X}:{vtype}"]

    cmd = [
        sys.executable,
        "tools/uart_smoke.py",
        "--port",
        args.port,
        "--baud",
        str(args.baud),
        "--timeout",
        "2.0",
        "--scope-timeout",
        "3.0",
        "--write-var",
        f"com_u1_system_mode:0x{symbol_addr['com_u1_system_mode']:08X}:u8:1",
        "--write-var",
        f"com_f4_ref_speed_rpm:0x{symbol_addr['com_f4_ref_speed_rpm']:08X}:f32:900",
        "--scope",
        "--scope-var",
        f"0:0x{symbol_addr['g_st_sensorless_vector.f4_vdc_ad']:08X}:f32",
        "--scope-var",
        f"1:0x{symbol_addr['g_st_sensorless_vector.f4_iu_ad']:08X}:f32",
        "--scope-var",
        f"2:0x{symbol_addr['g_st_sensorless_vector.st_speed_output.f4_speed_rad_lpf']:08X}:f32",
    ] + read_args

    rc = run(cmd, root)
    if rc != 0:
        print("[FAIL] uart smoke failed")
        return rc

    print("[PASS] auto debug checks completed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
