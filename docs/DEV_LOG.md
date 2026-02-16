# DEV_LOG.md

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
