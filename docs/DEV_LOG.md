# DEV_LOG.md

## 2026-02-18
Summary: Fixed STM32 Keil MAP variable undercount (only a few globals shown) by parsing image symbol table entries.
Files: src/MCUScope/Services/VariableFileService.cs, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- Added Keil `Image Symbol Table` parser in `VariableFileService` and merged results with existing execution-region parser using `(name,address)` dedup.
- Included only RAM `Data` symbols and filtered linker/internal/local-static style symbols (`[Anonymous Symbol]`, `$$`, dotted names, internal prefixes).
- This restores discovery of merged-section symbols such as `com_u1_system_mode`, `com_f4_ref_speed_rpm`, `g_u1_system_mode`, etc., instead of only a few top-level section symbols.
Tests:
- `dotnet build MCUScope.sln` executed; passed (`0 warning`, `0 error`) after releasing `MCUScope.exe` file lock.
- `python tools/auto_debug_stm32.py --port COM7 --baud 2000000 --skip-build` executed; passed (`GetInfo/read/write/scope/stop` all pass).

## 2026-02-18
Summary: Added a dedicated GUI auto-debug plan and executed full STM32 automated validation run.
Files: docs/GUI_AUTO_DEBUG_PLAN.md, docs/DEV_LOG.md.
Behavior:
- Added `docs/GUI_AUTO_DEBUG_PLAN.md` with:
  - dual MAP compatibility validation steps (STM32 Keil + RX CCRX),
  - automated execution flow for `tools/auto_debug_stm32.py`,
  - GUI functional validation matrix (watch/scope/drag-drop/logs),
  - regression checklist and triage order.
Tests:
- `python tools/auto_debug_stm32.py --port COM7 --baud 2000000` executed; passed (`GetInfo/read/write/scope/stop` all pass).
- `python tools/auto_debug_stm32.py --port COM7 --baud 2000000 --skip-build` executed; passed.

## 2026-02-18
Summary: Fixed high-frequency NACK/debugability issues, added scope drag-drop, and added one-click STM32 automated validation.
Files: src/MCUScope/Models/VariableInfo.cs, src/MCUScope/Services/VariableFileService.cs, src/MCUScope/Services/SessionState.cs, src/MCUScope/Services/IcsProtocolService.cs, src/MCUScope/ViewModels/ScopeViewModel.cs, src/MCUScope/ViewModels/VariableBrowserViewModel.cs, src/MCUScope/Views/VariableBrowserPanel.xaml, src/MCUScope/Views/VariableBrowserPanel.xaml.cs, src/MCUScope/Views/MainWindow.xaml, src/MCUScope/Views/MainWindow.xaml.cs, Reference/G431_KEIL_Sample/Core/Src/usart.c, tools/auto_debug_stm32.py, docs/USAGE.md, docs/TESTING.md, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- GUI variable pipeline:
  - Added `DeclaredSize` and scalar/internal symbol checks to `VariableInfo`.
  - MAP parser now stamps declared symbol size and applies internal-symbol filtering earlier.
  - Session variable-name list now excludes internal or non-scalar symbols to prevent invalid watch/scope bindings.
- GUI runtime safety:
  - Scope run path rejects non-scalar/internal variables before sending protocol commands.
  - Read/write requests now skip invalid/read-only symbols with explicit log entries.
  - NACK logs now decode reason code (`invalid payload`, `unsupported cmd`, `bad variable`, `bad scope`, etc.).
- UX:
  - Implemented drag-drop from Variable Browser DataGrid to Scope channel DataGrid row/first-empty channel.
  - Added drop-time validation and log feedback.
- STM32 firmware protocol:
  - Added `Ping (0x01)` ACK handling.
  - Added NACK payload reason codes for invalid payload/unsupported command/bad variable/write denied/bad scope/bad trigger.
- Automation:
  - Added `tools/auto_debug_stm32.py` for one-click build (optional) + map-symbol resolution + UART smoke read/write/scope.
  - Added derived-offset fallback for struct-member addresses when Keil map does not emit nested member symbols.
