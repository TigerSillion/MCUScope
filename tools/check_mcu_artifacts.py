#!/usr/bin/env python3
"""Validate that the MCU reference project is fully detached from closed libs.

This script checks:
1) Project configuration files do not reference deprecated closed libraries.
2) Replacement C source files exist and are listed in the project file.
3) (Optional) Linker map also excludes closed libs and includes replacement objects.
"""

from __future__ import annotations

import argparse
from pathlib import Path
import sys
from typing import Iterable, List


PROJECT_NAME = "RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100"
DEFAULT_FIRMWARE_ROOT = Path("Reference") / PROJECT_NAME

CLOSED_LIBS = [
    "ICS2_RX26T.lib",
    "r_motor_current_bemf_observer.lib",
    "r_motor_current_stall_detection.lib",
    "r_motor_current_trq_vib_comp.lib",
    "r_motor_current_volt_err_comp.lib",
    "r_motor_sensorless_vector_flyingstart.lib",
    "r_motor_speed_fluxwkn.lib",
    "r_motor_speed_opl_damp_ctrl.lib",
    "r_motor_speed_opl2less.lib",
]

REPLACEMENT_SOURCES = [
    "src/application/user_interface/ics/ICS2_RX26T.c",
    "src/application/motor_module/current/r_motor_current_bemf_observer.c",
    "src/application/motor_module/current/r_motor_current_stall_detection.c",
    "src/application/motor_module/current/r_motor_current_trq_vib_comp.c",
    "src/application/motor_module/current/r_motor_current_volt_err_comp.c",
    "src/application/motor_module/sensorless_vector/r_motor_sensorless_vector_flyingstart.c",
    "src/application/motor_module/speed/r_motor_speed_fluxwkn.c",
    "src/application/motor_module/speed/r_motor_speed_opl_damp_ctrl.c",
    "src/application/motor_module/speed/r_motor_speed_opl2less.c",
]

REPLACEMENT_OBJECTS = [Path(src).with_suffix(".obj").name for src in REPLACEMENT_SOURCES]


def _read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="ignore")


def _normalized_slash(text: str) -> str:
    # Normalize path separators to keep checks stable across Windows/XML variants.
    return text.replace("\\", "/").lower()


def _find_tokens(text: str, tokens: Iterable[str]) -> List[str]:
    lowered = text.lower()
    return [token for token in tokens if token.lower() in lowered]


def _print_result(ok: bool, message: str) -> None:
    tag = "PASS" if ok else "FAIL"
    print(f"[{tag}] {message}")


def _check_file_exists(path: Path, failures: List[str], description: str) -> bool:
    if path.exists():
        _print_result(True, f"{description}: {path}")
        return True
    failures.append(f"Missing {description}: {path}")
    _print_result(False, f"{description}: {path}")
    return False


def _validate_project_configs(firmware_root: Path, failures: List[str]) -> None:
    rcpc_path = firmware_root / f"{PROJECT_NAME}.rcpc"
    makefile_path = firmware_root / "HardwareDebug" / "makefile"

    if not _check_file_exists(rcpc_path, failures, "RCPC project file"):
        return
    if not _check_file_exists(makefile_path, failures, "HardwareDebug makefile"):
        return

    rcpc_text_raw = _read_text(rcpc_path)
    makefile_text = _read_text(makefile_path)
    rcpc_text = _normalized_slash(rcpc_text_raw)

    # Check no closed libraries remain in project settings.
    for label, text in (("RCPC", rcpc_text_raw), ("makefile", makefile_text)):
        hits = _find_tokens(text, CLOSED_LIBS)
        if hits:
            failures.append(f"{label} still references closed libs: {', '.join(hits)}")
            _print_result(False, f"{label} closed-lib check")
        else:
            _print_result(True, f"{label} closed-lib check")

    # Check replacement sources are listed in RCPC.
    missing_sources = [
        rel for rel in REPLACEMENT_SOURCES if rel.lower() not in rcpc_text
    ]
    if missing_sources:
        failures.append(
            "RCPC is missing replacement source entries: " + ", ".join(missing_sources)
        )
        _print_result(False, "RCPC replacement-source entry check")
    else:
        _print_result(True, "RCPC replacement-source entry check")


def _validate_source_files(firmware_root: Path, failures: List[str]) -> None:
    missing = []
    for rel in REPLACEMENT_SOURCES:
        full_path = firmware_root / Path(rel)
        if not full_path.exists():
            missing.append(rel)
    if missing:
        failures.append("Missing replacement source files: " + ", ".join(missing))
        _print_result(False, "Replacement source file existence check")
    else:
        _print_result(True, "Replacement source file existence check")


def _validate_map_file(map_path: Path, failures: List[str]) -> None:
    if not map_path.exists():
        failures.append(f"Map file not found: {map_path}")
        _print_result(False, f"Map file exists: {map_path}")
        return

    map_text = _read_text(map_path)

    closed_hits = _find_tokens(map_text, CLOSED_LIBS)
    if closed_hits:
        failures.append(
            "Map file still references closed libs (likely stale build): "
            + ", ".join(closed_hits)
        )
        _print_result(False, "Map closed-lib check")
    else:
        _print_result(True, "Map closed-lib check")

    missing_objects = [
        obj_name for obj_name in REPLACEMENT_OBJECTS if obj_name.lower() not in map_text.lower()
    ]
    if missing_objects:
        failures.append(
            "Map file missing replacement objects: " + ", ".join(missing_objects)
        )
        _print_result(False, "Map replacement-object check")
    else:
        _print_result(True, "Map replacement-object check")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Check that the RX26T reference project no longer depends on closed MCU libs."
        )
    )
    parser.add_argument(
        "--firmware-root",
        default=str(DEFAULT_FIRMWARE_ROOT),
        help=f"Path to MCU reference project root (default: {DEFAULT_FIRMWARE_ROOT})",
    )
    parser.add_argument(
        "--map",
        default=None,
        help=(
            "Optional linker map file path. "
            "If provided, script also validates map references and linked objects."
        ),
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    firmware_root = Path(args.firmware_root)
    failures: List[str] = []

    print("== MCU Artifact Validation ==")
    _check_file_exists(firmware_root, failures, "Firmware root")

    _validate_project_configs(firmware_root, failures)
    _validate_source_files(firmware_root, failures)

    if args.map:
        _validate_map_file(Path(args.map), failures)
    else:
        print("[INFO] Map validation skipped (use --map <path> to enable).")

    if failures:
        print("\nSummary: FAILED")
        for item in failures:
            print(f" - {item}")
        return 1

    print("\nSummary: PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
