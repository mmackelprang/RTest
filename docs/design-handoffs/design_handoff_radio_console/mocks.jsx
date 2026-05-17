// handoff-mocks.jsx — Mock components for each proposed change in the
// Radio.Web design analysis. Each component renders at a specific fixed
// size meant to be wrapped in a <DCArtboard>. Styling is intentionally
// inline so this file is self-contained and doesn't compete with the
// design-canvas chrome's CSS.

const T = {
  base:       '#0D0D0F',
  raised:     '#141416',
  inset:      '#0A0A0C',
  overlay:    '#1A1A1D',
  sep:        '#1F1F22',
  hair:       '#25252A',

  accent:     '#5CD4E8',
  accentDim:  'rgba(92,212,232,0.10)',
  accentGlow: 'rgba(92,212,232,0.25)',
  amber:      '#F0A830',
  amberGlow:  'rgba(240,168,48,0.30)',

  sVinyl:     '#A78BFA',
  sRadio:     '#F0A830',
  sBT:        '#60A5FA',
  sUSB:       '#4ADE80',
  sFile:      '#5CD4E8',

  red:        '#F87171',
  yellow:     '#FBBF24',
  green:      '#4ADE80',

  hi:         '#F0EFF4',
  md:         '#9CA3AF',
  lo:         '#4B5563',
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

const SectionLabel = ({ kicker, title, after }) => (
  <div style={{
    display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
    padding: '14px 20px 10px', borderBottom: `1px solid ${T.sep}`,
  }}>
    <div>
      <div style={{
        fontFamily: T.mono, fontSize: 10, letterSpacing: '0.16em',
        textTransform: 'uppercase', color: after ? T.accent : T.red,
      }}>{kicker}</div>
      <div style={{ fontSize: 13, color: T.hi, marginTop: 2 }}>{title}</div>
    </div>
    <div style={{
      fontFamily: T.mono, fontSize: 10, letterSpacing: '0.14em',
      textTransform: 'uppercase', color: T.lo,
    }}>{after ? 'After' : 'Before'}</div>
  </div>
);

const Divider = () => (
  <div style={{ width: 1, background: T.sep, alignSelf: 'stretch' }} />
);


/* ════════════════════════════════════════════════════════════
   1. P0 — Top bar redesign
   ════════════════════════════════════════════════════════════ */

const Icon = ({ ch, color = T.md, size = 18 }) => (
  <span style={{ fontSize: size, color, lineHeight: 1, display: 'inline-block' }}>{ch}</span>
);

const TbCircle = ({ children, on, accent, w = 64 }) => (
  <div style={{
    width: w, height: 64, borderRadius: 32,
    display: 'flex', alignItems: 'center', justifyContent: 'center',
    background: on ? (accent ? `${accent}1F` : T.accentDim) : 'transparent',
    color: on ? (accent || T.accent) : T.md,
    position: 'relative',
  }}>
    {children}
    {on && <span style={{
      position: 'absolute', bottom: 2, left: '25%', right: '25%',
      height: 2, background: accent || T.accent, borderRadius: 1,
    }} />}
  </div>
);

const TopBarBefore = () => (
  <div style={{
    width: '100%', height: 80, background: T.base,
    borderBottom: `1px solid ${T.sep}`,
    display: 'flex', alignItems: 'center', padding: '0 16px', gap: 16,
  }}>
    <div style={{
      fontFamily: T.led, fontSize: 24, color: T.amber,
      textShadow: `0 0 8px ${T.amberGlow}`, letterSpacing: 4, minWidth: 100,
    }}>11:29</div>
    <div style={{ width: 1, height: 48, background: T.sep }} />
    <div style={{
      display: 'flex', alignItems: 'center', gap: 2,
      background: T.inset, border: `1px solid ${T.sep}`,
      borderRadius: 36, padding: '4px 12px',
    }}>
      <span style={{
        fontFamily: T.mono, fontSize: 12, textTransform: 'uppercase',
        letterSpacing: '0.10em', color: T.lo, padding: '0 6px',
      }}>In</span>
      <TbCircle><Icon ch="♪" /></TbCircle>
      <TbCircle on accent={T.sRadio}><Icon ch="📻" size={20} /></TbCircle>
      <TbCircle><Icon ch="⌁" /></TbCircle>
      <TbCircle><Icon ch="▼" /></TbCircle>
      <TbCircle><Icon ch="⎘" /></TbCircle>
    </div>
    <div style={{ width: 1, height: 48, background: T.sep }} />
    <div style={{
      display: 'flex', alignItems: 'center', gap: 2,
      background: T.inset, border: `1px solid ${T.sep}`,
      borderRadius: 36, padding: '4px 12px',
    }}>
      <span style={{
        fontFamily: T.mono, fontSize: 12, textTransform: 'uppercase',
        letterSpacing: '0.10em', color: T.lo, padding: '0 6px',
      }}>Out</span>
      <TbCircle on><Icon ch="🔊" size={18} /></TbCircle>
      <TbCircle><Icon ch="🔈" size={18} /></TbCircle>
      <TbCircle><Icon ch="📺" size={18} /></TbCircle>
      <TbCircle w={120}>
        <Icon ch="📡" /><span style={{ fontSize: 11, color: T.md, marginLeft: 4 }}>Cast</span>
      </TbCircle>
    </div>
    <TbCircle><Icon ch="🐞" size={20} color={T.lo} /></TbCircle>
    <div style={{ marginLeft: 'auto', display: 'flex', gap: 2 }}>
      <TbCircle on w={56}><Icon ch="⌂" size={22} color={T.accent} /></TbCircle>
      <TbCircle w={56}><Icon ch="≡" size={22} /></TbCircle>
      <TbCircle w={56}><Icon ch="▥" size={22} /></TbCircle>
      <TbCircle w={56}><Icon ch="⌥" size={22} /></TbCircle>
      <TbCircle w={56}><Icon ch="⌖" size={22} /></TbCircle>
      <TbCircle w={56}><Icon ch="⏱" size={22} /></TbCircle>
      <TbCircle w={56}><Icon ch="⚙" size={22} /></TbCircle>
      <TbCircle w={56}><Icon ch="☎" size={22} /></TbCircle>
      <TbCircle w={56}><Icon ch="⏻" size={22} /></TbCircle>
    </div>
  </div>
);

const NavPill = ({ label, badge, on }) => (
  <div style={{
    height: 56, padding: '0 16px',
    display: 'inline-flex', alignItems: 'center', gap: 8,
    borderRadius: 10,
    background: on ? T.accentDim : 'transparent',
    color: on ? T.accent : T.md,
    fontFamily: T.mono, fontSize: 12, letterSpacing: '0.10em',
    textTransform: 'uppercase', fontWeight: 500,
    position: 'relative',
  }}>
    {label}
    {badge && <span style={{
      background: T.amber, color: T.base, fontSize: 10, fontWeight: 700,
      borderRadius: 8, padding: '2px 6px', fontFamily: T.mono,
    }}>{badge}</span>}
  </div>
);

const SourceBubble = ({ ch, label, sub, color = T.md, accent, disabled, chev }) => (
  <div style={{
    height: 48, padding: '0 16px',
    display: 'inline-flex', alignItems: 'center', gap: 10,
    borderRadius: 24,
    background: accent ? `${accent}14` : T.inset,
    border: `1px solid ${accent ? `${accent}55` : T.sep}`,
    color: accent || color,
    opacity: disabled ? 0.4 : 1,
    fontSize: 14, fontWeight: 500,
  }}>
    <span style={{
      width: 28, height: 28, borderRadius: 14,
      background: accent ? `${accent}33` : T.overlay,
      display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
      fontSize: 14, color: accent || T.md,
    }}>{ch}</span>
    <span>{label}{sub && <span style={{
      color: accent ? `${accent}cc` : T.lo, fontSize: 12, marginLeft: 6,
    }}>· {sub}</span>}</span>
    {chev && <span style={{
      marginLeft: 4, color: accent || T.md, opacity: 0.7, fontSize: 16,
    }}>›</span>}
  </div>
);

const TopBarAfter = () => (
  <div style={{
    width: '100%', background: T.base,
    borderBottom: `1px solid ${T.sep}`,
    padding: '12px 24px',
    display: 'flex', flexDirection: 'column', gap: 10,
  }}>
    <div style={{ display: 'flex', alignItems: 'center', gap: 28 }}>
      <div>
        <div style={{
          fontFamily: T.mono, fontSize: 10, letterSpacing: '0.16em',
          textTransform: 'uppercase', color: T.lo,
        }}>Time</div>
        <div style={{
          fontFamily: T.led, fontSize: 22, color: T.amber,
          textShadow: `0 0 8px ${T.amberGlow}`, letterSpacing: 4,
        }}>11:29</div>
      </div>
      <div style={{ width: 1, height: 40, background: T.sep }} />
      <div>
        <div style={{
          fontFamily: T.mono, fontSize: 10, letterSpacing: '0.16em',
          textTransform: 'uppercase', color: T.lo,
        }}>In · Source</div>
        <div style={{ fontSize: 15, color: T.hi, marginTop: 4, fontWeight: 500 }}>
          <span style={{
            display: 'inline-block', width: 8, height: 8, borderRadius: 2,
            background: T.sRadio, marginRight: 8, verticalAlign: 1,
          }} />
          FM Radio · <span style={{ fontFamily: T.led, color: T.amber, letterSpacing: 2 }}>88.5</span>
        </div>
      </div>
      <div style={{ color: T.lo, fontSize: 18, alignSelf: 'flex-end', marginBottom: 4 }}>→</div>
      <div>
        <div style={{
          fontFamily: T.mono, fontSize: 10, letterSpacing: '0.16em',
          textTransform: 'uppercase', color: T.lo,
        }}>Out · Destination</div>
        <div style={{ fontSize: 15, color: T.hi, marginTop: 4, fontWeight: 500 }}>
          <span style={{
            display: 'inline-block', width: 8, height: 8, borderRadius: 2,
            background: T.accent, marginRight: 8, verticalAlign: 1,
          }} />
          Living Room <span style={{ color: T.md, fontWeight: 400 }}>· Cast</span>
        </div>
      </div>
      <div style={{ marginLeft: 'auto', display: 'flex', gap: 4 }}>
        <NavPill label="Home" on />
        <NavPill label="Queue" badge="45" />
        <NavPill label="Radio" />
        <NavPill label="History" />
        <NavPill label="Devices" />
        <NavPill label="System" />
      </div>
    </div>
    <div style={{ display: 'flex', gap: 8 }}>
      <SourceBubble ch="♪" label="File" />
      <SourceBubble ch="📻" label="Radio" sub="88.5" accent={T.sRadio} chev />
      <SourceBubble ch="⌁" label="Bluetooth" />
      <SourceBubble ch="⌖" label="Vinyl" sub="offline" disabled />
      <SourceBubble ch="⎘" label="USB" />
    </div>
  </div>
);

const Mock_TopBar = () => (
  <Stage w={1920} h={420}>
    <SectionLabel kicker="Current" title="Three clusters of identical circular pills · debug button live · IDs unlabeled" />
    <TopBarBefore />
    <div style={{ height: 28 }} />
    <SectionLabel kicker="Proposed" title="Labeled routing reads as a sentence · pill sources strip · rectangular nav · debug gated"
      after />
    <TopBarAfter />
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   2. P0 — Source / device naming
   ════════════════════════════════════════════════════════════ */

const NameRow = ({ raw, sub, danger }) => (
  <div style={{
    padding: '12px 16px', borderBottom: `1px solid ${T.sep}`,
    display: 'flex', flexDirection: 'column', gap: 2,
    fontFamily: danger ? T.mono : T.body,
    fontSize: danger ? 12 : 14,
    color: danger ? T.red : T.hi,
  }}>
    <span style={danger ? { wordBreak: 'break-all' } : null}>{raw}</span>
    {sub && <span style={{ fontSize: 12, color: T.md, fontFamily: T.body }}>{sub}</span>}
  </div>
);

const Mock_Naming = () => (
  <Stage w={880} h={460} style={{ display: 'flex', flexDirection: 'column' }}>
    <div style={{ display: 'flex', flex: 1 }}>
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <SectionLabel kicker="Raw API leakage" title="Audio Source · Devices · Tracks" />
        <div style={{ flex: 1, background: T.raised, margin: '12px 16px', borderRadius: 8, border: `1px solid ${T.sep}` }}>
          <NameRow raw="FilePlayer-da7eb94888fa43aab0021b8c0a4c2e1f7" danger />
          <NameRow raw="1 - LG TV SSCR2 (AMD High Definition Audio Device)" danger />
          <NameRow raw="CABLE In 16ch (VB-Audio Virtual Cable)" danger />
          <NameRow raw="Track 8" sub="00:03:00.6628571" />
        </div>
      </div>
      <Divider />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <SectionLabel kicker="Display layer" title="DisplayName projection on every DTO" after />
        <div style={{ flex: 1, background: T.raised, margin: '12px 16px', borderRadius: 8, border: `1px solid ${T.sep}` }}>
          <NameRow raw="File Player" sub="Local · 45 tracks queued" />
          <NameRow raw="LG TV" sub="HDMI · 5.1 surround capable" />
          <NameRow raw="VB-Audio Cable" sub="16-channel virtual" />
          <NameRow raw="We're Ready" sub="3:00 · from file name «08 - We're Ready.flac»" />
        </div>
      </div>
    </div>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   3. P0 — Queue formatting
   ════════════════════════════════════════════════════════════ */

const QRowBefore = ({ n, title, art, dur, active }) => (
  <div style={{
    display: 'grid', gridTemplateColumns: '24px 1fr 140px 24px',
    alignItems: 'center', gap: 12, padding: '14px 16px',
    borderBottom: `1px solid ${T.sep}`,
    background: active ? 'rgba(92,212,232,0.06)' : 'transparent',
  }}>
    <span style={{ fontFamily: T.mono, color: T.lo, fontSize: 12 }}>{n}</span>
    <div>
      <div style={{ color: active ? T.accent : T.hi, fontSize: 14, fontWeight: 500 }}>{title}</div>
      <div style={{ color: T.md, fontSize: 12 }}>{art}</div>
    </div>
    <span style={{ fontFamily: T.mono, color: T.md, fontSize: 11 }}>{dur}</span>
    <span style={{ color: T.lo, textAlign: 'center' }}>×</span>
  </div>
);

const QRowAfter = ({ n, title, art, alb, dur, active }) => (
  <div style={{
    display: 'grid', gridTemplateColumns: '28px 1fr 56px 24px',
    alignItems: 'center', gap: 14, padding: '12px 16px',
    borderBottom: `1px solid ${T.sep}`,
    background: active ? 'rgba(240,168,48,0.06)' : 'transparent',
    borderLeft: active ? `3px solid ${T.amber}` : '3px solid transparent',
    paddingLeft: 13,
  }}>
    <span style={{
      fontFamily: T.mono, color: active ? T.amber : T.lo, fontSize: 13, textAlign: 'center',
    }}>{active ? '▶' : n}</span>
    <div>
      <div style={{ color: T.hi, fontSize: 15, fontWeight: 500 }}>{title}</div>
      <div style={{ color: T.md, fontSize: 13 }}>{art} · {alb}</div>
    </div>
    <span style={{
      fontFamily: T.mono, color: T.md, fontSize: 13, textAlign: 'right',
      fontVariantNumeric: 'tabular-nums',
    }}>{dur}</span>
    <span style={{ color: T.lo, textAlign: 'center', fontSize: 16 }}>×</span>
  </div>
);

const Mock_Queue = () => (
  <Stage w={1280} h={540} style={{ display: 'flex', flexDirection: 'column' }}>
    <div style={{ display: 'flex', flex: 1 }}>
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <SectionLabel kicker="Raw TimeSpan in column" title="Sub-tick precision · column eats space · no playing indicator" />
        <div style={{ flex: 1, padding: 16 }}>
          <div style={{
            background: T.raised, borderRadius: 8, border: `1px solid ${T.sep}`,
          }}>
            <QRowBefore n="0" title="Track 8" art="Cary High Chorus" dur="00:03:00.6628571" active />
            <QRowBefore n="1" title="I'm Not in Love" art="10cc" dur="00:06:04.9044897" />
            <QRowBefore n="2" title="Track 9" art="Cary High Chorus" dur="00:02:19.9640816" />
            <QRowBefore n="3" title="Bixby Canyon Bridge" art="Death Cab for Cutie" dur="00:05:15.4546938" />
            <QRowBefore n="4" title="Track 10" art="Cary High Chorus" dur="00:01:17.1395918" />
            <QRowBefore n="5" title="Track 11" art="Cary High Chorus" dur="00:02:00.5551020" />
          </div>
        </div>
      </div>
      <Divider />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <SectionLabel kicker="Formatted · amber accent for now-playing" title="FormatDuration() · accent border · tabular numerals" after />
        <div style={{ flex: 1, padding: 16 }}>
          <div style={{
            background: T.raised, borderRadius: 8, border: `1px solid ${T.sep}`,
          }}>
            <QRowAfter n="1" title="We're Ready" art="Boston" alb="Third Stage" dur="3:00" active />
            <QRowAfter n="2" title="I'm Not in Love" art="10cc" alb="The Original Soundtrack" dur="6:04" />
            <QRowAfter n="3" title="Track 9" art="Cary High Chorus" alb="Fall Concert 2006" dur="2:20" />
            <QRowAfter n="4" title="Bixby Canyon Bridge" art="Death Cab for Cutie" alb="Narrow Stairs" dur="5:15" />
            <QRowAfter n="5" title="Hot for Teacher" art="Van Halen" alb="1984" dur="4:42" />
            <QRowAfter n="6" title="Don't Look Back" art="Boston" alb="Don't Look Back" dur="5:58" />
          </div>
        </div>
      </div>
    </div>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   4. P0 — Metrics tiles
   ════════════════════════════════════════════════════════════ */

const TileBefore = ({ cat, name, val, suffix }) => (
  <div style={{
    background: T.raised, borderRadius: 8, padding: 16,
    border: `1px solid ${T.sep}`,
  }}>
    <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, letterSpacing: '0.12em' }}>{cat}</div>
    <div style={{ fontSize: 13, color: T.md, marginTop: 4 }}>{name}</div>
    <div style={{ fontFamily: T.mono, fontSize: 28, color: T.accent, marginTop: 6, fontWeight: 500 }}>
      {val}<span style={{ fontSize: 13, color: T.md }}>{suffix}</span>
    </div>
  </div>
);

const Sparkline = ({ stroke = T.accent, pts }) => (
  <svg viewBox="0 0 120 28" preserveAspectRatio="none" style={{ width: '100%', height: 28, marginTop: 8, display: 'block' }}>
    <polyline fill={`${stroke}1A`} stroke="none" points={`${pts} 120,28 0,28`} />
    <polyline fill="none" stroke={stroke} strokeWidth="1.5" points={pts} />
  </svg>
);

const TileAfter = ({ cat, name, val, suffix, delta, deltaColor = T.green, valColor, spark }) => (
  <div style={{
    background: T.raised, borderRadius: 8, padding: 16,
    border: `1px solid ${T.sep}`,
  }}>
    <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, letterSpacing: '0.10em', textTransform: 'uppercase' }}>{cat}</div>
    <div style={{ fontSize: 13, color: T.md, marginTop: 4 }}>{name}</div>
    <div style={{ fontFamily: T.mono, fontSize: 30, color: valColor || T.hi, marginTop: 6, fontWeight: 500, fontVariantNumeric: 'tabular-nums' }}>
      {val}<span style={{ fontSize: 13, color: T.md, marginLeft: 4 }}>{suffix}</span>
    </div>
    {delta && <div style={{ fontSize: 11, color: deltaColor, marginTop: 2, fontFamily: T.mono }}>{delta}</div>}
    {spark && <Sparkline {...spark} />}
  </div>
);

