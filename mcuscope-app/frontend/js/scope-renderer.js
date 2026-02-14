/**
 * MCUScope v2.0 - High-Performance Canvas Oscilloscope Renderer
 * Renders waveforms, grid, cursors, triggers, and measurements
 * using hardware-accelerated 2D canvas with optimized draw paths.
 */

class ScopeRenderer {
    constructor(canvas, overlayCanvas) {
        this.canvas = canvas;
        this.overlay = overlayCanvas;
        this.ctx = canvas.getContext('2d');
        this.octx = overlayCanvas.getContext('2d');

        // Display state
        this.channels = [];
        this.channelConfigs = [];
        this.timeScale = 1.0;       // seconds per division
        this.timeOffset = 0.0;      // seconds offset
        this.triggerLevel = null;
        this.triggerChannel = 0;
        this.sampleRate = 10000;

        // Grid config
        this.gridDivisionsX = 10;
        this.gridDivisionsY = 8;
        this.gridSubdivisions = 5;

        // Cursors
        this.cursors = {
            x1: { active: false, pos: 0.3 },
            x2: { active: false, pos: 0.7 },
            y1: { active: false, pos: 0.3 },
            y2: { active: false, pos: 0.7 },
        };
        this.activeCursor = null;

        // View state
        this.margin = { top: 8, right: 8, bottom: 8, left: 8 };
        this.plotArea = { x: 0, y: 0, w: 0, h: 0 };

        // Colors
        this.channelColors = [
            '#00dd88', '#00aaff', '#ffcc00', '#ff4466',
            '#aa66ff', '#ff8844', '#00ddff', '#ff66aa',
        ];

        // Performance
        this._rafId = null;
        this._needsRedraw = true;
        this._lastDrawTime = 0;

        this._setupResize();
    }

    _setupResize() {
        const ro = new ResizeObserver(() => this.resize());
        ro.observe(this.canvas.parentElement);
    }

    resize() {
        const parent = this.canvas.parentElement;
        const dpr = window.devicePixelRatio || 1;
        const w = parent.clientWidth;
        const h = parent.clientHeight;

        for (const cvs of [this.canvas, this.overlay]) {
            cvs.width = w * dpr;
            cvs.height = h * dpr;
            cvs.style.width = w + 'px';
            cvs.style.height = h + 'px';
            cvs.getContext('2d').setTransform(dpr, 0, 0, dpr, 0, 0);
        }

        this.plotArea = {
            x: this.margin.left + 50,
            y: this.margin.top,
            w: w - this.margin.left - this.margin.right - 50,
            h: h - this.margin.top - this.margin.bottom - 24,
        };

        this._needsRedraw = true;
    }

    /**
     * Update channel data and trigger redraw
     */
    updateData(channels, configs) {
        this.channels = channels || [];
        if (configs) this.channelConfigs = configs;
        this._needsRedraw = true;
    }

    /**
     * Main draw call
     */
    draw() {
        const ctx = this.ctx;
        const { x, y, w, h } = this.plotArea;
        const cw = this.canvas.width / (window.devicePixelRatio || 1);
        const ch = this.canvas.height / (window.devicePixelRatio || 1);

        ctx.clearRect(0, 0, cw, ch);

        // Background
        ctx.fillStyle = '#0a0e14';
        ctx.fillRect(0, 0, cw, ch);

        // Plot area background
        const grad = ctx.createLinearGradient(x, y, x, y + h);
        grad.addColorStop(0, '#0c1118');
        grad.addColorStop(0.5, '#0a0e14');
        grad.addColorStop(1, '#0c1118');
        ctx.fillStyle = grad;
        ctx.fillRect(x, y, w, h);

        this._drawGrid(ctx, x, y, w, h);
        this._drawWaveforms(ctx, x, y, w, h);
        this._drawTriggerLine(ctx, x, y, w, h);
        this._drawAxisLabels(ctx, x, y, w, h);

        // Border
        ctx.strokeStyle = '#2a3545';
        ctx.lineWidth = 1;
        ctx.strokeRect(x + 0.5, y + 0.5, w, h);

        this._needsRedraw = false;
    }

    /**
     * Draw overlay (cursors, measurements) — separate layer for performance
     */
    drawOverlay() {
        const ctx = this.octx;
        const cw = this.overlay.width / (window.devicePixelRatio || 1);
        const ch = this.overlay.height / (window.devicePixelRatio || 1);
        const { x, y, w, h } = this.plotArea;

        ctx.clearRect(0, 0, cw, ch);
        this._drawCursors(ctx, x, y, w, h);
    }

