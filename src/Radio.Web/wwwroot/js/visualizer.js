// Audio Visualizer JavaScript Interop
// Provides high-performance canvas rendering for audio visualizations

export const visualizer = {
  canvases: {},
  animationFrames: {},
  upsTracking: {}, // Track updates per second for each canvas

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

    canvas.width = width;
    canvas.height = height;

    this.canvases[canvasId] = {
      canvas: canvas,
      ctx: ctx,
      width: width,
      height: height,
      // Dynamic VU meter scaling
      recentPeaks: [],
      scaleFactor: 1.0,
      lastScaleUpdate: Date.now(),
      targetScaleFactor: 1.0
    };

    // Initialize UPS tracking for this canvas
    this.upsTracking[canvasId] = {
      timestamps: [],
      currentUPS: 0,
      lastUPSUpdate: Date.now()
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

  // Track update and calculate UPS
  trackUpdate: function (canvasId) {
    const tracking = this.upsTracking[canvasId];
    if (!tracking) return;

    const now = Date.now();
    tracking.timestamps.push(now);

    // Keep only last 60 timestamps (enough for 1 second of data at high rates)
    if (tracking.timestamps.length > 60) {
      tracking.timestamps.shift();
    }

    // Update UPS calculation every second
    if (now - tracking.lastUPSUpdate >= 1000) {
      // Calculate UPS from timestamps in the last second
      const oneSecondAgo = now - 1000;
      const recentTimestamps = tracking.timestamps.filter(t => t >= oneSecondAgo);
      tracking.currentUPS = recentTimestamps.length;
      tracking.lastUPSUpdate = now;
    }
  },

  // Draw UPS indicator
  drawUPSIndicator: function (ctx, width, height, ups) {
    // Determine color based on performance
    let color;
    if (ups > 30) {
      color = '#4ADE80'; // Green
    } else if (ups >= 15) {
      color = '#F0A830'; // Amber
    } else {
      color = '#F87171'; // Red
    }

    // Draw in bottom-left corner
    ctx.fillStyle = 'rgba(0, 0, 0, 0.5)';
    ctx.fillRect(5, height - 25, 120, 20);
    
    ctx.fillStyle = color;
    ctx.font = '14px Inter, sans-serif';
    ctx.textAlign = 'left';
    ctx.fillText(`Updates: ${ups}/sec`, 10, height - 10);
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

  // Draw VU meter
  drawVUMeter: function (canvasId, leftPeak, rightPeak, leftRms, rightRms, isClipping) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData) return;
    
    // Track this update for UPS calculation
    this.trackUpdate(canvasId);
    
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
    
    // Draw scale factor indicator (top-right corner)
    if (scaleFactor > 1.01) { // Only show if scaled
      ctx.fillStyle = 'rgba(0, 0, 0, 0.5)';
      ctx.fillRect(width - 85, 5, 80, 25);
      
      ctx.fillStyle = '#5CD4E8';
      ctx.font = '14px Inter, sans-serif';
      ctx.textAlign = 'right';
      ctx.fillText(`×${scaleFactor.toFixed(2)}`, width - 10, 22);
    }
    
    // Draw UPS indicator
    const tracking = this.upsTracking[canvasId];
    if (tracking) {
      this.drawUPSIndicator(ctx, width, height, tracking.currentUPS);
    }
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

    // Track this update for UPS calculation
    this.trackUpdate(canvasId);

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
    
    // Draw UPS indicator
    const tracking = this.upsTracking[canvasId];
    if (tracking) {
      this.drawUPSIndicator(ctx, width, height, tracking.currentUPS);
    }
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

    // Track this update for UPS calculation
    this.trackUpdate(canvasId);

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
    
    // Draw UPS indicator
    const tracking = this.upsTracking[canvasId];
    if (tracking) {
      this.drawUPSIndicator(ctx, width, height, tracking.currentUPS);
    }
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
