# CHANGELOG.md

## Unreleased
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