const Mock_Metrics = () => (
  <Stage w={1280} h={580} style={{ display: 'flex', flexDirection: 'column' }}>
    <div style={{ display: 'flex', flex: 1 }}>
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <SectionLabel kicker="Unit & hierarchy bugs" title="850.4% memory · raw category prefixes · no trend" />
        <div style={{ flex: 1, padding: 16, display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 12, gridAutoRows: 'min-content' }}>
          <TileBefore cat="SYSTEM" name="Memory Usage Mb" val="850.4" suffix="%" />
          <TileBefore cat="RADIO"  name="Signal Strength" val="65.39" />
          <TileBefore cat="SYSTEM" name="Disk Usage Percent" val="35.7" suffix="%" />
          <TileBefore cat="TTS"    name="Latency Ms" val="215" />
          <TileBefore cat="RADIO"  name="Frequency Changes" val="261" />
          <TileBefore cat="API"    name="Requests Total" val="135725" />
        </div>
      </div>
      <Divider />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <SectionLabel kicker="Grouped · sparklines · semantic color" title="One registry knows the unit · trend is part of the value" after />
        <div style={{ flex: 1, padding: 16, overflow: 'hidden' }}>
          <div style={{ fontFamily: T.mono, fontSize: 11, color: T.lo, letterSpacing: '0.14em', textTransform: 'uppercase', marginBottom: 8 }}>System</div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 10, marginBottom: 14 }}>
            <TileAfter cat="Memory" name="Heap in use" val="850" suffix="MB" delta="▲ 4% vs 1h ago"
              spark={{ pts: '0,20 10,18 20,19 30,16 40,17 50,14 60,15 70,12 80,13 90,10 100,11 110,9 120,7', stroke: T.accent }} />
            <TileAfter cat="Disk" name="Usage" val="35.7" suffix="%" valColor={T.green} delta="Stable"
              spark={{ pts: '0,16 30,16 60,16 90,15 120,16', stroke: T.green }} />
          </div>
          <div style={{ fontFamily: T.mono, fontSize: 11, color: T.lo, letterSpacing: '0.14em', textTransform: 'uppercase', marginBottom: 8 }}>Radio</div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 10 }}>
            <TileAfter cat="Reception" name="Signal strength" val="65" suffix="%" valColor={T.green} delta="Stable · last 5 min"
              spark={{ pts: '0,14 10,12 20,14 30,11 40,10 50,12 60,11 70,10 80,11 90,12 100,10 110,11 120,10', stroke: T.green }} />
            <TileAfter cat="Tuning" name="Frequency changes · 24h" val="261" delta="▲ 12 today · last at 11:14" deltaColor={T.md} />
          </div>
        </div>
      </div>
    </div>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   5. P0 — File browser filter chips
   ════════════════════════════════════════════════════════════ */

