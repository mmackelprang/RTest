// Main app — composes Topbar + content area + tab rail.
// Includes a few placeholder pages (Home, Devices, System) so navigation
// gives context for the Phone page redesign.

const TWEAK_DEFAULTS = /*EDITMODE-BEGIN*/{
  "tabStyle": "left",
  "devDrawerOpen": false,
  "callState": "Idle",
  "showOtherPages": true
}/*EDITMODE-END*/;

// ── Placeholder pages for other routes (just for nav context) ──
const PlaceholderPage = ({ title, icon, note, tabs }) => (
  <div className="phone-shell" style={{ gridTemplateColumns: tabs ? '156px 1fr' : '1fr' }}>
    {tabs && (
      <div className="tab-rail">
        <span className="rail-heading">{title}</span>
        {tabs.map((t, i) => (
          <button key={t} type="button" className={`rail-tab ${i === 0 ? 'active' : ''}`}>
            <span className="rail-label">{t}</span>
          </button>
        ))}
      </div>
    )}
    <div className="placeholder-page">
      <Icon name={icon} size={64} />
      <div className="text">{title}</div>
      <div className="sub">{note}</div>
    </div>
  </div>
);

// ── Phone page shell — tab rail or top tabs ──
const PhonePage = ({ tabStyle, callState, setCallState, devExpanded, setDevExpanded }) => {
  const [tab, setTab] = React.useState('dashboard');
  const [activeMode, setActiveMode] = React.useState('BluetoothHfp');

  const tabs = [
    { id: 'dashboard', label: 'Dashboard',     icon: 'dashboard' },
    { id: 'contacts',  label: 'Contacts',      icon: 'contacts' },
    { id: 'history',   label: 'Call History',  icon: 'history' },
  ];

  const Body = () => {
    if (tab === 'dashboard') {
      return (
        <PhoneDashboard
          callState={callState}
          setCallState={setCallState}
          activeMode={activeMode}
          setActiveMode={setActiveMode}
          devExpanded={devExpanded}
          setDevExpanded={setDevExpanded}
        />
      );
    }
    if (tab === 'contacts') return <ContactsPage />;
    if (tab === 'history')  return <HistoryPage />;
    return null;
  };

  if (tabStyle === 'top') {
    return (
      <div className="phone-shell" style={{ gridTemplateColumns: '1fr' }}>
        <div style={{ display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden' }}>
          <div className="top-tabs">
            {tabs.map((t) => (
              <button
                key={t.id}
                type="button"
                className={`top-tab ${tab === t.id ? 'active' : ''}`}
                onClick={() => setTab(t.id)}
              >
                <Icon name={t.icon} size={14} />
                {t.label}
              </button>
            ))}
          </div>
          <div style={{ flex: 1, overflow: 'hidden', minHeight: 0 }}>
            <Body />
          </div>
        </div>
      </div>
    );
  }

  // Left rail (default, matches System/Devices pages)
  return (
    <div className="phone-shell">
      <div className="tab-rail">
        <span className="rail-heading">Phone</span>
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            className={`rail-tab ${tab === t.id ? 'active' : ''}`}
            onClick={() => setTab(t.id)}
          >
            <Icon name={t.icon} size={18} />
            <span className="rail-label">{t.label}</span>
          </button>
        ))}
      </div>
      <div style={{ overflow: 'hidden', minHeight: 0 }}>
        <Body />
      </div>
    </div>
  );
};

