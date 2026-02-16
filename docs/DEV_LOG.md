# DEV_LOG.md

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