Tests:
- `dotnet build MCUScope.sln` executed; passed (`0 warning`, `0 error`).
- `python -m py_compile tools/auto_debug_stm32.py` executed; passed.
- `python tools/auto_debug_stm32.py --port COM7 --baud 2000000 --skip-build` executed; passed (`GetInfo/read/write/scope/stop` all pass).
- `dotnet run --project src/MCUScope/MCUScope.csproj` launched and process-alive check passed.

## 2026-02-18
Summary: Added dual MAP compatibility for STM32 Keil/ARM and RX CCRX variable parsing.
Files: src/MCUScope/Services/VariableFileService.cs, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- Enhanced `LoadMapFormat` to parse two concrete map families in one flow:
  - Renesas RX CCRX symbol-list style (existing logic retained).
  - STM32 Keil/ARM execution-region table style (new logic added).
- Added Keil row parser for execution-region lines and extracted RAM variables from section names such as:
  - `.data.<symbol>`
  - `.bss.<symbol>`
  - `.noinit.<symbol>`
  - `.zidata.<symbol>`
- Added filtering for linker/internal pseudo-symbols (`.L_*`, `Region$$*`, `HEAP`, `STACK`) and merged all parsed results by `(name,address)` dedup set.
- Updated MAP load log to include Keil parsed count for easier source-format diagnostics.
Tests:
- `dotnet build MCUScope.sln` executed; blocked by file lock on running process `MCUScope.exe` (`MSB3021/MSB3027`), so full build verification not completed in this run.

## 2026-02-18
Summary: Started STM32 G431 execution by implementing repository UART protocol handling and simulated motor-state variable updates for GUI joint debug.
Files: Reference/G431_KEIL_Sample/Core/Inc/motor_sim_vars.h, Reference/G431_KEIL_Sample/Core/Inc/usart.h, Reference/G431_KEIL_Sample/Core/Src/main.c, Reference/G431_KEIL_Sample/Core/Src/usart.c, docs/USAGE.md, docs/TESTING.md, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- Added shared simulation-global declarations in `motor_sim_vars.h` to expose command/status/sensorless debug variables with stable symbol addresses.
- Extended `main.c` with deterministic simulated motor-state updates driven by `com_u1_system_mode`, `com_f4_ref_speed_rpm`, and `com_f4_speed_rate_limit_rpm`.
- Added continuous update for key debug outputs under `g_st_sensorless_vector` (`vdc`, phase currents, speed/current outputs, state-machine status) and integrated this update in the main loop.
- Added `ICS2_ProtocolPoll()` call in main loop to support scope sampling/streaming scheduling.
- Reworked STM32 `LPUART1` user code path in `usart.c` to support repository protocol (`0xAA 0x55` + sum checksum) with:
  - frame parser and dispatch,
  - `GetInfo`, variable read/write, scope start/stop, trigger set handlers,
  - whitelist-based variable access table,
  - waveform packet generation (`WaveformData 0x84`),
  - ACK/NACK responses.
- Kept protocol path switchable through `USE_ICS2_PROTOCOL` compile-time flag; set to `1` by default for MCUScope GUI linkage.
- Increased UART TX queue frame limit to `768` bytes to fit scope packets at moderate record lengths.
- Updated `docs/USAGE.md` and `docs/TESTING.md` with STM32 integration and live smoke command templates at `2000000` bps.
Tests:
- Not run (local Keil/board flash and live serial validation pending in this workspace).

## 2026-02-18
Summary: Added a detailed STM32 G431 execution plan for RX26T algorithm porting, simulated motor-state updates, and GUI joint debug.
Files: docs/STM32_G431_RX26T_PORTING_PLAN.md, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- Added a repository-local plan document tailored to `Reference/G431_KEIL_Sample` current baseline (`LPUART1 + DMA`, `2000000` bps, SerialDriver callback flow).
- Documented protocol-gap handling between STM32 sample SerialDriver framing and repository GUI protocol in `docs/UART_PROTOCOL.md`, including a staged migration strategy.
- Defined phased execution details for: protocol alignment, RX26T algorithm portability layer, simulated motor plant/state machine, global variable update contracts, scope streaming, and GUI joint-debug acceptance.
- Included concrete file-level touch points, validation commands, risk controls, milestone estimates, and Definition of Done criteria.
Tests:
- Not run (documentation-only change).