    _drawGrid(ctx, x, y, w, h) {
        // Sub-grid
        ctx.strokeStyle = 'rgba(42, 53, 69, 0.3)';
        ctx.lineWidth = 0.5;
        const subX = w / (this.gridDivisionsX * this.gridSubdivisions);
        const subY = h / (this.gridDivisionsY * this.gridSubdivisions);

        ctx.beginPath();
        for (let i = 0; i <= this.gridDivisionsX * this.gridSubdivisions; i++) {
            const px = x + i * subX;
            ctx.moveTo(Math.round(px) + 0.5, y);
            ctx.lineTo(Math.round(px) + 0.5, y + h);
        }
        for (let i = 0; i <= this.gridDivisionsY * this.gridSubdivisions; i++) {
            const py = y + i * subY;
            ctx.moveTo(x, Math.round(py) + 0.5);
            ctx.lineTo(x + w, Math.round(py) + 0.5);
        }
        ctx.stroke();

        // Major grid
        ctx.strokeStyle = 'rgba(53, 69, 96, 0.6)';
        ctx.lineWidth = 0.8;
        const divX = w / this.gridDivisionsX;
        const divY = h / this.gridDivisionsY;

        ctx.beginPath();
        for (let i = 0; i <= this.gridDivisionsX; i++) {
            const px = x + i * divX;
            ctx.moveTo(Math.round(px) + 0.5, y);
            ctx.lineTo(Math.round(px) + 0.5, y + h);
        }
        for (let i = 0; i <= this.gridDivisionsY; i++) {
            const py = y + i * divY;
            ctx.moveTo(x, Math.round(py) + 0.5);
            ctx.lineTo(x + w, Math.round(py) + 0.5);
        }
        ctx.stroke();

        // Center cross (brighter)
        ctx.strokeStyle = 'rgba(53, 69, 96, 0.9)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        const cx = x + w / 2;
        const cy = y + h / 2;
        ctx.moveTo(cx + 0.5, y);
        ctx.lineTo(cx + 0.5, y + h);
        ctx.moveTo(x, cy + 0.5);
        ctx.lineTo(x + w, cy + 0.5);
        ctx.stroke();
    }

    _drawWaveforms(ctx, x, y, w, h) {
        const numCh = this.channels.length;
        if (numCh === 0) return;

        ctx.save();
        ctx.beginPath();
        ctx.rect(x, y, w, h);
        ctx.clip();

        for (let ch = 0; ch < numCh; ch++) {
            const data = this.channels[ch];
            if (!data || data.length === 0) continue;

            const config = this.channelConfigs[ch] || {};
            if (config.enabled === false) continue;

            const color = this.channelColors[ch % this.channelColors.length];
            const scale = config.scale || 1.0;
            const offset = config.offset || 0.0;

            // Determine Y range
            let yMin = config.yMin;
            let yMax = config.yMax;
            if (yMin === undefined || yMax === undefined) {
                let dMin = Infinity, dMax = -Infinity;
                for (let i = 0; i < data.length; i++) {
                    if (data[i] < dMin) dMin = data[i];
                    if (data[i] > dMax) dMax = data[i];
                }
                const pad = (dMax - dMin) * 0.1 || 1;
                yMin = dMin - pad;
                yMax = dMax + pad;
            }

            // Draw waveform with anti-aliased line
            ctx.strokeStyle = color;
            ctx.lineWidth = 1.5;
            ctx.lineJoin = 'round';
            ctx.lineCap = 'round';

            // Add subtle glow
            ctx.shadowColor = color;
            ctx.shadowBlur = 3;

            ctx.beginPath();
            const len = data.length;
            const xStep = w / len;

            // Use path optimization: skip points that map to same pixel
            let lastPx = -1;
            for (let i = 0; i < len; i++) {
                const px = x + i * xStep;
                const intPx = Math.round(px);

                // If multiple samples map to same pixel, draw min/max
                if (intPx === lastPx && xStep < 1) continue;
                lastPx = intPx;

                const val = (data[i] * scale + offset);
                const py = y + h - ((val - yMin) / (yMax - yMin)) * h;

                if (i === 0) {
                    ctx.moveTo(px, py);
                } else {
                    ctx.lineTo(px, py);
                }
            }
            ctx.stroke();
            ctx.shadowBlur = 0;
        }

        ctx.restore();
    }

