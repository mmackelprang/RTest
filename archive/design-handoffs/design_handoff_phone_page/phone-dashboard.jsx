// Phone Dashboard — the redesigned hero-centric layout.
//
// Layout (within 600px content area, after 156px tab rail):
//   ┌─────────────────────────────────────────────┬──────────────────┐
//   │                                             │  System Status   │
//   │           PHONE STATUS HERO                 ├──────────────────┤
//   │                                             │   Call Path      │
//   │                                             │                  │
//   ├─────────────────────────────────────────────┴──────────────────┤
//   │  ⚙ DEV TRAY (collapsed by default, expand to simulate)         │
//   └────────────────────────────────────────────────────────────────┘

// ─── Helpers ────────────────────────────────────────────────────────
const stateColor = (s) => {
  switch (s) {
    case 'Ringing': return { color: 'var(--signal-amber)', glow: 'rgba(240,168,48,0.50)', glowBg: 'rgba(240,168,48,0.15)' };
    case 'InCall':  return { color: 'var(--signal-green)', glow: 'rgba(74,222,128,0.45)', glowBg: 'rgba(74,222,128,0.12)' };
    case 'Dialing': return { color: 'var(--signal-blue)',  glow: 'rgba(96,165,250,0.45)', glowBg: 'rgba(96,165,250,0.12)' };
    case 'Idle':
    default:        return { color: 'var(--text-medium)',  glow: 'transparent',           glowBg: 'transparent' };
  }
};

// Demo data for each state (mirrors what real backend sends)
const STATE_DEMOS = {
  Idle:    { state: 'Idle',    number: null,            name: null,                duration: null   },
  Ringing: { state: 'Ringing', number: '(919) 555-0142', name: 'Anderson, Carol',   duration: null   },
  InCall:  { state: 'InCall',  number: '(919) 555-2871', name: 'Anderson, Frank',   duration: '00:04:18' },
  Dialing: { state: 'Dialing', number: '(252) 555-4422', name: 'Davis, Henry',      duration: null   },
};

// ─── System Status compact list ──────────────────────────────────
const SystemStatusCard = () => (
  <div className="card accent-green">
    <div className="card-title title-green">
      <span className="title-text">System Status</span>
      <span className="pill green">Healthy</span>
    </div>
    <div className="status-list">
      <div className="status-row">
        <span className="lbl">Platform</span>
        <span className="val">Linux · ARM64</span>
        <span className="pill gray">v 1.4</span>
      </div>
      <div className="status-row">
        <span className="lbl">Bluetooth</span>
        <span className="val">00:11:22:33:44:55</span>
        <span className="pill green">Connected</span>
      </div>
      <div className="status-row">
        <span className="lbl">SIP Device</span>
        <span className="val">0.0.0.0:5060</span>
        <span className="pill green">Listening</span>
      </div>
      <div className="status-row">
        <span className="lbl">HT801 ATA</span>
        <span className="val">192.168.86.22</span>
        <span className="pill green">Online</span>
      </div>
    </div>
  </div>
);

// ─── Call Path card (active mode + GV bridge + SIP trunk) ────────
const CallPathCard = ({ activeMode, onSwitchMode, bridgeConnected, trunkRegistered }) => (
  <div className="card accent-cyan">
    <div className="card-title title-cyan">
      <span className="title-text">Call Path</span>
    </div>

    <div className="callpath-row">
      <span className="sub">Active Mode</span>
      <div className="mode-selector">
        {[
          { id: 'BluetoothHfp', label: 'Bluetooth', icon: 'bluetooth' },
          { id: 'SipTrunk',     label: 'SIP Trunk', icon: 'sip' },
          { id: 'GVBrowser',    label: 'GV Browser', icon: 'globe' },
        ].map((m) => (
          <button
            key={m.id}
            type="button"
            className={`mode-btn ${activeMode === m.id ? 'active' : ''}`}
            onClick={() => onSwitchMode(m.id)}
          >
            <Icon name={m.icon} size={14} />
            {m.label}
          </button>
        ))}
      </div>
    </div>

    <div className="connector-row">
      <div className="conn-meta">
        <span className="conn-icon"><Icon name="globe" size={18} /></span>
        <div className="conn-name">
          <span>Chrome Extension</span>
          <span className="sub">{bridgeConnected ? 'v1.4.0 · GV Bridge' : 'GV Bridge'}</span>
        </div>
      </div>
      <div className="right-cluster">
        <span className={`pill ${bridgeConnected ? 'green' : 'red'}`}>
          {bridgeConnected ? 'Connected' : 'Disconnected'}
        </span>
      </div>
    </div>

    <div className="connector-row">
      <div className="conn-meta">
        <span className="conn-icon"><Icon name="sip" size={18} /></span>
        <div className="conn-name">
          <span>SIP Trunk</span>
          <span className="sub">voip.ms · 5060</span>
        </div>
      </div>
      <div className="right-cluster">
        <span className={`pill ${trunkRegistered ? 'green' : 'red'}`}>
          {trunkRegistered ? 'Registered' : 'Unregistered'}
        </span>
        {!trunkRegistered ? (
          <button type="button" className="link-btn">Re-register</button>
        ) : null}
      </div>
    </div>
  </div>
);