const Mock_FbFilter = () => (
  <Stage w={880} h={360} pad={20} style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
    <div>
      <div style={{ fontFamily: T.mono, fontSize: 10, color: T.red, letterSpacing: '0.16em', textTransform: 'uppercase' }}>
        Current — JSON re-serialization leaks
      </div>
      <div style={{
        marginTop: 8, background: T.raised, border: `1px solid ${T.sep}`,
        borderRadius: 8, padding: 14,
      }}>
        <div style={{ fontFamily: T.mono, fontSize: 11, color: T.lo, letterSpacing: '0.10em', textTransform: 'uppercase', marginBottom: 6 }}>Filter</div>
        <div style={{
          background: T.inset, border: `1px solid ${T.sep}`, borderRadius: 6,
          padding: '10px 12px', fontFamily: T.mono, fontSize: 10, color: T.red,
          wordBreak: 'break-all', lineHeight: 1.5,
        }}>
          "\u0022\u0022\\\u0022\\\\\\\\\u0022\\\\\\\\\\\\\\\\\u0022\\\\\\\\\\\\\\\\\\\\\\\\\u0022…
        </div>
      </div>
    </div>
    <div>
      <div style={{ fontFamily: T.mono, fontSize: 10, color: T.accent, letterSpacing: '0.16em', textTransform: 'uppercase' }}>
        Proposed — chips · one extension per chip · nothing to escape
      </div>
      <div style={{
        marginTop: 8, background: T.raised, border: `1px solid ${T.sep}`,
        borderRadius: 8, padding: 14,
      }}>
        <div style={{ fontFamily: T.mono, fontSize: 11, color: T.lo, letterSpacing: '0.10em', textTransform: 'uppercase', marginBottom: 8 }}>Show only</div>
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
          {['.mp3', '.flac', '.wav', '.m4a'].map((x) => (
            <span key={x} style={{
              height: 32, padding: '0 12px',
              display: 'inline-flex', alignItems: 'center', gap: 6,
              background: T.accentDim, border: `1px solid ${T.accent}55`,
              borderRadius: 16, fontSize: 13, color: T.accent,
              fontFamily: T.mono,
            }}>{x} <span style={{ opacity: 0.6, fontSize: 14 }}>×</span></span>
          ))}
          <span style={{
            height: 32, padding: '0 12px',
            display: 'inline-flex', alignItems: 'center', gap: 6,
            background: 'transparent', border: `1px dashed ${T.sep}`,
            borderRadius: 16, fontSize: 13, color: T.lo, fontFamily: T.mono,
          }}>＋ add type</span>
        </div>
      </div>
    </div>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   6. P1 — Persistent Now Playing dock
   ════════════════════════════════════════════════════════════ */

const FakePage = ({ title, children }) => (
  <div style={{ flex: 1, padding: 24, display: 'flex', flexDirection: 'column', gap: 16 }}>
    <div style={{ fontSize: 22, fontWeight: 600, color: T.hi }}>{title}</div>
    {children}
  </div>
);

const SkelRow = ({ w = '100%', h = 12 }) => (
  <div style={{ width: w, height: h, background: T.raised, borderRadius: 4 }} />
);

const Dock = () => (
  <div style={{
    height: 64, background: T.overlay, borderTop: `1px solid ${T.sep}`,
    padding: '0 24px',
    display: 'flex', alignItems: 'center', gap: 14,
    backdropFilter: 'blur(20px)',
  }}>
    <div style={{
      width: 48, height: 48, borderRadius: 6, flexShrink: 0,
      background: 'linear-gradient(135deg, #2a2a30, #1a1a1f)',
      position: 'relative', overflow: 'hidden',
    }}>
      <div style={{
        position: 'absolute', inset: 0,
        backgroundImage: `repeating-linear-gradient(135deg, rgba(255,255,255,0.05) 0 6px, transparent 6px 12px)`,
      }} />
    </div>
    <div style={{ minWidth: 200 }}>
      <div style={{ color: T.hi, fontSize: 14, fontWeight: 600 }}>We're Ready</div>
      <div style={{ color: T.md, fontSize: 12 }}>
        <span style={{
          display: 'inline-block', width: 8, height: 8, borderRadius: 2,
          background: T.sFile, marginRight: 6, verticalAlign: 1,
        }} />
        Boston · Third Stage
      </div>
    </div>
    <div style={{ display: 'flex', gap: 2, alignItems: 'flex-end', height: 16, marginRight: 4 }}>
      <span style={{ width: 3, height: 9, background: T.accent, borderRadius: 1 }} />
      <span style={{ width: 3, height: 16, background: T.accent, borderRadius: 1 }} />
      <span style={{ width: 3, height: 6, background: T.accent, borderRadius: 1 }} />
    </div>
    <div style={{ flex: 1, display: 'flex', alignItems: 'center', gap: 10 }}>
      <span style={{ fontFamily: T.mono, fontSize: 11, color: T.md, fontVariantNumeric: 'tabular-nums' }}>1:18</span>
      <div style={{ flex: 1, height: 3, background: T.sep, borderRadius: 2, overflow: 'hidden' }}>
        <div style={{ width: '44%', height: '100%', background: T.accent }} />
      </div>
      <span style={{ fontFamily: T.mono, fontSize: 11, color: T.md, fontVariantNumeric: 'tabular-nums' }}>3:00</span>
    </div>
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, color: T.md, fontSize: 20 }}>
      <span>⏮</span>
      <span style={{
        width: 40, height: 40, borderRadius: 20, background: T.accent, color: T.base,
        display: 'inline-flex', alignItems: 'center', justifyContent: 'center', fontSize: 16,
      }}>▶</span>
      <span>⏭</span>
    </div>
  </div>
);

