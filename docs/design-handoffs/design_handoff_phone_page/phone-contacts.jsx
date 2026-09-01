// Contacts tab — list with detail rail

const ContactsPage = () => {
  const [search, setSearch] = React.useState('');
  const [selectedId, setSelectedId] = React.useState('1');
  const [isSyncing, setIsSyncing] = React.useState(false);

  const filtered = React.useMemo(() => {
    if (!search.trim()) return MOCK_CONTACTS;
    const q = search.toLowerCase();
    return MOCK_CONTACTS.filter(c =>
      c.name.toLowerCase().includes(q) ||
      c.phone.replace(/[^0-9]/g, '').includes(q.replace(/[^0-9]/g, ''))
    );
  }, [search]);

  const selected = MOCK_CONTACTS.find(c => c.id === selectedId);

  const handleSync = () => {
    setIsSyncing(true);
    setTimeout(() => setIsSyncing(false), 1800);
  };

  return (
    <div className="contacts">
      <div className="contacts-list">
        <div className="contacts-toolbar">
          <div className="toolbar-left">
            <div className="search-wrap">
              <Icon name="search" size={16} />
              <input
                className="search"
                placeholder="Search contacts…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
            <span style={{ fontFamily: 'var(--font-mono)', fontSize: 11, color: 'var(--text-low)', letterSpacing: '0.10em', textTransform: 'uppercase' }}>
              {filtered.length} of {MOCK_CONTACTS.length}
            </span>
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <button type="button" className="btn" onClick={handleSync} disabled={isSyncing}>
              <Icon name="smartphone" size={14} />
              {isSyncing ? 'Syncing…' : 'Sync from Phone'}
            </button>
            <button type="button" className="btn btn-success">
              <Icon name="add" size={14} /> Add Contact
            </button>
          </div>
        </div>

        <div className="col-headers">
          <span />
          <span>Name</span>
          <span>Phone</span>
          <span>Source</span>
          <span style={{ textAlign: 'right' }}>Actions</span>
        </div>

        <div className="contact-rows">
          {filtered.map((c) => (
            <div
              key={c.id}
              className={`contact-row ${selectedId === c.id ? 'selected' : ''}`}
              onClick={() => setSelectedId(c.id)}
            >
              <div className="contact-avatar" style={c.favorite ? { background: 'rgba(240,168,48,0.18)', color: 'var(--signal-amber)' } : null}>
                {initials(c.name)}
              </div>
              <div style={{ display: 'flex', flexDirection: 'column' }}>
                <span className="contact-name">{c.name}</span>
                {c.email ? (
                  <span style={{ fontSize: 11, color: 'var(--text-low)', fontFamily: 'var(--font-mono)' }}>{c.email}</span>
                ) : null}
              </div>
              <span className="contact-phone">{c.phone}</span>
              <span className={`pill ${c.source === 'pbap' ? 'blue' : 'gray'}`}>
                {c.source === 'pbap' ? 'PBAP' : 'Manual'}
              </span>
              <div className="row-actions">
                <button type="button" className="icon-btn" title="Call"><Icon name="phone" size={13} /></button>
                {c.source === 'manual' && (
                  <>
                    <button type="button" className="icon-btn" title="Edit"><Icon name="edit" size={13} /></button>
                    <button type="button" className="icon-btn danger" title="Delete"><Icon name="trash" size={13} /></button>
                  </>
                )}
              </div>
            </div>
          ))}
        </div>
      </div>

      <div className="contacts-detail">
        <div className="sync-card">
          <div className="card-title title-blue">
            <span className="title-text">PBAP Sync</span>
            <span className="pill green">Fresh</span>
          </div>
          <div className="device-strip">
            <span className="conn-icon" style={{ background: 'rgba(96,165,250,0.14)', color: 'var(--signal-blue)' }}>
              <Icon name="smartphone" size={18} />
            </span>
            <div style={{ display: 'flex', flexDirection: 'column' }}>
              <span className="name">Carol's iPhone</span>
              <span className="meta">5 of 12 · 4 min ago</span>
            </div>
          </div>
          <button type="button" className="btn" onClick={handleSync} disabled={isSyncing}>
            <Icon name="refresh" size={14} /> {isSyncing ? 'Syncing…' : 'Sync Now'}
          </button>
        </div>

        <div className="detail-card">
          {selected ? (
            <>
              <div className="detail-avatar" style={selected.favorite ? { background: 'rgba(240,168,48,0.18)', color: 'var(--signal-amber)' } : null}>
                {initials(selected.name)}
              </div>
              <div className="detail-name">{selected.name}</div>
              <div className="detail-rows">
                <div className="detail-row">
                  <span className="lbl">Phone</span>
                  <span className="val">{selected.phone}</span>
                </div>
                {selected.email ? (
                  <div className="detail-row">
                    <span className="lbl">Email</span>
                    <span className="val" style={{ fontSize: 12 }}>{selected.email}</span>
                  </div>
                ) : null}
                <div className="detail-row">
                  <span className="lbl">Source</span>
                  <span className="val">
                    <span className={`pill ${selected.source === 'pbap' ? 'blue' : 'gray'}`}>
                      {selected.source === 'pbap' ? 'PBAP · Read-only' : 'Manual'}
                    </span>
                  </span>
                </div>
              </div>
              <div className="detail-actions">
                <button type="button" className="btn btn-success" style={{ flex: 1 }}>
                  <Icon name="phone" size={14} /> Call
                </button>
                {selected.source === 'manual' && (
                  <button type="button" className="btn"><Icon name="edit" size={14} /></button>
                )}
              </div>
            </>
          ) : (
            <div className="empty">
              <Icon name="contacts" />
              Select a contact to see details
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

Object.assign(window, { ContactsPage });