## 2026-02-16
Summary: Fixed GUI variable loading/visibility issues and completed requested UX upgrades (serial local status, arbitrary baud, Chinese help menu, microsecond auto-read).
Files: src/MCUScope/Services/VariableFileService.cs, src/MCUScope/ViewModels/MainViewModel.cs, src/MCUScope/Views/MainWindow.xaml, src/MCUScope/Views/MainWindow.xaml.cs, src/MCUScope/Dialogs/CommunicationSettingsDialog.xaml, src/MCUScope/Dialogs/CommunicationSettingsDialog.xaml.cs, src/MCUScope/Dialogs/VariableSettingsDialog.xaml.cs, docs/GUI_OPERATION_MANUAL_CN.md, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- `VariableFileService.LoadMapFormat` now parses Renesas linker symbol-list map format (`symbol line + data metadata line`) and falls back to generic map parsing if needed, improving `.map` variable discovery.
- `VariableSettingsDialog` now loads editable copies from real runtime variables and applies edits back through `MainViewModel.ApplyVariableSettings` (including alias/type/scale/RW/comment).
- `MainWindow` status bar now shows serial local state (`Port/Baud/Local/MCU`) via `SerialLocalStatusText`, refreshed on port/connection/setting changes.
- Communication settings dialog now provides editable arbitrary baud-rate input plus common presets and writes changes back with immediate local-status refresh.
- Help menu added `使用说明（中文）`, with click handler that opens `docs/GUI_OPERATION_MANUAL_CN.md` directly.
- Auto-read interval now uses microseconds (`AutoReadIntervalUs`) with minimum clamp `1 us`.
- Watch/Scope Name columns switched to template `ComboBox` bindings against `VariableNames`, improving variable-list refresh consistency after load/update.
- Updated Chinese GUI manual with the above behavior and a revised improvement plan.
Tests:
- `dotnet build MCUScope.sln` executed; passed with `0 warning` and `0 error`.
- `dotnet run --project src/MCUScope/MCUScope.csproj` executed via background process; GUI process started successfully and remained running until terminated by test script.
- PowerShell regex check executed on `Reference/.../HardwareDebug/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100.map`; Renesas symbol pattern matched 99 global data symbols.

## 2026-02-16
Summary: Fixed WPF GUI runtime startup failure and verified the desktop app launches successfully.
Files: src/MCUScope/Views/MainWindow.xaml, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- Removed invalid `Height=\"*\"` from `TabControl` in `MainWindow.xaml` (WPF `Height` expects a numeric length, not star sizing).
- Eliminated startup `XamlParseException` at `MainWindow.xaml` line 114 during `dotnet run`.
Tests:
- `dotnet run --project src/MCUScope/MCUScope.csproj` executed; no startup parse exception.
- GUI process confirmed running: `MCUScope.exe` started via background run.

## 2026-02-16
Summary: Implemented unrestricted (address-based) global variable read/write and dynamic scope variable binding from GUI to MCU.
Files: Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/user_interface/ics/ICS2_RX26T.c, src/MCUScope/Services/IcsProtocolService.cs, src/MCUScope/ViewModels/MainViewModel.cs, tools/uart_smoke.py, tools/run_regression.py, docs/UART_PROTOCOL.md, docs/USAGE.md, docs/TESTING.md, docs/GUI_OPERATION_MANUAL_CN.md, docs/MCU_CODE_AND_MOTOR_DEBUG_GUIDE.md, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- MCU side (`ICS2_RX26T.c`):
  - Removed fixed variable whitelist dependency and switched read/write to direct `address + type` access.
  - Added configurable RAM-region validation guards before dereferencing pointers, preventing invalid-range access by protocol requests.
  - Updated scope engine to accept per-channel descriptors (`slot + type + address`) in `StartScope`/`SetChannels` payloads.
  - Removed fixed M1..M12 hardcoded signal mapping; sampling now reads selected variables dynamically.
