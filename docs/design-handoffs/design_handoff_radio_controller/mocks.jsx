// mocks.jsx — Radio Controller design improvement artboards.
// Each component renders inside a <DCArtboard>. Inline styles only so this
// file is self-contained and won't fight the canvas chrome.

const T = {
  base:       '#0D0D0F',
  raised:     '#141416',
  inset:      '#0A0A0C',
  well:       '#040406',
  sep:        '#1F1F22',
  hair:       '#25252A',

  accent:     '#5CD4E8',
  accentDim:  'rgba(92,212,232,0.10)',
  accentGlow: 'rgba(92,212,232,0.25)',
  amber:      '#F0A830',
  amberDim:   'rgba(240,168,48,0.08)',
  amberGlow:  'rgba(240,168,48,0.30)',
  amberSoft:  'rgba(240,168,48,0.15)',

  sRadio:     '#F0A830',

  red:        '#F87171',
  yellow:     '#FBBF24',
  green:      '#4ADE80',
  purple:     '#A78BFA',

  hi:         '#F0EFF4',
  md:         '#9CA3AF',
  lo:         '#6B7280',
  dim:        '#353841',

  body:       "'Inter', -apple-system, BlinkMacSystemFont, sans-serif",
  mono:       "'JetBrains Mono', 'SF Mono', Consolas, monospace",
  led:        "'Share Tech Mono', 'JetBrains Mono', monospace",
};

/* ─────────── shared atoms ─────────── */
const Stage = ({ w, h, pad = 0, children, style }) => (
  <div style={{
    width: w, height: h, background: T.base, color: T.hi,
    fontFamily: T.body, fontSize: 14, lineHeight: 1.45,
    overflow: 'hidden', boxSizing: 'border-box', padding: pad,
    ...style,
  }}>{children}</div>
);

const Split = ({ children }) => (
  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1px 1fr', height: '100%' }}>
    {children[0]}
    <div style={{ background: T.sep }} />
    {children[1]}
  </div>
);

const Half = ({ title, kicker, kickerColor, children, pad = 0 }) => (
  <div style={{ display: 'flex', flexDirection: 'column', height: '100%', minWidth: 0 }}>
    <div style={{
      display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
      padding: '14px 20px 12px', borderBottom: `1px solid ${T.sep}`, flexShrink: 0,
    }}>
      <div>
        <div style={{
          fontFamily: T.mono, fontSize: 10, letterSpacing: '0.16em',
          textTransform: 'uppercase', color: kickerColor || T.lo,
        }}>{kicker}</div>
        <div style={{ fontSize: 13, color: T.hi, marginTop: 3 }}>{title}</div>
      </div>
    </div>
    <div style={{ flex: 1, minHeight: 0, padding: pad, position: 'relative' }}>
      {children}
    </div>
  </div>
);

const Caption = ({ children, color = T.md }) => (
  <div style={{
    position: 'absolute', bottom: 14, left: 20, right: 20,
    fontSize: 11.5, lineHeight: 1.5, color,
    fontFamily: T.body,
  }}>{children}</div>
);


/* ════════════════════════════════════════════════════════════
   #1 — Signal meter overflow (118%, 111%)
   The bar is being filled from a value that's not clamped, AND the
   stage between green/amber/red is hard-coded to 60/85% of an
   already-overflowing scale. Fix: clamp 0–100, add a separate
   "headroom" indicator that lights up red only when overdriven.
   ════════════════════════════════════════════════════════════ */

const SignalBar = ({ pct, bug = false, w = 320 }) => {
  // 20 segments.
  const segs = [];
  for (let i = 0; i < 20; i++) {
    const filled = pct >= (i + 1) * 5;
    let color = T.dim;
    if (filled) {
      if (i < 12) color = T.green;
      else if (i < 17) color = T.amber;
      else color = T.red;
    }
    segs.push(
      <div key={i} style={{
        flex: 1, height: '100%', borderRadius: 1,
        background: color,
        boxShadow: filled ? `0 0 4px ${color}55, inset 0 1px 0 rgba(255,255,255,.18)` : 'none',
      }} />
    );
  }
  return (
    <div style={{ width: w }}>
      <div style={{
        display: 'flex', justifyContent: 'space-between',
        fontFamily: T.mono, fontSize: 9.5, letterSpacing: '0.16em',
        color: T.lo, marginBottom: 6, textTransform: 'uppercase',
      }}>
        <span>Signal</span>
        <span style={{ color: bug ? T.red : T.md }}>{pct}%{bug ? ' ⚠' : ''}</span>
      </div>
      <div style={{
        display: 'flex', gap: 2, height: 14, padding: '3px 4px',
        background: T.well, borderRadius: 3, border: `1px solid ${T.hair}`,
        boxShadow: 'inset 0 1px 4px rgba(0,0,0,0.7)',
      }}>{segs}</div>
    </div>
  );
};

// "After" version: clamped + separate clip indicator + dB label.
const SignalBarAfter = ({ dbu, clip = false, w = 320 }) => {
  // 0 dBu = full scale. Show 20 segs from -60 to 0 dBu.
  // pct = (dbu + 60) / 60 * 100, clamped.
  const pct = Math.max(0, Math.min(100, ((dbu + 60) / 60) * 100));
  const segs = [];
  for (let i = 0; i < 20; i++) {
    const filled = pct >= (i + 1) * 5;
    let color = T.dim;
    if (filled) {
      if (i < 12) color = T.green;
      else if (i < 17) color = T.amber;
      else color = T.red;
    }
    segs.push(
      <div key={i} style={{
        flex: 1, height: '100%', borderRadius: 1,
        background: color,
        boxShadow: filled ? `0 0 4px ${color}55, inset 0 1px 0 rgba(255,255,255,.18)` : 'none',
      }} />
    );
  }
  return (
    <div style={{ width: w }}>
      <div style={{
        display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
        fontFamily: T.mono, fontSize: 9.5, letterSpacing: '0.16em',
        color: T.lo, marginBottom: 6, textTransform: 'uppercase',
      }}>
        <span>RSSI</span>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <span style={{
            fontSize: 9, padding: '1px 6px', borderRadius: 2,
            color: clip ? T.red : T.dim,
            border: `1px solid ${clip ? T.red : T.hair}`,
            background: clip ? 'rgba(248,113,113,0.10)' : 'transparent',
            boxShadow: clip ? `0 0 6px rgba(248,113,113,0.4)` : 'none',
            letterSpacing: '0.18em',
          }}>CLIP</span>
          <span style={{ color: T.hi, fontVariantNumeric: 'tabular-nums' }}>{dbu > 0 ? '+' : ''}{dbu} dBu</span>
        </div>
      </div>
      <div style={{
        display: 'flex', gap: 2, height: 14, padding: '3px 4px',
        background: T.well, borderRadius: 3, border: `1px solid ${T.hair}`,
        boxShadow: 'inset 0 1px 4px rgba(0,0,0,0.7)',
      }}>{segs}</div>
      <div style={{
        display: 'flex', justifyContent: 'space-between',
        fontFamily: T.mono, fontSize: 8.5, color: T.dim, marginTop: 3,
        letterSpacing: '0.10em',
      }}>
        <span>−60</span><span>−30</span><span>−12</span><span>0 dBu</span>
      </div>
    </div>
  );
};

