# CHANGELOG.md

## Unreleased
- Fixed MAP loading flow for Renesas linker map files by adding a dedicated symbol-list parser and fallback parser in `VariableFileService`, so variable lists populate after `.map` import.
- Fixed `Variables Settings` dialog to edit real variable metadata (type/scale/RW/alias/comment) and apply changes back to runtime variable definitions.
- Added serial local status display (`Port/Baud/Local/MCU`) to the status bar and refreshed it on port refresh/select/connect/disconnect/communication setting updates.
- Upgraded communication settings dialog to support editable arbitrary baud rates with common preset options and immediate effective-rate feedback.
- Added Help menu entry `使用说明（中文）` and wired it to open `docs/GUI_OPERATION_MANUAL_CN.md` directly from GUI.
- Switched auto-read interval to microseconds (`AutoReadIntervalUs`) with `1 us` minimum configuration support.
- Updated Watch/Scope variable name columns to ComboBox templates bound to runtime variable list for reliable refresh after variable-file reload.
- Updated `docs/GUI_OPERATION_MANUAL_CN.md` to document the new Help entry, arbitrary baud-rate operation, us-level auto-read, MAP-variable refresh behavior, and revised improvement backlog.
- Fixed GUI startup crash caused by invalid WPF `Height="*"` on `TabControl` in `MainWindow.xaml` (XAML parse error at runtime).
- Added free global-variable address access on MCU UART protocol (removed fixed whitelist dependency) with RAM-range safety checks in `ICS2_RX26T.c`.
- Added dynamic scope channel binding (`slot + type + address`) so GUI channels can sample arbitrary variables instead of fixed M1..M12 source mapping.
- Updated PC protocol client (`IcsProtocolService`) and scope start flow (`MainViewModel`) to send address/type metadata per channel and support alias/display-name variable resolution.
- Updated UART tooling/docs (`tools/uart_smoke.py`, `tools/run_regression.py`, `docs/UART_PROTOCOL.md`, `docs/USAGE.md`, `docs/TESTING.md`, `docs/GUI_OPERATION_MANUAL_CN.md`, `docs/MCU_CODE_AND_MOTOR_DEBUG_GUIDE.md`) for the new address-based read/write and dynamic scope format.
- Added `docs/GUI_OPERATION_MANUAL_CN.md` as a full Chinese GUI operation manual (ICS-style chapter flow), including current implemented behavior, MCU-side variable/scope mapping tables, known GUI gaps, and a prioritized improvement plan (P0/P1/P2).
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
