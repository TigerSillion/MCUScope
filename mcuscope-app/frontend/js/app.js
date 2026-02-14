/**
 * MCUScope v2.0 - Main Application Controller
 * Manages WebSocket connection, UI state, and coordinates between
 * the renderer, DSP backend, and user interface.
 */

class MCUScopeApp {
    constructor() {
        // State
        this.ws = null;
        this.connected = false;
        this.running = false;
        this.mode = 'simulator';
        this.sampleRate = 10000;
        this.bufferSize = 1024;
        this.numChannels = 4;

        // Channel state
        this.channelConfigs = [];
        for (let i = 0; i < 8; i++) {
            this.channelConfigs.push({
                name: `CH${i + 1}`,
                enabled: i < 4,
                scale: 1.0,
                offset: 0.0,
                yMin: undefined,
                yMax: undefined,
            });
        }

        // Latest data
        this.latestData = [];
        this.dataHistory = [];
        this.maxHistory = 100;

        // Measurements
        this.measurements = {};

        // FFT state
        this.fftEnabled = false;
        this.fftChannel = 0;
        this.fftWindow = 'hamming';

        // Trigger state
        this.triggerEnabled = false;
        this.triggerChannel = 0;
        this.triggerLevel = 0;
        this.triggerEdge = 'rising';

        // Renderers (set up after DOM)
        this.scopeRenderer = null;
        this.fftRenderer = null;

        // Cursor drag state
        this._draggingCursor = null;

        // FPS counter
        this._frameCount = 0;
        this._lastFpsTime = performance.now();
        this._fps = 0;

        // Init
        this._initDOM();
        this._initRenderers();
        this._initWebSocket();
        this._initEventListeners();
        this._startFpsCounter();
        this._updateUI();
    }

    _initDOM() {
        // Cache DOM elements
        this.el = {
            runBtn: document.getElementById('btn-run'),
            stopBtn: document.getElementById('btn-stop'),
            singleBtn: document.getElementById('btn-single'),
            fftBtn: document.getElementById('btn-fft'),
            cursorBtn: document.getElementById('btn-cursors'),
            connectBtn: document.getElementById('btn-connect'),
            statusDot: document.getElementById('status-dot'),
            statusText: document.getElementById('status-text'),
            channelList: document.getElementById('channel-list'),
            measurePanel: document.getElementById('measurements'),
            cursorInfo: document.getElementById('cursor-info'),
            scopeCanvas: document.getElementById('scope-canvas'),
            overlayCanvas: document.getElementById('overlay-canvas'),
            cursorCanvas: document.getElementById('cursor-canvas'),
            fftCanvas: document.getElementById('fft-canvas'),
            fftContainer: document.getElementById('fft-container'),
            timePerDiv: document.getElementById('time-per-div'),
            sampleRateInput: document.getElementById('sample-rate'),
            bufferSizeInput: document.getElementById('buffer-size'),
            triggerEnable: document.getElementById('trigger-enable'),
            triggerChannel: document.getElementById('trigger-channel'),
            triggerLevel: document.getElementById('trigger-level'),
            triggerEdge: document.getElementById('trigger-edge'),
            fftWindowSelect: document.getElementById('fft-window'),
            statusFps: document.getElementById('status-fps'),
            statusSps: document.getElementById('status-sps'),
            statusSamples: document.getElementById('status-samples'),
            statusMode: document.getElementById('status-mode'),
        };
    }

    _initRenderers() {
        this.scopeRenderer = new ScopeRenderer(
            this.el.scopeCanvas,
            this.el.overlayCanvas
        );
        this.scopeRenderer.sampleRate = this.sampleRate;
        this.scopeRenderer.startLoop();

        this.fftRenderer = new FFTRenderer(this.el.fftCanvas);

        // Initial resize
        setTimeout(() => {
            this.scopeRenderer.resize();
            this.fftRenderer.resize();
        }, 50);
    }

    _initWebSocket() {
        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const wsUrl = `${protocol}//${window.location.host}/ws`;

        try {
            this.ws = new WebSocket(wsUrl);

            this.ws.onopen = () => {
                console.log('WebSocket connected');
                this.connected = true;
                this._updateConnectionStatus();
            };

            this.ws.onmessage = (event) => {
                this._handleMessage(JSON.parse(event.data));
            };

            this.ws.onclose = () => {
                console.log('WebSocket disconnected');
                this.connected = false;
                this._updateConnectionStatus();
                // Auto-reconnect
                setTimeout(() => this._initWebSocket(), 2000);
            };

            this.ws.onerror = (err) => {
                console.error('WebSocket error:', err);
            };
        } catch (e) {
            console.error('WebSocket init failed:', e);
            setTimeout(() => this._initWebSocket(), 3000);
        }
    }