const Mock_SignalMeter = () => (
  <Stage w={1280} height={460}>
    <Split>
      <Half kicker="Before" kickerColor={T.red} title="Signal can read 118 % — bar fills past full and re-uses the green band">
        <div style={{ padding: '36px 40px', display: 'flex', flexDirection: 'column', gap: 30 }}>
          <SignalBar pct={65} w={520} />
          <SignalBar pct={111} bug w={520} />
          <SignalBar pct={118} bug w={520} />
        </div>
        <Caption color={T.md}>
          The percentage is a raw RTL-SDR power value with no normalisation. When the antenna gain is high or AGC over-corrects, it overshoots 100 %. The bar visually saturates at 20 segments and then the number contradicts the bar.
        </Caption>
      </Half>
      <Half kicker="After" kickerColor={T.accent} title="Clamp to a calibrated dBu scale and surface clipping as its own state">
        <div style={{ padding: '36px 40px', display: 'flex', flexDirection: 'column', gap: 30 }}>
          <SignalBarAfter dbu={-22} w={520} />
          <SignalBarAfter dbu={-3} w={520} />
          <SignalBarAfter dbu={0} clip w={520} />
        </div>
        <Caption color={T.md}>
          Map the raw value to <span style={{ color: T.hi }}>dBu (−60 → 0)</span>, scale labels appear under the bar, and the <span style={{ color: T.red }}>CLIP</span> pill lights up only when the front-end is actually overdriving. The bar can never exceed 100 % full.
        </Caption>
      </Half>
    </Split>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   #2 — Tuner header + RDS info
   The current "TUNER" caption is 9 px and tucked in the corner; the
   RDS station name appears as a tiny "Don · CLASSIC ROCK" between
   the freq display and the meter. Promote both.
   ════════════════════════════════════════════════════════════ */

const BandPill = ({ label, active }) => (
  <div style={{
    padding: '0 14px', height: 32, display: 'flex', alignItems: 'center',
    fontFamily: T.mono, fontSize: 11, fontWeight: 700, letterSpacing: '0.10em',
    color: active ? T.amber : T.lo,
    background: active
      ? 'linear-gradient(180deg,#1a1508 0%,#14100a 100%)'
      : 'linear-gradient(180deg,#161618 0%,#111113 100%)',
    border: `1px solid ${active ? 'rgba(240,168,48,0.25)' : '#1e1e21'}`,
    borderBottom: `2px solid ${active ? 'rgba(240,168,48,0.15)' : '#0a0a0c'}`,
    borderRadius: 3, textShadow: active ? `0 0 8px ${T.amberGlow}` : 'none',
  }}>{label}</div>
);

const BandPillTall = ({ label, sub, active }) => (
  <div style={{
    padding: '8px 14px 6px', minWidth: 64,
    display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 1,
    fontFamily: T.mono,
    color: active ? T.amber : T.md,
    background: active
      ? 'linear-gradient(180deg,#1a1508 0%,#14100a 100%)'
      : 'linear-gradient(180deg,#161618 0%,#111113 100%)',
    border: `1px solid ${active ? 'rgba(240,168,48,0.30)' : '#1e1e21'}`,
    borderBottom: `2px solid ${active ? 'rgba(240,168,48,0.20)' : '#0a0a0c'}`,
    borderRadius: 4,
    boxShadow: active ? `0 0 12px ${T.amberDim}, inset 0 1px 0 rgba(240,168,48,0.08)` : 'none',
  }}>
    <div style={{
      fontSize: 13, fontWeight: 700, letterSpacing: '0.08em',
      textShadow: active ? `0 0 8px ${T.amberGlow}` : 'none',
    }}>{label}</div>
    <div style={{ fontSize: 8, letterSpacing: '0.14em', color: active ? T.amber : T.dim, opacity: 0.7 }}>{sub}</div>
  </div>
);

const FreqDisplayBefore = () => (
  <div style={{
    padding: '14px 32px 10px', background: T.well, borderRadius: 6,
    border: `1px solid #0e0e10`, width: 400, textAlign: 'center',
    boxShadow: 'inset 0 2px 10px rgba(0,0,0,0.9)',
  }}>
    <div style={{
      fontFamily: T.led, fontSize: 56, color: T.amber, letterSpacing: '0.02em',
      textShadow: `0 0 14px ${T.amberGlow}`,
    }}>92.30<span style={{ fontSize: '0.55em', marginLeft: 6, opacity: 0.85 }}>MHz</span></div>
    <div style={{
      display: 'flex', justifyContent: 'space-between', marginTop: 8,
      fontFamily: T.mono, fontSize: 9.5, color: T.lo, letterSpacing: '0.14em',
    }}>
      <span>STEP 100 kHz</span>
      <span style={{
        color: T.accent, padding: '1px 8px',
        border: `1px solid ${T.accentGlow}`, borderRadius: 2, fontSize: 8.5,
        background: T.accentDim,
      }}>STEREO</span>
    </div>
  </div>
);

const FreqDisplayAfter = () => (
  <div style={{ width: 460 }}>
    {/* Station card above frequency */}
    <div style={{
      display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10,
      padding: '6px 14px', background: '#0c0c0e', borderRadius: 6,
      border: `1px solid ${T.hair}`,
    }}>
      <div style={{
        fontFamily: T.mono, fontSize: 10, letterSpacing: '0.14em',
        color: T.dim, fontWeight: 600,
      }}>RDS</div>
      <div style={{
        fontFamily: T.mono, fontSize: 14, fontWeight: 700,
        color: T.accent, letterSpacing: '0.18em',
        textShadow: `0 0 10px ${T.accentGlow}`, flex: 1,
      }}>WRAL-FM</div>
      <div style={{
        fontFamily: T.mono, fontSize: 9.5, color: T.md, letterSpacing: '0.12em',
        padding: '2px 8px', borderRadius: 2,
        border: `1px solid ${T.hair}`, background: 'rgba(255,255,255,0.03)',
        textTransform: 'uppercase',
      }}>Classic Rock</div>
    </div>
    {/* Frequency well */}
    <div style={{
      padding: '14px 32px 10px', background: T.well, borderRadius: 6,
      border: `1px solid #0e0e10`, textAlign: 'center',
      boxShadow: 'inset 0 2px 10px rgba(0,0,0,0.9)',
    }}>
      <div style={{
        fontFamily: T.led, fontSize: 60, color: T.amber, letterSpacing: '0.02em',
        textShadow: `0 0 14px ${T.amberGlow}`,
      }}>92.30<span style={{ fontSize: '0.55em', marginLeft: 6, opacity: 0.85 }}>MHz</span></div>
      <div style={{
        display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 8,
        fontFamily: T.mono, fontSize: 9.5, color: T.lo, letterSpacing: '0.14em',
      }}>
        <span>STEP 100 kHz · 76–108 MHz</span>
        <span style={{
          color: T.accent, padding: '1px 8px',
          border: `1px solid ${T.accentGlow}`, borderRadius: 2, fontSize: 8.5,
          background: T.accentDim,
        }}>STEREO</span>
      </div>
    </div>
    {/* RDS RadioText scroller */}
    <div style={{
      marginTop: 8, padding: '6px 14px', background: '#08080a', borderRadius: 4,
      border: `1px solid #1a1a1c`, display: 'flex', gap: 10, alignItems: 'center',
      fontFamily: T.mono, fontSize: 11, color: T.md, letterSpacing: '0.04em',
    }}>
      <span style={{ color: T.dim, fontSize: 9, letterSpacing: '0.18em' }}>RT</span>
      <span style={{ flex: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
        Don Henley — Dirty Laundry  ·  Up next: Boston — More Than a Feeling
      </span>
    </div>
  </div>
);

const Mock_TunerHeader = () => (
  <Stage w={1280} h={560}>
    <Split>
      <Half kicker="Before" kickerColor={T.red} title="Tuner label is 9 px, band buttons are uniform, RDS shrinks to two whispered words">
        <div style={{ padding: '32px 32px 0', position: 'absolute', inset: 0 }}>
          <div style={{
            fontFamily: T.mono, fontSize: 9, fontWeight: 700, letterSpacing: '0.22em',
            color: T.lo, opacity: 0.45, position: 'absolute', top: 12, left: 22,
          }}>TUNER</div>
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 14, marginTop: 24 }}>
            <div style={{
              display: 'flex', gap: 2, padding: 3, background: T.inset, borderRadius: 5,
              border: `1px solid #1a1a1d`,
              boxShadow: 'inset 0 1px 4px rgba(0,0,0,0.6)',
            }}>
              <BandPill label="AM" />
              <BandPill label="FM" active />
              <BandPill label="SW" />
              <BandPill label="AIR" />
              <BandPill label="WB" />
              <BandPill label="VHF" />
            </div>
            <FreqDisplayBefore />
            <div style={{
              fontFamily: T.mono, fontSize: 11, color: T.accent, letterSpacing: '0.20em',
              marginTop: 4,
            }}>Don<span style={{ color: T.dim, margin: '0 8px' }}>·</span><span style={{
              color: T.md, fontSize: 9.5, padding: '2px 8px', border: `1px solid ${T.hair}`,
              borderRadius: 2, background: 'rgba(255,255,255,0.03)',
            }}>CLASSIC ROCK</span></div>
          </div>
        </div>
      </Half>
      <Half kicker="After" kickerColor={T.accent} title="Promote the band strip to tall pills with band-purpose subtitle, lift the RDS card above the freq display">
        <div style={{ padding: '24px 32px 0', position: 'absolute', inset: 0 }}>
          {/* Real header row */}
          <div style={{
            display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
            padding: '0 0 16px',
            borderBottom: `1px solid ${T.sep}`, marginBottom: 24,
          }}>
            <div style={{
              fontFamily: T.body, fontSize: 14, fontWeight: 600, letterSpacing: '0.04em',
              color: T.hi,
            }}>Tuner</div>
            <div style={{
              fontFamily: T.mono, fontSize: 10, letterSpacing: '0.16em',
              color: T.amber, textTransform: 'uppercase',
            }}>FM · 76–108 MHz</div>
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 16 }}>
            <div style={{
              display: 'flex', gap: 3, padding: 3, background: T.inset, borderRadius: 6,
              border: `1px solid #1a1a1d`,
              boxShadow: 'inset 0 1px 4px rgba(0,0,0,0.6)',
            }}>
              <BandPillTall label="AM" sub="535–1700 kHz" />
              <BandPillTall label="FM" sub="76–108 MHz" active />
              <BandPillTall label="SW" sub="1.7–30 MHz" />
              <BandPillTall label="AIR" sub="118–137 MHz" />
              <BandPillTall label="WB" sub="WX 1–7" />
              <BandPillTall label="VHF" sub="136–174 MHz" />
            </div>
            <FreqDisplayAfter />
          </div>
        </div>
      </Half>
    </Split>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   #3 — AGC / gain control strip
   The current strip has a switch on the left and an EMPTY slot on the
   right when AGC is on. With AGC off, the slider crowds in. Replace
   with a single full-width strip that always shows useful info.
   ════════════════════════════════════════════════════════════ */

