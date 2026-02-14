"""
Serial port manager for MCU device communication.
Handles connection, auto-detection, read/write with the ICSP protocol.
"""

import asyncio
import logging
import time
from dataclasses import dataclass, field
from typing import Optional, Callable

import serial
import serial.tools.list_ports

from .protocol import (
    Command, DataType, DATA_TYPE_SIZE,
    build_frame, parse_frame, decode_value, encode_value
)

logger = logging.getLogger(__name__)


@dataclass
class ChannelConfig:
    """Configuration for a scope channel."""
    channel_id: int
    variable_address: int = 0
    data_type: DataType = DataType.FLOAT32
    name: str = ""
    enabled: bool = True
    scale: float = 1.0
    offset: float = 0.0


@dataclass
class DeviceInfo:
    """Information about the connected device."""
    device_name: str = "Unknown"
    firmware_version: str = "0.0.0"
    max_wave_channels: int = 4
    max_watch_channels: int = 8
    max_sample_rate: int = 100000
    buffer_size: int = 4096


class SerialManager:
    """Manages serial communication with MCU devices."""

    def __init__(self):
        self._port: Optional[serial.Serial] = None
        self._connected = False
        self._device_info = DeviceInfo()
        self._lock = asyncio.Lock()
        self._channels: list[ChannelConfig] = []
        self._on_data_callback: Optional[Callable] = None

    @property
    def connected(self) -> bool:
        return self._connected

    @property
    def device_info(self) -> DeviceInfo:
        return self._device_info

    @staticmethod
    def list_ports() -> list[dict]:
        """List available serial ports."""
        ports = []
        for port in serial.tools.list_ports.comports():
            ports.append({
                "port": port.device,
                "description": port.description,
                "hwid": port.hwid,
            })
        return ports

    async def connect(self, port: str, baudrate: int = 115200,
                      timeout: float = 1.0) -> bool:
        """Connect to a serial port."""
        async with self._lock:
            try:
                if self._port and self._port.is_open:
                    self._port.close()

                self._port = serial.Serial(
                    port=port,
                    baudrate=baudrate,
                    bytesize=serial.EIGHTBITS,
                    parity=serial.PARITY_NONE,
                    stopbits=serial.STOPBITS_ONE,
                    timeout=timeout,
                )

                # Send connect command
                frame = build_frame(Command.CONNECT)
                self._port.write(frame)
                response = self._read_response()

                if response and response[0] == Command.ACK:
                    self._connected = True
                    # Try to get device info
                    await self._fetch_device_info()
                    logger.info(f"Connected to {port} at {baudrate} baud")
                    return True
                else:
                    # Even without ACK, consider connected for basic serial
                    self._connected = True
                    logger.info(f"Connected to {port} (no ACK, basic mode)")
                    return True

            except serial.SerialException as e:
                logger.error(f"Connection failed: {e}")
                self._connected = False
                return False

    async def disconnect(self):
        """Disconnect from serial port."""
        async with self._lock:
            if self._port and self._port.is_open:
                try:
                    frame = build_frame(Command.DISCONNECT)
                    self._port.write(frame)
                except Exception:
                    pass
                finally:
                    self._port.close()
            self._connected = False
            logger.info("Disconnected")

    def _read_response(self, timeout: float = 0.5) -> Optional[tuple[int, bytes]]:
        """Read and parse a response frame from the device."""
        if not self._port or not self._port.is_open:
            return None

        start_time = time.time()
        buf = bytearray()

        while time.time() - start_time < timeout:
            if self._port.in_waiting:
                buf.extend(self._port.read(self._port.in_waiting))
                # Try to find a complete frame
                stx_idx = buf.find(0x02)
                if stx_idx >= 0:
                    etx_idx = buf.find(0x03, stx_idx)
                    if etx_idx >= 0:
                        frame_data = bytes(buf[stx_idx:etx_idx + 1])
                        return parse_frame(frame_data)
            time.sleep(0.01)

        return None

    async def _fetch_device_info(self):
        """Fetch device information."""
        if not self._port or not self._port.is_open:
            return
        try:
            frame = build_frame(Command.GET_DEVICE_INFO)
            self._port.write(frame)
            response = self._read_response()
            if response and len(response[1]) >= 4:
                payload = response[1]
                # Parse device info from payload
                self._device_info.max_wave_channels = payload[0] if len(payload) > 0 else 4
                self._device_info.max_watch_channels = payload[1] if len(payload) > 1 else 8
        except Exception as e:
            logger.warning(f"Could not fetch device info: {e}")

    async def read_variable(self, address: int, dtype: DataType) -> Optional[float]:
        """Read a single variable from the device."""
        async with self._lock:
            if not self._connected or not self._port:
                return None
            try:
                size = DATA_TYPE_SIZE[dtype]
                payload = address.to_bytes(4, 'little') + bytes([dtype, size])
                frame = build_frame(Command.READ_VARIABLE, payload)
                self._port.write(frame)
                response = self._read_response()
                if response and response[0] == Command.ACK:
                    return decode_value(response[1], dtype)
            except Exception as e:
                logger.error(f"Read variable error: {e}")
            return None

    async def write_variable(self, address: int, value: float,
                             dtype: DataType) -> bool:
        """Write a value to a device variable."""
        async with self._lock:
            if not self._connected or not self._port:
                return False
            try:
                encoded = encode_value(value, dtype)
                payload = address.to_bytes(4, 'little') + bytes([dtype]) + encoded
                frame = build_frame(Command.WRITE_VARIABLE, payload)
                self._port.write(frame)
                response = self._read_response()
                return response is not None and response[0] == Command.ACK
            except Exception as e:
                logger.error(f"Write variable error: {e}")
                return False

    async def configure_channel(self, config: ChannelConfig) -> bool:
        """Configure a scope channel on the device."""
        async with self._lock:
            if not self._connected or not self._port:
                return False
            try:
                payload = bytes([config.channel_id, config.data_type]) + \
                          config.variable_address.to_bytes(4, 'little')
                frame = build_frame(Command.SET_SCOPE_CHANNEL, payload)
                self._port.write(frame)
                response = self._read_response()
                return response is not None
            except Exception as e:
                logger.error(f"Configure channel error: {e}")
                return False

    async def start_scope(self, sample_rate: int, buffer_size: int) -> bool:
        """Start scope data acquisition."""
        async with self._lock:
            if not self._connected or not self._port:
                return False
            try:
                payload = sample_rate.to_bytes(4, 'little') + \
                          buffer_size.to_bytes(4, 'little')
                frame = build_frame(Command.START_SCOPE, payload)
                self._port.write(frame)
                response = self._read_response()
                return response is not None
            except Exception as e:
                logger.error(f"Start scope error: {e}")
                return False

    async def stop_scope(self) -> bool:
        """Stop scope data acquisition."""
        async with self._lock:
            if not self._connected or not self._port:
                return False
            try:
                frame = build_frame(Command.STOP_SCOPE)
                self._port.write(frame)
                response = self._read_response()
                return response is not None
            except Exception as e:
                logger.error(f"Stop scope error: {e}")
                return False

    async def read_scope_data(self, num_channels: int,
                              buffer_size: int) -> Optional[list[list[float]]]:
        """Read scope buffer data from device."""
        async with self._lock:
            if not self._connected or not self._port:
                return None
            try:
                payload = bytes([num_channels]) + buffer_size.to_bytes(4, 'little')
                frame = build_frame(Command.READ_SCOPE_DATA, payload)
                self._port.write(frame)

                # Read large data response
                response = self._read_response(timeout=2.0)
                if response and response[0] == Command.ACK:
                    return self._parse_scope_data(response[1], num_channels, buffer_size)
            except Exception as e:
                logger.error(f"Read scope data error: {e}")
            return None

    def _parse_scope_data(self, payload: bytes, num_channels: int,
                          buffer_size: int) -> list[list[float]]:
        """Parse raw scope data into channel arrays."""
        import struct
        channels = [[] for _ in range(num_channels)]
        bytes_per_sample = 4  # float32
        total_samples = len(payload) // bytes_per_sample

        for i in range(total_samples):
            ch = i % num_channels
            offset = i * bytes_per_sample
            value = struct.unpack('<f', payload[offset:offset + bytes_per_sample])[0]
            channels[ch].append(value)

        return channels