const Mock_Dock = () => (
  <Stage w={1920} h={760} style={{ display: 'flex', flexDirection: 'column' }}>
    <div style={{
      height: 64, background: T.base, borderBottom: `1px solid ${T.sep}`,
      display: 'flex', alignItems: 'center', padding: '0 24px', gap: 20,
    }}>
      <div style={{ fontFamily: T.led, fontSize: 18, color: T.amber, letterSpacing: 3, textShadow: `0 0 6px ${T.amberGlow}` }}>11:29</div>
      <div style={{ width: 1, height: 32, background: T.sep }} />
      <div style={{ color: T.md, fontSize: 13 }}>FM Radio · 88.5 → Living Room</div>
      <div style={{ marginLeft: 'auto', display: 'flex', gap: 4 }}>
        <NavPill label="Home" />
        <NavPill label="Devices" on />
        <NavPill label="Metrics" />
        <NavPill label="System" />
      </div>
    </div>
    <FakePage title="Device Management">
      <div style={{
        background: T.raised, border: `1px solid ${T.sep}`, borderRadius: 8,
        padding: 0, overflow: 'hidden',
      }}>
        {['VB-Audio Cable', 'Arctis Pro Wireless', 'LG TV · HDMI', 'Realtek USB', 'HTTP Audio Stream'].map((d, i) => (
          <div key={d} style={{
            padding: '14px 18px', display: 'flex', alignItems: 'center', gap: 14,
            borderBottom: i < 4 ? `1px solid ${T.sep}` : 'none',
            background: i === 0 ? 'rgba(92,212,232,0.04)' : 'transparent',
          }}>
            <span style={{ color: i === 0 ? T.accent : T.md, fontSize: 18 }}>{i === 0 ? '★' : '○'}</span>
            <span style={{ color: T.hi, fontSize: 15, fontWeight: 500, flex: 1 }}>{d}</span>
            {i === 0 && <span style={{
              fontFamily: T.mono, fontSize: 11, color: T.accent,
              background: T.accentDim, padding: '2px 8px', borderRadius: 4, letterSpacing: '0.1em', textTransform: 'uppercase',
            }}>Default</span>}
            <span style={{ color: T.md, fontSize: 13 }}>Output</span>
          </div>
        ))}
      </div>
      <div style={{ flex: 1 }} />
    </FakePage>
    <Dock />
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   7. P1 — Source pill semantics
   ════════════════════════════════════════════════════════════ */

const Mock_PillSemantics = () => (
  <Stage w={1100} h={320} pad={24} style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
    <div>
      <div style={{ fontFamily: T.mono, fontSize: 10, color: T.red, letterSpacing: '0.16em', textTransform: 'uppercase' }}>
        Current — one button, three different actions, no chevron
      </div>
      <div style={{ marginTop: 12, display: 'flex', gap: 8, fontSize: 12, color: T.lo, fontFamily: T.mono }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4, alignItems: 'center' }}>
          <SourceBubble ch="📻" label="Radio" accent={T.sRadio} />
          <span>tap → switch source</span>
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4, alignItems: 'center' }}>
          <SourceBubble ch="📻" label="Radio" accent={T.sRadio} />
          <span style={{ color: T.amber }}>tap again → toggle home panel</span>
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4, alignItems: 'center' }}>
          <SourceBubble ch="⌁" label="Bluetooth" accent={T.sBT} />
          <span style={{ color: T.amber }}>tap again → navigate /bluetooth</span>
        </div>
      </div>
    </div>
    <div>
      <div style={{ fontFamily: T.mono, fontSize: 10, color: T.accent, letterSpacing: '0.16em', textTransform: 'uppercase' }}>
        Proposed — body switches source · chevron opens detail
      </div>
      <div style={{ marginTop: 12, display: 'flex', gap: 8, fontSize: 12, color: T.lo, fontFamily: T.mono }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4, alignItems: 'center' }}>
          <SourceBubble ch="📻" label="Radio" sub="88.5" accent={T.sRadio} chev />
          <span>body → switch  ·  ›  → detail</span>
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4, alignItems: 'center' }}>
          <SourceBubble ch="⌁" label="Bluetooth" sub="JBL Charge" accent={T.sBT} chev />
          <span>body → switch  ·  ›  → device list</span>
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4, alignItems: 'center' }}>
          <SourceBubble ch="♪" label="File" />
          <span>no detail · just switches</span>
        </div>
      </div>
    </div>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   8. P1 — Visualizer panel
   ════════════════════════════════════════════════════════════ */

