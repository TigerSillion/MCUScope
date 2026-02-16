#!/usr/bin/env python3
"""Run one-click regression checks and persist a detailed execution log.

Default workflow:
1) MCU dependency check (closed-lib replacement verification).
2) UART protocol smoke dry-run (or live serial when --port is supplied).
3) PC GUI build check (`dotnet build MCUScope.sln`).
"""

from __future__ import annotations

import argparse
from datetime import datetime
from pathlib import Path
import subprocess
import sys
from typing import List, Optional, Sequence, Tuple


PROJECT_NAME = "RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100"
DEFAULT_FIRMWARE_ROOT = Path("Reference") / PROJECT_NAME
DEFAULT_MAP_PATH = (
    DEFAULT_FIRMWARE_ROOT / "HardwareDebug" / f"{PROJECT_NAME}.map"
)


def _now_str() -> str:
    return datetime.now().strftime("%Y%m%d_%H%M%S")


def _write_line(log_stream, text: str) -> None:
    print(text)
    log_stream.write(text + "\n")
    log_stream.flush()


def _run_step(
    label: str,
    command: Sequence[str],
    workdir: Path,
    log_stream,
) -> int:
    _write_line(log_stream, f"\n=== STEP: {label} ===")
    _write_line(log_stream, f"CMD: {' '.join(command)}")
    _write_line(log_stream, f"CWD: {workdir}")

    process = subprocess.Popen(
        command,
        cwd=str(workdir),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
    )

    assert process.stdout is not None
    for raw_line in process.stdout:
        line = raw_line.rstrip("\n")
        _write_line(log_stream, line)

    rc = process.wait()
    if rc == 0:
        _write_line(log_stream, f"[PASS] {label}")
    else:
        _write_line(log_stream, f"[FAIL] {label} (exit={rc})")
    return rc


def _default_log_path() -> Path:
    return Path("logs") / f"regression_{_now_str()}.log"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run batch regression checks with persistent logs.")
    parser.add_argument(
        "--firmware-root",
        default=str(DEFAULT_FIRMWARE_ROOT),
        help=f"Firmware root path (default: {DEFAULT_FIRMWARE_ROOT})",
    )
    parser.add_argument(
        "--map",
        default=None,
        help=(
            "Optional map path for strict artifact check. "
            "If omitted and default map exists, default map is used."
        ),
    )
    parser.add_argument(
        "--no-map",
        action="store_true",
        help="Skip map validation even if map file exists.",
    )
    parser.add_argument(
        "--fail-on-stale-map",
        action="store_true",
        help="Fail when map is older than replacement source/config files.",
    )
    parser.add_argument(
        "--port",
        default=None,
        help="Serial port for live UART smoke. If omitted, dry-run is used.",
    )
    parser.add_argument(
        "--baud",
        type=int,
        default=1_000_000,
        help="UART baud rate for live mode (default: 1000000).",
    )
    parser.add_argument(
        "--scope-channels",
        default="0,1",
        help="Scope channel list passed to uart_smoke.py (default: 0,1).",
    )
    parser.add_argument(
        "--read-var",
        dest="read_vars",
        action="append",
        default=[],
        help="Optional read variable spec NAME:ADDRESS (repeatable).",
    )
    parser.add_argument(
        "--write-var",
        dest="write_vars",
        action="append",
        default=[],
        help="Optional write variable spec NAME:ADDRESS:TYPE:VALUE (repeatable).",
    )
    parser.add_argument(
        "--skip-dotnet-build",
        action="store_true",
        help="Skip `dotnet build MCUScope.sln` step.",
    )
    parser.add_argument(
        "--log-file",
        default=str(_default_log_path()),
        help="Regression log file path.",
    )
    return parser.parse_args()


def _resolve_map_path(args: argparse.Namespace, workdir: Path) -> Optional[Path]:
    if args.no_map:
        return None
    if args.map:
        return Path(args.map)
    default_map = workdir / DEFAULT_MAP_PATH
    if default_map.exists():
        return DEFAULT_MAP_PATH
    return None


def main() -> int:
    args = parse_args()
    workdir = Path.cwd()
    python_exe = sys.executable
    log_path = Path(args.log_file)
    log_path.parent.mkdir(parents=True, exist_ok=True)

    summary: List[Tuple[str, int]] = []

    with log_path.open("w", encoding="utf-8") as log_stream:
        _write_line(log_stream, "MCUScope regression batch")
        _write_line(log_stream, f"timestamp: {datetime.now().isoformat(timespec='seconds')}")
        _write_line(log_stream, f"workspace: {workdir}")
        _write_line(log_stream, f"log_file: {log_path}")

        map_path = _resolve_map_path(args, workdir)

        check_cmd: List[str] = [
            python_exe,
            "tools/check_mcu_artifacts.py",
            "--firmware-root",
            args.firmware_root,
        ]
        if map_path is not None:
            check_cmd.extend(["--map", str(map_path)])
        if args.fail_on_stale_map:
            check_cmd.append("--fail-on-stale-map")
        check_rc = _run_step("MCU artifact check", check_cmd, workdir, log_stream)
        summary.append(("MCU artifact check", check_rc))

        uart_cmd: List[str] = [python_exe, "tools/uart_smoke.py", "--scope", "--scope-channels", args.scope_channels]
        if args.port:
            uart_cmd.extend(["--port", args.port, "--baud", str(args.baud)])
        else:
            uart_cmd.append("--dry-run")
        for token in args.read_vars:
            uart_cmd.extend(["--read-var", token])
        for token in args.write_vars:
            uart_cmd.extend(["--write-var", token])
        uart_rc = _run_step("UART smoke", uart_cmd, workdir, log_stream)
        summary.append(("UART smoke", uart_rc))

        if not args.skip_dotnet_build:
            dotnet_rc = _run_step(
                "dotnet build",
                ["dotnet", "build", "MCUScope.sln"],
                workdir,
                log_stream,
            )
            summary.append(("dotnet build", dotnet_rc))

        _write_line(log_stream, "\n=== SUMMARY ===")
        failed = 0
        for label, rc in summary:
            status = "PASS" if rc == 0 else "FAIL"
            _write_line(log_stream, f"{status}  {label} (exit={rc})")
            if rc != 0:
                failed += 1

        if failed == 0:
            _write_line(log_stream, "Overall: PASSED")
        else:
            _write_line(log_stream, f"Overall: FAILED ({failed} step(s) failed)")

    print(f"\nRegression log written to: {log_path}")
    return 0 if all(rc == 0 for _, rc in summary) else 1


if __name__ == "__main__":
    sys.exit(main())