- PC GUI side:
  - `IcsProtocolService.RequestReadVariable` now sends variable type along with address.
  - Added alias/display-name variable resolution path for watch/scope operations.
  - `StartScope` now sends dynamic channel descriptors for each visible channel.
  - `MainViewModel.RunScope` now resolves selected channel variables to address/type and blocks run if no valid channel is configured.
  - Watch readback update now supports alias/display-name rows.
- Tooling/docs:
  - `tools/uart_smoke.py` updated to new packet format (`--read-var NAME:ADDR[:TYPE]`, `--scope-var SLOT:ADDR:TYPE`).
  - `tools/run_regression.py` passes through `--scope-var`.
  - Protocol/usage/testing/manual/debug docs updated to reflect "no whitelist + dynamic scope binding" behavior.
Tests:
- `dotnet build MCUScope.sln` executed; passed with `0 warning` and `0 error`.
- `python -m py_compile tools/uart_smoke.py tools/run_regression.py` executed; passed.
- `python tools/uart_smoke.py --dry-run --scope --scope-var 0:0x00001829:u8 --scope-var 1:0x00007650:f32 --read-var com_u1_system_mode:0x00001829:u8` executed; passed.
- `python tools/run_regression.py --skip-dotnet-build --no-map` executed; passed.

## 2026-02-16
Summary: Added a complete Chinese GUI operation manual aligned to current MCU UART implementation and documented a prioritized GUI improvement backlog.
Files: docs/GUI_OPERATION_MANUAL_CN.md, docs/USAGE.md, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- Added `docs/GUI_OPERATION_MANUAL_CN.md` with chapter-style structure similar to ICS manuals, covering:
  - system architecture and communication prerequisites,
  - GUI menu/panel operation details,
  - end-to-end read/write/scope workflows,
  - exact MCU variable whitelist (29 entries) and fixed scope channel mapping (M1..M12) derived from `ICS2_RX26T.c`.
- Added an explicit “current gaps and improvement plan” section with P0/P1/P2 priorities and acceptance targets for unfinished GUI features (Variable Settings, Array Editor, Custom Control Panel, Timetable Player, trigger/cursor/roll/FFT enhancements, logging/tests).
- Updated `docs/USAGE.md` overview to link the new Chinese GUI manual.
Tests:
- `dotnet build MCUScope.sln` executed; passed with `0 warning` and `0 error`.

## 2026-02-16
Summary: Fixed RX MCU linker failure caused by unsupported inline assembly token in UART init delay loop.
Files: Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/user_interface/ics/ICS2_RX26T.c, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- Replaced `__asm("nop")` in `serial_init_sci6()` delay loop with a pure volatile loop body.
- Prevents generation of unresolved external symbol `___asm` in `ICS2_RX26T.obj` on toolchains where `__asm` is not treated as inline assembly keyword.
- Delay intent is preserved because loop counter is `volatile`.
Tests:
- `rg --line-number "__asm\\(" Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application` executed; no remaining inline `__asm` in application sources.

## 2026-02-16
Summary: Added dedicated MCU algorithm/debug documentation and implemented obsolete closed-lib cleanup tool.
Files: tools/cleanup_legacy_libs.py, docs/MCU_CODE_AND_MOTOR_DEBUG_GUIDE.md, docs/USAGE.md, docs/TESTING.md, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- Added `docs/MCU_CODE_AND_MOTOR_DEBUG_GUIDE.md` as a standalone document covering MCU source architecture, module explanations, UART whitelist variable usage, tuning sequence, and "motor not rotating" debug workflow.
- Added `tools/cleanup_legacy_libs.py` to dry-run or delete obsolete closed `.lib` files that are replaced by C implementations.
- Updated usage/testing docs to include cleanup commands and pointer to the new dedicated debug guide.
- Executed cleanup of obsolete closed libs in local firmware workspace (`--delete`), removing 9 legacy module `.lib` files under `src/application/...`.
Tests:
- `python tools/cleanup_legacy_libs.py` executed; dry-run found 9 removable legacy libs.
- `python tools/cleanup_legacy_libs.py --delete` executed; removed 9 legacy libs.
- `python -m py_compile tools/cleanup_legacy_libs.py` executed; passed.