    _drawTriggerLine(ctx, x, y, w, h) {
        if (this.triggerLevel === null) return;

        const config = this.channelConfigs[this.triggerChannel] || {};
        let yMin = config.yMin, yMax = config.yMax;
        if (yMin === undefined || yMax === undefined) {
            const data = this.channels[this.triggerChannel];
            if (!data || data.length === 0) return;
            let dMin = Infinity, dMax = -Infinity;
            for (let i = 0; i < data.length; i++) {
                if (data[i] < dMin) dMin = data[i];
                if (data[i] > dMax) dMax = data[i];
            }
            const pad = (dMax - dMin) * 0.1 || 1;
            yMin = dMin - pad;
            yMax = dMax + pad;
        }

        const trigY = y + h - ((this.triggerLevel - yMin) / (yMax - yMin)) * h;

        ctx.save();
        ctx.strokeStyle = '#ff4466';
        ctx.lineWidth = 1;
        ctx.setLineDash([6, 4]);
        ctx.beginPath();
        ctx.moveTo(x, trigY + 0.5);
        ctx.lineTo(x + w, trigY + 0.5);
        ctx.stroke();
        ctx.setLineDash([]);

        // Triangle marker
        ctx.fillStyle = '#ff4466';
        ctx.beginPath();
        ctx.moveTo(x - 8, trigY);
        ctx.lineTo(x, trigY - 5);
        ctx.lineTo(x, trigY + 5);
        ctx.fill();

        ctx.font = '10px "JetBrains Mono", monospace';
        ctx.fillStyle = '#ff4466';
        ctx.textAlign = 'right';
        ctx.fillText(`T: ${this.triggerLevel.toFixed(2)}`, x - 10, trigY + 3);
        ctx.restore();
    }

    _drawCursors(ctx, x, y, w, h) {
        const drawVCursor = (cursor, label, color) => {
            if (!cursor.active) return;
            const cx = x + cursor.pos * w;
            ctx.strokeStyle = color;
            ctx.lineWidth = 1;
            ctx.setLineDash([4, 3]);
            ctx.beginPath();
            ctx.moveTo(Math.round(cx) + 0.5, y);
            ctx.lineTo(Math.round(cx) + 0.5, y + h);
            ctx.stroke();
            ctx.setLineDash([]);

            // Label
            ctx.fillStyle = color;
            ctx.font = 'bold 10px "JetBrains Mono", monospace';
            ctx.textAlign = 'center';
            ctx.fillText(label, cx, y - 4);
        };

        const drawHCursor = (cursor, label, color) => {
            if (!cursor.active) return;
            const cy = y + cursor.pos * h;
            ctx.strokeStyle = color;
            ctx.lineWidth = 1;
            ctx.setLineDash([4, 3]);
            ctx.beginPath();
            ctx.moveTo(x, Math.round(cy) + 0.5);
            ctx.lineTo(x + w, Math.round(cy) + 0.5);
            ctx.stroke();
            ctx.setLineDash([]);

            ctx.fillStyle = color;
            ctx.font = 'bold 10px "JetBrains Mono", monospace';
            ctx.textAlign = 'left';
            ctx.fillText(label, x + w + 4, cy + 3);
        };

        drawVCursor(this.cursors.x1, 'X1', '#00aaff');
        drawVCursor(this.cursors.x2, 'X2', '#00ddff');
        drawHCursor(this.cursors.y1, 'Y1', '#ffcc00');
        drawHCursor(this.cursors.y2, 'Y2', '#ff8844');

        // Delta display
        if (this.cursors.x1.active && this.cursors.x2.active) {
            const dt = Math.abs(this.cursors.x2.pos - this.cursors.x1.pos);
            const dtTime = dt * this.channels[0]?.length / this.sampleRate || 0;
            ctx.fillStyle = '#00aaff';
            ctx.font = '11px "JetBrains Mono", monospace';
            ctx.textAlign = 'center';
            ctx.fillText(
                `\u0394t = ${this._formatTime(dtTime)}  f = ${dtTime > 0 ? this._formatFreq(1 / dtTime) : '-'}`,
                x + w / 2, y + h + 16
            );
        }
    }