    _handleMessage(msg) {
        switch (msg.type) {
            case 'scope_data':
                this._handleScopeData(msg);
                break;
            case 'fft_result':
                this._handleFFTResult(msg);
                break;
            case 'measurements':
                this._handleMeasurements(msg);
                break;
            case 'fft_phase_result':
                // Could display phase if needed
                break;
            case 'pong':
                break;
        }
    }

    _handleScopeData(msg) {
        this.latestData = msg.channels;
        this.sampleRate = msg.sample_rate || this.sampleRate;
        this._frameCount++;

        // Update renderer
        this.scopeRenderer.sampleRate = this.sampleRate;
        this.scopeRenderer.updateData(this.latestData, this.channelConfigs);

        // Request FFT if enabled
        if (this.fftEnabled && this.latestData[this.fftChannel]) {
            this._requestFFT();
        }

        // Request measurements for visible channels
        this._requestMeasurements();

        // Update channel values
        this._updateChannelValues();

        // Update status bar
        this._updateStatusBar();
    }

    _handleFFTResult(msg) {
        this.fftRenderer.updateData(msg.frequencies, msg.magnitudes);
        this.fftRenderer.draw();
    }

    _handleMeasurements(msg) {
        this.measurements[msg.channel] = msg.values;
        this._updateMeasurementsPanel();
    }

    _requestFFT() {
        if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;
        this.ws.send(JSON.stringify({
            command: 'get_fft',
            channel: this.fftChannel,
            window: this.fftWindow,
            data: this.latestData[this.fftChannel],
        }));
    }

    _requestMeasurements() {
        if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;
        // Only request for first enabled channel to reduce traffic
        for (let i = 0; i < this.channelConfigs.length; i++) {
            if (this.channelConfigs[i].enabled && this.latestData[i]) {
                this.ws.send(JSON.stringify({
                    command: 'get_measurements',
                    channel: i,
                    data: this.latestData[i],
                }));
                break;
            }
        }
    }

    // --- UI Event Listeners ---

    _initEventListeners() {
        // Toolbar buttons
        this.el.runBtn.addEventListener('click', () => this.startAcquisition());
        this.el.stopBtn.addEventListener('click', () => this.stopAcquisition());
        this.el.singleBtn.addEventListener('click', () => this.singleCapture());
        this.el.fftBtn.addEventListener('click', () => this.toggleFFT());
        this.el.cursorBtn.addEventListener('click', () => this.toggleCursors());

        // Connection
        this.el.connectBtn.addEventListener('click', () => this.showConnectionDialog());

        // Trigger controls
        this.el.triggerEnable?.addEventListener('change', (e) => {
            this.triggerEnabled = e.target.checked;
            this._updateTrigger();
        });
        this.el.triggerChannel?.addEventListener('change', (e) => {
            this.triggerChannel = parseInt(e.target.value);
            this._updateTrigger();
        });
        this.el.triggerLevel?.addEventListener('change', (e) => {
            this.triggerLevel = parseFloat(e.target.value);
            this._updateTrigger();
        });
        this.el.triggerEdge?.addEventListener('change', (e) => {
            this.triggerEdge = e.target.value;
            this._updateTrigger();
        });

        // FFT window
        this.el.fftWindowSelect?.addEventListener('change', (e) => {
            this.fftWindow = e.target.value;
        });

        // Acquisition settings
        this.el.sampleRateInput?.addEventListener('change', (e) => {
            this.sampleRate = parseFloat(e.target.value);
            this._updateAcquisitionConfig();
        });
        this.el.bufferSizeInput?.addEventListener('change', (e) => {
            this.bufferSize = parseInt(e.target.value);
            this._updateAcquisitionConfig();
        });

        // Cursor interaction on canvas
        this.el.cursorCanvas.addEventListener('mousedown', (e) => this._onCursorMouseDown(e));
        this.el.cursorCanvas.addEventListener('mousemove', (e) => this._onCursorMouseMove(e));
        this.el.cursorCanvas.addEventListener('mouseup', () => this._onCursorMouseUp());
        this.el.cursorCanvas.addEventListener('mouseleave', () => this._onCursorMouseUp());

        // Keyboard
        document.addEventListener('keydown', (e) => this._onKeyDown(e));

        // Panel collapse
        document.querySelectorAll('.panel-header').forEach(header => {
            header.addEventListener('click', () => {
                header.classList.toggle('collapsed');
                const content = header.nextElementSibling;
                if (content) content.classList.toggle('collapsed');
            });
        });

        // Scope canvas scroll zoom
        this.el.cursorCanvas.addEventListener('wheel', (e) => {
            e.preventDefault();
            // Zoom time scale
            const delta = e.deltaY > 0 ? 1.1 : 0.9;
            this.scopeRenderer.timeScale *= delta;
            this.scopeRenderer.requestRedraw();
        });
    }

