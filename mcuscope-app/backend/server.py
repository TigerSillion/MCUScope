"""
FastAPI WebSocket server for MCUScope.
Provides real-time data streaming, REST API for configuration,
and serves the frontend application.
"""

import asyncio
import json
import logging
import os
import time
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Optional

import numpy as np
from fastapi import FastAPI, WebSocket, WebSocketDisconnect, HTTPException
from fastapi.responses import FileResponse, HTMLResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel

from .dsp import (
    compute_fft, compute_fft_phase, compute_measurements,
    apply_filter, evaluate_math_expression, detect_trigger,
    WindowFunction, FilterType, interpolate_data, decimate_data
)
from .serial_manager import SerialManager, ChannelConfig, DataType
from .simulator import SignalSimulator

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Global state
serial_mgr = SerialManager()
simulator = SignalSimulator(num_channels=4, sample_rate=10000.0, buffer_size=1024)
active_connections: list[WebSocket] = []
acquisition_task: Optional[asyncio.Task] = None
app_state = {
    "mode": "simulator",  # "simulator" or "device"
    "running": False,
    "sample_rate": 10000.0,
    "buffer_size": 1024,
    "num_channels": 4,
    "trigger_enabled": False,
    "trigger_channel": 0,
    "trigger_level": 0.0,
    "trigger_edge": "rising",
    "update_interval": 0.05,  # 50ms = 20fps
}


# --- Pydantic models ---

class ConnectionRequest(BaseModel):
    port: str
    baudrate: int = 115200


class ChannelConfigRequest(BaseModel):
    channel_id: int
    variable_address: int = 0
    data_type: str = "FLOAT32"
    name: str = ""
    enabled: bool = True
    scale: float = 1.0
    offset: float = 0.0


class TriggerConfig(BaseModel):
    enabled: bool = False
    channel: int = 0
    level: float = 0.0
    edge: str = "rising"
    holdoff: int = 0


class FFTRequest(BaseModel):
    channel: int = 0
    window: str = "hamming"


class FilterRequest(BaseModel):
    channel: int
    filter_type: str = "lowpass"
    cutoff: float = 1000.0
    order: int = 4


class MathChannelRequest(BaseModel):
    expression: str
    channel_names: dict[str, int] = {}


class SimulatorConfig(BaseModel):
    channel: int
    config: dict


class AcquisitionConfig(BaseModel):
    sample_rate: float = 10000.0
    buffer_size: int = 1024
    num_channels: int = 4
    update_interval: float = 0.05


# --- App lifecycle ---

@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("MCUScope server starting...")
    yield
    # Cleanup
    if acquisition_task and not acquisition_task.done():
        acquisition_task.cancel()
    await serial_mgr.disconnect()
    logger.info("MCUScope server stopped")


app = FastAPI(title="MCUScope", version="2.0.0", lifespan=lifespan)

# Serve frontend
frontend_dir = Path(__file__).parent.parent / "frontend"
if frontend_dir.exists():
    app.mount("/css", StaticFiles(directory=str(frontend_dir / "css")), name="css")
    app.mount("/js", StaticFiles(directory=str(frontend_dir / "js")), name="js")


# --- Frontend routes ---

@app.get("/")
async def serve_index():
    index_path = frontend_dir / "index.html"
    if index_path.exists():
        return FileResponse(str(index_path))
    return HTMLResponse("<h1>MCUScope v2.0</h1><p>Frontend not found.</p>")


# --- REST API ---

@app.get("/api/status")
async def get_status():
    return {
        "connected": serial_mgr.connected,
        "mode": app_state["mode"],
        "running": app_state["running"],
        "sample_rate": app_state["sample_rate"],
        "buffer_size": app_state["buffer_size"],
        "num_channels": app_state["num_channels"],
        "device_info": {
            "name": serial_mgr.device_info.device_name,
            "firmware": serial_mgr.device_info.firmware_version,
            "max_wave_channels": serial_mgr.device_info.max_wave_channels,
            "max_watch_channels": serial_mgr.device_info.max_watch_channels,
        } if serial_mgr.connected else None,
    }


