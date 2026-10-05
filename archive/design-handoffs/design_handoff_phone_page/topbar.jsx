// Topbar — mirrors the real Radio Console chrome (read-only here).
// Two rows: primary command row + source bubble strip.

const Topbar = ({ currentPage, onNavigate, queueCount = 12 }) => {
  const [time, setTime] = React.useState(() => {
    const d = new Date();
    return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  });

  React.useEffect(() => {
    const t = setInterval(() => {
      const d = new Date();
      setTime(`${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`);
    }, 1000);
    return () => clearInterval(t);
  }, []);

  const navItems = [
    { id: 'home',    label: 'Home',     icon: 'home',     route: '/' },
    { id: 'queue',   label: 'Queue',    icon: 'queue',    route: '/queue',   badge: queueCount },
    { id: 'metrics', label: 'Metrics',  icon: 'metrics',  route: '/metrics' },
    { id: 'devices', label: 'Devices',  icon: 'devices',  route: '/devices' },
    { id: 'history', label: 'History',  icon: 'history',  route: '/history' },
    { id: 'system',  label: 'Settings', icon: 'settings', route: '/system' },
    { id: 'phone',   label: 'Phone',    icon: 'phone',    route: '/phone' },
    { id: 'sleep',   label: 'Sleep',    icon: 'sleep',    route: '/sleep' },
  ];

  return (
    <div className="topbar">
      {/* Row 1: primary */}
      <div className="topbar-primary">
        <div className="cluster">
          <span className="cluster-label">Time</span>
          <span className="cluster-value"><span className="font-led">{time}</span></span>
        </div>
        <div className="topbar-separator" />
        <div className="cluster">
          <span className="cluster-label">In</span>
          <span className="cluster-value">
            <span className="cluster-swatch" style={{ '--bubble-accent': 'var(--source-radio)' }} />
            FM/AM Radio
          </span>
        </div>
        <span className="topbar-arrow" aria-hidden="true">→</span>
        <div className="cluster">
          <span className="cluster-label">Out</span>
          <span className="cluster-value">
            <span className="cluster-swatch" style={{ '--bubble-accent': 'var(--source-bluetooth)' }} />
            Soundbar
          </span>
        </div>

        {/* Right-aligned nav pills */}
        <div className="topbar-nav">
          {navItems.map((it) => (
            <button
              key={it.id}
              type="button"
              className={`nav-pill ${currentPage === it.id ? 'nav-active' : ''}`}
              onClick={() => onNavigate(it.id)}
              aria-label={it.label}
            >
              <Icon name={it.icon} size={20} />
              <span className="nav-pill-label">{it.label}</span>
              {it.badge ? <span className="nav-badge">{it.badge}</span> : null}
            </button>
          ))}
        </div>
      </div>

      {/* Row 2: source bubble strip */}
      <div className="topbar-sources">
        {MOCK_SOURCES.map((s) => {
          const isActive = s.active;
          const accentColor = `var(--source-${s.accent})`;
          const style = isActive ? {
            '--bubble-bg': `color-mix(in oklab, ${accentColor} 14%, transparent)`,
            '--bubble-border': `color-mix(in oklab, ${accentColor} 45%, transparent)`,
            '--bubble-fg': accentColor,
            '--bubble-chip': `color-mix(in oklab, ${accentColor} 22%, transparent)`,
          } : {};
          return (
            <button
              key={s.id}
              type="button"
              className={`source-bubble ${isActive ? 'is-active' : ''}`}
              style={style}
            >
              <span className="bubble-chip"><Icon name={s.icon} size={16} /></span>
              <span className="bubble-label">{s.label}</span>
              {isActive ? <Icon name="chevron" size={14} style={{ marginLeft: 4, opacity: 0.7 }} /> : null}
            </button>
          );
        })}
      </div>
    </div>
  );
};

Object.assign(window, { Topbar });