const VizSpectrum = ({ bars = 32, w = '100%', h = 380, color = T.accent }) => {
  const data = Array.from({ length: bars }, (_, i) => {
    const x = i / bars;
    return 0.35 + 0.6 * Math.abs(Math.sin(x * 6 + 1)) * (1 - x * 0.4);
  });
  return (
    <svg viewBox={`0 0 ${bars * 10} 100`} preserveAspectRatio="none" style={{ width: w, height: h, display: 'block' }}>
      {data.map((v, i) => (
        <rect key={i} x={i * 10 + 1.5} y={100 - v * 90} width={7} height={v * 90}
              fill={color} opacity={0.75 + 0.25 * v} />
      ))}
    </svg>
  );
};

const Mock_Visualizer = () => (
  <Stage w={1480} h={620} style={{ display: 'flex', flexDirection: 'column' }}>
    <div style={{ display: 'flex', flex: 1 }}>
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <SectionLabel kicker="Current" title="Mode picker in corner · debug telemetry overlaid · canvas inset from edges" />
        <div style={{ flex: 1, padding: 16 }}>
          <div style={{ height: '100%', background: T.raised, border: `1px solid ${T.sep}`, borderRadius: 8, padding: 16, position: 'relative' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
              <div style={{ color: T.hi, fontSize: 14 }}>Visualizer</div>
              <div style={{ display: 'flex', gap: 4, alignItems: 'center' }}>
                <span style={{ background: T.green, color: T.base, padding: '2px 8px', borderRadius: 10, fontSize: 10, fontWeight: 600 }}>Connected</span>
                <span style={{ border: `1px solid ${T.accent}`, color: T.accent, padding: '2px 8px', borderRadius: 4, fontSize: 11 }}>VU</span>
                <span style={{ border: `1px solid ${T.sep}`, color: T.md, padding: '2px 8px', borderRadius: 4, fontSize: 11 }}>WAVE</span>
                <span style={{ border: `1px solid ${T.sep}`, color: T.md, padding: '2px 8px', borderRadius: 4, fontSize: 11 }}>SPEC</span>
              </div>
            </div>
            <div style={{ position: 'relative', background: T.inset, borderRadius: 4, height: 360, overflow: 'hidden' }}>
              <VizSpectrum h={360} bars={28} />
              <div style={{
                position: 'absolute', bottom: 8, left: 8,
                color: T.yellow, fontFamily: T.mono, fontSize: 11, fontWeight: 600,
              }}>Updates: 22/sec</div>
              <div style={{
                position: 'absolute', bottom: 8, left: '50%', transform: 'translateX(-50%)',
                color: T.lo, fontFamily: T.mono, fontSize: 9, display: 'flex', gap: 60,
              }}>
                <span>375Hz</span><span>750Hz</span><span>1.1kHz</span><span>1.5kHz</span>
              </div>
            </div>
          </div>
        </div>
      </div>
      <Divider />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <SectionLabel kicker="Promoted" title="Mode picker = top segmented control · canvas fills · dev telemetry gated" after />
        <div style={{ flex: 1, padding: 16 }}>
          <div style={{ height: '100%', background: T.raised, border: `1px solid ${T.sep}`, borderRadius: 8, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
            <div style={{
              display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)',
              borderBottom: `1px solid ${T.sep}`,
            }}>
              {['VU Meter', 'Waveform', 'Spectrum'].map((m, i) => (
                <div key={m} style={{
                  padding: '14px 0', textAlign: 'center',
                  background: i === 2 ? T.accentDim : 'transparent',
                  color: i === 2 ? T.accent : T.md,
                  fontFamily: T.mono, fontSize: 12, letterSpacing: '0.10em',
                  textTransform: 'uppercase', fontWeight: 600,
                  borderRight: i < 2 ? `1px solid ${T.sep}` : 'none',
                  position: 'relative',
                }}>
                  {m}
                  {i === 2 && <span style={{
                    position: 'absolute', bottom: 0, left: '20%', right: '20%',
                    height: 2, background: T.accent,
                  }} />}
                </div>
              ))}
            </div>
            <div style={{ flex: 1, position: 'relative', background: T.inset }}>
              <VizSpectrum h="100%" bars={48} />
              <div style={{
                position: 'absolute', top: 12, right: 12,
                width: 8, height: 8, borderRadius: 4, background: T.green,
                boxShadow: `0 0 8px ${T.green}`,
              }} />
              <div style={{
                position: 'absolute', bottom: 8, left: 0, right: 0,
                color: T.lo, fontFamily: T.mono, fontSize: 10, display: 'flex',
                justifyContent: 'space-around',
              }}>
                <span>250Hz</span><span>500Hz</span><span>1kHz</span><span>2kHz</span><span>4kHz</span><span>8kHz</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   9. P1 — Queue split layout
   ════════════════════════════════════════════════════════════ */

const Mock_QueueSplit = () => (
  <Stage w={1200} h={660} style={{ display: 'flex', flexDirection: 'column' }}>
    <SectionLabel kicker="Center column of Home" title="Queue list + context panel · uses the whole 640px of vertical content area"
      after />
    <div style={{ flex: 1, padding: 16, display: 'flex', gap: 12 }}>
      <div style={{ flex: 1.6, display: 'flex', flexDirection: 'column' }}>
        <div style={{
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          padding: '6px 10px 12px',
        }}>
          <div style={{ display: 'flex', gap: 4 }}>
            <span style={{
              padding: '6px 14px', background: T.accentDim, color: T.accent,
              borderRadius: 6, fontFamily: T.mono, fontSize: 12,
              letterSpacing: '0.10em', textTransform: 'uppercase', fontWeight: 600,
            }}>Queue · 45</span>
            <span style={{
              padding: '6px 14px', color: T.md,
              borderRadius: 6, fontFamily: T.mono, fontSize: 12,
              letterSpacing: '0.10em', textTransform: 'uppercase', fontWeight: 600,
            }}>History</span>
            <span style={{
              padding: '6px 14px', color: T.md,
              borderRadius: 6, fontFamily: T.mono, fontSize: 12,
              letterSpacing: '0.10em', textTransform: 'uppercase', fontWeight: 600,
            }}>Radio</span>
          </div>
          <div style={{ display: 'flex', gap: 6 }}>
            <span style={{ color: T.accent, fontSize: 16 }}>＋</span>
            <span style={{ color: T.md, fontSize: 16 }}>⋮</span>
          </div>
        </div>
        <div style={{
          flex: 1, background: T.raised, borderRadius: 8, border: `1px solid ${T.sep}`,
          overflow: 'hidden',
        }}>
          <QRowAfter n="1" title="We're Ready" art="Boston" alb="Third Stage" dur="3:00" active />
          <QRowAfter n="2" title="I'm Not in Love" art="10cc" alb="Original Soundtrack" dur="6:04" />
          <QRowAfter n="3" title="Bixby Canyon Bridge" art="Death Cab" alb="Narrow Stairs" dur="5:15" />
          <QRowAfter n="4" title="Hot for Teacher" art="Van Halen" alb="1984" dur="4:42" />
          <QRowAfter n="5" title="Don't Look Back" art="Boston" alb="Don't Look Back" dur="5:58" />
          <QRowAfter n="6" title="Foreplay / Long Time" art="Boston" alb="Boston" dur="7:48" />
          <QRowAfter n="7" title="More Than a Feeling" art="Boston" alb="Boston" dur="4:44" />
        </div>
      </div>
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 12, marginTop: 50 }}>
        <div style={{ background: T.raised, border: `1px solid ${T.sep}`, borderRadius: 8, padding: 16 }}>
          <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, letterSpacing: '0.14em', textTransform: 'uppercase' }}>Queue Total</div>
          <div style={{ fontFamily: T.led, fontSize: 30, color: T.amber, marginTop: 6, letterSpacing: 4, textShadow: `0 0 8px ${T.amberGlow}` }}>
            2:48:15
          </div>
          <div style={{ color: T.md, fontSize: 12, marginTop: 4 }}>45 tracks · ends ~14:17</div>
        </div>
        <div style={{ background: T.raised, border: `1px solid ${T.sep}`, borderRadius: 8, padding: 16 }}>
          <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, letterSpacing: '0.14em', textTransform: 'uppercase', marginBottom: 8 }}>Up next</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {[
              ['I\'m Not in Love', '10cc', T.hi],
              ['Bixby Canyon Bridge', 'Death Cab', T.md],
              ['Hot for Teacher', 'Van Halen', T.lo],
            ].map(([t, a, c]) => (
              <div key={t} style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
                <div style={{ width: 32, height: 32, background: T.overlay, borderRadius: 4, flexShrink: 0 }} />
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ color: c, fontSize: 13, fontWeight: 500, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{t}</div>
                  <div style={{ color: T.lo, fontSize: 11 }}>{a}</div>
                </div>
              </div>
            ))}
          </div>
        </div>
        <div style={{ background: T.raised, border: `1px solid ${T.accent}55`, borderRadius: 8, padding: 14, display: 'flex', alignItems: 'center', gap: 10 }}>
          <span style={{ color: T.accent, fontSize: 18 }}>＋</span>
          <span style={{ color: T.accent, fontSize: 14, fontWeight: 500 }}>Save as playlist</span>
        </div>
      </div>
    </div>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   10. P1 — Skeleton loading
   ════════════════════════════════════════════════════════════ */