## 2026-02-16
Summary: Cleaned up stale ViewModel fields to eliminate remaining WPF build warnings.
Files: src/MCUScope/ViewModels/MainViewModel.cs, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- Removed unused fields `_autoSaveTimer` and `_isRunning` from `MainViewModel`.
- Removed the now-unneeded `_autoSaveTimer` disposal call.
- Build output is now warning-free for current solution state.
Tests:
- `dotnet build MCUScope.sln` executed; passed with `0 warning` and `0 error`.

## 2026-02-16
Summary: Extended automation to one-click regression runs with persistent logs and map freshness guard.
Files: tools/check_mcu_artifacts.py, tools/uart_smoke.py, tools/run_regression.py, docs/USAGE.md, docs/TESTING.md, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- `tools/check_mcu_artifacts.py` now checks map freshness against RCPC/makefile/replacement source timestamps when `--map` is provided.
- Added `--fail-on-stale-map` option to enforce strict stale-map failure for CI-like gating.
- `tools/uart_smoke.py` now supports `--log-file` and mirrors full stdout/stderr to a trace file for protocol debug traceability.
- Added `tools/run_regression.py` to execute artifact checks + UART smoke (dry-run or live serial) + optional `dotnet build` in one command with timestamped logs.
- Updated usage/testing docs with concrete batch commands and log-related examples.
Tests:
- `python -m py_compile tools/check_mcu_artifacts.py tools/uart_smoke.py tools/run_regression.py` executed; passed.
- `python tools/check_mcu_artifacts.py` executed; passed.
- `python tools/check_mcu_artifacts.py --map Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/HardwareDebug/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100.map` executed; failed as expected because the stale map still references removed `.lib` files and does not include replacement object files.
- `python tools/uart_smoke.py --dry-run --scope --scope-channels 0,1 --log-file logs/uart_smoke_dryrun.log` executed; passed.
- `python tools/run_regression.py --skip-dotnet-build --no-map` executed; passed.

## 2026-02-16
Summary: Added automation tooling for MCU dependency regression checks and UART protocol smoke testing.
Files: tools/check_mcu_artifacts.py, tools/uart_smoke.py, docs/USAGE.md, docs/TESTING.md, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- Added `tools/check_mcu_artifacts.py` to verify replacement source files are present, RCPC/makefile no longer reference removed closed libs, and (optionally) map files contain replacement objects and no legacy `.lib` usage.
- Added `tools/uart_smoke.py` to run UART protocol smoke flows over serial (`GetInfo`, optional variable read/write, optional scope start/stop and waveform wait).
- Added `--dry-run` path in UART smoke tool to validate packet generation and parser helpers without hardware.
- Updated usage/testing docs with concrete command lines for host-only checks and hardware smoke runs.
Tests:
- `python tools/check_mcu_artifacts.py` executed; passed.
- `python tools/uart_smoke.py --dry-run --scope --scope-channels 0,1 --read-var com_u1_system_mode:0x00001829 --write-var com_u1_system_mode:0x00001829:u8:1` executed; passed.
- `python -m py_compile tools/check_mcu_artifacts.py tools/uart_smoke.py` executed; passed.

## 2026-02-16
Summary: Fixed PC GUI compile blockers in WPF dialog and project icon configuration.
Files: src/MCUScope/Dialogs/CustomControlPanelDialog.xaml.cs, src/MCUScope/MCUScope.csproj, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- Added `System.Windows.Controls.Primitives` import so `ToggleButton` resolves during WPF build.
- Removed missing icon reference (`Resources\\mcuscope.ico`) from `MCUScope.csproj` to unblock compilation in current workspace.
Tests: `dotnet build MCUScope.sln` executed; build succeeds with warnings only (`MainViewModel` unused/private fields).