    _drawAxisLabels(ctx, x, y, w, h) {
        ctx.save();
        ctx.font = '10px "JetBrains Mono", monospace';
        ctx.fillStyle = '#556677';
        ctx.textAlign = 'center';

        // X-axis (time)
        const totalTime = this.channels[0]
            ? this.channels[0].length / this.sampleRate
            : 1;
        const divX = w / this.gridDivisionsX;
        for (let i = 0; i <= this.gridDivisionsX; i++) {
            const t = (i / this.gridDivisionsX) * totalTime + this.timeOffset;
            ctx.fillText(this._formatTime(t), x + i * divX, y + h + 14);
        }

        // Y-axis labels for first visible channel
        ctx.textAlign = 'right';
        for (let ch = 0; ch < this.channels.length; ch++) {
            const config = this.channelConfigs[ch] || {};
            if (config.enabled === false) continue;

            const data = this.channels[ch];
            if (!data || data.length === 0) continue;

            let yMin = config.yMin, yMax = config.yMax;
            if (yMin === undefined || yMax === undefined) {
                let dMin = Infinity, dMax = -Infinity;
                for (let i = 0; i < data.length; i++) {
                    if (data[i] < dMin) dMin = data[i];
                    if (data[i] > dMax) dMax = data[i];
                }
                const pad = (dMax - dMin) * 0.1 || 1;
                yMin = dMin - pad;
                yMax = dMax + pad;
            }

            ctx.fillStyle = this.channelColors[ch % this.channelColors.length] + '88';
            const divY = h / this.gridDivisionsY;
            for (let i = 0; i <= this.gridDivisionsY; i++) {
                const v = yMax - (i / this.gridDivisionsY) * (yMax - yMin);
                ctx.fillText(this._formatValue(v), x - 4, y + i * divY + 3);
            }
            break; // Only show for first active channel
        }

        ctx.restore();
    }

    _formatTime(t) {
        if (Math.abs(t) >= 1) return t.toFixed(2) + 's';
        if (Math.abs(t) >= 0.001) return (t * 1000).toFixed(1) + 'ms';
        if (Math.abs(t) >= 0.000001) return (t * 1e6).toFixed(1) + '\u00b5s';
        return (t * 1e9).toFixed(0) + 'ns';
    }

    _formatFreq(f) {
        if (f >= 1e6) return (f / 1e6).toFixed(2) + 'MHz';
        if (f >= 1e3) return (f / 1e3).toFixed(2) + 'kHz';
        return f.toFixed(1) + 'Hz';
    }

    _formatValue(v) {
        if (Math.abs(v) >= 1000) return (v / 1000).toFixed(1) + 'k';
        if (Math.abs(v) >= 1) return v.toFixed(2);
        if (Math.abs(v) >= 0.001) return (v * 1000).toFixed(1) + 'm';
        if (Math.abs(v) >= 0.000001) return (v * 1e6).toFixed(1) + '\u00b5';
        return v.toExponential(2);
    }

    /**
     * Start animation loop
     */
    startLoop() {
        const loop = (ts) => {
            if (this._needsRedraw) {
                this.draw();
                this.drawOverlay();
            }
            this._rafId = requestAnimationFrame(loop);
        };
        this._rafId = requestAnimationFrame(loop);
    }

    stopLoop() {
        if (this._rafId) {
            cancelAnimationFrame(this._rafId);
            this._rafId = null;
        }
    }

    requestRedraw() {
        this._needsRedraw = true;
    }
}


/**
 * FFT Renderer - dedicated renderer for frequency domain display
 */
class FFTRenderer {
    constructor(canvas) {
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
        this.frequencies = [];
        this.magnitudes = [];
        this.sampleRate = 10000;
        this._needsRedraw = true;

        this.margin = { top: 8, right: 8, bottom: 24, left: 58 };
        this.plotArea = { x: 0, y: 0, w: 0, h: 0 };

        const ro = new ResizeObserver(() => this.resize());
        ro.observe(this.canvas.parentElement);
    }

    resize() {
        const parent = this.canvas.parentElement;
        const dpr = window.devicePixelRatio || 1;
        const w = parent.clientWidth;
        const h = parent.clientHeight;

        this.canvas.width = w * dpr;
        this.canvas.height = h * dpr;
        this.canvas.style.width = w + 'px';
        this.canvas.style.height = h + 'px';
        this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        this.plotArea = {
            x: this.margin.left,
            y: this.margin.top,
            w: w - this.margin.left - this.margin.right,
            h: h - this.margin.top - this.margin.bottom,
        };

        this._needsRedraw = true;
    }

    updateData(frequencies, magnitudes) {
        this.frequencies = frequencies || [];
        this.magnitudes = magnitudes || [];
        this._needsRedraw = true;
    }