const Skel = ({ w, h, br = 4 }) => (
  <div style={{ width: w, height: h, background: `linear-gradient(90deg, ${T.raised} 0%, ${T.overlay} 50%, ${T.raised} 100%)`, backgroundSize: '200% 100%', borderRadius: br, animation: 'shimmer 1.4s infinite' }} />
);

const Mock_Skeleton = () => (
  <Stage w={1280} h={660} style={{ display: 'flex', flexDirection: 'column' }}>
    <SectionLabel kicker="Loading states" title="Shape-matched skeletons replace centered spinners on every primary panel"
      after />
    <style>{`@keyframes shimmer { 0% { background-position: -200% 0; } 100% { background-position: 200% 0; } }`}</style>
    <div style={{ flex: 1, padding: 16, display: 'flex', gap: 12 }}>
      <div style={{ width: 360, background: T.raised, border: `1px solid ${T.sep}`, borderRadius: 8, padding: 16, display: 'flex', flexDirection: 'column', gap: 14 }}>
        <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, letterSpacing: '0.12em', textTransform: 'uppercase' }}>Now Playing · loading</div>
        <Skel w={280} h={280} br={8} />
        <Skel w="80%" h={18} />
        <Skel w="60%" h={14} />
        <div style={{ flex: 1 }} />
        <div style={{ display: 'flex', gap: 12, justifyContent: 'center' }}>
          <Skel w={40} h={40} br={20} />
          <Skel w={56} h={56} br={28} />
          <Skel w={40} h={40} br={20} />
        </div>
      </div>
      <div style={{ flex: 1, background: T.raised, border: `1px solid ${T.sep}`, borderRadius: 8, padding: 20, display: 'flex', flexDirection: 'column', gap: 18 }}>
        <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, letterSpacing: '0.12em', textTransform: 'uppercase' }}>Radio control · loading</div>
        <div style={{ display: 'flex', gap: 8 }}>
          <Skel w={64} h={36} br={6} />
          <Skel w={64} h={36} br={6} />
          <Skel w={64} h={36} br={6} />
        </div>
        <div style={{ background: T.inset, borderRadius: 8, padding: 24, textAlign: 'center', border: `1px solid ${T.sep}` }}>
          <Skel w={300} h={56} br={4} />
        </div>
        <div>
          <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo, letterSpacing: '0.12em', textTransform: 'uppercase', marginBottom: 6 }}>Signal</div>
          <div style={{ display: 'flex', gap: 2 }}>
            {Array.from({ length: 20 }).map((_, i) => (
              <Skel key={i} w={20} h={20} br={2} />
            ))}
          </div>
        </div>
        <div style={{ display: 'flex', gap: 8, marginTop: 'auto' }}>
          <Skel w={48} h={48} br={24} />
          <Skel w={48} h={48} br={24} />
          <Skel w={64} h={48} br={24} />
          <Skel w={48} h={48} br={24} />
          <Skel w={48} h={48} br={24} />
        </div>
      </div>
    </div>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   11. P2 — Sleep screen ambient
   ════════════════════════════════════════════════════════════ */

