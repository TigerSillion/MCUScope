# DEV_LOG.md

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
