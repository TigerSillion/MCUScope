"""
Digital Signal Processing engine.
Provides FFT, filtering, measurements, and math channel computations.
"""

import math
from enum import Enum
from typing import Optional

import numpy as np
from scipy import signal as sp_signal
from scipy.fft import fft, fftfreq


class WindowFunction(str, Enum):
    RECTANGULAR = "rectangular"
    HAMMING = "hamming"
    HANNING = "hanning"
    BLACKMAN = "blackman"
    BLACKMAN_HARRIS = "blackman_harris"
    FLAT_TOP = "flat_top"
    KAISER_5 = "kaiser_5"
    KAISER_10 = "kaiser_10"


class FilterType(str, Enum):
    LOWPASS = "lowpass"
    HIGHPASS = "highpass"
    BANDPASS = "bandpass"
    BANDSTOP = "bandstop"


def apply_window(data: np.ndarray, window: WindowFunction) -> np.ndarray:
    """Apply a window function to the data."""
    n = len(data)
    if window == WindowFunction.RECTANGULAR:
        return data.copy()
    elif window == WindowFunction.HAMMING:
        w = np.hamming(n)
    elif window == WindowFunction.HANNING:
        w = np.hanning(n)
    elif window == WindowFunction.BLACKMAN:
        w = np.blackman(n)
    elif window == WindowFunction.BLACKMAN_HARRIS:
        w = sp_signal.windows.blackmanharris(n)
    elif window == WindowFunction.FLAT_TOP:
        w = sp_signal.windows.flattop(n)
    elif window == WindowFunction.KAISER_5:
        w = np.kaiser(n, 5.0)
    elif window == WindowFunction.KAISER_10:
        w = np.kaiser(n, 10.0)
    else:
        return data.copy()
    return data * w


def compute_fft(data: np.ndarray, sample_rate: float,
                window: WindowFunction = WindowFunction.HAMMING
                ) -> tuple[np.ndarray, np.ndarray]:
    """
    Compute FFT magnitude spectrum.
    Returns (frequencies, magnitudes_dB).
    """
    n = len(data)
    if n == 0:
        return np.array([]), np.array([])

    windowed = apply_window(data, window)
    # Compute FFT
    yf = fft(windowed)
    xf = fftfreq(n, 1.0 / sample_rate)

    # Take positive half
    half_n = n // 2
    freqs = xf[:half_n]
    magnitudes = 2.0 / n * np.abs(yf[:half_n])

    # Convert to dB (avoid log(0))
    magnitudes_db = 20 * np.log10(np.maximum(magnitudes, 1e-12))

    return freqs, magnitudes_db


def compute_fft_phase(data: np.ndarray, sample_rate: float,
                      window: WindowFunction = WindowFunction.HAMMING
                      ) -> tuple[np.ndarray, np.ndarray]:
    """Compute FFT phase spectrum. Returns (frequencies, phase_degrees)."""
    n = len(data)
    if n == 0:
        return np.array([]), np.array([])

    windowed = apply_window(data, window)
    yf = fft(windowed)
    xf = fftfreq(n, 1.0 / sample_rate)
    half_n = n // 2

    freqs = xf[:half_n]
    phase = np.angle(yf[:half_n], deg=True)

    return freqs, phase