const AgcStripBefore = ({ agc }) => (
  <div style={{
    display: 'flex', alignItems: 'center', gap: 12, width: 460,
    padding: '6px 14px', background: T.inset, borderRadius: 4,
    border: `1px solid #141416`, boxShadow: 'inset 0 1px 3px rgba(0,0,0,0.4)',
  }}>
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      <div style={{
        width: 32, height: 18, borderRadius: 9, padding: 2,
        background: agc ? 'rgba(167,139,250,0.40)' : '#222',
        display: 'flex', alignItems: 'center',
      }}>
        <div style={{
          width: 14, height: 14, borderRadius: '50%',
          background: agc ? T.purple : '#555',
          transform: agc ? 'translateX(14px)' : 'translateX(0)',
          transition: 'transform 150ms',
          boxShadow: '0 1px 2px rgba(0,0,0,0.5)',
        }} />
      </div>
      <span style={{
        fontFamily: T.mono, fontSize: 11, fontWeight: 700,
        letterSpacing: '0.12em', color: T.amber,
      }}>AGC</span>
    </div>
    <div style={{
      flex: 1, minHeight: 18, // empty slot
    }}>
      {!agc && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <span style={{ fontFamily: T.mono, fontSize: 11, color: T.amber }}>GAIN 28 dB</span>
          <div style={{ flex: 1, height: 4, borderRadius: 2, background: '#222', position: 'relative' }}>
            <div style={{ position: 'absolute', inset: '0 44% 0 0', borderRadius: 2, background: T.purple }} />
            <div style={{
              position: 'absolute', top: -4, left: '56%', width: 12, height: 12, borderRadius: '50%',
              background: T.purple, transform: 'translateX(-50%)',
              boxShadow: '0 1px 3px rgba(0,0,0,0.5)',
            }} />
          </div>
        </div>
      )}
    </div>
  </div>
);

const AgcStripAfter = ({ agc }) => (
  <div style={{
    display: 'flex', alignItems: 'stretch', width: 460,
    background: T.inset, borderRadius: 4,
    border: `1px solid #141416`, boxShadow: 'inset 0 1px 3px rgba(0,0,0,0.4)',
    overflow: 'hidden',
  }}>
    {/* Left cell — AGC toggle */}
    <button style={{
      all: 'unset',
      padding: '8px 14px',
      display: 'flex', alignItems: 'center', gap: 8,
      cursor: 'pointer', borderRight: `1px solid ${T.hair}`,
      background: agc ? 'rgba(240,168,48,0.05)' : 'transparent',
    }}>
      <span style={{
        fontFamily: T.mono, fontSize: 11, fontWeight: 700,
        letterSpacing: '0.14em', color: agc ? T.amber : T.lo,
      }}>AGC</span>
      <span style={{
        fontFamily: T.mono, fontSize: 9, padding: '1px 6px', borderRadius: 2,
        letterSpacing: '0.18em',
        color: agc ? T.green : T.dim,
        border: `1px solid ${agc ? 'rgba(74,222,128,0.30)' : T.hair}`,
        background: agc ? 'rgba(74,222,128,0.06)' : 'transparent',
      }}>{agc ? 'AUTO' : 'OFF'}</span>
    </button>
    {/* Right cell — always-useful content */}
    <div style={{ flex: 1, padding: '8px 14px', display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
      {agc ? (
        <>
          <span style={{
            fontFamily: T.mono, fontSize: 11, color: T.md, letterSpacing: '0.04em',
          }}>Tuner is choosing</span>
          <span style={{
            flex: 1, textAlign: 'right',
            fontFamily: T.mono, fontSize: 11, color: T.amber, letterSpacing: '0.06em',
          }}>
            <span style={{ color: T.dim, marginRight: 6 }}>now</span>28.0 dB
          </span>
        </>
      ) : (
        <>
          <span style={{ fontFamily: T.mono, fontSize: 11, color: T.amber, fontWeight: 600 }}>28 dB</span>
          <div style={{ flex: 1, height: 4, borderRadius: 2, background: '#222', position: 'relative' }}>
            <div style={{ position: 'absolute', inset: '0 44% 0 0', borderRadius: 2, background: T.amber }} />
            <div style={{
              position: 'absolute', top: -4, left: '56%', width: 12, height: 12, borderRadius: '50%',
              background: T.amber, transform: 'translateX(-50%)',
              boxShadow: '0 1px 3px rgba(0,0,0,0.6)',
            }} />
          </div>
          <span style={{ fontFamily: T.mono, fontSize: 9, color: T.dim, letterSpacing: '0.14em' }}>0 – 50</span>
        </>
      )}
    </div>
  </div>
);

const Mock_AgcStrip = () => (
  <Stage w={1280} h={520}>
    <Split>
      <Half kicker="Before" kickerColor={T.red} title="The right half of the strip is empty when AGC is on">
        <div style={{ padding: '36px 40px', display: 'flex', flexDirection: 'column', gap: 20 }}>
          <div>
            <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, marginBottom: 8, letterSpacing: '0.16em' }}>AGC ON · empty slot</div>
            <AgcStripBefore agc />
          </div>
          <div>
            <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, marginBottom: 8, letterSpacing: '0.16em' }}>AGC OFF · slider appears</div>
            <AgcStripBefore agc={false} />
          </div>
        </div>
        <Caption>
          The strip is a two-column flex with one column doing real work and one column blank. The slider appears/disappears, which means the strip changes height between states.
        </Caption>
      </Half>
      <Half kicker="After" kickerColor={T.accent} title="Two cells, always full. AGC cell shows AUTO/OFF; gain cell shows the chosen-or-manual value">
        <div style={{ padding: '36px 40px', display: 'flex', flexDirection: 'column', gap: 20 }}>
          <div>
            <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, marginBottom: 8, letterSpacing: '0.16em' }}>AGC ON · shows what the tuner chose</div>
            <AgcStripAfter agc />
          </div>
          <div>
            <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, marginBottom: 8, letterSpacing: '0.16em' }}>AGC OFF · slider with range labels</div>
            <AgcStripAfter agc={false} />
          </div>
        </div>
        <Caption>
          Same height in both states. When AGC is on the right cell reports the gain the tuner currently selected — turning AGC off is no longer a leap of faith.
        </Caption>
      </Half>
    </Split>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   #4 — Memory presets
   Current: vertical list, each row is "WB - 162.55 MHz" with a redundant
   "162.55 MHz WB" right beneath. Slot number is a 9 px ghost in the corner.
   ════════════════════════════════════════════════════════════ */

