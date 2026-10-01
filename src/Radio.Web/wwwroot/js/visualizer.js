// Audio Visualizer JavaScript Interop
// Provides high-performance canvas rendering for audio visualizations

export const visualizer = {
  canvases: {},
  animationFrames: {},

  // Initialize a canvas for visualization
  init: function (canvasId, width, height) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
      console.error(`Canvas ${canvasId} not found`);
      return false;
    }

    const ctx = canvas.getContext('2d');
    if (!ctx) {
      console.error(`Could not get 2D context for canvas ${canvasId}`);
      return false;
    }

    // Auto-size from container if dimensions not provided (fallback to known panel size)
    const parent = canvas.parentElement;
    const actualWidth = width || (parent && parent.clientWidth > 0 ? parent.clientWidth : 710);
    const actualHeight = height || (parent && parent.clientHeight > 0 ? parent.clientHeight : 640);
    canvas.width = actualWidth;
    canvas.height = actualHeight;
    width = actualWidth;
    height = actualHeight;

    this.canvases[canvasId] = {
      canvas: canvas,
      ctx: ctx,
      width: width,
      height: height,
      // Phase scope decay buffer
      phaseScopeBuffer: null,
      // BAND (AUD-76): the canvas tap handler while BAND is shown, so it can be removed
      bandTapHandler: null
    };

    console.log(`Initialized canvas ${canvasId} (${width}x${height})`);
    return true;
  },

  // Clear a canvas
  clear: function (canvasId) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData) return;

    const { ctx, width, height } = canvasData;
    ctx.clearRect(0, 0, width, height);
  },

  // Lazy-resize: fix canvas if it was initialized before parent had layout
  ensureCanvasSize: function (canvasData) {
    if (canvasData.width > 0 && canvasData.height > 0) return;
    const parent = canvasData.canvas.parentElement;
    const w = parent && parent.clientWidth > 0 ? parent.clientWidth : 710;
    const h = parent && parent.clientHeight > 0 ? parent.clientHeight : 640;
    canvasData.canvas.width = w;
    canvasData.canvas.height = h;
    canvasData.width = w;
    canvasData.height = h;
  },

  // Draw waveform
  drawWaveform: function (canvasId, leftSamples, rightSamples) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData) return;
    this.ensureCanvasSize(canvasData);

    const { ctx, width, height } = canvasData;
    
    // Clear canvas
    ctx.fillStyle = '#0A0A0C';
    ctx.fillRect(0, 0, width, height);

    const channelHeight = height / 2;
    
    // Draw left channel (positive=cyan, negative=amber)
    this.drawWaveformChannel(ctx, leftSamples, 0, 0, width, channelHeight, '#5CD4E8', '#F0A830');

    // Draw right channel (positive=cyan, negative=amber)
    this.drawWaveformChannel(ctx, rightSamples, 0, channelHeight, width, channelHeight, '#5CD4E8', '#F0A830');

    // Draw center line for each channel
    ctx.strokeStyle = '#1F1F22';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(0, channelHeight / 2);
    ctx.lineTo(width, channelHeight / 2);
    ctx.stroke();

    ctx.beginPath();
    ctx.moveTo(0, channelHeight + channelHeight / 2);
    ctx.lineTo(width, channelHeight + channelHeight / 2);
    ctx.stroke();

    // Draw labels
    ctx.fillStyle = '#F0EFF4';
    ctx.font = '14px Inter, sans-serif';
    ctx.textAlign = 'left';
    ctx.fillText('Left', 10, 20);
    ctx.fillText('Right', 10, channelHeight + 20);
  },

  drawWaveformChannel: function (ctx, samples, x, y, width, height, colorPositive, colorNegative) {
    if (!samples || samples.length === 0) return;

    const centerY = y + height / 2;
    const step = width / samples.length;
    const halfHeight = height / 2;

    // Find peak amplitude for auto-scaling
    let maxSample = 0;
    for (let i = 0; i < samples.length; i++) {
      maxSample = Math.max(maxSample, Math.abs(samples[i]));
    }

    // Auto-scale: boost quiet signals, cap at 2.5x
    let amplitude = halfHeight * 0.95;
    if (maxSample > 0 && maxSample < 0.4) {
      amplitude = halfHeight * Math.min(2.5, 1.0 / maxSample) * 0.95;
    }

    // Draw vertical bars from center line to sample level
    // Batch positive and negative samples separately to minimize style switches
    const barWidth = Math.max(1, step);

    // Positive samples (above center line) — accent cyan
    ctx.fillStyle = colorPositive || '#5CD4E8';
    for (let i = 0; i < samples.length; i++) {
      if (samples[i] > 0) {
        const sampleX = x + i * step;
        const barHeight = samples[i] * amplitude;
        ctx.fillRect(sampleX, centerY - barHeight, barWidth, barHeight);
      }
    }

    // Negative samples (below center line) — signal amber
    ctx.fillStyle = colorNegative || '#F0A830';
    for (let i = 0; i < samples.length; i++) {
      if (samples[i] < 0) {
        const sampleX = x + i * step;
        const barHeight = -samples[i] * amplitude;
        ctx.fillRect(sampleX, centerY, barWidth, barHeight);
      }
    }
  },

  // Draw spectrum analyzer
  drawSpectrum: function (canvasId, magnitudes, frequencies) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData) return;
    this.ensureCanvasSize(canvasData);

    const { ctx, width, height } = canvasData;
    
    // Clear canvas
    ctx.fillStyle = '#0A0A0C';
    ctx.fillRect(0, 0, width, height);

    if (!magnitudes || magnitudes.length === 0) return;

    const barCount = Math.min(magnitudes.length, 64); // Limit to 64 bars for performance
    const barWidth = width / barCount;
    const barGap = barWidth * 0.1;

    for (let i = 0; i < barCount; i++) {
      const magnitude = magnitudes[i];
      const barHeight = height * magnitude;
      const barX = i * barWidth;
      const barY = height - barHeight;

      // Color gradient based on magnitude: cyan → amber → red
      let color;
      if (magnitude < 0.6) {
        const t = magnitude / 0.6;
        color = this.interpolateColor('#5CD4E8', '#F0A830', t);
      } else {
        const t = (magnitude - 0.6) / 0.4;
        color = this.interpolateColor('#F0A830', '#F87171', t);
      }

      ctx.fillStyle = color;
      ctx.fillRect(barX + barGap / 2, barY, barWidth - barGap, barHeight);
    }

    // Draw frequency labels
    ctx.fillStyle = '#4B5563';
    ctx.font = '10px Inter, sans-serif';
    ctx.textAlign = 'center';
    
    const labelIndices = [0, Math.floor(barCount / 4), Math.floor(barCount / 2), Math.floor(barCount * 3 / 4), barCount - 1];
    labelIndices.forEach(i => {
      if (i < frequencies.length) {
        const freq = frequencies[i];
        const labelX = i * barWidth + barWidth / 2;
        let label;
        if (freq < 1000) {
          label = `${Math.round(freq)}Hz`;
        } else {
          label = `${(freq / 1000).toFixed(1)}kHz`;
        }
        ctx.fillText(label, labelX, height - 5);
      }
    });
  },

  // Draw circular spectrum — radial frequency bars from center
  drawCircularSpectrum: function (canvasId, magnitudes, frequencies) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData || !magnitudes || magnitudes.length === 0) return;
    this.ensureCanvasSize(canvasData);

    const { ctx, width, height } = canvasData;

    ctx.fillStyle = '#0A0A0C';
    ctx.fillRect(0, 0, width, height);

    const centerX = width / 2;
    const centerY = height / 2;
    const innerRadius = Math.min(width, height) * 0.12;
    const maxRadius = Math.min(width, height) * 0.45;
    const barCount = Math.min(magnitudes.length, 128);
    const angleStep = (Math.PI * 2) / barCount;

    for (let i = 0; i < barCount; i++) {
      const mag = magnitudes[i];
      const barLength = mag * (maxRadius - innerRadius);
      const angle = i * angleStep - Math.PI / 2; // Start from top

      const x1 = centerX + Math.cos(angle) * innerRadius;
      const y1 = centerY + Math.sin(angle) * innerRadius;
      const x2 = centerX + Math.cos(angle) * (innerRadius + barLength);
      const y2 = centerY + Math.sin(angle) * (innerRadius + barLength);

      // Color: cyan → amber → red based on magnitude
      let color;
      if (mag < 0.6) {
        color = this.interpolateColor('#5CD4E8', '#F0A830', mag / 0.6);
      } else {
        color = this.interpolateColor('#F0A830', '#F87171', (mag - 0.6) / 0.4);
      }

      ctx.beginPath();
      ctx.moveTo(x1, y1);
      ctx.lineTo(x2, y2);
      ctx.strokeStyle = color;
      ctx.lineWidth = Math.max(1.5, (angleStep * innerRadius) * 0.7);
      ctx.lineCap = 'round';
      ctx.stroke();
    }

    // Inner circle glow
    const gradient = ctx.createRadialGradient(centerX, centerY, 0, centerX, centerY, innerRadius);
    gradient.addColorStop(0, 'rgba(92, 212, 232, 0.15)');
    gradient.addColorStop(1, 'rgba(92, 212, 232, 0.02)');
    ctx.fillStyle = gradient;
    ctx.beginPath();
    ctx.arc(centerX, centerY, innerRadius, 0, Math.PI * 2);
    ctx.fill();
  },

  // Draw stereo phase scope — L vs R XY scatter with phosphor decay
  drawPhaseScope: function (canvasId, leftSamples, rightSamples) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData || !leftSamples || !rightSamples) return;
    this.ensureCanvasSize(canvasData);

    const { ctx, width, height } = canvasData;

    // Initialize or fade the phosphor buffer
    if (!canvasData.phaseScopeBuffer) {
      canvasData.phaseScopeBuffer = ctx.createImageData(width, height);
      // Fill with dark background
      for (let i = 0; i < canvasData.phaseScopeBuffer.data.length; i += 4) {
        canvasData.phaseScopeBuffer.data[i] = 10;     // R
        canvasData.phaseScopeBuffer.data[i + 1] = 10;  // G
        canvasData.phaseScopeBuffer.data[i + 2] = 12;  // B
        canvasData.phaseScopeBuffer.data[i + 3] = 255; // A
      }
    }

    // Phosphor decay: fade existing pixels toward background
    const buf = canvasData.phaseScopeBuffer.data;
    for (let i = 0; i < buf.length; i += 4) {
      buf[i] = buf[i] + (10 - buf[i]) * 0.08;       // R → 10
      buf[i + 1] = buf[i + 1] + (10 - buf[i + 1]) * 0.08; // G → 10
      buf[i + 2] = buf[i + 2] + (12 - buf[i + 2]) * 0.08; // B → 12
    }

    const centerX = width / 2;
    const centerY = height / 2;
    const len = Math.min(leftSamples.length, rightSamples.length);

    // Adaptive scaling: find peak amplitude in current frame
    let maxAmp = 0;
    for (let i = 0; i < len; i++) {
      const l = Math.abs(leftSamples[i] || 0);
      const r = Math.abs(rightSamples[i] || 0);
      maxAmp = Math.max(maxAmp, l, r);
    }

    // Track recent peak for smooth scaling (avoid jitter)
    if (!canvasData.phaseScopePeak) canvasData.phaseScopePeak = maxAmp || 0.5;
    if (maxAmp > canvasData.phaseScopePeak) {
      // Attack: fast rise to new peak
      canvasData.phaseScopePeak = canvasData.phaseScopePeak * 0.3 + maxAmp * 0.7;
    } else {
      // Release: slow decay
      canvasData.phaseScopePeak = canvasData.phaseScopePeak * 0.97 + maxAmp * 0.03;
    }

    // Scale so the tracked peak fills ~80% of the display; floor at 0.01 to avoid division issues
    const effectivePeak = Math.max(0.01, canvasData.phaseScopePeak);
    const scale = (Math.min(width, height) * 0.4) / effectivePeak;

    // Plot L vs R as XY — rotated 45° (Lissajous convention: mid = vertical, side = horizontal)
    for (let i = 0; i < len; i++) {
      const l = leftSamples[i] || 0;
      const r = rightSamples[i] || 0;
      // Rotate 45°: x = (L - R), y = -(L + R) / sqrt(2)
      const px = Math.round(centerX + (l - r) * scale);
      const py = Math.round(centerY - (l + r) * scale * 0.707);

      if (px >= 0 && px < width && py >= 0 && py < height) {
        const idx = (py * width + px) * 4;
        // Bright cyan-green phosphor dot
        buf[idx] = 92;       // R
        buf[idx + 1] = 232;  // G (phosphor green-ish)
        buf[idx + 2] = 212;  // B
      }
    }

    ctx.putImageData(canvasData.phaseScopeBuffer, 0, 0);

    // Draw crosshair axes
    ctx.strokeStyle = 'rgba(31, 31, 34, 0.8)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(centerX, 0);
    ctx.lineTo(centerX, height);
    ctx.moveTo(0, centerY);
    ctx.lineTo(width, centerY);
    ctx.stroke();

    // Labels
    ctx.fillStyle = 'rgba(240,239,244,0.4)';
    ctx.font = '12px Inter, sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText('M', centerX, 14);
    ctx.fillText('S', width - 10, centerY - 4);
    ctx.fillText('L', centerX - scale * 0.7, centerY - scale * 0.5);
    ctx.fillText('R', centerX + scale * 0.7, centerY - scale * 0.5);
  },

  // ── BAND (AUD-76): the stored FM band map ─────────────────────────────────
  //
  // The model arrives in plot coordinates, computed in C# (VisualizerPanel.DrawBandAsync):
  //   levels:   [{ f, v }]  channel position 0..1 across 87.5–108 MHz, normalised height 0..1
  //   station:  f | null    the tuned station (FM, radio active), else null
  //   presets:  [{ f, label }] FM presets, ascending
  //   sweeping: bool
  // x = f * width exactly, with no side padding, so the MHz axis strip under the canvas (positioned
  // by the same fraction) and the tap handler (which reports x / width) line up with what is drawn.
  // Text the owner reads ("No scan yet", the age, "Scanning…") is Blazor markup, not drawn here.

  // Space reserved above the plot: the markup status bar + Scan button (64px, the height of
  // .band-overlay in design-system.css), then two preset-label lanes, then room for the station caret
  // (9px tall) so it does not overprint a label in the second lane.
  bandTopReserve: 64,
  bandLabelLane: 18,
  bandCaretGap: 12,

  // Reads a design token from the canvas's computed style, with a fallback for a missing token.
  token: function (canvas, name, fallback) {
    const value = getComputedStyle(canvas).getPropertyValue(name).trim();
    return value || fallback;
  },

  drawBandMap: function (canvasId, model) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData) return;
    this.ensureCanvasSize(canvasData);

    const { canvas, ctx, width, height } = canvasData;
    const accent = this.token(canvas, '--accent-primary', '#5CD4E8');
    const station = this.token(canvas, '--source-radio', '#F0A830');
    const textMedium = this.token(canvas, '--text-medium', '#B5BCC9');
    const textLow = this.token(canvas, '--text-low', '#4B5563');
    const separator = this.token(canvas, '--surface-separator', '#1F1F22');
    const background = this.token(canvas, '--surface-inset', '#0A0A0C');
    const monoFont = this.token(canvas, '--font-mono', 'monospace');

    ctx.fillStyle = background;
    ctx.fillRect(0, 0, width, height);

    const labelTop = this.bandTopReserve;
    const plotTop = labelTop + this.bandLabelLane * 2 + this.bandCaretGap;
    const plotBottom = height - 2;
    const plotHeight = Math.max(1, plotBottom - plotTop);
    const xOf = (f) => f * width;
    const yOf = (v) => plotBottom - v * plotHeight;

    // MHz gridlines, matching the axis strip's 88/92/96/100/104/108.
    ctx.strokeStyle = separator;
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (const mhz of [88, 92, 96, 100, 104, 108]) {
      const x = Math.round(xOf((mhz - 87.5) / 20.5)) + 0.5;
      ctx.moveTo(x, plotTop);
      ctx.lineTo(x, plotBottom);
    }
    ctx.stroke();

    // Signal: a filled trace through the channel centres, plus a bar per channel so isolated
    // stations read as stations rather than as the slope between two points.
    const levels = (model && model.levels) || [];
    if (levels.length > 0) {
      const fill = ctx.createLinearGradient(0, plotTop, 0, plotBottom);
      fill.addColorStop(0, this.withAlpha(accent, 0.45));
      fill.addColorStop(1, this.withAlpha(accent, 0.04));

      ctx.beginPath();
      ctx.moveTo(xOf(levels[0].f), plotBottom);
      for (const p of levels) ctx.lineTo(xOf(p.f), yOf(p.v));
      ctx.lineTo(xOf(levels[levels.length - 1].f), plotBottom);
      ctx.closePath();
      ctx.fillStyle = fill;
      ctx.fill();

      const barWidth = Math.max(2, (width / 102.5) * 0.5);
      ctx.fillStyle = accent;
      for (const p of levels) {
        const h = p.v * plotHeight;
        if (h < 1) continue;
        ctx.fillRect(xOf(p.f) - barWidth / 2, plotBottom - h, barWidth, h);
      }
    }

    // Presets: a dashed tick down the plot and a short label in one of two lanes. A label that
    // would overlap the previous one in both lanes is skipped; its tick is still drawn.
    const presets = (model && model.presets) || [];
    ctx.font = `11px ${monoFont}`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    const laneRight = [-Infinity, -Infinity];
    for (const p of presets) {
      const x = xOf(p.f);
      ctx.strokeStyle = textLow;
      ctx.setLineDash([3, 4]);
      ctx.beginPath();
      ctx.moveTo(Math.round(x) + 0.5, plotTop);
      ctx.lineTo(Math.round(x) + 0.5, plotBottom);
      ctx.stroke();
      ctx.setLineDash([]);

      const label = p.label || '';
      const w = ctx.measureText(label).width;
      const left = Math.min(Math.max(x - w / 2, 2), width - w - 2);
      for (let lane = 0; lane < 2; lane++) {
        if (left >= laneRight[lane] + 6) {
          ctx.fillStyle = textMedium;
          ctx.fillText(label, left + w / 2, labelTop + this.bandLabelLane * lane + this.bandLabelLane / 2);
          laneRight[lane] = left + w;
          break;
        }
      }
    }

    // Current station: a solid line in the radio source colour with a downward caret on top.
    if (model && typeof model.station === 'number') {
      const x = Math.round(xOf(model.station)) + 0.5;
      ctx.strokeStyle = station;
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(x, plotTop);
      ctx.lineTo(x, plotBottom);
      ctx.stroke();
      ctx.fillStyle = station;
      ctx.beginPath();
      ctx.moveTo(x - 7, plotTop - 9);
      ctx.lineTo(x + 7, plotTop - 9);
      ctx.lineTo(x, plotTop);
      ctx.closePath();
      ctx.fill();
      ctx.lineWidth = 1;
    }
  },

  // Converts a #RRGGBB token to rgba() with the given alpha; other forms are returned unchanged.
  withAlpha: function (color, alpha) {
    const m = /^#([0-9a-f]{6})$/i.exec(color);
    if (!m) return color;
    const n = parseInt(m[1], 16);
    return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`;
  },

  // Reports a tap on the plot to .NET as a fraction of the canvas width (0 = 87.5, 1 = 108 MHz).
  // pointerup covers touch and mouse alike; one handler per canvas, replaced on re-registration.
  // A tap inside the top bar (status text + Scan button) is ignored: the bar passes pointer events
  // through to the canvas, so without this a near-miss on Scan would retune the radio.
  registerBandTap: function (canvasId, dotNetRef) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData) return;
    this.unregisterBandTap(canvasId);

    const canvas = canvasData.canvas;
    const handler = (e) => {
      const rect = canvas.getBoundingClientRect();
      if (rect.width <= 0 || rect.height <= 0) return;
      const yCanvas = (e.clientY - rect.top) * (canvas.height / rect.height);
      if (yCanvas < this.bandTopReserve) return;
      const fraction = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width));
      dotNetRef.invokeMethodAsync('OnBandTap', fraction).catch(() => { /* circuit gone */ });
    };
    canvas.addEventListener('pointerup', handler);
    canvasData.bandTapHandler = handler;
  },

  unregisterBandTap: function (canvasId) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData || !canvasData.bandTapHandler) return;
    canvasData.canvas.removeEventListener('pointerup', canvasData.bandTapHandler);
    canvasData.bandTapHandler = null;
  },

  interpolateColor: function (color1, color2, t) {
    const hex1 = color1.replace('#', '');
    const hex2 = color2.replace('#', '');
    
    const r1 = parseInt(hex1.substring(0, 2), 16);
    const g1 = parseInt(hex1.substring(2, 4), 16);
    const b1 = parseInt(hex1.substring(4, 6), 16);
    
    const r2 = parseInt(hex2.substring(0, 2), 16);
    const g2 = parseInt(hex2.substring(2, 4), 16);
    const b2 = parseInt(hex2.substring(4, 6), 16);
    
    const r = Math.round(r1 + (r2 - r1) * t);
    const g = Math.round(g1 + (g2 - g1) * t);
    const b = Math.round(b1 + (b2 - b1) * t);
    
    return `#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}${b.toString(16).padStart(2, '0')}`;
  },

  // Reset the phase scope buffer
  resetBuffers: function (canvasId) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData) return;
    canvasData.phaseScopeBuffer = null;
    canvasData.phaseScopePeak = null;
  },

  // Dispose a canvas
  dispose: function (canvasId) {
    if (this.animationFrames[canvasId]) {
      cancelAnimationFrame(this.animationFrames[canvasId]);
      delete this.animationFrames[canvasId];
    }
    this.unregisterBandTap(canvasId);
    delete this.canvases[canvasId];
    console.log(`Disposed canvas ${canvasId}`);
  }
};