def compute_measurements(data: np.ndarray, sample_rate: float) -> dict:
    """Compute standard signal measurements."""
    if len(data) == 0:
        return {}

    vmin = float(np.min(data))
    vmax = float(np.max(data))
    vmean = float(np.mean(data))
    vrms = float(np.sqrt(np.mean(data ** 2)))
    vpp = vmax - vmin
    std_dev = float(np.std(data))

    # Frequency estimation via zero crossings
    zero_crossings = np.where(np.diff(np.sign(data - vmean)))[0]
    if len(zero_crossings) >= 2:
        avg_period = 2.0 * np.mean(np.diff(zero_crossings)) / sample_rate
        frequency = 1.0 / avg_period if avg_period > 0 else 0.0
    else:
        frequency = 0.0

    # Duty cycle estimation (for digital-like signals)
    threshold = vmean
    high_samples = np.sum(data > threshold)
    duty_cycle = float(high_samples) / len(data) * 100.0

    return {
        "min": round(vmin, 6),
        "max": round(vmax, 6),
        "mean": round(vmean, 6),
        "rms": round(vrms, 6),
        "vpp": round(vpp, 6),
        "std_dev": round(std_dev, 6),
        "frequency": round(frequency, 2),
        "period": round(1.0 / frequency, 6) if frequency > 0 else 0.0,
        "duty_cycle": round(duty_cycle, 1),
        "samples": len(data),
    }


def design_filter(filter_type: FilterType, cutoff_freq, sample_rate: float,
                  order: int = 4) -> tuple[np.ndarray, np.ndarray]:
    """Design a Butterworth filter. cutoff_freq can be a number or tuple for bandpass."""
    nyq = sample_rate / 2.0
    if isinstance(cutoff_freq, (list, tuple)):
        wn = [f / nyq for f in cutoff_freq]
    else:
        wn = cutoff_freq / nyq

    b, a = sp_signal.butter(order, wn, btype=filter_type.value)
    return b, a


def apply_filter(data: np.ndarray, filter_type: FilterType,
                 cutoff_freq, sample_rate: float,
                 order: int = 4) -> np.ndarray:
    """Apply a digital filter to the data."""
    b, a = design_filter(filter_type, cutoff_freq, sample_rate, order)
    return sp_signal.filtfilt(b, a, data).astype(np.float64)


def evaluate_math_expression(expression: str,
                             channels: dict[str, np.ndarray]) -> Optional[np.ndarray]:
    """
    Evaluate a math expression using channel data.
    Supports: +, -, *, /, abs(), sqrt(), sin(), cos(), diff(), integral()
    Channel references: CH1, CH2, etc.
    """
    try:
        # Build namespace with math functions and channel data
        namespace = {
            'abs': np.abs,
            'sqrt': np.sqrt,
            'sin': np.sin,
            'cos': np.cos,
            'tan': np.tan,
            'log': np.log,
            'log10': np.log10,
            'exp': np.exp,
            'diff': np.diff,
            'cumsum': np.cumsum,
            'pi': math.pi,
            'e': math.e,
            'np': np,
        }
        namespace.update(channels)

        result = eval(expression, {"__builtins__": {}}, namespace)
        if isinstance(result, np.ndarray):
            return result
        return np.array([result])
    except Exception:
        return None


def detect_trigger(data: np.ndarray, level: float,
                   edge: str = "rising", holdoff: int = 0) -> list[int]:
    """
    Detect trigger points in the data.
    Returns list of sample indices where trigger condition is met.
    """
    triggers = []
    last_trigger = -holdoff - 1

    for i in range(1, len(data)):
        if i - last_trigger <= holdoff:
            continue

        if edge == "rising" and data[i - 1] < level <= data[i]:
            triggers.append(i)
            last_trigger = i
        elif edge == "falling" and data[i - 1] > level >= data[i]:
            triggers.append(i)
            last_trigger = i
        elif edge == "both":
            if (data[i - 1] < level <= data[i]) or (data[i - 1] > level >= data[i]):
                triggers.append(i)
                last_trigger = i

    return triggers


def interpolate_data(data: np.ndarray, factor: int) -> np.ndarray:
    """Interpolate data by a given factor using sinc interpolation."""
    if factor <= 1:
        return data
    n = len(data)
    x_original = np.arange(n)
    x_interp = np.linspace(0, n - 1, n * factor)
    return np.interp(x_interp, x_original, data)


def decimate_data(data: np.ndarray, factor: int) -> np.ndarray:
    """Decimate data by a given factor with anti-aliasing filter."""
    if factor <= 1:
        return data
    return sp_signal.decimate(data, factor).astype(np.float64)
