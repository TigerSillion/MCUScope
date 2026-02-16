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

## Regression Checklist

After any protocol or variable map change:
- Validate `docs/UART_PROTOCOL.md` matches code.
- Validate GUI can still read at least one variable and one scope channel.
- Validate no buffer overflow or lockup under continuous scope run.

## Current Gaps

- No automated unit test harness exists yet for MCU protocol parser.
- Hardware-in-loop validation is required for timing and throughput confirmation.
- Control-loop dynamic behavior must be tuned and verified on real motor/inverter hardware.