## 2026-02-16
Summary: Replaced all remaining closed motor helper `.lib` modules with open C source implementations and removed linker dependencies from the RX26T project config.
Files: Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/motor_module/current/r_motor_current_bemf_observer.c, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/motor_module/current/r_motor_current_stall_detection.c, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/motor_module/current/r_motor_current_trq_vib_comp.c, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/motor_module/current/r_motor_current_volt_err_comp.c, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/motor_module/sensorless_vector/r_motor_sensorless_vector_flyingstart.c, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/motor_module/speed/r_motor_speed_fluxwkn.c, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/motor_module/speed/r_motor_speed_opl_damp_ctrl.c, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/motor_module/speed/r_motor_speed_opl2less.c, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100.rcpc, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/HardwareDebug/makefile, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/user_interface/ics/ICS2_RX26T.h, AGENTS.md, docs/AI_RULES.md, docs/AGENT_MCP_SKILLS.md, docs/USAGE.md, docs/TESTING.md, docs/CHANGELOG.md, docs/DEV_LOG.md.
Behavior:
- BEMF observer + PLL path now implemented in C with runtime-safe limits and configurable gains.
- Stall detection now implemented in C using filtered current magnitude and configurable threshold path.
- Torque vibration compensation (LUT/PAT entry points, tracking filter, state transitions) now implemented in C.
- Voltage error compensation now implemented in C with interpolation tables and configurable voltage limit ratio.
- Flying-start sequence now implemented in C with action/state outputs compatible with existing state-machine calls.
- Flux weakening, open-loop damping, and OPL2LESS helper functions now implemented in C.
- Project linker config no longer references the closed motor helper `.lib` modules listed above.
- Added a dedicated `docs/AGENT_MCP_SKILLS.md` baseline and linked it from existing agent rule files.
Tests:
- `dotnet build MCUScope.sln` executed; still fails at pre-existing `ToggleButton` type error in `src/MCUScope/Dialogs/CustomControlPanelDialog.xaml.cs`.
- Symbol/prototype presence check completed by `rg` against all replaced module headers and new C files.

## 2026-02-16
Summary: Implemented MCU-side UART protocol stack in C to replace `ICS2_RX26T.lib` and aligned PC packet layout.
Files: Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/user_interface/ics/ICS2_RX26T.c, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/user_interface/ics/ICS2_RX26T.h, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/mcu/rx26t/r_app_mcu.c, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/mcu/rx26t/r_app_mcu.h, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100.rcpc, Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/HardwareDebug/makefile, src/MCUScope/Services/IcsProtocolService.cs, docs/UART_PROTOCOL.md, docs/USAGE.md, docs/TESTING.md, docs/DEV_LOG.md, docs/CHANGELOG.md.
Behavior: MCU now supports UART frame parsing, ACK/NACK, GetInfo, whitelisted variable read/write, scope start/stop, trigger/sampling commands, and waveform packet streaming without relying on the closed ICS communication lib. PC now sends strict-length StartScope/SetTrigger payloads.
Tests: `dotnet build MCUScope.sln` executed; failed due pre-existing `ToggleButton` missing type in `src/MCUScope/Dialogs/CustomControlPanelDialog.xaml.cs`. No new error from this round was observed in build output.

## 2026-02-16
Summary: Added UART baud rate configuration and base clock fallback in the PC GUI.
Files: src/MCUScope/Dialogs/CommunicationSettingsDialog.xaml, src/MCUScope/Dialogs/CommunicationSettingsDialog.xaml.cs, src/MCUScope/ViewModels/MainViewModel.cs, docs/DEV_LOG.md, docs/CHANGELOG.md.
Behavior: GUI uses explicit baud rate when set; base clock is optional fallback for compatibility.
Tests: Not run (manual verification pending).

## 2026-02-16
Summary: Added agent and AI rules plus protocol documentation scaffolding.
Files: AGENTS.md, docs/AI_RULES.md, docs/DEV_LOG.md, docs/CHANGELOG.md, docs/UART_PROTOCOL.md.
Behavior: No runtime behavior changes. Documentation only.
Tests: Not run (docs-only change).
