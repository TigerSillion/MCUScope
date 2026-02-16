# USAGE.md

## Overview

This project contains:
- A PC GUI (`src/MCUScope`) that talks to MCU over UART.
- A reference RX26T firmware project under `Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100`.

The closed `ICS2_RX26T.lib` path is replaced by a C implementation:
- `Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/user_interface/ics/ICS2_RX26T.c`

## PC GUI Setup

1. Build and run the WPF app from `src/MCUScope/MCUScope.csproj`.
2. Open `Communication Settings`.
3. Set:
- `Baud Rate (bps)` to match MCU UART. Default firmware target is `1000000`.
- `ICS Base Clock (MHz)` is optional fallback and can be ignored in pure UART mode.
4. Select COM port and click connect.

## Variable Read/Write

1. Load a variable file (`.map`, `.sym`, `.csv`, `.xml`).
2. Add variables to watch table.
3. Click read/write controls.

Notes:
- Firmware enforces a whitelist. Only allowed addresses can be read/written.
- Type must match whitelist type for write operations.

## Scope Capture

1. Select channels in GUI (M1..M12).
2. Configure sample period and record length.
3. Click run.

Firmware side behavior:
- Sampling runs in `ics2_watchpoint()` path (called from control interrupt).
- Completed records are sent channel-by-channel using `WaveformData (0x84)` packets.

## MCU Reference Project Integration

Key files:
- `Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/mcu/rx26t/r_app_mcu.c`
- `Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/mcu/rx26t/r_app_mcu.h`
- `Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/user_interface/ics/ICS2_RX26T.c`
- `Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/src/application/user_interface/ics/ICS2_RX26T.h`

Current default UART config in firmware:
- Port: SCI6, P81/TXD6 and P80/RXD6 (`ICS_SCI6_P81_P80`)
- BRR: `4` with BGDM+ABCS enabled (about 1 Mbps when PCLKB is 40 MHz)
- Interrupt level: `4`

## Known Limits (Current Stage)

- This round replaces the ICS communication library only.
- Other closed motor control libraries are still linked and are tracked as follow-up work.
- Scope channel data source is fixed to 12 predefined runtime signals in `ICS2_RX26T.c`.