    _onKeyDown(e) {
        switch (e.key) {
            case ' ':
                e.preventDefault();
                if (this.running) this.stopAcquisition();
                else this.startAcquisition();
                break;
            case 'f':
                this.toggleFFT();
                break;
            case 'c':
                this.toggleCursors();
                break;
            case '1': case '2': case '3': case '4':
            case '5': case '6': case '7': case '8':
                const ch = parseInt(e.key) - 1;
                if (ch < this.channelConfigs.length) {
                    this.channelConfigs[ch].enabled = !this.channelConfigs[ch].enabled;
                    this._updateChannelList();
                    this.scopeRenderer.requestRedraw();
                }
                break;
        }
    }

    _onCursorMouseDown(e) {
        const rect = this.el.cursorCanvas.getBoundingClientRect();
        const x = (e.clientX - rect.left);
        const y = (e.clientY - rect.top);
        const { x: px, y: py, w: pw, h: ph } = this.scopeRenderer.plotArea;

        // Check proximity to cursors
        const thresh = 8;
        const cursors = this.scopeRenderer.cursors;

        if (cursors.x1.active && Math.abs(x - (px + cursors.x1.pos * pw)) < thresh) {
            this._draggingCursor = 'x1';
        } else if (cursors.x2.active && Math.abs(x - (px + cursors.x2.pos * pw)) < thresh) {
            this._draggingCursor = 'x2';
        } else if (cursors.y1.active && Math.abs(y - (py + cursors.y1.pos * ph)) < thresh) {
            this._draggingCursor = 'y1';
        } else if (cursors.y2.active && Math.abs(y - (py + cursors.y2.pos * ph)) < thresh) {
            this._draggingCursor = 'y2';
        }
    }

    _onCursorMouseMove(e) {
        if (!this._draggingCursor) return;
        const rect = this.el.cursorCanvas.getBoundingClientRect();
        const { x: px, y: py, w: pw, h: ph } = this.scopeRenderer.plotArea;

        if (this._draggingCursor.startsWith('x')) {
            const pos = (e.clientX - rect.left - px) / pw;
            this.scopeRenderer.cursors[this._draggingCursor].pos = Math.max(0, Math.min(1, pos));
        } else {
            const pos = (e.clientY - rect.top - py) / ph;
            this.scopeRenderer.cursors[this._draggingCursor].pos = Math.max(0, Math.min(1, pos));
        }
        this.scopeRenderer.drawOverlay();
        this._updateCursorInfo();
    }

    _onCursorMouseUp() {
        this._draggingCursor = null;
    }

    // --- Actions ---

    async startAcquisition() {
        try {
            await fetch('/api/acquisition/start', { method: 'POST' });
            this.running = true;
            this._updateUI();
        } catch (e) {
            console.error('Start failed:', e);
        }
    }

    async stopAcquisition() {
        try {
            await fetch('/api/acquisition/stop', { method: 'POST' });
            this.running = false;
            this._updateUI();
        } catch (e) {
            console.error('Stop failed:', e);
        }
    }

    async singleCapture() {
        await this.startAcquisition();
        setTimeout(() => this.stopAcquisition(), this.bufferSize / this.sampleRate * 1000 + 200);
    }

    toggleFFT() {
        this.fftEnabled = !this.fftEnabled;
        this.el.fftContainer.classList.toggle('visible', this.fftEnabled);
        this.el.fftBtn.classList.toggle('active', this.fftEnabled);
        if (this.fftEnabled) {
            setTimeout(() => this.fftRenderer.resize(), 350);
        }
        // Resize scope after FFT panel changes
        setTimeout(() => this.scopeRenderer.resize(), 350);
    }

    toggleCursors() {
        const cursors = this.scopeRenderer.cursors;
        const anyActive = cursors.x1.active || cursors.x2.active;
        cursors.x1.active = !anyActive;
        cursors.x2.active = !anyActive;
        cursors.y1.active = !anyActive;
        cursors.y2.active = !anyActive;
        this.el.cursorBtn.classList.toggle('active', !anyActive);
        this.scopeRenderer.drawOverlay();
        this._updateCursorInfo();
    }