    draw() {
        if (!this._needsRedraw) return;

        const ctx = this.ctx;
        const { x, y, w, h } = this.plotArea;
        const cw = this.canvas.width / (window.devicePixelRatio || 1);
        const ch = this.canvas.height / (window.devicePixelRatio || 1);

        ctx.clearRect(0, 0, cw, ch);
        ctx.fillStyle = '#0a0e14';
        ctx.fillRect(0, 0, cw, ch);

        // Grid
        ctx.strokeStyle = 'rgba(42, 53, 69, 0.5)';
        ctx.lineWidth = 0.5;
        ctx.beginPath();
        for (let i = 0; i <= 10; i++) {
            const px = x + (i / 10) * w;
            ctx.moveTo(Math.round(px) + 0.5, y);
            ctx.lineTo(Math.round(px) + 0.5, y + h);
        }
        for (let i = 0; i <= 6; i++) {
            const py = y + (i / 6) * h;
            ctx.moveTo(x, Math.round(py) + 0.5);
            ctx.lineTo(x + w, Math.round(py) + 0.5);
        }
        ctx.stroke();

        // Border
        ctx.strokeStyle = '#2a3545';
        ctx.lineWidth = 1;
        ctx.strokeRect(x + 0.5, y + 0.5, w, h);

        if (this.frequencies.length === 0) {
            ctx.fillStyle = '#556677';
            ctx.font = '12px Inter, sans-serif';
            ctx.textAlign = 'center';
            ctx.fillText('FFT - No Data', x + w / 2, y + h / 2);
            this._needsRedraw = false;
            return;
        }

        // Draw spectrum
        const maxFreq = this.frequencies[this.frequencies.length - 1] || 1;
        let magMin = -120, magMax = 0;
        for (const m of this.magnitudes) {
            if (m > magMax) magMax = m;
        }
        magMax = Math.ceil(magMax / 10) * 10 + 10;
        magMin = magMax - 120;

        // Fill gradient
        ctx.save();
        ctx.beginPath();
        ctx.rect(x, y, w, h);
        ctx.clip();

        const grad = ctx.createLinearGradient(x, y, x, y + h);
        grad.addColorStop(0, 'rgba(0, 170, 255, 0.3)');
        grad.addColorStop(1, 'rgba(0, 170, 255, 0.02)');

        ctx.fillStyle = grad;
        ctx.beginPath();
        ctx.moveTo(x, y + h);
        for (let i = 0; i < this.frequencies.length; i++) {
            const fx = x + (this.frequencies[i] / maxFreq) * w;
            const fy = y + h - ((this.magnitudes[i] - magMin) / (magMax - magMin)) * h;
            ctx.lineTo(fx, fy);
        }
        ctx.lineTo(x + w, y + h);
        ctx.closePath();
        ctx.fill();

        // Line
        ctx.strokeStyle = '#00aaff';
        ctx.lineWidth = 1.2;
        ctx.shadowColor = '#00aaff';
        ctx.shadowBlur = 2;
        ctx.beginPath();
        for (let i = 0; i < this.frequencies.length; i++) {
            const fx = x + (this.frequencies[i] / maxFreq) * w;
            const fy = y + h - ((this.magnitudes[i] - magMin) / (magMax - magMin)) * h;
            if (i === 0) ctx.moveTo(fx, fy);
            else ctx.lineTo(fx, fy);
        }
        ctx.stroke();
        ctx.shadowBlur = 0;
        ctx.restore();

        // Axis labels
        ctx.fillStyle = '#556677';
        ctx.font = '10px "JetBrains Mono", monospace';
        ctx.textAlign = 'center';
        for (let i = 0; i <= 10; i++) {
            const f = (i / 10) * maxFreq;
            const label = f >= 1000 ? (f / 1000).toFixed(1) + 'k' : f.toFixed(0);
            ctx.fillText(label, x + (i / 10) * w, y + h + 14);
        }

        ctx.textAlign = 'right';
        for (let i = 0; i <= 6; i++) {
            const v = magMax - (i / 6) * (magMax - magMin);
            ctx.fillText(v.toFixed(0) + 'dB', x - 4, y + (i / 6) * h + 3);
        }

        // FFT label
        ctx.fillStyle = '#00aaff88';
        ctx.font = 'bold 10px Inter, sans-serif';
        ctx.textAlign = 'left';
        ctx.fillText('FFT', x + 6, y + 14);

        this._needsRedraw = false;
    }

    requestRedraw() {
        this._needsRedraw = true;
    }
}

// Export
window.ScopeRenderer = ScopeRenderer;
window.FFTRenderer = FFTRenderer;
