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
      // Dynamic VU meter scaling
      recentPeaks: [],
      scaleFactor: 1.0,
      lastScaleUpdate: Date.now(),
      targetScaleFactor: 1.0,
      // Spectrogram/waterfall history (circular buffer of spectrum columns)
      spectrogramHistory: [],
      spectrogramMaxColumns: width,
      // Phase scope decay buffer
      phaseScopeBuffer: null
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

  // Update dynamic VU meter scaling
  updateDynamicScaling: function (canvasData, maxPeak) {
    const now = Date.now();
    
    // Add current peak to history (we keep roughly the last 8 seconds of peaks)
    canvasData.recentPeaks.push({ value: maxPeak, time: now });
    
    // Remove peaks older than 8 seconds
    const cutoffTime = now - 8000;
    canvasData.recentPeaks = canvasData.recentPeaks.filter(p => p.time >= cutoffTime);
    
    // Hard cap on history size to avoid memory growth at extremely high update rates
    const maxRecentPeaks = 600; // ~8 seconds at 75+ updates/sec; adjust if needed
    if (canvasData.recentPeaks.length > maxRecentPeaks) {
      const excess = canvasData.recentPeaks.length - maxRecentPeaks;
      // Remove the oldest entries, keep the most recent ones
      canvasData.recentPeaks.splice(0, excess);
    }
    
    // Update scale factor every 2 seconds
    if (now - canvasData.lastScaleUpdate >= 2000 && canvasData.recentPeaks.length > 0) {
      // Calculate average maximum peak over the recent window
      const avgMax = canvasData.recentPeaks.reduce((sum, p) => sum + p.value, 0) / canvasData.recentPeaks.length;
      
      // Target scale so average max reaches ~80% of display (but don't scale below 1.0)
      if (avgMax > 0.1) { // Only scale if there's meaningful audio
        canvasData.targetScaleFactor = Math.max(1.0, 0.8 / avgMax);
      } else {
        // Reset to 1.0 when audio is very quiet
        canvasData.targetScaleFactor = 1.0;
      }
      
      canvasData.lastScaleUpdate = now;
    }
    
    // Smooth transition to target scale factor (ease-in-out)
    const transitionSpeed = 0.05; // Slower = smoother
    canvasData.scaleFactor += (canvasData.targetScaleFactor - canvasData.scaleFactor) * transitionSpeed;
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

  // Draw VU meter
  drawVUMeter: function (canvasId, leftPeak, rightPeak, leftRms, rightRms, isClipping) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData) return;
    this.ensureCanvasSize(canvasData);

    // Validate and clamp inputs
    leftPeak = Math.max(0, Math.min(1, leftPeak || 0));
    rightPeak = Math.max(0, Math.min(1, rightPeak || 0));
    leftRms = Math.max(0, Math.min(1, leftRms || 0));
    rightRms = Math.max(0, Math.min(1, rightRms || 0));
    
    // Update dynamic scaling based on peak values
    const maxPeak = Math.max(leftPeak, rightPeak);
    this.updateDynamicScaling(canvasData, maxPeak);
    
    // Apply scale factor to peak and RMS values
    const scaleFactor = canvasData.scaleFactor;
    leftPeak = Math.min(1.0, leftPeak * scaleFactor);
    rightPeak = Math.min(1.0, rightPeak * scaleFactor);
    leftRms = Math.min(1.0, leftRms * scaleFactor);
    rightRms = Math.min(1.0, rightRms * scaleFactor);

    const { ctx, width, height } = canvasData;
    
    // Clear canvas
    ctx.fillStyle = '#0A0A0C';
    ctx.fillRect(0, 0, width, height);

    const meterWidth = width * 0.45;
    const meterHeight = height * 0.7;
    const meterX = width * 0.025;
    const meterY = (height - meterHeight) / 2;
    const spacing = width * 0.05;

    // Draw left meter
    this.drawMeter(ctx, meterX, meterY, meterWidth, meterHeight, leftPeak, leftRms, isClipping, 'Left');
    
    // Draw right meter
    this.drawMeter(ctx, meterX + meterWidth + spacing, meterY, meterWidth, meterHeight, rightPeak, rightRms, isClipping, 'Right');
    
  },

  drawMeter: function (ctx, x, y, width, height, peak, rms, isClipping, label) {
    // Draw background
    ctx.fillStyle = '#101012';
    ctx.fillRect(x, y, width, height);

    // Draw border
    ctx.strokeStyle = '#1F1F22';
    ctx.lineWidth = 2;
    ctx.strokeRect(x, y, width, height);

    // Calculate bar heights
    const peakHeight = height * peak;
    const rmsHeight = height * rms;

    // Helper function to get meter color based on height percentage
    // Green (#4ADE80) → Amber (#F0A830) → Red (#F87171)
    const getMeterColor = (percentage) => {
      if (percentage < 0.6) {
        // Green to Amber
        const t = percentage / 0.6;
        const r = Math.floor(74 + (240 - 74) * t);
        const g = Math.floor(222 + (168 - 222) * t);
        const b = Math.floor(128 + (48 - 128) * t);
        return `rgb(${r}, ${g}, ${b})`;
      }
      else if (percentage < 0.85) {
        // Amber to Red
        const t = (percentage - 0.6) / 0.25;
        const r = Math.floor(240 + (248 - 240) * t);
        const g = Math.floor(168 + (113 - 168) * t);
        const b = Math.floor(48 + (113 - 48) * t);
        return `rgb(${r}, ${g}, ${b})`;
      }
      else {
        // Hot red
        return `rgb(248, 113, 113)`;
      }
    };

    // Draw RMS bar with rainbow gradient (dimmer)
    ctx.globalAlpha = 0.5;
    for (let i = 0; i < rmsHeight; i++) {
      const currentY = y + height - i;
      const percentage = i / height;
      const color = getMeterColor(percentage);
      
      ctx.fillStyle = color;
      ctx.fillRect(x + width * 0.1, currentY, width * 0.35, 1);
    }

    // Draw peak bar with rainbow gradient (brighter)
    ctx.globalAlpha = 1.0;
    for (let i = 0; i < peakHeight; i++) {
      const currentY = y + height - i;
      const percentage = i / height;
      const color = getMeterColor(percentage);

      ctx.fillStyle = color;
      ctx.fillRect(x + width * 0.55, currentY, width * 0.35, 1);
    }

    // Draw peak hold indicator
    const peakY = y + height - peakHeight;
    ctx.fillStyle = isClipping ? '#F87171' : '#F0EFF4';
    ctx.fillRect(x + width * 0.1, peakY - 2, width * 0.8, 4);

    // Draw label
    ctx.fillStyle = '#F0EFF4';
    ctx.font = '16px Inter, sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText(label, x + width / 2, y - 10);

    // Draw scale markers
    ctx.fillStyle = '#4B5563';
    ctx.font = '10px Inter, sans-serif';
    ctx.textAlign = 'right';
    
    const markers = [0, -6, -12, -18, -24, -30, -40, -60];
    markers.forEach(db => {
      const linearValue = Math.pow(10, db / 20);
      const markerY = y + height * (1 - linearValue);
      ctx.fillText(`${db}`, x - 5, markerY + 3);
    });
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

  // Draw spectrogram/waterfall — scrolling frequency-time heatmap
  drawSpectrogram: function (canvasId, magnitudes, frequencies) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData || !magnitudes || magnitudes.length === 0) return;
    this.ensureCanvasSize(canvasData);

    const { ctx, width, height } = canvasData;

    // Add current spectrum as a new column
    const barCount = Math.min(magnitudes.length, 128);
    canvasData.spectrogramHistory.push(magnitudes.slice(0, barCount));
    canvasData.spectrogramMaxColumns = width;
    if (canvasData.spectrogramHistory.length > width)
      canvasData.spectrogramHistory.shift();

    // Draw the full spectrogram
    ctx.fillStyle = '#0A0A0C';
    ctx.fillRect(0, 0, width, height);

    const cols = canvasData.spectrogramHistory;
    const colWidth = Math.max(1, width / cols.length);

    for (let x = 0; x < cols.length; x++) {
      const col = cols[x];
      const rowHeight = height / col.length;
      for (let y = 0; y < col.length; y++) {
        const mag = col[y];
        if (mag < 0.01) continue; // Skip near-silent bins

        // Heatmap: black → deep blue → cyan → yellow → white
        const intensity = Math.min(1, mag * 1.5);
        let r, g, b;
        if (intensity < 0.25) {
          const t = intensity / 0.25;
          r = 0; g = 0; b = Math.floor(80 * t);
        } else if (intensity < 0.5) {
          const t = (intensity - 0.25) / 0.25;
          r = 0; g = Math.floor(200 * t); b = 80 + Math.floor(148 * t);
        } else if (intensity < 0.75) {
          const t = (intensity - 0.5) / 0.25;
          r = Math.floor(240 * t); g = 200 + Math.floor(30 * t); b = Math.floor(228 * (1 - t));
        } else {
          const t = (intensity - 0.75) / 0.25;
          r = 240 + Math.floor(15 * t); g = 230 + Math.floor(25 * t); b = Math.floor(200 * t);
        }

        ctx.fillStyle = `rgb(${r},${g},${b})`;
        // Frequency axis: low at bottom, high at top
        ctx.fillRect(x * colWidth, height - (y + 1) * rowHeight, colWidth + 0.5, rowHeight + 0.5);
      }
    }

    // Frequency axis labels
    if (frequencies && frequencies.length > 0) {
      ctx.fillStyle = 'rgba(240,239,244,0.5)';
      ctx.font = '10px Inter, sans-serif';
      ctx.textAlign = 'right';
      const labelCount = 5;
      for (let i = 0; i < labelCount; i++) {
        const freqIdx = Math.floor(i * (barCount - 1) / (labelCount - 1));
        const freq = frequencies[Math.min(freqIdx, frequencies.length - 1)];
        const labelY = height - (freqIdx / barCount) * height;
        const label = freq < 1000 ? `${Math.round(freq)}Hz` : `${(freq / 1000).toFixed(1)}k`;
        ctx.fillText(label, width - 4, labelY + 3);
      }
    }
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

  // Reset spectrogram and phase scope buffers
  resetBuffers: function (canvasId) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData) return;
    canvasData.spectrogramHistory = [];
    canvasData.phaseScopeBuffer = null;
    canvasData.phaseScopePeak = null;
  },

  // Dispose a canvas
  dispose: function (canvasId) {
    if (this.animationFrames[canvasId]) {
      cancelAnimationFrame(this.animationFrames[canvasId]);
      delete this.animationFrames[canvasId];
    }
    delete this.canvases[canvasId];
    console.log(`Disposed canvas ${canvasId}`);
  }
};
