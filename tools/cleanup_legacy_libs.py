#!/usr/bin/env python3
"""Clean up obsolete closed .lib artifacts replaced by C source implementations.

Default mode is dry-run. Use --delete to remove files.
"""

from __future__ import annotations

import argparse
from pathlib import Path
import sys
from typing import List


PROJECT_NAME = "RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100"
DEFAULT_FIRMWARE_ROOT = Path("Reference") / PROJECT_NAME

OBSOLETE_LIB_RELATIVE_PATHS = [
    "src/application/user_interface/ics/ICS2_RX26T.lib",
    "src/application/motor_module/current/r_motor_current_bemf_observer.lib",
    "src/application/motor_module/current/r_motor_current_stall_detection.lib",
    "src/application/motor_module/current/r_motor_current_trq_vib_comp.lib",
    "src/application/motor_module/current/r_motor_current_volt_err_comp.lib",
    "src/application/motor_module/sensorless_vector/r_motor_sensorless_vector_flyingstart.lib",
    "src/application/motor_module/speed/r_motor_speed_fluxwkn.lib",
    "src/application/motor_module/speed/r_motor_speed_opl_damp_ctrl.lib",
    "src/application/motor_module/speed/r_motor_speed_opl2less.lib",
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Remove obsolete closed .lib files that are already replaced by C sources."
        )
    )
    parser.add_argument(
        "--firmware-root",
        default=str(DEFAULT_FIRMWARE_ROOT),
        help=f"Firmware root path (default: {DEFAULT_FIRMWARE_ROOT})",
    )
    parser.add_argument(
        "--delete",
        action="store_true",
        help="Actually delete files. Without this flag, only dry-run output is shown.",
    )
    parser.add_argument(
        "--include-build-lib",
        action="store_true",
        help=(
            "Also remove HardwareDebug build library output "
            f"({PROJECT_NAME}.lib)."
        ),
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    firmware_root = Path(args.firmware_root)

    targets: List[Path] = [
        firmware_root / Path(rel_path) for rel_path in OBSOLETE_LIB_RELATIVE_PATHS
    ]
    if args.include_build_lib:
        targets.append(firmware_root / "HardwareDebug" / f"{PROJECT_NAME}.lib")

    existing = [path for path in targets if path.exists()]
    missing = [path for path in targets if not path.exists()]

    mode = "DELETE" if args.delete else "DRY-RUN"
    print(f"== Legacy Lib Cleanup ({mode}) ==")
    print(f"Firmware root: {firmware_root}")

    for path in existing:
        if args.delete:
            path.unlink()
            print(f"[DELETED] {path}")
        else:
            print(f"[FOUND]   {path}")

    for path in missing:
        print(f"[MISSING] {path}")

    if args.delete:
        print(f"\nRemoved {len(existing)} file(s).")
    else:
        print(f"\nDry-run complete. {len(existing)} removable file(s) found.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