const Mock_Sleep = () => (
  <Stage w={1920} h={760} style={{ background: '#050507', position: 'relative' }}>
    <div style={{
      position: 'absolute', inset: 0,
      background: 'radial-gradient(ellipse at center, rgba(240,168,48,0.06) 0%, transparent 60%)',
    }} />
    <div style={{
      position: 'absolute', inset: 0, display: 'flex',
      alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: 28,
    }}>
      <div style={{
        width: 160, height: 160, borderRadius: 16,
        background: 'linear-gradient(135deg, #1a1517 0%, #0d0a0c 100%)',
        position: 'relative', overflow: 'hidden',
        boxShadow: '0 20px 80px rgba(240,168,48,0.08)',
        opacity: 0.4,
      }}>
        <div style={{ position: 'absolute', inset: 0, backgroundImage: `repeating-linear-gradient(135deg, rgba(240,168,48,0.05) 0 10px, transparent 10px 20px)` }} />
      </div>
      <div style={{
        fontFamily: T.led, fontSize: 96, color: T.amber,
        textShadow: `0 0 24px ${T.amberGlow}, 0 0 60px rgba(240,168,48,0.15)`,
        letterSpacing: 14,
      }}>11:29</div>
      <div style={{
        fontFamily: T.body, fontSize: 18, color: T.dim,
        letterSpacing: '0.08em',
      }}>We're Ready · Boston</div>
      <div style={{
        fontFamily: T.mono, fontSize: 11, color: '#1f1a1d',
        letterSpacing: '0.18em', textTransform: 'uppercase',
        marginTop: 60,
      }}>tap anywhere to wake</div>
    </div>
  </Stage>
);


