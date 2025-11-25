# DSEG Font Integration Guide

This document explains how to integrate the DSEG14 Classic seven-segment display fonts into the Audio Controller application for authentic LED-style date/time displays.

## Current Status

The application currently uses **Orbitron** (a Google Font) as a fallback for the LED-style text displays. While Orbitron provides a digital/futuristic aesthetic, it is not a true seven-segment display font.

## DSEG Font Files

The DSEG (Digital Seven Segment) font files are located in:
```
/src/assets/documents/fonts-DSEG_v046.zip
```

This archive contains the DSEG font family, specifically designed to replicate authentic seven-segment LED displays commonly found in clocks, radios, and electronic equipment.

## Integration Steps

### 1. Extract the Font Files

From the `fonts-DSEG_v046.zip` archive, extract the following files:
- `DSEG14Classic-Bold.woff2` (or .ttf)
- `DSEG14Classic-Regular.woff2` (or .ttf)
- `DSEG14Classic-Light.woff2` (or .ttf) - optional

### 2. Create Assets Folder Structure

Create the fonts directory if it doesn't exist:
```bash
mkdir -p src/assets/fonts
```

Move the extracted font files to:
```
src/assets/fonts/DSEG14Classic-Bold.woff2
src/assets/fonts/DSEG14Classic-Regular.woff2
```

### 3. Add @font-face Declarations

Add the following to `/src/index.css` (after the imports, before `:root`):

```css
@font-face {
  font-family: 'DSEG14 Classic';
  src: url('/src/assets/fonts/DSEG14Classic-Bold.woff2') format('woff2'),
       url('/src/assets/fonts/DSEG14Classic-Bold.ttf') format('truetype');
  font-weight: 700;
  font-style: normal;
  font-display: swap;
}

@font-face {
  font-family: 'DSEG14 Classic';
  src: url('/src/assets/fonts/DSEG14Classic-Regular.woff2') format('woff2'),
       url('/src/assets/fonts/DSEG14Classic-Regular.ttf') format('truetype');
  font-weight: 400;
  font-style: normal;
  font-display: swap;
}
```

### 4. Update CSS Classes

Modify the `.led-text-time` class in `/src/index.css`:

**Before:**
```css
.led-text-time {
  font-family: 'Orbitron', monospace;
  font-weight: 900;
  letter-spacing: 0.15em;
  /* ... */
}
```

**After:**
```css
.led-text-time {
  font-family: 'DSEG14 Classic', 'Orbitron', monospace;
  font-weight: 700;
  letter-spacing: 0.15em;
  /* ... */
}
```

### 5. Adjust Styling for DSEG Characteristics

DSEG fonts have unique characteristics that may require styling adjustments:

```css
.led-text-time {
  font-family: 'DSEG14 Classic', 'Orbitron', monospace;
  font-weight: 700;
  letter-spacing: 0.2em;  /* Increase spacing for segment clarity */
  text-shadow: 
    0 0 5px currentColor,
    0 0 10px currentColor,
    0 0 15px currentColor,
    0 0 20px currentColor;
  font-variant-numeric: tabular-nums;
  filter: brightness(1.3);
  line-height: 1;  /* Tighter line height for digital look */
}
```

### 6. Test the Integration

After implementing the changes:

1. Reload the application
2. Check the MainBar time display
3. Verify the font loads correctly (check browser DevTools > Network for font requests)
4. Ensure the glow effect works well with the seven-segment characters
5. Test on different screen resolutions

## Font Characteristics

### DSEG7 Classic Features

- **Style**: Seven-segment display (like classic LED/LCD displays)
- **Characters**: Numbers (0-9), colon (:), slash (/), limited letters
- **Best Use**: Time, dates, numeric readings (frequency, stats)
- **Authenticity**: Mimics real electronic displays with proper segment gaps

### Display Format

The DSEG14 font includes:
- **Digits**: 0-9 with authentic segment rendering
- **Separators**: Colon (:) for time, slash (/) for dates
- **Decimal point**: For frequency displays (e.g., 101.5 MHz)
- **Segment gaps**: Visible gaps between segments for realism

### Character Support

Not all characters render well in seven-segment fonts. Use DSEG14 for:
- ✅ Time: `12:34:56`
- ✅ Date: `12/25/2024`
- ✅ Numbers: `101.5`, `45.2%`, `256`
- ❌ Full text (use Inter or Orbitron for labels)