const PresetBefore = ({ name, freq, band, slot, active }) => (
  <div style={{
    position: 'relative', padding: '7px 10px', borderRadius: 3,
    border: `1px solid ${active ? 'rgba(240,168,48,0.30)' : 'transparent'}`,
    background: active ? 'rgba(240,168,48,0.06)' : 'transparent',
    marginBottom: 2,
  }}>
    <div style={{ fontSize: 12, fontWeight: 600, color: T.hi, paddingRight: 24 }}>{name}</div>
    <div style={{ fontFamily: T.mono, fontSize: 11, color: T.amber, marginTop: 1 }}>
      {freq} <span style={{ color: T.lo, fontSize: 9, letterSpacing: '0.10em', marginLeft: 4 }}>{band}</span>
    </div>
    <div style={{
      position: 'absolute', right: 8, bottom: 5,
      fontFamily: T.mono, fontSize: 9, color: T.lo, opacity: 0.20,
    }}>{String(slot).padStart(2, '0')}</div>
  </div>
);

const PresetAfter = ({ name, freq, band, slot, active, empty }) => empty ? (
  <div style={{
    display: 'grid', gridTemplateColumns: '22px 1fr 64px', gap: 10, alignItems: 'center',
    padding: '6px 8px', borderRadius: 3,
    border: `1px dashed ${T.hair}`,
    opacity: 0.5,
    background: 'transparent', marginBottom: 2,
  }}>
    <div style={{
      fontFamily: T.mono, fontSize: 10, color: T.dim, textAlign: 'right',
      letterSpacing: '0.05em',
    }}>{String(slot).padStart(2, '0')}</div>
    <div style={{ fontFamily: T.mono, fontSize: 10, color: T.dim, letterSpacing: '0.12em' }}>EMPTY · long-press to save</div>
    <div />
  </div>
) : (
  <div style={{
    display: 'grid', gridTemplateColumns: '22px 1fr 64px', gap: 10, alignItems: 'center',
    padding: '6px 8px', borderRadius: 3,
    border: `1px solid ${active ? 'rgba(240,168,48,0.30)' : 'transparent'}`,
    background: active ? 'rgba(240,168,48,0.06)' : 'transparent',
    marginBottom: 2,
  }}>
    <div style={{
      fontFamily: T.mono, fontSize: 10,
      color: active ? T.amber : T.lo, textAlign: 'right',
      letterSpacing: '0.05em',
    }}>{String(slot).padStart(2, '0')}</div>
    <div style={{ minWidth: 0 }}>
      <div style={{
        fontSize: 12, fontWeight: 500, color: T.hi,
        whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
      }}>{name}</div>
      <div style={{
        fontFamily: T.mono, fontSize: 9, color: T.lo, letterSpacing: '0.10em', marginTop: 1,
      }}>{band}</div>
    </div>
    <div style={{
      fontFamily: T.led, fontSize: 13, color: active ? T.amber : T.md,
      textAlign: 'right', letterSpacing: '0.02em',
      textShadow: active ? `0 0 6px ${T.amberGlow}` : 'none',
    }}>{freq}</div>
  </div>
);

