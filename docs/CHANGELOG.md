# CHANGELOG.md

## Unreleased
- Added `ICS2_RX26T.c` UART protocol implementation in the RX26T firmware reference project, replacing the closed ICS communication library path.
- Added MCU-side whitelist-based variable read/write and 12-channel scope waveform streaming over UART frames.
- Updated PC protocol sender to use exact payload lengths for `StartScope` and `SetTrigger`.
- Added `docs/USAGE.md` and `docs/TESTING.md`.
- Added UART baud rate configuration in the communication settings UI with base clock fallback.
- Added project rules, logging structure, and UART protocol documentation.
