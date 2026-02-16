# CHANGELOG.md

## Unreleased
- Fixed RX build linker issue `Undefined external symbol "___asm"` by removing unsupported inline `__asm("nop")` usage in `ICS2_RX26T.c`.
- Added `docs/MCU_CODE_AND_MOTOR_DEBUG_GUIDE.md` as a dedicated MCU source explanation, variable usage reference, and motor no-rotation debug workflow.
- Added `tools/cleanup_legacy_libs.py` to detect/remove obsolete closed `.lib` artifacts now replaced by C source files.
- Removed stale unused `MainViewModel` fields (`_autoSaveTimer`, `_isRunning`) to clear build warnings.
- Added `tools/run_regression.py` batch runner to execute artifact checks, UART smoke, and `dotnet build` with timestamped logs.
- Enhanced `tools/check_mcu_artifacts.py` with map freshness detection (warn by default, optional fail mode).
- Enhanced `tools/uart_smoke.py` with `--log-file` output mirroring for traceable smoke runs.
- Added `tools/check_mcu_artifacts.py` to verify replacement C sources are present and closed MCU libs are no longer referenced by project config (optional map-file strict validation included).
- Added `tools/uart_smoke.py` for UART protocol smoke testing with dry-run packet generation plus live GetInfo/read/write/scope checks.
- Fixed GUI build blockers by adding the missing `ToggleButton` namespace import and removing a missing icon reference from `MCUScope.csproj`.
- Added `ICS2_RX26T.c` UART protocol implementation in the RX26T firmware reference project, replacing the closed ICS communication library path.
- Added MCU-side whitelist-based variable read/write and 12-channel scope waveform streaming over UART frames.
- Updated PC protocol sender to use exact payload lengths for `StartScope` and `SetTrigger`.
- Replaced all remaining closed motor helper libraries with open C implementations:
  `r_motor_current_bemf_observer`, `r_motor_current_stall_detection`, `r_motor_current_trq_vib_comp`,
  `r_motor_current_volt_err_comp`, `r_motor_sensorless_vector_flyingstart`,
  `r_motor_speed_fluxwkn`, `r_motor_speed_opl_damp_ctrl`, `r_motor_speed_opl2less`.
- Updated RX26T project link configuration to remove all above `.lib` dependencies and build from C source files.
- Added `docs/USAGE.md` and `docs/TESTING.md`.
- Added `docs/AGENT_MCP_SKILLS.md` and linked it from `AGENTS.md`/`docs/AI_RULES.md`.
- Added UART baud rate configuration in the communication settings UI with base clock fallback.
- Added project rules, logging structure, and UART protocol documentation.