const Mock_Presets = () => (
  <Stage w={1100} h={620}>
    <Split>
      <Half kicker="Before" kickerColor={T.red} title="Name is the frequency, frequency is also the frequency, slot number is invisible">
        <div style={{ padding: '20px 24px 0' }}>
          <div style={{
            display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
            paddingBottom: 12, borderBottom: `1px solid ${T.sep}`, marginBottom: 12,
          }}>
            <div style={{
              fontFamily: T.mono, fontSize: 9, fontWeight: 700, letterSpacing: '0.18em',
              color: T.lo,
            }}>MEMORY</div>
            <span style={{
              width: 18, height: 18, borderRadius: 3, background: T.amberDim,
              border: `1px solid ${T.amberSoft}`,
              display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
              fontFamily: T.mono, fontSize: 10, color: T.amber,
            }}>＋</span>
          </div>
          <PresetBefore slot={1} name="WB - 162.55 MHz"      freq="162.55 MHz" band="WB" />
          <PresetBefore slot={2} name="FM - 105.10 MHz"      freq="105.10 MHz" band="FM" />
          <PresetBefore slot={3} name="FM - 106.90 MHz"      freq="106.90 MHz" band="FM" />
          <PresetBefore slot={4} name="FM - 91.50 MHz"       freq="91.50 MHz"  band="FM" />
          <PresetBefore slot={5} name="FM WFJA 105.50 MHz"   freq="105.50 MHz" band="FM" />
          <PresetBefore slot={6} name="FM WSMW 97.75 MHz"    freq="97.75 MHz"  band="FM" />
          <PresetBefore slot={7} name="FM Rock 92 92.30 MHz" freq="92.30 MHz"  band="FM" active />
        </div>
        <Caption>
          The "Save current" prompt seeds the name with the same string the row already shows below. Eight rows in, the column has been three different formats of the same MHz number.
        </Caption>
      </Half>
      <Half kicker="After" kickerColor={T.accent} title="Slot # on the left, custom name in the middle, big LED frequency on the right">
        <div style={{ padding: '20px 24px 0' }}>
          <div style={{
            display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
            paddingBottom: 12, borderBottom: `1px solid ${T.sep}`, marginBottom: 12,
          }}>
            <div style={{
              fontFamily: T.mono, fontSize: 9, fontWeight: 700, letterSpacing: '0.18em',
              color: T.lo,
            }}>MEMORY <span style={{ color: T.dim, marginLeft: 4 }}>· 7 of 16</span></div>
            <div style={{
              fontFamily: T.mono, fontSize: 9, letterSpacing: '0.14em', color: T.lo,
            }}>HOLD <kbd style={{
              fontFamily: T.mono, fontSize: 9, padding: '0 4px', borderRadius: 2,
              border: `1px solid ${T.hair}`, background: '#0c0c0e', color: T.md,
            }}>FM</kbd> TO SAVE</div>
          </div>
          <PresetAfter slot={1} name="NOAA Raleigh"  freq="162.55"  band="WB" />
          <PresetAfter slot={2} name="The River"     freq="105.10"  band="FM" />
          <PresetAfter slot={3} name="WCMC"          freq="106.90"  band="FM" />
          <PresetAfter slot={4} name="WUNC"          freq="91.50"   band="FM" />
          <PresetAfter slot={5} name="WFJA"          freq="105.50"  band="FM" />
          <PresetAfter slot={6} name="WSMW"          freq="97.75"   band="FM" />
          <PresetAfter slot={7} name="Rock 92"       freq="92.30"   band="FM" active />
          <PresetAfter slot={8} empty />
          <PresetAfter slot={9} empty />
        </div>
        <Caption>
          Saving from the tuner offers the station's RDS name as the default — never the frequency. The first empty slot is always visible so capacity is legible at a glance.
        </Caption>
      </Half>
    </Split>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   #5 — Song recognition panel
   Current: ASCII-ish table with Src / Match / # / Conf% / Track / Art
   columns. Conf% is always "80%". Art is a ✓ / ✗. No "now" marker.
   ════════════════════════════════════════════════════════════ */

const SongRowBefore = ({ src, match, idx, conf, track, art, dim }) => (
  <tr style={{ opacity: dim ? 0.45 : 1 }}>
    <td style={{ padding: '5px 8px', color: T.lo, fontSize: 13, fontFamily: T.mono }}>
      {src === 'mic' ? '🎙' : '📁'}
    </td>
    <td style={{ padding: '5px 8px', textAlign: 'center', fontFamily: T.mono, fontSize: 11, color: match ? T.green : T.red }}>{match ? '✓' : '✗'}</td>
    <td style={{ padding: '5px 8px', fontFamily: T.mono, fontSize: 11, color: T.md, textAlign: 'right' }}>{idx}</td>
    <td style={{ padding: '5px 8px', fontFamily: T.mono, fontSize: 11, color: T.md }}>{conf ? `${conf}%` : '—'}</td>
    <td style={{
      padding: '5px 8px', fontFamily: T.mono, fontSize: 11, color: T.md,
      maxWidth: 280, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
    }}>{track || '—'}</td>
    <td style={{ padding: '5px 8px', textAlign: 'center', fontFamily: T.mono, fontSize: 11, color: art ? T.green : T.lo }}>{art ? '✓' : ''}</td>
  </tr>
);

const ConfPip = ({ level }) => {
  // level: 0/1/2/3
  const colors = ['#22251c', '#5C7C3A', '#7FAB46', T.green];
  return (
    <div style={{ display: 'inline-flex', gap: 2 }}>
      {[0, 1, 2].map(i => (
        <div key={i} style={{
          width: 5, height: 10, borderRadius: 1,
          background: i < level ? colors[level] : '#1c1f1a',
        }} />
      ))}
    </div>
  );
};

const SongRowAfter = ({ time, conf, track, artist, art, source, active, ago }) => {
  const confLabel = conf === 3 ? 'Strong' : conf === 2 ? 'Likely' : conf === 1 ? 'Possible' : 'No match';
  const confColor = conf === 3 ? T.green : conf === 2 ? '#86b96b' : conf === 1 ? T.amber : T.lo;
  return (
    <div style={{
      display: 'grid', gridTemplateColumns: '34px 1fr 80px 60px', gap: 12, alignItems: 'center',
      padding: '6px 10px', borderRadius: 4,
      borderLeft: active ? `2px solid ${T.amber}` : '2px solid transparent',
      background: active ? 'rgba(240,168,48,0.05)' : 'transparent',
      marginBottom: 1,
    }}>
      {/* Art / fallback */}
      <div style={{
        width: 34, height: 34, borderRadius: 3,
        background: art ? `linear-gradient(135deg, #2a1e14, #4a3520)` : '#1a1a1d',
        border: `1px solid ${T.hair}`,
        position: 'relative',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        color: T.dim, fontFamily: T.mono, fontSize: 9,
      }}>
        {!art && '♪'}
        {active && (
          <span style={{
            position: 'absolute', top: -3, right: -3, width: 8, height: 8, borderRadius: '50%',
            background: T.amber, boxShadow: `0 0 6px ${T.amber}`,
          }} />
        )}
      </div>
      {/* Track */}
      <div style={{ minWidth: 0 }}>
        <div style={{
          fontSize: 12.5, color: active ? T.hi : T.md, fontWeight: active ? 600 : 500,
          whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
        }}>{track || <span style={{ color: T.lo, fontStyle: 'italic' }}>No match in window</span>}</div>
        <div style={{
          fontFamily: T.mono, fontSize: 10, color: T.lo, letterSpacing: '0.04em',
          whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
        }}>{artist || `source · ${source}`}</div>
      </div>
      {/* Confidence */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 6,
      }}>
        <ConfPip level={conf} />
        <span style={{
          fontFamily: T.mono, fontSize: 9.5, color: confColor, letterSpacing: '0.06em',
        }}>{confLabel}</span>
      </div>
      {/* Time */}
      <div style={{
        fontFamily: T.mono, fontSize: 10, color: T.lo, textAlign: 'right', letterSpacing: '0.02em',
      }}>{active ? 'now' : ago}</div>
    </div>
  );
};

