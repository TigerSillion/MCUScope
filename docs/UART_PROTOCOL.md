# UART_PROTOCOL.md

This document defines the UART protocol between the PC GUI and MCU firmware.

Frame format
- SYNC: 2 bytes, fixed 0xAA 0x55
- CMD: 1 byte
- LEN: 2 bytes, little-endian payload length
- PAYLOAD: LEN bytes
- CHECKSUM: 1 byte, sum of bytes from CMD through last payload byte, modulo 256

Notes
- All multi-byte numeric fields are little-endian.
- Float fields use IEEE-754 float32 as produced by BitConverter in .NET.

Command IDs
- 0x01 Ping
- 0x02 GetInfo
- 0x10 ReadVariable
- 0x11 WriteVariable
- 0x20 StartScope
- 0x21 StopScope
- 0x22 ScopeData
- 0x23 SetChannels (reserved)
- 0x24 SetTrigger
- 0x25 SetSampling (reserved)
- 0x80 Ack
- 0x81 Nack
- 0x82 InfoResponse
- 0x83 VariableData
- 0x84 WaveformData

Payload formats
GetInfo (0x02)
- Request: empty payload
- Response InfoResponse (0x82):
- cpuNameLen: 1 byte
- cpuName: ASCII
- libVersionLen: 1 byte
- libVersion: ASCII
- clockMHz: float32

ReadVariable (0x10)
- Request:
- nameLen: 1 byte
- name: ASCII
- address: uint32
- type: 1 byte, VariableType enum value
- Response VariableData (0x83):
- nameLen: 1 byte
- name: ASCII
- type: 1 byte, VariableType enum value
- value: variable-size raw bytes by type (1/2/4 bytes)

WriteVariable (0x11)
- Request:
- nameLen: 1 byte
- name: ASCII
- address: uint32
- type: 1 byte, VariableType enum value
- value: size depends on type
- Response: optional Ack (0x80) or Nack (0x81)

StartScope (0x20)
- Request:
- samplePeriod: float32
- recordLength: int32
- channelCount: 1 byte
- channel entries: repeated `channelCount` times
  - slot: 1 byte (GUI channel index 0..11)
  - type: 1 byte (VariableType enum value)
  - address: uint32 (target variable address)

StopScope (0x21)
- Request: empty payload

SetTrigger (0x24)
- Request:
- position: float32
- level: float32
- source: 1 byte
- mode: 1 byte
- edge: 1 byte

SetChannels (0x23)
- Request:
- channelCount: 1 byte
- channel entries: repeated `channelCount` times
  - slot: 1 byte
  - type: 1 byte
  - address: uint32

WaveformData (0x84)
- Response from MCU to PC:
- channelIndex: 1 byte
- samplePeriod: float32
- dataCount: uint16
- data: dataCount float32 values

VariableType enum values
- 0 UInt8
- 1 Int8
- 2 UInt16
- 3 Int16
- 4 UInt32
- 5 Int32
- 6 Float32
- 7 Bool
- 8 Logic