@app.get("/api/ports")
async def list_ports():
    return {"ports": serial_mgr.list_ports()}


@app.post("/api/connect")
async def connect_device(req: ConnectionRequest):
    success = await serial_mgr.connect(req.port, req.baudrate)
    if success:
        app_state["mode"] = "device"
        return {"status": "connected", "port": req.port}
    raise HTTPException(status_code=500, detail="Connection failed")


@app.post("/api/disconnect")
async def disconnect_device():
    await serial_mgr.disconnect()
    app_state["mode"] = "simulator"
    return {"status": "disconnected"}


@app.post("/api/acquisition/config")
async def configure_acquisition(config: AcquisitionConfig):
    app_state["sample_rate"] = config.sample_rate
    app_state["buffer_size"] = config.buffer_size
    app_state["num_channels"] = config.num_channels
    app_state["update_interval"] = config.update_interval
    simulator.sample_rate = config.sample_rate
    simulator.buffer_size = config.buffer_size
    simulator.num_channels = config.num_channels
    return {"status": "configured"}


@app.post("/api/acquisition/start")
async def start_acquisition():
    global acquisition_task
    if app_state["running"]:
        return {"status": "already_running"}
    app_state["running"] = True
    acquisition_task = asyncio.create_task(acquisition_loop())
    return {"status": "started"}


@app.post("/api/acquisition/stop")
async def stop_acquisition():
    global acquisition_task
    app_state["running"] = False
    if acquisition_task and not acquisition_task.done():
        acquisition_task.cancel()
        try:
            await acquisition_task
        except asyncio.CancelledError:
            pass
    acquisition_task = None
    return {"status": "stopped"}


@app.post("/api/trigger")
async def configure_trigger(config: TriggerConfig):
    app_state["trigger_enabled"] = config.enabled
    app_state["trigger_channel"] = config.channel
    app_state["trigger_level"] = config.level
    app_state["trigger_edge"] = config.edge
    return {"status": "configured"}


@app.post("/api/simulator/configure")
async def configure_simulator(config: SimulatorConfig):
    simulator.configure_channel(config.channel, config.config)
    return {"status": "configured"}


@app.post("/api/channel/config")
async def configure_channel(config: ChannelConfigRequest):
    if app_state["mode"] == "device":
        dtype = DataType[config.data_type]
        ch_config = ChannelConfig(
            channel_id=config.channel_id,
            variable_address=config.variable_address,
            data_type=dtype,
            name=config.name,
            enabled=config.enabled,
            scale=config.scale,
            offset=config.offset,
        )
        await serial_mgr.configure_channel(ch_config)
    return {"status": "configured"}


# --- WebSocket ---

async def acquisition_loop():
    """Main acquisition loop that generates/reads data and broadcasts to clients."""
    logger.info("Acquisition started")
    try:
        while app_state["running"]:
            start_time = time.time()

            # Get data
            if app_state["mode"] == "device" and serial_mgr.connected:
                data = await serial_mgr.read_scope_data(
                    app_state["num_channels"],
                    app_state["buffer_size"]
                )
                if data is None:
                    data = simulator.generate_samples()
            else:
                data = simulator.generate_samples()

            # Apply trigger if enabled
            trigger_point = None
            if app_state["trigger_enabled"] and data:
                ch_idx = app_state["trigger_channel"]
                if ch_idx < len(data):
                    triggers = detect_trigger(
                        np.array(data[ch_idx]),
                        app_state["trigger_level"],
                        app_state["trigger_edge"]
                    )
                    if triggers:
                        trigger_point = triggers[0]

            # Build message
            timestamp = time.time()
            message = {
                "type": "scope_data",
                "timestamp": timestamp,
                "sample_rate": app_state["sample_rate"],
                "channels": data,
                "trigger_point": trigger_point,
            }

            # Broadcast to all connected clients
            msg_str = json.dumps(message)
            disconnected = []
            for ws in active_connections:
                try:
                    await ws.send_text(msg_str)
                except Exception:
                    disconnected.append(ws)
            for ws in disconnected:
                active_connections.remove(ws)

            # Maintain update rate
            elapsed = time.time() - start_time
            sleep_time = max(0, app_state["update_interval"] - elapsed)
            await asyncio.sleep(sleep_time)

    except asyncio.CancelledError:
        pass
    except Exception as e:
        logger.error(f"Acquisition error: {e}")
    finally:
        app_state["running"] = False
        logger.info("Acquisition stopped")