const Mock_SongRecognition = () => (
  <Stage w={1480} h={720}>
    <Split>
      <Half kicker="Before" kickerColor={T.red} title="ASCII table. Six columns of which two are checkboxes and one is always 80 %">
        <div style={{ padding: '14px 18px 0' }}>
          <div style={{
            display: 'flex', alignItems: 'center', gap: 12,
            fontFamily: T.mono, fontSize: 10, color: T.lo,
            marginBottom: 8, letterSpacing: '0.06em',
          }}>
            <span style={{
              padding: '2px 8px', borderRadius: 12, fontSize: 9,
              background: 'rgba(74,222,128,0.10)', color: T.green,
              border: `1px solid rgba(74,222,128,0.30)`,
            }}>● Searching</span>
            <span>Fingerprints: 4.1/min</span>
            <span>Lookups: 4.1/min</span>
          </div>
          <table style={{ borderCollapse: 'collapse', width: '100%' }}>
            <thead>
              <tr style={{ borderBottom: `1px solid ${T.sep}` }}>
                {['Src', 'Match', '#', 'Conf%', 'Track', 'Art'].map(h => (
                  <th key={h} style={{
                    padding: '6px 8px', textAlign: 'left',
                    fontFamily: T.mono, fontSize: 9.5, fontWeight: 600,
                    letterSpacing: '0.14em', color: T.lo, textTransform: 'uppercase',
                  }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              <SongRowBefore src="file" match={false} idx={0} conf={null} track="—"  art={false} dim />
              <SongRowBefore src="mic"  match={true}  idx={1} conf={80}   track="Dirty Laundry / Don Henley / I Can't S…" art />
              <SongRowBefore src="file" match={true}  idx={7} conf={80}   track="Dirty Laundry / Don Henley / I Can't S…" art />
              <SongRowBefore src="file" match={true}  idx={13} conf={80}  track="More Than a Feeling / Boston / Boston"   art />
              <SongRowBefore src="file" match={true}  idx={9}  conf={80}  track="It's Raining Men / The Weather Girls /…" art />
              <SongRowBefore src="file" match={true}  idx={13} conf={80}  track="Take Me Home Tonight / Eddie Money / C…" art />
              <SongRowBefore src="file" match={true}  idx={24} conf={80}  track="American Pie / Don Mclean / American P…" art />
              <SongRowBefore src="file" match={true}  idx={15} conf={80}  track="I Can't Dance / Genesis / We Can't Dan…" art />
              <SongRowBefore src="file" match={false} idx={18} conf={null} track="—" art={false} dim />
              <SongRowBefore src="file" match={true}  idx={1}  conf={80}  track="Centerfield / John Fogerty / Centerfie…" art />
              <SongRowBefore src="file" match={true}  idx={1}  conf={80}  track="Higher Love / Steve Winwood / Back In …" art />
            </tbody>
          </table>
        </div>
        <Caption>
          The user can't tell what's playing right now, can't tell which row is the live mic match versus a historical file lookup, and reads "80 %" eleven times in a column whose only signal is "yes, the fingerprinter found something."
        </Caption>
      </Half>
      <Half kicker="After" kickerColor={T.accent} title="Stream view. Currently-playing match is the anchor; older matches recede; confidence is encoded with bars, not numbers">
        <div style={{ padding: '14px 18px 0' }}>
          <div style={{
            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            marginBottom: 12, paddingBottom: 8, borderBottom: `1px solid ${T.sep}`,
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{
                width: 6, height: 6, borderRadius: '50%', background: T.green,
                boxShadow: `0 0 6px ${T.green}`,
              }} />
              <span style={{
                fontFamily: T.mono, fontSize: 10.5, letterSpacing: '0.14em',
                color: T.md, textTransform: 'uppercase',
              }}>Listening · 4 / min</span>
            </div>
            <div style={{
              fontFamily: T.mono, fontSize: 9.5, color: T.lo, letterSpacing: '0.10em',
            }}>SOURCE: SDR + LIBRARY</div>
          </div>
          <div style={{
            fontFamily: T.mono, fontSize: 9, letterSpacing: '0.20em',
            color: T.amber, marginBottom: 6,
          }}>NOW</div>
          <SongRowAfter
            track="Dirty Laundry" artist="Don Henley · I Can't Stand Still"
            conf={3} art ago="now" source="mic" active />

          <div style={{
            fontFamily: T.mono, fontSize: 9, letterSpacing: '0.20em',
            color: T.dim, margin: '12px 0 6px',
          }}>EARLIER</div>
          <SongRowAfter time="08:18" track="More Than a Feeling" artist="Boston" conf={3} art ago="3 min" source="file" />
          <SongRowAfter time="08:15" track="It's Raining Men" artist="The Weather Girls" conf={2} art ago="6 min" source="file" />
          <SongRowAfter time="08:12" track="Take Me Home Tonight" artist="Eddie Money" conf={3} art ago="9 min" source="file" />
          <SongRowAfter time="08:08" track="American Pie" artist="Don McLean" conf={2} ago="13 min" source="file" />
          <SongRowAfter time="08:04" track={null} artist={null} conf={0} ago="17 min" source="file" />
          <SongRowAfter time="08:00" track="I Can't Dance" artist="Genesis" conf={1} ago="21 min" source="file" />
          <SongRowAfter time="07:55" track="Higher Love" artist="Steve Winwood" conf={2} art ago="26 min" source="file" />
          <SongRowAfter time="07:48" track="Centerfield" artist="John Fogerty" conf={3} art ago="33 min" source="file" />
        </div>
      </Half>
    </Split>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   #6 — Gain control popover
   The current popover floats above the dB pill and has just a slider
   from -∞ to +6 dB with no feedback. Add a live peak meter, AUTO state,
   and a reset button. Use clean min/max labels.
   ════════════════════════════════════════════════════════════ */

const GainPopBefore = () => (
  <div style={{
    width: 320, padding: '14px 16px 12px',
    background: '#1a1a1c', borderRadius: 8,
    border: `1px solid ${T.sep}`,
    boxShadow: '0 12px 32px rgba(0,0,0,0.6)',
  }}>
    <div style={{
      display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
      marginBottom: 12,
    }}>
      <div style={{ fontSize: 12.5, color: T.hi, fontWeight: 500 }}>SDR Radio (RTL-SDR) Level</div>
      <div style={{ fontFamily: T.mono, fontSize: 11, color: T.lo }}>−∞dB</div>
    </div>
    <div style={{ height: 4, borderRadius: 2, background: '#2a2a2c', position: 'relative', marginBottom: 6 }}>
      <div style={{ position: 'absolute', inset: '0 80% 0 0', borderRadius: 2, background: T.purple }} />
      <div style={{
        position: 'absolute', top: -6, left: '20%', width: 16, height: 16, borderRadius: '50%',
        background: T.purple, transform: 'translateX(-50%)',
      }} />
    </div>
    <div style={{
      display: 'flex', justifyContent: 'space-between',
      fontFamily: T.mono, fontSize: 10, color: T.lo, letterSpacing: '0.08em',
    }}>
      <span>−∞</span><span>0dB</span><span>+6dB</span>
    </div>
  </div>
);

// Vertical peak meter
const PeakMeter = ({ peak, hold }) => {
  // peak as 0..1
  const segs = [];
  for (let i = 19; i >= 0; i--) {
    const filled = peak >= (i + 1) / 20;
    let color = T.dim;
    if (filled) {
      if (i < 12) color = T.green;
      else if (i < 17) color = T.amber;
      else color = T.red;
    }
    segs.push(
      <div key={i} style={{
        width: '100%', height: 4, borderRadius: 1,
        background: color, marginBottom: 1,
        boxShadow: filled ? `0 0 3px ${color}55` : 'none',
        position: 'relative',
      }}>
        {hold === (i + 1) / 20 && (
          <div style={{
            position: 'absolute', inset: 0, border: `1px solid ${T.hi}`, borderRadius: 1,
          }} />
        )}
      </div>
    );
  }
  return (
    <div style={{
      width: 16, height: '100%',
      padding: 3, borderRadius: 3,
      background: T.well, border: `1px solid ${T.hair}`,
      boxShadow: 'inset 0 1px 4px rgba(0,0,0,0.7)',
      display: 'flex', flexDirection: 'column',
    }}>{segs}</div>
  );
};

const GainPopAfter = ({ agc }) => (
  <div style={{
    width: 340, padding: '14px 16px 12px',
    background: '#141416', borderRadius: 8,
    border: `1px solid ${T.sep}`,
    boxShadow: '0 12px 32px rgba(0,0,0,0.6)',
  }}>
    <div style={{
      display: 'flex', justifyContent: 'space-between', alignItems: 'center',
      marginBottom: 14,
    }}>
      <div>
        <div style={{
          fontFamily: T.mono, fontSize: 9, letterSpacing: '0.18em',
          color: T.lo, textTransform: 'uppercase',
        }}>SDR · RTL-SDR</div>
        <div style={{ fontSize: 13, color: T.hi, fontWeight: 500, marginTop: 2 }}>RF gain</div>
      </div>
      <button style={{
        all: 'unset', cursor: 'pointer',
        padding: '4px 10px',
        fontFamily: T.mono, fontSize: 9.5, letterSpacing: '0.14em',
        color: agc ? T.green : T.md, textTransform: 'uppercase',
        borderRadius: 12,
        border: `1px solid ${agc ? 'rgba(74,222,128,0.30)' : T.hair}`,
        background: agc ? 'rgba(74,222,128,0.06)' : 'transparent',
      }}>{agc ? '● Auto' : 'Auto off'}</button>
    </div>

    <div style={{ display: 'flex', gap: 12, height: 124 }}>
      {/* Peak meter */}
      <PeakMeter peak={0.7} hold={0.85} />
      {/* Slider */}
      <div style={{
        flex: 1, display: 'flex', flexDirection: 'column', justifyContent: 'space-between',
        position: 'relative',
      }}>
        <div style={{ position: 'relative', height: '100%', display: 'flex', justifyContent: 'center' }}>
          {/* Track */}
          <div style={{
            width: 4, height: '100%', borderRadius: 2,
            background: '#2a2a2c',
            position: 'relative',
            opacity: agc ? 0.35 : 1,
          }}>
            <div style={{
              position: 'absolute', bottom: 0, left: 0, right: 0, height: '52%',
              background: T.amber, borderRadius: 2,
              boxShadow: `0 0 8px ${T.amberGlow}`,
            }} />
            {/* Tick at 0dB */}
            <div style={{
              position: 'absolute', left: -4, right: -4, top: '60%',
              borderTop: `1px dashed ${T.dim}`,
            }} />
          </div>
          {/* Knob */}
          <div style={{
            position: 'absolute', top: '48%', left: '50%',
            transform: 'translate(-50%,-50%)',
            width: 22, height: 22, borderRadius: '50%',
            background: agc ? '#3a3a3c' : T.amber,
            boxShadow: agc
              ? '0 1px 3px rgba(0,0,0,0.5)'
              : `0 1px 3px rgba(0,0,0,0.5), 0 0 8px ${T.amberGlow}`,
          }} />
        </div>
      </div>
      {/* Scale labels */}
      <div style={{
        width: 40, display: 'flex', flexDirection: 'column', justifyContent: 'space-between',
        fontFamily: T.mono, fontSize: 10, color: T.lo, letterSpacing: '0.06em',
        textAlign: 'right',
      }}>
        <div>+6 dB</div>
        <div style={{ color: T.dim }}>+3</div>
        <div style={{ color: T.amber }}>0 dB</div>
        <div style={{ color: T.dim }}>−12</div>
        <div style={{ color: T.dim }}>−24</div>
        <div>−∞</div>
      </div>
    </div>

    {/* Footer — value + reset */}
    <div style={{
      display: 'flex', justifyContent: 'space-between', alignItems: 'center',
      marginTop: 12, paddingTop: 12, borderTop: `1px solid ${T.sep}`,
    }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 6 }}>
        <span style={{ fontFamily: T.led, fontSize: 22, color: T.amber, textShadow: `0 0 6px ${T.amberGlow}` }}>
          {agc ? '—' : '+0.5'}
        </span>
        <span style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, letterSpacing: '0.10em' }}>dB</span>
      </div>
      <button style={{
        all: 'unset', cursor: 'pointer',
        padding: '4px 10px',
        fontFamily: T.mono, fontSize: 10, letterSpacing: '0.14em',
        color: T.md, textTransform: 'uppercase',
        border: `1px solid ${T.hair}`, borderRadius: 4,
        opacity: agc ? 0.4 : 1,
      }}>Reset to 0 dB</button>
    </div>
  </div>
);

const Mock_GainPopover = () => (
  <Stage w={1280} h={520}>
    <Split>
      <Half kicker="Before" kickerColor={T.red} title="A bare slider with no feedback; the title is the verbose source ID">
        <div style={{
          padding: '40px 40px 0', display: 'flex', justifyContent: 'center',
          alignItems: 'flex-start',
        }}>
          <GainPopBefore />
        </div>
        <Caption>
          User is asked to set an analog gain on a slider that goes to <span style={{ color: T.hi }}>−∞</span> and has no idea whether they're clipping, attenuated, or matched. Adjusting the dB requires guesswork until the next track plays.
        </Caption>
      </Half>
      <Half kicker="After" kickerColor={T.accent} title="Peak meter + AUTO state + reset; the slider stops being a faith-based interface">
        <div style={{
          padding: '32px 40px 0', display: 'flex', justifyContent: 'center', gap: 22,
        }}>
          <GainPopAfter agc={false} />
          <GainPopAfter agc />
        </div>
        <Caption>
          The live peak meter shares the popover so you see the signal you're shaping. <span style={{ color: T.green }}>Auto</span> visibly dims the slider rather than hiding it — you can still see what the tuner is choosing for you.
        </Caption>
      </Half>
    </Split>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   #7 — Now Playing source status pill cluster
   On the radio main page, the "Searching · " pill and the source pill
   ("SDR RADIO (RTL-SDR)") and the dB pill sit on three separate corners
   doing related work. Unify them.
   ════════════════════════════════════════════════════════════ */

const NowPlayingBefore = () => (
  <div style={{
    width: 520, height: 520, position: 'relative',
    background: T.base, padding: 14, boxSizing: 'border-box',
  }}>
    {/* Status pill — top left */}
    <div style={{
      position: 'absolute', top: 18, left: 18,
      fontFamily: T.mono, fontSize: 10, letterSpacing: '0.08em',
      color: T.green, padding: '4px 10px', borderRadius: 12,
      background: 'rgba(74,222,128,0.10)', border: `1px solid rgba(74,222,128,0.30)`,
    }}>● Searching</div>
    {/* Source pill — top right area */}
    <div style={{
      position: 'absolute', top: 18, right: 70,
      fontFamily: T.mono, fontSize: 10, letterSpacing: '0.10em',
      color: T.purple, padding: '4px 12px', borderRadius: 12,
      background: 'rgba(167,139,250,0.10)', border: `1px solid rgba(167,139,250,0.30)`,
    }}>SDR RADIO (RTL-SDR)</div>
    {/* dB pill — top right */}
    <div style={{
      position: 'absolute', top: 18, right: 18,
      fontFamily: T.mono, fontSize: 10, letterSpacing: '0.10em',
      color: T.accent, padding: '4px 10px', borderRadius: 12,
      background: T.accentDim, border: `1px solid ${T.accentGlow}`,
    }}>0dB</div>
    {/* Album art */}
    <div style={{
      position: 'absolute', bottom: 60, left: 60, right: 60, top: 90,
      background: 'linear-gradient(135deg, #4a4338 0%, #2c2620 60%, #1a1612 100%)',
      borderRadius: 4, overflow: 'hidden',
      boxShadow: '0 4px 20px rgba(0,0,0,0.5)',
      display: 'flex', alignItems: 'flex-end', padding: 18,
    }}>
      <div>
        <div style={{ fontSize: 22, fontWeight: 600, color: T.hi }}>Dirty Laundry</div>
        <div style={{ fontSize: 14, color: T.md, marginTop: 2 }}>Don Henley</div>
        <div style={{ fontSize: 11, color: T.lo, marginTop: 1 }}>FM</div>
      </div>
    </div>
  </div>
);

const NowPlayingAfter = () => (
  <div style={{
    width: 520, height: 520, position: 'relative',
    background: T.base, padding: 14, boxSizing: 'border-box',
  }}>
    {/* Single consolidated status strip — bottom of art */}
    <div style={{
      position: 'absolute', top: 18, left: 18, right: 18,
      display: 'flex', alignItems: 'center', gap: 0,
      padding: 0, borderRadius: 6,
      background: 'rgba(20,20,22,0.85)', border: `1px solid ${T.sep}`,
      backdropFilter: 'blur(8px)', overflow: 'hidden',
      fontFamily: T.mono, fontSize: 10, letterSpacing: '0.10em',
    }}>
      <div style={{
        padding: '7px 12px', display: 'flex', alignItems: 'center', gap: 8,
        borderRight: `1px solid ${T.hair}`, color: T.purple,
      }}>
        <span style={{ width: 8, height: 8, borderRadius: 2, background: T.purple }} />
        SDR · RTL-SDR
      </div>
      <div style={{
        padding: '7px 12px', display: 'flex', alignItems: 'center', gap: 6,
        borderRight: `1px solid ${T.hair}`, color: T.amber,
      }}>
        <span style={{ fontFamily: T.led, fontSize: 12, letterSpacing: '0.02em' }}>92.30</span>
        <span style={{ color: T.lo, fontSize: 8 }}>MHz</span>
      </div>
      <div style={{
        flex: 1, padding: '7px 12px', color: T.md,
        whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
      }}>
        <span style={{ color: T.accent, marginRight: 8 }}>WRAL · CLASSIC ROCK</span>
        <span style={{ color: T.dim }}>RDS</span>
      </div>
      <div style={{
        padding: '7px 12px', color: T.accent,
        borderLeft: `1px solid ${T.hair}`,
      }}>0 dB</div>
    </div>

    {/* Album art */}
    <div style={{
      position: 'absolute', bottom: 70, left: 60, right: 60, top: 90,
      background: 'linear-gradient(135deg, #4a4338 0%, #2c2620 60%, #1a1612 100%)',
      borderRadius: 4, overflow: 'hidden',
      boxShadow: '0 4px 20px rgba(0,0,0,0.5)',
      display: 'flex', alignItems: 'flex-end', padding: 18,
    }}>
      <div>
        <div style={{ fontSize: 22, fontWeight: 600, color: T.hi }}>Dirty Laundry</div>
        <div style={{ fontSize: 14, color: T.md, marginTop: 2 }}>Don Henley · I Can't Stand Still</div>
        <div style={{
          fontFamily: T.mono, fontSize: 10, color: T.green, marginTop: 6,
          display: 'flex', alignItems: 'center', gap: 6, letterSpacing: '0.10em',
        }}>
          <ConfPip level={3} /> STRONG MATCH · 12 s ago
        </div>
      </div>
    </div>
  </div>
);

const Mock_NowPlayingStatus = () => (
  <Stage w={1280} h={620}>
    <Split>
      <Half kicker="Before" kickerColor={T.red} title="Three pills in three corners doing the same job">
        <div style={{ padding: '24px 40px 0', display: 'flex', justifyContent: 'center' }}>
          <NowPlayingBefore />
        </div>
        <Caption>
          "Searching" lives top-left. The source label lives top-right. The dB readout is its own pill. Nothing tells the user whether the song name below was recognised, supplied by RDS, or guessed from history.
        </Caption>
      </Half>
      <Half kicker="After" kickerColor={T.accent} title="One status strip across the top, one match badge on the song">
        <div style={{ padding: '24px 40px 0', display: 'flex', justifyContent: 'center' }}>
          <NowPlayingAfter />
        </div>
        <Caption>
          Source, frequency, RDS, and gain are one piece of furniture. The match confidence is attached to the song where you read it, not on a separate pill twelve inches away.
        </Caption>
      </Half>
    </Split>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   #8 — Full Tuner page layout (composed)
   Pulls #2, #3, #4 into a single 1920-wide assembly so reviewers
   can see how the changes interact.
   ════════════════════════════════════════════════════════════ */

const Mock_TunerComposed = () => (
  <Stage w={1920} h={780} pad={0} style={{ background: '#0a0a0c' }}>
    {/* topbar stub */}
    <div style={{
      height: 64, borderBottom: `1px solid ${T.sep}`, padding: '0 24px',
      display: 'flex', alignItems: 'center', gap: 20, background: T.base,
    }}>
      <div style={{
        fontFamily: T.led, fontSize: 20, color: T.amber,
        textShadow: `0 0 6px ${T.amberGlow}`,
      }}>07:23</div>
      <div style={{ flex: 1 }} />
      <div style={{
        fontFamily: T.mono, fontSize: 11, letterSpacing: '0.12em', color: T.md,
      }}>RADIO · TUNER</div>
    </div>

    {/* main row */}
    <div style={{ display: 'grid', gridTemplateColumns: '380px 1fr 320px', height: 'calc(100% - 64px)' }}>
      {/* Left: now playing */}
      <div style={{ borderRight: `1px solid ${T.sep}`, padding: 16, position: 'relative' }}>
        <NowPlayingAfter />
      </div>
      {/* Center: tuner */}
      <div style={{ padding: '24px 32px 0', borderRight: `1px solid ${T.sep}`, display: 'flex', flexDirection: 'column' }}>
        <div style={{
          display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
          padding: '0 0 16px',
          borderBottom: `1px solid ${T.sep}`, marginBottom: 24,
        }}>
          <div style={{ fontSize: 14, fontWeight: 600, color: T.hi }}>Tuner</div>
          <div style={{
            fontFamily: T.mono, fontSize: 10, letterSpacing: '0.16em',
            color: T.amber, textTransform: 'uppercase',
          }}>FM · 76–108 MHz</div>
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 18 }}>
          <div style={{
            display: 'flex', gap: 3, padding: 3, background: T.inset, borderRadius: 6,
            border: `1px solid #1a1a1d`,
            boxShadow: 'inset 0 1px 4px rgba(0,0,0,0.6)',
          }}>
            <BandPillTall label="AM" sub="535–1700 kHz" />
            <BandPillTall label="FM" sub="76–108 MHz" active />
            <BandPillTall label="SW" sub="1.7–30 MHz" />
            <BandPillTall label="AIR" sub="118–137 MHz" />
            <BandPillTall label="WB" sub="WX 1–7" />
            <BandPillTall label="VHF" sub="136–174 MHz" />
          </div>
          <FreqDisplayAfter />
          <SignalBarAfter dbu={-18} w={460} />
          {/* control row */}
          <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <button style={{
              width: 56, height: 48, borderRadius: 6,
              background: 'linear-gradient(180deg,#161618 0%,#0f0f11 100%)',
              border: `1px solid #222225`, borderBottom: '2px solid #0a0a0c',
              color: T.amber, fontSize: 18, cursor: 'pointer',
            }}>‹</button>
            <button style={{
              padding: '0 18px', height: 44, borderRadius: 4,
              background: 'linear-gradient(180deg,#141416 0%,#0d0d0f 100%)',
              border: `1px solid #222225`, borderBottom: '2px solid #0a0a0c',
              color: T.md, fontFamily: T.mono, fontSize: 11, fontWeight: 700,
              letterSpacing: '0.10em', cursor: 'pointer',
            }}>⌕ SCAN ◀</button>
            <button style={{
              padding: '0 18px', height: 44, borderRadius: 4,
              background: 'linear-gradient(180deg,#141416 0%,#0d0d0f 100%)',
              border: `1px solid #222225`, borderBottom: '2px solid #0a0a0c',
              color: T.md, fontFamily: T.mono, fontSize: 11, fontWeight: 700,
              letterSpacing: '0.10em', cursor: 'pointer',
            }}>▶ SCAN ⌕</button>
            <button style={{
              width: 56, height: 48, borderRadius: 6,
              background: 'linear-gradient(180deg,#161618 0%,#0f0f11 100%)',
              border: `1px solid #222225`, borderBottom: '2px solid #0a0a0c',
              color: T.amber, fontSize: 18, cursor: 'pointer',
            }}>›</button>
          </div>
          <AgcStripAfter agc />
        </div>
      </div>
      {/* Right: presets */}
      <div style={{ padding: '20px 18px 0' }}>
        <div style={{
          display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
          paddingBottom: 12, borderBottom: `1px solid ${T.sep}`, marginBottom: 12,
        }}>
          <div style={{
            fontFamily: T.mono, fontSize: 9, fontWeight: 700, letterSpacing: '0.18em',
            color: T.lo,
          }}>MEMORY <span style={{ color: T.dim, marginLeft: 4 }}>· 7 of 16</span></div>
          <div style={{
            fontFamily: T.mono, fontSize: 9, letterSpacing: '0.14em', color: T.lo,
          }}>HOLD <kbd style={{
            fontFamily: T.mono, fontSize: 9, padding: '0 4px', borderRadius: 2,
            border: `1px solid ${T.hair}`, background: '#0c0c0e', color: T.md,
          }}>FM</kbd> TO SAVE</div>
        </div>
        <PresetAfter slot={1} name="NOAA Raleigh"  freq="162.55"  band="WB" />
        <PresetAfter slot={2} name="The River"     freq="105.10"  band="FM" />
        <PresetAfter slot={3} name="WCMC"          freq="106.90"  band="FM" />
        <PresetAfter slot={4} name="WUNC"          freq="91.50"   band="FM" />
        <PresetAfter slot={5} name="WFJA"          freq="105.50"  band="FM" />
        <PresetAfter slot={6} name="WSMW"          freq="97.75"   band="FM" />
        <PresetAfter slot={7} name="Rock 92"       freq="92.30"   band="FM" active />
        <PresetAfter slot={8} empty />
      </div>
    </div>
  </Stage>
);