// ── Root App ──
const App = () => {
  const [tweaks, setTweak] = (window.useTweaks
    ? window.useTweaks(TWEAK_DEFAULTS)
    : (() => {
        const [t, setT] = React.useState(TWEAK_DEFAULTS);
        return [t, (a, b) => setT((x) => typeof a === 'object' ? { ...x, ...a } : { ...x, [a]: b })];
      })()
  );

  const [currentPage, setCurrentPage] = React.useState('phone');
  const [callState, setCallStateInner] = React.useState(tweaks.callState || 'Idle');
  const [devExpanded, setDevExpanded] = React.useState(!!tweaks.devDrawerOpen);

  // Keep tweak-state and local state in sync (one-way: tweak → local)
  React.useEffect(() => { setCallStateInner(tweaks.callState); }, [tweaks.callState]);
  React.useEffect(() => { setDevExpanded(!!tweaks.devDrawerOpen); }, [tweaks.devDrawerOpen]);

  const setCallState = (v) => { setCallStateInner(v); setTweak('callState', v); };

  return (
    <div className="layout-container" data-screen-label="01 Phone Page Redesign">
      <Topbar currentPage={currentPage} onNavigate={setCurrentPage} />
      <div className="content-area">
        {currentPage === 'phone' && (
          <PhonePage
            tabStyle={tweaks.tabStyle}
            callState={callState}
            setCallState={setCallState}
            devExpanded={devExpanded}
            setDevExpanded={(v) => { setDevExpanded(v); setTweak('devDrawerOpen', v); }}
          />
        )}
        {currentPage === 'home' && (
          <PlaceholderPage
            title="Home"
            icon="home"
            note="Now Playing · Queue · Visualizer — unchanged"
          />
        )}
        {currentPage === 'devices' && (
          <PlaceholderPage
            title="Devices"
            icon="devices"
            note="Display · Outputs · Cast · Inputs — light consistency pass"
            tabs={['Display', 'Outputs', 'Cast', 'Inputs']}
          />
        )}
        {currentPage === 'system' && (
          <PlaceholderPage
            title="Settings"
            icon="settings"
            note="System Stats · Configuration · Secrets — light consistency pass"
            tabs={['System Stats', 'Configuration', 'Secrets', 'Audio Engine', 'Integrations']}
          />
        )}
        {currentPage === 'queue'   && <PlaceholderPage title="Queue"   icon="queue"   note="Routes to Home queue panel" />}
        {currentPage === 'metrics' && <PlaceholderPage title="Metrics" icon="metrics" note="Metrics dashboard — unchanged" />}
        {currentPage === 'history' && <PlaceholderPage title="History" icon="history" note="Play history — unchanged" />}
        {currentPage === 'sleep'   && <PlaceholderPage title="Sleep"   icon="sleep"   note="Sleep route — unchanged" />}
      </div>

      {/* Tweaks panel (only shown when host toggles edit mode) */}
      {window.TweaksPanel && (
        <window.TweaksPanel title="Tweaks">
          <window.TweakSection label="Layout">
            <window.TweakRadio
              label="Tab style"
              value={tweaks.tabStyle}
              onChange={(v) => setTweak('tabStyle', v)}
              options={[
                { label: 'Left rail',  value: 'left' },
                { label: 'Top tabs',   value: 'top' },
              ]}
            />
          </window.TweakSection>

          <window.TweakSection label="Phone state preview">
            <window.TweakRadio
              label="Call state"
              value={tweaks.callState}
              onChange={(v) => setTweak('callState', v)}
              options={[
                { label: 'Idle',    value: 'Idle' },
                { label: 'Ringing', value: 'Ringing' },
                { label: 'InCall',  value: 'InCall' },
                { label: 'Dialing', value: 'Dialing' },
              ]}
            />
          </window.TweakSection>

          <window.TweakSection label="Dev tray">
            <window.TweakToggle
              label="Open by default"
              value={tweaks.devDrawerOpen}
              onChange={(v) => setTweak('devDrawerOpen', v)}
            />
          </window.TweakSection>

          <window.TweakSection label="Quick nav">
            <window.TweakSelect
              label="Show page"
              value={currentPage}
              onChange={setCurrentPage}
              options={[
                { label: 'Phone',    value: 'phone' },
                { label: 'Home',     value: 'home' },
                { label: 'Devices',  value: 'devices' },
                { label: 'Settings', value: 'system' },
              ]}
            />
          </window.TweakSection>
        </window.TweaksPanel>
      )}
    </div>
  );
};

const root = ReactDOM.createRoot(document.getElementById('root'));
root.render(<App />);
