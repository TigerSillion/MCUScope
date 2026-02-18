# TESTING.md

## Test Matrix

- Protocol framing and checksum
- GetInfo handshake
- Variable read/write correctness
- Scope start/stop and waveform packet stream
- UART stability at target baud rate
- BEMF observer and PLL update path (no linker dependency on closed lib)
- Flux weakening / OPL damping / OPL2LESS switching helper path
- Flying-start state-action sequence
- Torque vibration compensation function path (LUT and PAT entry points)
- Automated project/link dependency regression check
- Automated UART protocol smoke execution script

## Automated Checks (Host Side)

Run from repository root:

1. Project/source dependency check (no hardware needed):
`python tools/check_mcu_artifacts.py`

2. Optional map-level verification (requires a fresh firmware build artifact):
`python tools/check_mcu_artifacts.py --map Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/HardwareDebug/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100.map`

3. UART smoke dry-run (packet build and parser path check):
`python tools/uart_smoke.py --dry-run --scope --scope-var 0:0x00001829:u8 --scope-var 1:0x00007650:f32`

4. UART smoke on hardware (`pyserial` required):
`python tools/uart_smoke.py --port COM5 --baud 1000000 --scope --scope-var 0:0x00001829:u8 --scope-var 1:0x00007650:f32`

5. UART variable read/write smoke on hardware:
`python tools/uart_smoke.py --port COM5 --read-var com_u1_system_mode:0x00001829:u8 --write-var com_u1_system_mode:0x00001829:u8:1`

6. One-click batch regression (artifacts + UART smoke + dotnet build):
`python tools/run_regression.py`

7. Enable stale-map hard fail in batch mode:
`python tools/run_regression.py --fail-on-stale-map --map Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/HardwareDebug/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100.map`

8. Persist UART smoke output in custom file:
`python tools/uart_smoke.py --dry-run --scope --scope-var 0:0x00001829:u8 --scope-var 1:0x00007650:f32 --log-file logs/uart_smoke_latest.log`

9. Verify and clean obsolete closed libs after source replacement:
`python tools/cleanup_legacy_libs.py`
`python tools/cleanup_legacy_libs.py --delete`

## PC-side Checks

1. Open GUI and connect to target COM port.
2. Confirm status string changes to connected after `GetInfo`.
3. Trigger single variable read and verify value updates in watch table.
4. Write a safe writable variable and confirm value changes on next read.
5. Start scope and verify waveform traces update.
6. Stop scope and verify stream stops.

## Firmware-side Checks

1. Confirm SCI6 pin mux and BRR configuration is applied in `ics2_init()`.
2. Confirm RX interrupt stores bytes (ring buffer head changes).
3. Confirm TX interrupt drains queued packets.
4. Confirm malformed packets are ignored (bad checksum).
5. Confirm unsupported variable address returns NACK.
6. Confirm project links without `*.lib` motor helper dependencies listed in `docs/USAGE.md`.
7. Confirm all replacement C files are compiled into target (map file symbol check).

## Recommended Bench Procedure

1. Power MCU board.
2. Connect USB-UART to P81/P80 path used by firmware.
3. Start at `1000000 bps, 8N1, no flow control`.
4. Run protocol smoke tests in this order:
- `GetInfo`
- `ReadVariable`
- `WriteVariable`
- `StartScope`
- `StopScope`

## STM32 G431 Live Smoke (2000000 bps)

Example commands for `Reference/G431_KEIL_Sample` firmware:

1. GetInfo + read:
`python tools/uart_smoke.py --port COM5 --baud 2000000 --read-var com_u1_system_mode:0xADDR:u8`

2. Write speed command + read back:
`python tools/uart_smoke.py --port COM5 --baud 2000000 --write-var com_f4_ref_speed_rpm:0xADDR:f32:900 --read-var com_f4_ref_speed_rpm:0xADDR:f32`

3. Scope two float channels:
`python tools/uart_smoke.py --port COM5 --baud 2000000 --scope --scope-var 0:0xADDR:f32 --scope-var 1:0xADDR:f32`

Notes:
- Replace `0xADDR` with real symbol addresses from STM32 map.
- STM32 variable access currently uses a whitelist table in `Core/Src/usart.c`.

## Regression Checklist

After any protocol or variable map change:
- Validate `docs/UART_PROTOCOL.md` matches code.
- Validate GUI can still read at least one variable and one scope channel.
- Validate no buffer overflow or lockup under continuous scope run.

## Current Gaps

- No pure unit test project exists for GUI protocol handlers; current automation is script-based smoke coverage.
- Hardware-in-loop validation is required for timing and throughput confirmation.
- Control-loop dynamic behavior must be tuned and verified on real motor/inverter hardware.
