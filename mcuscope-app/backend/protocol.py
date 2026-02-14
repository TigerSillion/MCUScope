"""
ICSP Protocol implementation for MCU communication.
Handles serial framing, checksum, and command/response encoding
compatible with ScopeBox-style devices.
"""

import struct
from enum import IntEnum
from typing import Optional


class Command(IntEnum):
    """ICSP command codes."""
    CONNECT = 0x01
    DISCONNECT = 0x02
    READ_VARIABLE = 0x10
    WRITE_VARIABLE = 0x11
    READ_SCOPE_DATA = 0x20
    SET_SCOPE_CHANNEL = 0x21
    SET_TRIGGER = 0x22
    START_SCOPE = 0x23
    STOP_SCOPE = 0x24
    GET_DEVICE_INFO = 0x30
    GET_VARIABLE_LIST = 0x31
    ACK = 0x06
    NAK = 0x15


class DataType(IntEnum):
    """Supported MCU data types."""
    INT8 = 0x01
    UINT8 = 0x02
    INT16 = 0x03
    UINT16 = 0x04
    INT32 = 0x05
    UINT32 = 0x06
    FLOAT32 = 0x07
    FLOAT64 = 0x08
    BOOL = 0x09


DATA_TYPE_SIZE = {
    DataType.INT8: 1,
    DataType.UINT8: 1,
    DataType.INT16: 2,
    DataType.UINT16: 2,
    DataType.INT32: 4,
    DataType.UINT32: 4,
    DataType.FLOAT32: 4,
    DataType.FLOAT64: 8,
    DataType.BOOL: 1,
}

DATA_TYPE_FORMAT = {
    DataType.INT8: '<b',
    DataType.UINT8: '<B',
    DataType.INT16: '<h',
    DataType.UINT16: '<H',
    DataType.INT32: '<i',
    DataType.UINT32: '<I',
    DataType.FLOAT32: '<f',
    DataType.FLOAT64: '<d',
    DataType.BOOL: '<B',
}


STX = 0x02
ETX = 0x03


def compute_checksum(data: bytes) -> int:
    """Compute XOR checksum over data."""
    cs = 0
    for b in data:
        cs ^= b
    return cs & 0xFF


def build_frame(command: int, payload: bytes = b'') -> bytes:
    """Build an ICSP protocol frame: STX LEN CMD PAYLOAD CHECKSUM ETX."""
    length = len(payload) + 1  # +1 for command byte
    body = bytes([length, command]) + payload
    cs = compute_checksum(body)
    return bytes([STX]) + body + bytes([cs, ETX])


def parse_frame(data: bytes) -> Optional[tuple[int, bytes]]:
    """
    Parse an ICSP response frame.
    Returns (command, payload) or None if invalid.
    """
    if len(data) < 5:
        return None
    if data[0] != STX or data[-1] != ETX:
        return None
    length = data[1]
    command = data[2]
    payload = data[3:3 + length - 1]
    expected_cs = data[3 + length - 1]
    actual_cs = compute_checksum(data[1:3 + length - 1])
    if expected_cs != actual_cs:
        return None
    return command, payload


def decode_value(raw: bytes, dtype: DataType) -> float:
    """Decode raw bytes to a numeric value based on data type."""
    size = DATA_TYPE_SIZE[dtype]
    fmt = DATA_TYPE_FORMAT[dtype]
    return struct.unpack(fmt, raw[:size])[0]


def encode_value(value: float, dtype: DataType) -> bytes:
    """Encode a numeric value to raw bytes based on data type."""
    fmt = DATA_TYPE_FORMAT[dtype]
    return struct.pack(fmt, int(value) if dtype != DataType.FLOAT32 and dtype != DataType.FLOAT64 else value)
