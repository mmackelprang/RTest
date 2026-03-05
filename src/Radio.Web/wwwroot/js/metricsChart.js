// Metrics Dashboard — Canvas Time-Series Chart
// ES module following the visualizer.js pattern
// Provides area-fill charts with hover tooltips, gridlines, and threshold lines

export const metricsChart = {
  canvases: {},

  // Initialize a canvas for chart rendering
  init: function (canvasId) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
      console.error(`[metricsChart] Canvas ${canvasId} not found`);
      return false;
    }

    const ctx = canvas.getContext('2d');
    if (!ctx) {
      console.error(`[metricsChart] Could not get 2D context for ${canvasId}`);
      return false;
    }

    // Size from container
    const parent = canvas.parentElement;
    const width = parent && parent.clientWidth > 0 ? parent.clientWidth : 800;
    const height = parent && parent.clientHeight > 0 ? parent.clientHeight : 230;
    canvas.width = width;
    canvas.height = height;

    // Observe container resize
    const ro = new ResizeObserver(entries => {
      for (const entry of entries) {
        const w = Math.floor(entry.contentRect.width);
        const h = Math.floor(entry.contentRect.height);
        if (w > 0 && h > 0 && (canvas.width !== w || canvas.height !== h)) {
          canvas.width = w;
          canvas.height = h;
          const cd = this.canvases[canvasId];
          if (cd) { cd.width = w; cd.height = h; }
          // Re-render with last data if available
          if (cd && cd.lastData && cd.lastOptions) {
            this.render(canvasId, cd.lastData, cd.lastOptions);
          }
        }
      }
    });
    if (parent) ro.observe(parent);

    // Tooltip element (reuse or create)
    let tooltip = document.getElementById(canvasId + '-tooltip');
    if (!tooltip) {
      tooltip = document.createElement('div');
      tooltip.id = canvasId + '-tooltip';
      tooltip.style.cssText = 'position:absolute;display:none;pointer-events:none;' +
        'background:rgba(20,20,22,0.92);border:1px solid rgba(92,212,232,0.3);' +
        'border-radius:6px;padding:6px 10px;font-family:Inter,sans-serif;font-size:12px;' +
        'color:#F0EFF4;white-space:nowrap;z-index:10;backdrop-filter:blur(8px);' +
        'box-shadow:0 4px 12px rgba(0,0,0,0.4);';
      // Position relative to parent
      if (parent) {
        parent.style.position = parent.style.position || 'relative';
        parent.appendChild(tooltip);
      }
    }

    // Mouse/touch move handler for tooltip
    const handleMove = (e) => {
      const cd = this.canvases[canvasId];
      if (!cd || !cd.lastData || !cd.lastOptions) return;
      const rect = canvas.getBoundingClientRect();
      const clientX = e.touches ? e.touches[0].clientX : e.clientX;
      const clientY = e.touches ? e.touches[0].clientY : e.clientY;
      const x = clientX - rect.left;
      const y = clientY - rect.top;
      this._showTooltip(canvasId, x, y);
    };

    const handleLeave = () => {
      const cd = this.canvases[canvasId];
      if (!cd) return;
      tooltip.style.display = 'none';
      // Re-render without crosshair
      if (cd.lastData && cd.lastOptions) {
        this.render(canvasId, cd.lastData, cd.lastOptions);
      }
    };

    canvas.addEventListener('mousemove', handleMove);
    canvas.addEventListener('touchmove', handleMove, { passive: true });
    canvas.addEventListener('mouseleave', handleLeave);
    canvas.addEventListener('touchend', handleLeave);

    this.canvases[canvasId] = {
      canvas, ctx, width, height, tooltip, resizeObserver: ro,
      lastData: null, lastOptions: null,
      handleMove, handleLeave,
      // Computed layout (set during render)
      layout: null
    };

    console.log(`[metricsChart] Initialized ${canvasId} (${width}x${height})`);
    return true;
  },

  // Render the full chart
  // data: { labels: string[], values: number[], min?: number[], max?: number[] }
  // options: { color: string, fillOpacity: number, thresholds?: [{value, color, label}], unit: string }
  render: function (canvasId, data, options) {
    const cd = this.canvases[canvasId];
    if (!cd) return;

    // Store for resize re-render and tooltip
    cd.lastData = data;
    cd.lastOptions = options;

    const { ctx, width, height } = cd;
    const color = options.color || '#5CD4E8';
    const fillOpacity = options.fillOpacity ?? 0.15;
    const unit = options.unit || '';

    // Layout constants
    const padLeft = 56;
    const padRight = 16;
    const padTop = 12;
    const padBottom = 32;
    const chartW = width - padLeft - padRight;
    const chartH = height - padTop - padBottom;

    cd.layout = { padLeft, padRight, padTop, padBottom, chartW, chartH };

    // Clear
    ctx.fillStyle = '#101012';
    ctx.fillRect(0, 0, width, height);

    if (!data || !data.values || data.values.length === 0) {
      ctx.fillStyle = '#4B5563';
      ctx.font = '14px Inter, sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText('No data available', width / 2, height / 2);
      return;
    }

    const values = data.values;
    const labels = data.labels || [];
    const minVals = data.min;
    const maxVals = data.max;

    // Compute Y range
    let yMin = Math.min(...values);
    let yMax = Math.max(...values);
    if (minVals) yMin = Math.min(yMin, ...minVals.filter(v => v != null));
    if (maxVals) yMax = Math.max(yMax, ...maxVals.filter(v => v != null));
    // Include thresholds in range
    if (options.thresholds) {
      for (const t of options.thresholds) {
        yMin = Math.min(yMin, t.value);
        yMax = Math.max(yMax, t.value);
      }
    }
    // Add 10% padding
    const yRange = yMax - yMin || 1;
    yMin -= yRange * 0.05;
    yMax += yRange * 0.05;
    const finalRange = yMax - yMin;

    // Helper: value → canvas Y
    const toY = (v) => padTop + chartH - ((v - yMin) / finalRange) * chartH;
    // Helper: index → canvas X
    const toX = (i) => padLeft + (i / Math.max(1, values.length - 1)) * chartW;

    // Draw horizontal gridlines
    const gridCount = 5;
    ctx.strokeStyle = '#1F1F22';
    ctx.lineWidth = 1;
    ctx.fillStyle = '#4B5563';
    ctx.font = '11px Inter, sans-serif';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    for (let i = 0; i <= gridCount; i++) {
      const v = yMin + (finalRange * i) / gridCount;
      const y = toY(v);
      ctx.beginPath();
      ctx.moveTo(padLeft, y);
      ctx.lineTo(width - padRight, y);
      ctx.stroke();
      ctx.fillText(this._formatAxisValue(v, unit), padLeft - 6, y);
    }

    // Draw X-axis timestamp labels
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    ctx.fillStyle = '#4B5563';
    const labelCount = Math.min(labels.length, 8);
    if (labelCount > 0) {
      const step = Math.max(1, Math.floor((labels.length - 1) / (labelCount - 1)));
      for (let i = 0; i < labels.length; i += step) {
        const x = toX(i);
        ctx.fillText(labels[i], x, height - padBottom + 6);
      }
      // Always show last label
      if ((labels.length - 1) % step !== 0) {
        ctx.fillText(labels[labels.length - 1], toX(labels.length - 1), height - padBottom + 6);
      }
    }

    // Draw min/max band
    if (minVals && maxVals && minVals.length === values.length && maxVals.length === values.length) {
      ctx.beginPath();
      for (let i = 0; i < values.length; i++) {
        const x = toX(i);
        const yVal = toY(maxVals[i] ?? values[i]);
        if (i === 0) ctx.moveTo(x, yVal);
        else ctx.lineTo(x, yVal);
      }
      for (let i = values.length - 1; i >= 0; i--) {
        ctx.lineTo(toX(i), toY(minVals[i] ?? values[i]));
      }
      ctx.closePath();
      ctx.fillStyle = this._hexToRgba(color, 0.08);
      ctx.fill();
    }

    // Draw area fill (gradient from line to bottom)
    ctx.beginPath();
    ctx.moveTo(toX(0), toY(values[0]));
    for (let i = 1; i < values.length; i++) {
      ctx.lineTo(toX(i), toY(values[i]));
    }
    ctx.lineTo(toX(values.length - 1), padTop + chartH);
    ctx.lineTo(toX(0), padTop + chartH);
    ctx.closePath();

    const gradient = ctx.createLinearGradient(0, padTop, 0, padTop + chartH);
    gradient.addColorStop(0, this._hexToRgba(color, fillOpacity));
    gradient.addColorStop(1, this._hexToRgba(color, 0.01));
    ctx.fillStyle = gradient;
    ctx.fill();

    // Draw line
    ctx.beginPath();
    ctx.moveTo(toX(0), toY(values[0]));
    for (let i = 1; i < values.length; i++) {
      ctx.lineTo(toX(i), toY(values[i]));
    }
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    ctx.stroke();

    // Draw threshold lines
    if (options.thresholds) {
      for (const t of options.thresholds) {
        const y = toY(t.value);
        ctx.beginPath();
        ctx.setLineDash([6, 4]);
        ctx.moveTo(padLeft, y);
        ctx.lineTo(width - padRight, y);
        ctx.strokeStyle = t.color || '#F0A830';
        ctx.lineWidth = 1;
        ctx.stroke();
        ctx.setLineDash([]);
        // Label
        ctx.fillStyle = t.color || '#F0A830';
        ctx.font = '10px Inter, sans-serif';
        ctx.textAlign = 'left';
        ctx.textBaseline = 'bottom';
        ctx.fillText(t.label || '', padLeft + 4, y - 3);
      }
    }

    // Draw chart border
    ctx.strokeStyle = '#1F1F22';
    ctx.lineWidth = 1;
    ctx.setLineDash([]);
    ctx.strokeRect(padLeft, padTop, chartW, chartH);
  },

  // Show crosshair + tooltip at mouse position
  _showTooltip: function (canvasId, mouseX, mouseY) {
    const cd = this.canvases[canvasId];
    if (!cd || !cd.layout || !cd.lastData || !cd.lastData.values) return;

    const { padLeft, padTop, chartW, chartH } = cd.layout;
    const data = cd.lastData;
    const options = cd.lastOptions;
    const values = data.values;
    const labels = data.labels || [];

    // Check bounds
    if (mouseX < padLeft || mouseX > padLeft + chartW || mouseY < padTop || mouseY > padTop + chartH) {
      cd.tooltip.style.display = 'none';
      this.render(canvasId, data, options);
      return;
    }

    // Find nearest data point
    const ratio = (mouseX - padLeft) / chartW;
    const idx = Math.round(ratio * (values.length - 1));
    if (idx < 0 || idx >= values.length) return;

    const yMin = cd._yMin;
    const yMax = cd._yMax;

    // Re-render base chart then draw crosshair on top
    this.render(canvasId, data, options);

    const { ctx, width, height } = cd;
    const color = options.color || '#5CD4E8';
    const toX = (i) => padLeft + (i / Math.max(1, values.length - 1)) * chartW;
    const pointX = toX(idx);

    // Vertical crosshair line
    ctx.beginPath();
    ctx.setLineDash([3, 3]);
    ctx.moveTo(pointX, padTop);
    ctx.lineTo(pointX, padTop + chartH);
    ctx.strokeStyle = 'rgba(240, 239, 244, 0.3)';
    ctx.lineWidth = 1;
    ctx.stroke();
    ctx.setLineDash([]);

    // Dot on the data point — recompute Y range as in render
    let yMinCalc = Math.min(...values);
    let yMaxCalc = Math.max(...values);
    if (data.min) yMinCalc = Math.min(yMinCalc, ...data.min.filter(v => v != null));
    if (data.max) yMaxCalc = Math.max(yMaxCalc, ...data.max.filter(v => v != null));
    if (options.thresholds) {
      for (const t of options.thresholds) {
        yMinCalc = Math.min(yMinCalc, t.value);
        yMaxCalc = Math.max(yMaxCalc, t.value);
      }
    }
    const yRange = yMaxCalc - yMinCalc || 1;
    yMinCalc -= yRange * 0.05;
    yMaxCalc += yRange * 0.05;
    const finalRange = yMaxCalc - yMinCalc;
    const toY = (v) => padTop + chartH - ((v - yMinCalc) / finalRange) * chartH;

    const pointY = toY(values[idx]);
    ctx.beginPath();
    ctx.arc(pointX, pointY, 4, 0, Math.PI * 2);
    ctx.fillStyle = color;
    ctx.fill();
    ctx.beginPath();
    ctx.arc(pointX, pointY, 6, 0, Math.PI * 2);
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.stroke();

    // Update tooltip div
    const unit = options.unit || '';
    let text = `<div style="font-weight:600;color:${color};">${this._formatAxisValue(values[idx], unit)}</div>`;
    if (labels[idx]) {
      text += `<div style="font-size:11px;color:#9CA3AF;margin-top:2px;">${labels[idx]}</div>`;
    }
    if (data.min && data.max && data.min[idx] != null && data.max[idx] != null) {
      text += `<div style="font-size:10px;color:#4B5563;margin-top:2px;">Min: ${this._formatAxisValue(data.min[idx], unit)} / Max: ${this._formatAxisValue(data.max[idx], unit)}</div>`;
    }
    cd.tooltip.innerHTML = text;
    cd.tooltip.style.display = 'block';

    // Position tooltip (avoid overflow)
    const tipW = cd.tooltip.offsetWidth;
    const tipH = cd.tooltip.offsetHeight;
    let tipX = pointX + 12;
    let tipY = pointY - tipH - 8;
    if (tipX + tipW > cd.width - 8) tipX = pointX - tipW - 12;
    if (tipY < 4) tipY = pointY + 12;
    cd.tooltip.style.left = tipX + 'px';
    cd.tooltip.style.top = tipY + 'px';
  },

  // Format Y-axis value for display
  _formatAxisValue: function (value, unit) {
    if (value == null || isNaN(value)) return '—';
    const abs = Math.abs(value);
    let formatted;
    if (abs >= 1000000) formatted = (value / 1000000).toFixed(1) + 'M';
    else if (abs >= 10000) formatted = (value / 1000).toFixed(1) + 'K';
    else if (abs >= 100) formatted = value.toFixed(0);
    else if (abs >= 1) formatted = value.toFixed(1);
    else if (abs >= 0.01) formatted = value.toFixed(2);
    else formatted = value.toFixed(3);
    return unit ? formatted + ' ' + unit : formatted;
  },

  // Convert hex color to rgba string
  _hexToRgba: function (hex, alpha) {
    hex = hex.replace('#', '');
    const r = parseInt(hex.substring(0, 2), 16);
    const g = parseInt(hex.substring(2, 4), 16);
    const b = parseInt(hex.substring(4, 6), 16);
    return `rgba(${r},${g},${b},${alpha})`;
  },

  // Cleanup
  destroy: function (canvasId) {
    const cd = this.canvases[canvasId];
    if (!cd) return;

    // Remove event listeners
    cd.canvas.removeEventListener('mousemove', cd.handleMove);
    cd.canvas.removeEventListener('touchmove', cd.handleMove);
    cd.canvas.removeEventListener('mouseleave', cd.handleLeave);
    cd.canvas.removeEventListener('touchend', cd.handleLeave);

    // Stop resize observer
    if (cd.resizeObserver) cd.resizeObserver.disconnect();

    // Remove tooltip
    if (cd.tooltip && cd.tooltip.parentNode) {
      cd.tooltip.parentNode.removeChild(cd.tooltip);
    }

    delete this.canvases[canvasId];
    console.log(`[metricsChart] Destroyed ${canvasId}`);
  }
};
