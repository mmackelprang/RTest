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
      height: height
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
      color = '#00ff00'; // Green
    } else if (ups >= 15) {
      color = '#ffff00'; // Yellow
    } else {
      color = '#ff0000'; // Red
    }

    // Draw in bottom-left corner
    ctx.fillStyle = 'rgba(0, 0, 0, 0.5)';
    ctx.fillRect(5, height - 25, 120, 20);
    
    ctx.fillStyle = color;
    ctx.font = '14px Inter, sans-serif';
    ctx.textAlign = 'left';
    ctx.fillText(`Updates: ${ups}/sec`, 10, height - 10);
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

    const { ctx, width, height } = canvasData;
    
    // Clear canvas
    ctx.fillStyle = '#1a1a1a';
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
    
    // Draw UPS indicator
    const tracking = this.upsTracking[canvasId];
    if (tracking) {
      this.drawUPSIndicator(ctx, width, height, tracking.currentUPS);
    }
  },

  drawMeter: function (ctx, x, y, width, height, peak, rms, isClipping, label) {
    // Draw background
    ctx.fillStyle = '#2a2a2a';
    ctx.fillRect(x, y, width, height);

    // Draw border
    ctx.strokeStyle = '#444444';
    ctx.lineWidth = 2;
    ctx.strokeRect(x, y, width, height);

    // Calculate bar heights
    const peakHeight = height * peak;
    const rmsHeight = height * rms;

    // Helper function to get rainbow color based on height percentage
    const getRainbowColor = (percentage) => {
      // 0-20%: Blue (#0000FF to #0080FF)
      if (percentage < 0.2) {
        const t = percentage / 0.2;
        return `rgb(0, ${Math.floor(128 * t)}, 255)`;
      }
      // 20-40%: Cyan (#0080FF to #00FFFF)
      else if (percentage < 0.4) {
        const t = (percentage - 0.2) / 0.2;
        return `rgb(0, ${Math.floor(128 + 127 * t)}, 255)`;
      }
      // 40-60%: Green (#00FFFF to #00FF00)
      else if (percentage < 0.6) {
        const t = (percentage - 0.4) / 0.2;
        return `rgb(0, 255, ${Math.floor(255 * (1 - t))})`;
      }
      // 60-80%: Yellow (#00FF00 to #FFFF00)
      else if (percentage < 0.8) {
        const t = (percentage - 0.6) / 0.2;
        return `rgb(${Math.floor(255 * t)}, 255, 0)`;
      }
      // 80-95%: Orange (#FFFF00 to #FF8000)
      else if (percentage < 0.95) {
        const t = (percentage - 0.8) / 0.15;
        return `rgb(255, ${Math.floor(255 * (1 - t * 0.5))}, 0)`;
      }
      // 95-100%: Red (#FF8000 to #FF0000)
      else {
        const t = (percentage - 0.95) / 0.05;
        return `rgb(255, ${Math.floor(128 * (1 - t))}, 0)`;
      }
    };

    // Draw RMS bar with rainbow gradient (dimmer)
    ctx.globalAlpha = 0.5;
    for (let i = 0; i < rmsHeight; i++) {
      const currentY = y + height - i;
      const percentage = i / height;
      const color = getRainbowColor(percentage);
      
      ctx.fillStyle = color;
      ctx.fillRect(x + width * 0.1, currentY, width * 0.35, 1);
    }

    // Draw peak bar with rainbow gradient (brighter)
    ctx.globalAlpha = 1.0;
    for (let i = 0; i < peakHeight; i++) {
      const currentY = y + height - i;
      const percentage = i / height;
      const color = getRainbowColor(percentage);

      ctx.fillStyle = color;
      ctx.fillRect(x + width * 0.55, currentY, width * 0.35, 1);
    }

    // Draw peak hold indicator
    const peakY = y + height - peakHeight;
    ctx.fillStyle = isClipping ? '#ff0000' : '#ffffff';
    ctx.fillRect(x + width * 0.1, peakY - 2, width * 0.8, 4);

    // Draw label
    ctx.fillStyle = '#ffffff';
    ctx.font = '16px Inter, sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText(label, x + width / 2, y - 10);

    // Draw scale markers
    ctx.fillStyle = '#666666';
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
    ctx.fillStyle = '#1a1a1a';
    ctx.fillRect(0, 0, width, height);

    const channelHeight = height / 2;
    
    // Draw left channel
    this.drawWaveformChannel(ctx, leftSamples, 0, 0, width, channelHeight, '#00d4ff');
    
    // Draw right channel
    this.drawWaveformChannel(ctx, rightSamples, 0, channelHeight, width, channelHeight, '#00d4ff');

    // Draw center line for each channel
    ctx.strokeStyle = '#444444';
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
    ctx.fillStyle = '#ffffff';
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

  drawWaveformChannel: function (ctx, samples, x, y, width, height, color) {
    if (!samples || samples.length === 0) return;

    const centerY = y + height / 2;
    // Increased amplitude multiplier from 0.9 to 2.5 for better visibility
    // Also add auto-scaling based on peak amplitude
    let maxSample = 0;
    for (let i = 0; i < samples.length; i++) {
      maxSample = Math.max(maxSample, Math.abs(samples[i]));
    }
    
    // Auto-scale: if signal is too quiet, boost it; if too loud, keep it
    let scaleFactor = maxSample > 0 ? Math.min(2.5, 1.0 / maxSample) : 2.5;
    const amplitude = height / 2 * scaleFactor * 0.95; // Use 95% to prevent clipping

    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.beginPath();

    const step = width / samples.length;
    
    for (let i = 0; i < samples.length; i++) {
      const sample = samples[i];
      const sampleX = x + i * step;
      const sampleY = centerY - (sample * amplitude);

      if (i === 0) {
        ctx.moveTo(sampleX, sampleY);
      } else {
        ctx.lineTo(sampleX, sampleY);
      }
    }

    ctx.stroke();
  },

  // Draw spectrum analyzer
  drawSpectrum: function (canvasId, magnitudes, frequencies) {
    const canvasData = this.canvases[canvasId];
    if (!canvasData) return;

    // Track this update for UPS calculation
    this.trackUpdate(canvasId);

    const { ctx, width, height } = canvasData;
    
    // Clear canvas
    ctx.fillStyle = '#1a1a1a';
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

      // Color gradient based on magnitude
      let color;
      if (magnitude < 0.5) {
        // Green to yellow
        const t = magnitude * 2;
        color = this.interpolateColor('#00ff00', '#ffff00', t);
      } else {
        // Yellow to red
        const t = (magnitude - 0.5) * 2;
        color = this.interpolateColor('#ffff00', '#ff0000', t);
      }

      ctx.fillStyle = color;
      ctx.fillRect(barX + barGap / 2, barY, barWidth - barGap, barHeight);
    }

    // Draw frequency labels
    ctx.fillStyle = '#888888';
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