// ─── Phone Status Hero ───────────────────────────────────────────
const PhoneStatusHero = ({ callState, onAnswer, onHangup, onDial }) => {
  const demo = STATE_DEMOS[callState] || STATE_DEMOS.Idle;
  const colors = stateColor(callState);

  const stateLabel = {
    Idle:    'Awaiting Call',
    Ringing: 'Incoming Call',
    InCall:  'Active Call',
    Dialing: 'Dialing Out',
  }[callState];

  return (
    <div
      className="hero"
      style={{
        '--hero-state-color': colors.color,
        '--hero-state-glow':  colors.glow,
        '--hero-glow-color':  colors.glowBg,
      }}
    >
      <div className="hero-glow" />
      <div className="hero-body">
        <div className="hero-top">
          <span className="hero-state-label">{stateLabel}</span>
          <span className="hero-source-tag">
            <span className="dot" style={{ background: colors.color, boxShadow: `0 0 6px ${colors.glow}` }} />
            via {callState === 'InCall' || callState === 'Dialing' ? 'Bluetooth HFP' : 'Rotary Phone'}
          </span>
        </div>

        <div
          className={`hero-state ${callState === 'Ringing' ? 'ring-pulse' : ''}`}
          style={{ color: colors.color, textShadow: `0 0 20px ${colors.glow}` }}
        >
          {demo.state.toUpperCase()}
        </div>

        {demo.number ? (
          <div className="hero-meta">
            <div className="hero-meta-row">
              <span
                className="hero-icon"
                style={{
                  background: `color-mix(in oklab, ${colors.color} 18%, transparent)`,
                  color: colors.color,
                }}
              >
                <Icon name={
                  callState === 'Ringing' ? 'callIn' :
                  callState === 'Dialing' ? 'callOut' : 'phoneTalk'
                } size={22} />
              </span>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                <span className="hero-number">{demo.number}</span>
                {demo.name ? <span className="hero-name">{demo.name}</span> : null}
              </div>
              {demo.duration ? (
                <span className="hero-duration">{demo.duration}</span>
              ) : null}
            </div>
          </div>
        ) : (
          <div className="hero-empty">
            <Icon name="phone" size={20} />
            Lift the handset to place a call, or wait for an incoming ring.
          </div>
        )}

        {/* Actions — contextual to state */}
        <div className="hero-actions">
          {callState === 'Ringing' && (
            <>
              <button type="button" className="phone-btn btn-answer" onClick={onAnswer}>
                <Icon name="phone" size={20} /> Answer
              </button>
              <button type="button" className="phone-btn btn-hangup" onClick={onHangup}>
                <Icon name="phoneOff" size={20} /> Reject
              </button>
              <button type="button" className="phone-btn btn-ghost">
                <Icon name="ringVolume" size={20} /> Silence
              </button>
            </>
          )}
          {callState === 'InCall' && (
            <>
              <button type="button" className="phone-btn btn-hangup" onClick={onHangup}>
                <Icon name="phoneOff" size={20} /> Hang Up
              </button>
              <button type="button" className="phone-btn btn-ghost">
                <Icon name="mic" size={20} /> Mute
              </button>
              <button type="button" className="phone-btn btn-ghost">
                <Icon name="dialpad" size={20} /> Keypad
              </button>
              <button type="button" className="phone-btn btn-ghost">
                <Icon name="swap" size={20} /> Move to Soundbar
              </button>
            </>
          )}
          {callState === 'Dialing' && (
            <>
              <button type="button" className="phone-btn btn-hangup" onClick={onHangup}>
                <Icon name="phoneOff" size={20} /> Cancel
              </button>
              <button type="button" className="phone-btn btn-ghost">
                <Icon name="speaker" size={20} /> Speaker
              </button>
            </>
          )}
          {callState === 'Idle' && (
            <>
              <button type="button" className="phone-btn" onClick={onDial}>
                <Icon name="dialpad" size={20} /> New Call
              </button>
              <button type="button" className="phone-btn btn-ghost">
                <Icon name="contacts" size={20} /> Pick Contact
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
};

// ─── Dev Drawer ───────────────────────────────────────────────────
const DevDrawer = ({ expanded, onToggle, callState, onSimulate, dialDigits, onDialDigitsChange, onSimulateDial }) => (
  <div className={`dev-drawer ${expanded ? 'expanded' : 'collapsed'}`}>
    <div className="dev-header" onClick={onToggle} role="button" aria-expanded={expanded}>
      <div className="left">
        <Icon name="settings" size={14} />
        Dev Tray · Simulate Hardware Events
      </div>
      <div className="right">
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
          {!expanded && <span className="dot-pulse" style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--signal-amber)', display: 'inline-block' }} />}
          {expanded ? 'Click to collapse' : 'Click to expand'}
          <Icon name="caret" size={14} style={{ transform: expanded ? 'rotate(180deg)' : '' }} />
        </span>
      </div>
    </div>
    {expanded && (
      <div className="dev-body">
        <div className="dev-section">
          <span className="dev-label">Handset</span>
          <div className="dev-buttons">
            <button
              type="button"
              className="btn btn-success"
              disabled={callState === 'InCall'}
              onClick={() => onSimulate('lift')}
            >
              <Icon name="phone" size={14} /> Lift
            </button>
            <button
              type="button"
              className="btn btn-danger"
              disabled={callState === 'Idle'}
              onClick={() => onSimulate('drop')}
            >
              <Icon name="phoneOff" size={14} /> Drop
            </button>
          </div>
        </div>

        <div className="dev-section">
          <span className="dev-label">Network</span>
          <div className="dev-buttons">
            <button type="button" className="btn btn-warn" onClick={() => onSimulate('incoming')}>
              <Icon name="ringVolume" size={14} /> Incoming Call
            </button>
          </div>
        </div>

        <div className="dev-section">
          <span className="dev-label">Dialer</span>
          <div className="dev-buttons">
            <input
              className="input"
              placeholder="Digits"
              value={dialDigits}
              onChange={(e) => onDialDigitsChange(e.target.value)}
              style={{ flex: 1, minWidth: 120 }}
            />
            <button type="button" className="btn" onClick={onSimulateDial} disabled={!dialDigits.trim()}>
              <Icon name="dialpad" size={14} /> Dial
            </button>
          </div>
        </div>
      </div>
    )}
  </div>
);

// ─── Compose the dashboard ────────────────────────────────────────
const PhoneDashboard = ({ callState, setCallState, activeMode, setActiveMode, devExpanded, setDevExpanded }) => {
  const [dialDigits, setDialDigits] = React.useState('');

  const simulate = (kind) => {
    if (kind === 'lift')     setCallState('Dialing');
    if (kind === 'drop')     setCallState('Idle');
    if (kind === 'incoming') setCallState('Ringing');
  };
  const onSimulateDial = () => {
    if (dialDigits.trim()) {
      setCallState('Dialing');
      setDialDigits('');
    }
  };

  return (
    <div className="dashboard">
      <PhoneStatusHero
        callState={callState}
        onAnswer={() => setCallState('InCall')}
        onHangup={() => setCallState('Idle')}
        onDial={() => setCallState('Dialing')}
      />
      {/* Right column rows */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12, gridColumn: 2, gridRow: '1 / 3', minHeight: 0, overflow: 'hidden' }}>
        <SystemStatusCard />
        <CallPathCard
          activeMode={activeMode}
          onSwitchMode={setActiveMode}
          bridgeConnected={true}
          trunkRegistered={false}
        />
      </div>
      {/* Dev drawer only spans left column; right column stretches full height */}
      <div style={{ gridColumn: 1, gridRow: 2 }}>
        <DevDrawer
          expanded={devExpanded}
          onToggle={() => setDevExpanded(!devExpanded)}
          callState={callState}
          onSimulate={simulate}
          dialDigits={dialDigits}
          onDialDigitsChange={setDialDigits}
          onSimulateDial={onSimulateDial}
        />
      </div>
    </div>
  );
};

Object.assign(window, { PhoneDashboard });
