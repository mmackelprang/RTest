// Call History tab — list + stats rail

const HistoryPage = () => {
  const [filter, setFilter] = React.useState('all');

  const filtered = React.useMemo(() => {
    if (filter === 'all') return MOCK_HISTORY;
    return MOCK_HISTORY.filter(h => h.dir === filter);
  }, [filter]);

  const dirIcon = (dir) => dir === 'in' ? 'callIn' : dir === 'out' ? 'callOut' : 'callMiss';
  const dirLabel = (dir) => dir === 'in' ? 'Incoming' : dir === 'out' ? 'Outgoing' : 'Missed';

  // Quick stats
  const stats = React.useMemo(() => {
    const total = MOCK_HISTORY.length;
    const missed = MOCK_HISTORY.filter(h => h.dir === 'miss').length;
    const inbound = MOCK_HISTORY.filter(h => h.dir === 'in').length;
    const outbound = MOCK_HISTORY.filter(h => h.dir === 'out').length;
    return { total, missed, inbound, outbound };
  }, []);

  return (
    <div className="history-page">
      <div className="history-list">
        <div className="history-filter-row">
          {[
            { id: 'all',  label: 'All',      count: stats.total },
            { id: 'in',   label: 'Incoming', count: stats.inbound },
            { id: 'out',  label: 'Outgoing', count: stats.outbound },
            { id: 'miss', label: 'Missed',   count: stats.missed },
          ].map((f) => (
            <button
              key={f.id}
              type="button"
              className={`filter-pill ${filter === f.id ? 'active' : ''}`}
              onClick={() => setFilter(f.id)}
            >
              {f.label}
              <span style={{ opacity: 0.7 }}>· {f.count}</span>
            </button>
          ))}
          <span className="spacer" />
          <button type="button" className="btn">
            <Icon name="trash" size={14} /> Clear History
          </button>
        </div>

        <div className="col-headers" style={{ gridTemplateColumns: '32px 1fr 140px 100px 80px 80px' }}>
          <span />
          <span>Caller</span>
          <span>Number</span>
          <span>When</span>
          <span>Answered</span>
          <span style={{ textAlign: 'right' }}>Duration</span>
        </div>

        <div className="history-rows">
          {filtered.map((h) => (
            <div key={h.id} className="history-row">
              <Icon name={dirIcon(h.dir)} size={20} color={
                h.dir === 'in' ? 'var(--signal-green)' :
                h.dir === 'out' ? 'var(--signal-blue)' : 'var(--signal-red)'
              } />
              <div style={{ display: 'flex', flexDirection: 'column' }}>
                <span style={{ fontSize: 14, fontWeight: 500, color: h.name === 'Unknown' ? 'var(--text-low)' : 'var(--text-high)' }}>
                  {h.name}
                </span>
                <span style={{ fontFamily: 'var(--font-mono)', fontSize: 10, letterSpacing: '0.10em', textTransform: 'uppercase', color: 'var(--text-low)' }}>
                  {dirLabel(h.dir)}
                </span>
              </div>
              <span className="when" style={{ fontFamily: 'var(--font-mono)', fontSize: 13, color: 'var(--text-medium)' }}>
                {h.number}
              </span>
              <span className="when">{h.when}</span>
              <span>
                {h.answeredOn ? (
                  <span className={`pill ${h.answeredOn === 'RotaryPhone' ? 'amber' : 'cyan'}`}>
                    {h.answeredOn === 'RotaryPhone' ? 'Rotary' : 'GV'}
                  </span>
                ) : (
                  <span className="pill gray">—</span>
                )}
              </span>
              <span className="duration" style={{ textAlign: 'right', color: h.dir === 'miss' ? 'var(--text-low)' : 'var(--text-medium)' }}>
                {h.duration}
              </span>
            </div>
          ))}
        </div>
      </div>

      {/* Stats rail */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <div className="stat-tile">
          <span className="lbl">Total Calls · 30 days</span>
          <span className="val">{stats.total}</span>
          <span className="sub">{stats.inbound} in · {stats.outbound} out · {stats.missed} missed</span>
        </div>
        <div className="stat-tile">
          <span className="lbl">Missed</span>
          <span className="val" style={{ color: 'var(--signal-red)', textShadow: '0 0 8px var(--signal-red-glow)' }}>
            {stats.missed}
          </span>
          <span className="sub">2 today · 1 yesterday</span>
        </div>
        <div className="card accent-amber" style={{ padding: '14px 16px' }}>
          <div className="card-title title-amber">
            <span className="title-text">Top Caller</span>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <div className="contact-avatar" style={{ width: 40, height: 40, fontSize: 13 }}>AC</div>
            <div style={{ display: 'flex', flexDirection: 'column' }}>
              <span style={{ fontSize: 14, fontWeight: 600 }}>Anderson, Carol</span>
              <span style={{ fontFamily: 'var(--font-mono)', fontSize: 11, color: 'var(--text-low)', letterSpacing: '0.10em', textTransform: 'uppercase' }}>
                4 calls · 38 min total
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

Object.assign(window, { HistoryPage });