## Where DSEG Fonts Are Used

### Current Implementation

1. **MainBar - Time Display** (`led-text-time`)
   - Format: `HH:MM:SS` (e.g., `14:32:45`)
   - Size: `text-4xl` (36px)
   - Color: Amber LED (`var(--led-amber)`)

2. **MainBar - Date Display** (`led-text-date`)
   - Format: `MM/DD/YYYY` (e.g., `12/25/2024`)
   - Size: `text-xs` (12px)
   - Color: Amber at 70% opacity

### Potential Future Uses

3. **Radio Frequency Display**
   - Format: `101.5 FM`
   - Large seven-segment display for frequency

4. **System Stats** (CPU, RAM, Threads)
   - Current: Small cyan LED text
   - Could use DSEG14 Regular for consistency

5. **Volume/Balance Indicators**
   - Numeric displays: `75%`, `L25`

6. **Track Position/Duration**
   - Format: `3:45 / 5:32`

## Fallback Strategy

The current implementation uses a fallback chain:

```css
font-family: 'DSEG14 Classic', 'Orbitron', monospace;
```

This ensures that if DSEG14 fails to load:
1. **First**: Try DSEG14 Classic (authentic seven-segment)
2. **Second**: Fall back to Orbitron (digital futuristic font from Google Fonts)
3. **Last**: Use system monospace font

## Troubleshooting

### Font Not Loading

**Problem**: Time still shows in Orbitron/fallback font

**Solutions**:
1. Check file paths are correct (case-sensitive on Linux)
2. Verify font files are in `/src/assets/fonts/`
3. Check browser console for 404 errors
4. Clear browser cache and hard reload (Ctrl+Shift+R)
5. Verify Vite is serving the assets folder correctly

### Characters Not Displaying

**Problem**: Some characters show as squares or fallback font

**Solutions**:
1. DSEG14 only supports numbers and limited symbols
2. Use DSEG14 only for numeric displays
3. Keep labels and text in Inter or Orbitron
4. Check character support in DSEG documentation

### Poor Legibility

**Problem**: Font is hard to read or segments blend together

**Solutions**:
1. Increase `letter-spacing` (0.2em or higher)
2. Adjust text shadow to reduce glow
3. Increase font size
4. Check color contrast against background
5. Reduce `brightness` filter if too intense

### Performance Issues

**Problem**: Font loading slows down page

**Solutions**:
1. Use `font-display: swap` in @font-face
2. Preload critical fonts in index.html:
   ```html
   <link rel="preload" href="/src/assets/fonts/DSEG14Classic-Bold.woff2" as="font" type="font/woff2" crossorigin>
   ```
3. Consider using only Bold weight if Regular isn't needed

## Alternative: Using Google Fonts API

If extracting and hosting fonts is not feasible, alternatives include:

1. **Orbitron** (currently used) - Digital/futuristic but not seven-segment
2. **Share Tech Mono** - Monospace with digital feel
3. **Audiowide** - Retro digital aesthetic
4. **VT323** - Authentic terminal/old LED style

However, none of these perfectly replicate the seven-segment display look that DSEG provides.

## Design Rationale

The PRD specifies:
> **Typographic Hierarchy**:
> H1 (Date/Time Display): DSEG14 Classic Bold/32px/tight tracking - dominant retro-digital presence

The DSEG14 font was specifically chosen to evoke:
- **Professional audio equipment aesthetics** (studio gear with LED displays)
- **Retro-digital charm** (1980s-90s hi-fi components)
- **Industrial authenticity** (embedded systems with seven-segment displays)
- **Nostalgic precision** (classic alarm clocks and radio tuners)

This creates a cohesive design language that bridges vintage electronic aesthetics with modern touchscreen interfaces.

## References

- DSEG Font Project: https://github.com/keshikan/DSEG
- Font Files: `/src/assets/documents/fonts-DSEG_v046.zip`
- Current Implementation: `/src/components/MainBar.tsx`
- Styling: `/src/index.css` (`.led-text-time` class)

## License

Check the DSEG font license included in the zip file. DSEG is typically released under the SIL Open Font License (OFL), which allows free use in both personal and commercial projects.