@app.websocket("/ws")
async def websocket_endpoint(ws: WebSocket):
    await ws.accept()
    active_connections.append(ws)
    logger.info(f"WebSocket client connected ({len(active_connections)} total)")

    try:
        while True:
            # Handle incoming commands from client
            text = await ws.receive_text()
            msg = json.loads(text)
            await handle_ws_command(ws, msg)
    except WebSocketDisconnect:
        pass
    except Exception as e:
        logger.error(f"WebSocket error: {e}")
    finally:
        if ws in active_connections:
            active_connections.remove(ws)
        logger.info(f"WebSocket client disconnected ({len(active_connections)} total)")


async def handle_ws_command(ws: WebSocket, msg: dict):
    """Handle commands received over WebSocket."""
    cmd = msg.get("command")

    if cmd == "get_fft":
        ch_idx = msg.get("channel", 0)
        window = msg.get("window", "hamming")
        data = msg.get("data")
        if data:
            arr = np.array(data)
            wf = WindowFunction(window)
            freqs, mags = compute_fft(arr, app_state["sample_rate"], wf)
            await ws.send_text(json.dumps({
                "type": "fft_result",
                "channel": ch_idx,
                "frequencies": freqs.tolist(),
                "magnitudes": mags.tolist(),
            }))

    elif cmd == "get_fft_phase":
        ch_idx = msg.get("channel", 0)
        window = msg.get("window", "hamming")
        data = msg.get("data")
        if data:
            arr = np.array(data)
            wf = WindowFunction(window)
            freqs, phase = compute_fft_phase(arr, app_state["sample_rate"], wf)
            await ws.send_text(json.dumps({
                "type": "fft_phase_result",
                "channel": ch_idx,
                "frequencies": freqs.tolist(),
                "phase": phase.tolist(),
            }))

    elif cmd == "get_measurements":
        data = msg.get("data")
        ch_idx = msg.get("channel", 0)
        if data:
            arr = np.array(data)
            measurements = compute_measurements(arr, app_state["sample_rate"])
            await ws.send_text(json.dumps({
                "type": "measurements",
                "channel": ch_idx,
                "values": measurements,
            }))

    elif cmd == "apply_filter":
        data = msg.get("data")
        filter_type = msg.get("filter_type", "lowpass")
        cutoff = msg.get("cutoff", 1000.0)
        order = msg.get("order", 4)
        if data:
            arr = np.array(data)
            ft = FilterType(filter_type)
            filtered = apply_filter(arr, ft, cutoff, app_state["sample_rate"], order)
            await ws.send_text(json.dumps({
                "type": "filtered_data",
                "data": filtered.tolist(),
            }))

    elif cmd == "eval_math":
        expression = msg.get("expression", "")
        channel_data = msg.get("channels", {})
        channels = {k: np.array(v) for k, v in channel_data.items()}
        result = evaluate_math_expression(expression, channels)
        if result is not None:
            await ws.send_text(json.dumps({
                "type": "math_result",
                "data": result.tolist(),
            }))

    elif cmd == "ping":
        await ws.send_text(json.dumps({"type": "pong", "timestamp": time.time()}))
