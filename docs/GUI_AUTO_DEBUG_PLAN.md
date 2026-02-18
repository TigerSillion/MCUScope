# GUI Auto Debug Plan (STM32 + RX MAP Compatible)

## 1. Objective

Build a repeatable, mostly automated debug flow that validates:
- STM32 protocol handshake and scope stream at 2000000 bps.
- GUI variable loading from both STM32 Keil MAP and RX CCRX MAP.
- Read/write/scope workflows and drag-drop scope binding behavior.
- Error diagnostics (NACK reason mapping, invalid variable rejection).

## 2. Scope

In scope:
- `tools/auto_debug_stm32.py` and `tools/uart_smoke.py` automation flow.
- GUI runtime checks for variable browser, watch, scope, trigger, and logs.
- MAP parsing compatibility for Keil/ARM and CCRX formats.

Out of scope:
- Real motor power stage behavior validation.
- MCU control algorithm numerical fidelity tuning.

## 3. Preconditions

- STM32 board is connected and running firmware with ICS2 protocol support.
- UART is available on host (example: `COM7`) at `2000000` bps.
- Firmware supports commands: `Ping/GetInfo/ReadVariable/WriteVariable/StartScope/StopScope/SetTrigger`.
- Project builds locally with `.NET 8` and Python 3.

## 4. Test Assets

- STM32 project: `Reference/G431_KEIL_Sample`
- STM32 map: `Reference/G431_KEIL_Sample/MDK-ARM/G431_KEIL_Sample/G431_KEIL_Sample.map`
- RX map: `Reference/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100/HardwareDebug/RX26T_MCBA2_MCILV1_PM_LESS_FOC_WFS_E2S_V100.map`
- Protocol spec: `docs/UART_PROTOCOL.md`

## 5. Automated Execution Flow

1. Build GUI and tools:
   - `dotnet build MCUScope.sln`
2. Run one-click STM32 smoke:
   - `python tools/auto_debug_stm32.py --port COM7 --baud 2000000`
3. Fast rerun for iterative debug:
   - `python tools/auto_debug_stm32.py --port COM7 --baud 2000000 --skip-build`

Expected automated checks:
- `GetInfo` response valid.
- Read/write command variables pass with ACK.
- Scope start/waveform/stop pass.
- Map symbol resolution supports missing nested Keil members via known offsets.

## 6. GUI Functional Validation Matrix

### 6.1 Variable import and filtering

- Load STM32 Keil MAP; verify non-empty variable list.
- Load RX CCRX MAP; verify non-empty variable list.
- Confirm internal/non-scalar symbols are filtered from bindable watch/scope lists.
- Confirm protocol scalar symbols (1/2/4 bytes) are selectable.

Pass criteria:
- No parser crash.
- No internal transport symbols appear in bindable dropdown sources.

### 6.2 Watch read/write

- Add command variable to watch.
- Perform read loop; values update.
- Write new value; MCU returns ACK.
- Try read-only or invalid variable write; GUI logs rejection without protocol storm.

Pass criteria:
- Correct value updates.
- Write rejection is explicit in log.

### 6.3 Scope and waveform

- Bind 2-3 valid variables to channels.
- Start scope and verify waveform data arrival.
- Stop scope and verify ACK.
- Try non-bindable variable; GUI rejects before sending invalid scope request.

Pass criteria:
- Chart updates and no repeated NACK flood.

### 6.4 Drag-drop binding

- Drag one variable from Variable Browser to a specific scope row.
- Drag one variable to grid blank area and verify fallback to first empty channel.
- Drop invalid/non-bindable symbol and verify warning log.

Pass criteria:
- Channel binding changes immediately.
- Rejected drops are explained in log.

### 6.5 Diagnostics and logs

- Disconnect MCU or send invalid command.
- Verify GUI NACK reason text mapping and connection health logs.

Pass criteria:
- Logs include reasoned messages (`invalid payload`, `bad variable`, etc.).

## 7. Regression Checklist (Per Change)

1. `dotnet build MCUScope.sln` must pass.
2. `python tools/auto_debug_stm32.py --port COM7 --baud 2000000 --skip-build` must pass.
3. Load both MAP formats and verify variable list generation.
4. Verify drag-drop + scope run once manually.
5. Update `docs/DEV_LOG.md`, `docs/TESTING.md`, and `docs/CHANGELOG.md` for user-visible behavior.

## 8. Failure Triage Order

1. Transport layer:
   - baud, COM, framing, checksum.
2. Protocol command path:
   - ACK/NACK rates, reason code mapping, payload lengths.
3. Symbol resolution:
   - MAP parser output, address/type/size correctness.
4. GUI bindability filter:
   - scalar/internal filtering logic and dropdown source.
5. Scope pipeline:
   - start payload, channel types/addresses, waveform decode.

## 9. Deliverables

- Stable automation command for STM32 smoke validation.
- Confirmed dual-MAP compatibility behavior (Keil + CCRX).
- Updated docs and logs for traceability.
