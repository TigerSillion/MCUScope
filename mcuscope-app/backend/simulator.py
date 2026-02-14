"""
Signal simulator for development and demo mode.
Generates realistic test signals when no hardware is connected.
"""

import math
import time
from typing import Optional

import numpy as np


class SignalSimulator:
    """Generates simulated MCU signals for testing."""

    def __init__(self, num_channels: int = 4, sample_rate: float = 10000.0,
                 buffer_size: int = 1024):
        self.num_channels = num_channels
        self.sample_rate = sample_rate
        self.buffer_size = buffer_size
        self._time_offset = 0.0
        self._running = False
        self._channel_configs = self._default_configs()

    def _default_configs(self) -> list[dict]:
        """Default signal configurations for each channel."""
        return [
            {  # CH1: Sine wave (simulating an analog sensor)
                "type": "sine",
                "frequency": 50.0,
                "amplitude": 3.3,
                "offset": 0.0,
                "noise": 0.05,
                "phase": 0.0,
            },
            {  # CH2: Square wave (simulating a PWM signal)
                "type": "square",
                "frequency": 100.0,
                "amplitude": 5.0,
                "offset": 0.0,
                "noise": 0.02,
                "duty_cycle": 0.6,
            },
            {  # CH3: Triangle/sawtooth (simulating a ramp)
                "type": "triangle",
                "frequency": 25.0,
                "amplitude": 2.5,
                "offset": 1.25,
                "noise": 0.03,
            },
            {  # CH4: Composite signal (simulating vibration data)
                "type": "composite",
                "frequencies": [30.0, 90.0, 150.0],
                "amplitudes": [2.0, 0.8, 0.3],
                "offset": 0.0,
                "noise": 0.1,
            },
        ]

    def configure_channel(self, channel: int, config: dict):
        """Update configuration for a specific channel."""
        if 0 <= channel < self.num_channels:
            self._channel_configs[channel].update(config)

    def generate_samples(self, num_samples: Optional[int] = None) -> list[list[float]]:
        """Generate a batch of samples for all channels."""
        n = num_samples or self.buffer_size
        t = np.linspace(
            self._time_offset,
            self._time_offset + n / self.sample_rate,
            n,
            endpoint=False,
        )
        self._time_offset += n / self.sample_rate

        channels = []
        for i in range(self.num_channels):
            if i < len(self._channel_configs):
                data = self._generate_signal(t, self._channel_configs[i])
            else:
                data = np.zeros(n)
            channels.append(data.tolist())

        return channels

    def _generate_signal(self, t: np.ndarray, config: dict) -> np.ndarray:
        """Generate signal based on configuration."""
        sig_type = config.get("type", "sine")
        noise_level = config.get("noise", 0.0)
        offset = config.get("offset", 0.0)

        if sig_type == "sine":
            freq = config.get("frequency", 50.0)
            amp = config.get("amplitude", 1.0)
            phase = config.get("phase", 0.0)
            data = amp * np.sin(2 * np.pi * freq * t + phase)

        elif sig_type == "square":
            freq = config.get("frequency", 50.0)
            amp = config.get("amplitude", 1.0)
            duty = config.get("duty_cycle", 0.5)
            data = amp * sp_square(2 * np.pi * freq * t, duty)

        elif sig_type == "triangle":
            freq = config.get("frequency", 50.0)
            amp = config.get("amplitude", 1.0)
            data = amp * sp_triangle(2 * np.pi * freq * t)

        elif sig_type == "sawtooth":
            freq = config.get("frequency", 50.0)
            amp = config.get("amplitude", 1.0)
            data = amp * sp_sawtooth(2 * np.pi * freq * t)

        elif sig_type == "composite":
            frequencies = config.get("frequencies", [50.0])
            amplitudes = config.get("amplitudes", [1.0])
            data = np.zeros_like(t)
            for f, a in zip(frequencies, amplitudes):
                data += a * np.sin(2 * np.pi * f * t)

        elif sig_type == "step":
            amp = config.get("amplitude", 1.0)
            step_time = config.get("step_time", t[len(t) // 2])
            data = np.where(t >= step_time, amp, 0.0)

        elif sig_type == "noise":
            amp = config.get("amplitude", 1.0)
            data = amp * np.random.randn(len(t))

        elif sig_type == "damped_sine":
            freq = config.get("frequency", 50.0)
            amp = config.get("amplitude", 1.0)
            decay = config.get("decay", 5.0)
            t_local = t - t[0]
            data = amp * np.exp(-decay * t_local) * np.sin(2 * np.pi * freq * t_local)

        else:
            data = np.zeros_like(t)

        # Add noise
        if noise_level > 0:
            data += noise_level * np.random.randn(len(t))

        return data + offset


def sp_square(x: np.ndarray, duty: float = 0.5) -> np.ndarray:
    """Generate square wave."""
    return np.where(np.mod(x / (2 * np.pi), 1.0) < duty, 1.0, -1.0)


def sp_triangle(x: np.ndarray) -> np.ndarray:
    """Generate triangle wave."""
    phase = np.mod(x / (2 * np.pi), 1.0)
    return 4.0 * np.abs(phase - 0.5) - 1.0


def sp_sawtooth(x: np.ndarray) -> np.ndarray:
    """Generate sawtooth wave."""
    return 2.0 * (np.mod(x / (2 * np.pi), 1.0) - 0.5)