    async _updateTrigger() {
        this.scopeRenderer.triggerLevel = this.triggerEnabled ? this.triggerLevel : null;
        this.scopeRenderer.triggerChannel = this.triggerChannel;
        this.scopeRenderer.requestRedraw();

        try {
            await fetch('/api/trigger', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    enabled: this.triggerEnabled,
                    channel: this.triggerChannel,
                    level: this.triggerLevel,
                    edge: this.triggerEdge,
                }),
            });
        } catch (e) {
            console.error('Trigger update failed:', e);
        }
    }

    async _updateAcquisitionConfig() {
        try {
            await fetch('/api/acquisition/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    sample_rate: this.sampleRate,
                    buffer_size: this.bufferSize,
                    num_channels: this.numChannels,
                }),
            });
        } catch (e) {
            console.error('Config update failed:', e);
        }
    }

    showConnectionDialog() {
        // Simple prompt-based connection for now
        const port = prompt('Enter serial port (e.g. /dev/ttyUSB0 or COM3):');
        if (!port) return;
        const baudrate = parseInt(prompt('Baud rate:', '115200') || '115200');
        this._connectDevice(port, baudrate);
    }

    async _connectDevice(port, baudrate) {
        try {
            const res = await fetch('/api/connect', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ port, baudrate }),
            });
            const data = await res.json();
            if (res.ok) {
                this.mode = 'device';
                this._updateConnectionStatus();
            } else {
                alert('Connection failed: ' + (data.detail || 'Unknown error'));
            }
        } catch (e) {
            console.error('Connection error:', e);
        }
    }

    // --- UI Updates ---

    _updateUI() {
        this.el.runBtn.classList.toggle('active', this.running);
        this._updateChannelList();
        this._updateConnectionStatus();
    }

    _updateConnectionStatus() {
        const dot = this.el.statusDot;
        const text = this.el.statusText;

        if (this.mode === 'device') {
            dot.className = 'status-dot connected';
            text.textContent = 'Device Connected';
        } else if (this.connected) {
            dot.className = 'status-dot simulating';
            text.textContent = 'Simulator Mode';
        } else {
            dot.className = 'status-dot';
            text.textContent = 'Disconnected';
        }
    }

    _updateChannelList() {
        const list = this.el.channelList;
        if (!list) return;

        const colors = this.scopeRenderer.channelColors;
        list.innerHTML = '';

        for (let i = 0; i < this.numChannels; i++) {
            const cfg = this.channelConfigs[i];
            const el = document.createElement('div');
            el.className = 'channel-item' + (cfg.enabled ? ' selected' : '');
            el.innerHTML = `
                <div class="channel-color" style="background:${colors[i]}"></div>
                <div class="channel-toggle ${cfg.enabled ? 'enabled' : ''}"
                     onclick="app.toggleChannel(${i})">${cfg.enabled ? '\u2713' : ''}</div>
                <div class="channel-name">${cfg.name}</div>
                <div class="channel-value" id="ch-val-${i}">---</div>
            `;
            list.appendChild(el);
        }
    }

    toggleChannel(ch) {
        if (ch < this.channelConfigs.length) {
            this.channelConfigs[ch].enabled = !this.channelConfigs[ch].enabled;
            this._updateChannelList();
            this.scopeRenderer.requestRedraw();
        }
    }

    _updateChannelValues() {
        for (let i = 0; i < this.numChannels; i++) {
            const el = document.getElementById(`ch-val-${i}`);
            if (el && this.latestData[i] && this.latestData[i].length > 0) {
                const last = this.latestData[i][this.latestData[i].length - 1];
                el.textContent = last.toFixed(3);
            }
        }
    }

    _updateMeasurementsPanel() {
        const panel = this.el.measurePanel;
        if (!panel) return;

        let html = '';
        for (const [ch, m] of Object.entries(this.measurements)) {
            const color = this.scopeRenderer.channelColors[ch] || '#fff';
            html += `
                <div class="measurement-grid">
                    <div class="measurement-item">
                        <div class="measurement-label">Min</div>
                        <div class="measurement-value" style="color:${color}">${m.min?.toFixed(3) ?? '-'}</div>
                    </div>
                    <div class="measurement-item">
                        <div class="measurement-label">Max</div>
                        <div class="measurement-value" style="color:${color}">${m.max?.toFixed(3) ?? '-'}</div>
                    </div>
                    <div class="measurement-item">
                        <div class="measurement-label">Mean</div>
                        <div class="measurement-value" style="color:${color}">${m.mean?.toFixed(3) ?? '-'}</div>
                    </div>
                    <div class="measurement-item">
                        <div class="measurement-label">RMS</div>
                        <div class="measurement-value" style="color:${color}">${m.rms?.toFixed(3) ?? '-'}</div>
                    </div>
                    <div class="measurement-item">
                        <div class="measurement-label">Vpp</div>
                        <div class="measurement-value" style="color:${color}">${m.vpp?.toFixed(3) ?? '-'}</div>
                    </div>
                    <div class="measurement-item">
                        <div class="measurement-label">Freq</div>
                        <div class="measurement-value" style="color:${color}">${m.frequency?.toFixed(1) ?? '-'} Hz</div>
                    </div>
                    <div class="measurement-item">
                        <div class="measurement-label">Period</div>
                        <div class="measurement-value" style="color:${color}">${m.period ? (m.period * 1000).toFixed(2) + ' ms' : '-'}</div>
                    </div>
                    <div class="measurement-item">
                        <div class="measurement-label">Duty</div>
                        <div class="measurement-value" style="color:${color}">${m.duty_cycle?.toFixed(1) ?? '-'}%</div>
                    </div>
                </div>
            `;
        }
        panel.innerHTML = html || '<div style="color:#556677;font-size:11px;padding:4px">No measurements</div>';
    }

    _updateCursorInfo() {
        const info = this.el.cursorInfo;
        if (!info) return;

        const cursors = this.scopeRenderer.cursors;
        if (!cursors.x1.active) {
            info.innerHTML = '<div style="color:#556677;font-size:11px;padding:4px">Cursors off (press C)</div>';
            return;
        }

        const totalSamples = this.latestData[0]?.length || 1;
        const totalTime = totalSamples / this.sampleRate;

        const t1 = cursors.x1.pos * totalTime;
        const t2 = cursors.x2.pos * totalTime;
        const dt = Math.abs(t2 - t1);

        let html = `
            <div class="cursor-info">
                <span class="label" style="color:#00aaff">X1:</span>
                <span class="value">${this._formatTimePrecise(t1)}</span>
                <span class="label" style="color:#00ddff">X2:</span>
                <span class="value">${this._formatTimePrecise(t2)}</span>
                <span class="label">\u0394t:</span>
                <span class="value">${this._formatTimePrecise(dt)}</span>
                <span class="label">1/\u0394t:</span>
                <span class="value">${dt > 0 ? (1/dt).toFixed(1) + ' Hz' : '-'}</span>
            </div>
        `;
        info.innerHTML = html;
    }

    _formatTimePrecise(t) {
        if (t >= 1) return t.toFixed(3) + ' s';
        if (t >= 0.001) return (t * 1000).toFixed(2) + ' ms';
        if (t >= 0.000001) return (t * 1e6).toFixed(1) + ' \u00b5s';
        return (t * 1e9).toFixed(0) + ' ns';
    }

    _updateStatusBar() {
        if (this.el.statusSps) {
            this.el.statusSps.textContent = this._formatSampleRate(this.sampleRate);
        }
        if (this.el.statusSamples) {
            this.el.statusSamples.textContent = (this.latestData[0]?.length || 0).toString();
        }
        if (this.el.statusMode) {
            this.el.statusMode.textContent = this.running ? 'RUN' : 'STOP';
            this.el.statusMode.style.color = this.running ? '#00dd88' : '#ff4466';
        }
    }

    _formatSampleRate(sr) {
        if (sr >= 1e6) return (sr / 1e6).toFixed(1) + ' MSa/s';
        if (sr >= 1e3) return (sr / 1e3).toFixed(1) + ' kSa/s';
        return sr.toFixed(0) + ' Sa/s';
    }

    _startFpsCounter() {
        setInterval(() => {
            const now = performance.now();
            const elapsed = (now - this._lastFpsTime) / 1000;
            this._fps = Math.round(this._frameCount / elapsed);
            this._frameCount = 0;
            this._lastFpsTime = now;
            if (this.el.statusFps) {
                this.el.statusFps.textContent = this._fps + ' fps';
            }
        }, 1000);
    }
}

// Initialize app when DOM is ready
let app;
document.addEventListener('DOMContentLoaded', () => {
    app = new MCUScopeApp();
    window.app = app;
});