/* ════════════════════════════════════════════════════════════
   12. P2 — Dev tools gesture & tray
   ════════════════════════════════════════════════════════════ */

const Mock_DevTools = () => (
  <Stage w={880} h={620} style={{ display: 'flex', flexDirection: 'column' }}>
    <SectionLabel kicker="Production hygiene" title="Distortion marker + telemetry hidden behind a corner gesture"
      after />
    <div style={{ flex: 1, padding: 20, display: 'flex', flexDirection: 'column', gap: 16, overflow: 'hidden' }}>
      <div style={{
        position: 'relative', height: 64, background: T.base,
        border: `1px solid ${T.sep}`, borderRadius: 6,
        display: 'flex', alignItems: 'center', padding: '0 16px', gap: 16,
      }}>
        <div style={{ fontFamily: T.led, fontSize: 18, color: T.amber, letterSpacing: 3, textShadow: `0 0 6px ${T.amberGlow}` }}>11:29</div>
        <span style={{ color: T.md, fontSize: 13 }}>FM Radio · 88.5 → Living Room</span>
        <div style={{
          position: 'absolute', top: 0, right: 0, width: 48, height: 48,
          border: `1px dashed ${T.accent}55`,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
        }}>
          <span style={{ fontSize: 10, color: T.accent, fontFamily: T.mono, letterSpacing: '0.10em' }}>3×TAP</span>
        </div>
      </div>
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12 }}>
        <div style={{ fontSize: 20, color: T.accent, marginTop: 4 }}>↓</div>
        <div style={{ color: T.md, fontSize: 13 }}>
          Triple-tap the clock corner to unlock the dev tray. Re-locks after 30s idle.
        </div>
      </div>
      <div style={{
        background: T.raised, border: `1px solid ${T.accent}55`,
        borderRadius: 8, padding: 14,
        boxShadow: `0 8px 32px rgba(0,0,0,0.4)`,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
          <div style={{ fontFamily: T.mono, fontSize: 10, color: T.accent, letterSpacing: '0.14em', textTransform: 'uppercase' }}>
            ● Dev Tray  ·  unlocked
          </div>
          <div style={{ fontFamily: T.mono, fontSize: 10, color: T.lo }}>auto-lock 0:27</div>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
          {[
            ['🐞', 'Mark distortion', 'reports a flag at current playback offset'],
            ['📊', 'Updates: 22/sec', 'live visualizer telemetry'],
            ['💾', 'Dump audio frame', 'last 5s buffered'],
            ['📜', 'Download logs', 'last hour · zip'],
            ['🔧', 'Fingerprint events', '12 events · last 5 min'],
            ['🧪', 'Engine state', 'Active · FilePlayer · Stopped'],
          ].map(([ic, t, sub]) => (
            <div key={t} style={{
              padding: 12, background: T.inset, borderRadius: 6,
              border: `1px solid ${T.sep}`,
              display: 'flex', gap: 10, alignItems: 'flex-start',
            }}>
              <span style={{ fontSize: 16 }}>{ic}</span>
              <div>
                <div style={{ color: T.hi, fontSize: 13, fontWeight: 500 }}>{t}</div>
                <div style={{ color: T.lo, fontSize: 11 }}>{sub}</div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  </Stage>
);


/* ─────────── publish ─────────── */
Object.assign(window, {
  Mock_TopBar,
  Mock_Naming,
  Mock_Queue,
  Mock_Metrics,
  Mock_FbFilter,
  Mock_Dock,
  Mock_PillSemantics,
  Mock_Visualizer,
  Mock_QueueSplit,
  Mock_Skeleton,
  Mock_Sleep,
  Mock_DevTools,
});
