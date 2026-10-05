# Rotary Phone Integration Design

**Date:** 2026-03-11
**Status:** Approved
**Scope:** Integrate RotaryPhone project into Radio Console — UI in Radio.Web, RotaryPhone.API as separate service on same Ubuntu box

---

## Architecture

### Three Services on Ubuntu x64

| Service | Port | Role |
|---------|------|------|
| `radio-api.service` | 5000 | Audio engine, BT A2DP, all Radio hardware |
| `radio-web.service` | 5002 | Blazor UI (proxies to both APIs) |
| `rotary-phone.service` | 5004 | Phone call management, SIP/RTP, BT HFP |

### Bluetooth Sharing

- Single TP-Link UB500 adapter (`78:20:51:F5:FB:A7`) on `hci0`
- Radio.API owns the BlueZ agent — authorizes both A2DP and HFP profiles
- RotaryPhone.API is BT-passive — monitors D-Bus for HFP RFCOMM connections on already-paired devices
- Phone pairs once with "Grandpas Radio", gets both music streaming (A2DP) and call routing (HFP)
- Both services use `BluetoothMgmtMonitor` for disconnect reason detection — `reason=Remote` suppresses reconnection

### Network

- HT801 ATA connected via crossover Ethernet to Ubuntu box
- HT801 IP address configurable from Radio.Web UI (stored in RotaryPhone.API config)
- SIP on port 5060 UDP, RTP on port 49000

---

## RotaryPhone.API on Linux (Phase 1 — separate session in RotaryPhone repo)

### Porting from Windows

- Update `BlueZHfpAdapter.cs` for current BlueZ/D-Bus patterns
- Replace NAudio (Windows-only) with PipeWire/cross-platform audio for RTP-to-SCO bridge
- Add `BluetoothMgmtMonitor` for disconnect reason detection (same pattern as Radio Console)
- Configure port 5004, CORS for Radio.Web origins

### Systemd Service

```ini
[Service]
ExecStart=/opt/rotary-phone/RotaryPhoneController.Server
WorkingDirectory=/opt/rotary-phone
User=mmack
Environment=ASPNETCORE_URLS=http://0.0.0.0:5004
AmbientCapabilities=CAP_NET_ADMIN
NoNewPrivileges=false
```

### Deployment

- Deploy script similar to Radio Console's `Deploy-ToLinux.ps1`
- Target directory: `/opt/rotary-phone/`
- Preserves `appsettings.Production.json`, data directories

---

## Radio.Web UI Integration (Phase 2 — this repo)

### Navigation

Phone icon added to MainLayout.razor nav bar, before the sleep button:

```
Home | Queue | Metrics | Devices | History | System | [Phone] | Sleep
```

- Icon: `phone` (Material icon)
- Only visible when RotaryPhone.API is reachable (health check polling)
- Shows ringing/in-call indicator badge when call state is active

### PhonePage.razor — Single Page with 3 Tabs

#### Tab 1: Dashboard

**System Status card:**
- BT connected: yes/no + MAC address
- SIP listening: yes/no + listen address
- HT801 reachable: yes/no + IP address

**Phone Status card:**
- Large centered status chip: Idle (green), Ringing (yellow), InCall (red)
- Incoming/dialed number display
- Call duration timer (when in call)

**Developer Controls:**
- Collapsible `RadzenAccordion` section (collapsed by default)
- Lift/Drop Handset buttons (disabled based on current state)
- Simulate Incoming Call button
- Dial digits input + Dial button

#### Tab 2: Contacts

- `RadzenDataGrid` with columns: Name, Phone Number, Email, Actions (Edit/Delete)
- "Add Contact" button opens `RadzenDialog` with form fields
- Edit and delete with confirmation

#### Tab 3: Call History

- List view with direction icons (incoming green/red, outgoing blue)
- Phone number, timestamp, "Rotary Phone"/"Cell Phone" badge, duration
- "Clear History" button with confirmation
- Real-time updates via SignalR

### Real-Time Updates

- Radio.Web connects to RotaryPhone.API SignalR hub at `http://localhost:5004/hub`
- Events: `CallStateChanged`, `IncomingCall`, `CallHistoryUpdated`, `SystemStatusChanged`
- Phone icon in nav bar reflects live call state

### HT801 Configuration

- Add "Phone" section to SystemConfigPage.razor
- Fields: HT801 IP address, SIP port, RTP port
- Writes to RotaryPhone.API config endpoint

### Theme

- Match Radio Console's Radzen material-dark theme
- Do NOT replicate RotaryPhone's cyberpunk cyan/pink aesthetic
- Consistent look across the whole Radio Console UI

---

## RotaryPhone React UI Reference

The existing React UI (for replication fidelity):

### Dashboard
- System Status: grid of status items with color-coded badges (green=active, red=inactive)
- Phone Status: centered chip with state name, phone number display, call duration
- Developer Controls: handset simulation buttons, incoming call trigger, digit dialer

### Contacts
- MUI DataGrid with name/phone/email/actions columns
- Dialog modal for add/edit (name, phone, email fields)
- Inline delete with confirmation

### Call History
- List items: direction icon (arrow up/down), phone number (monospace), timestamp, device chip, duration
- Icons: incoming answered (green), incoming missed (red), outgoing (blue)
- Device badges: "Rotary Phone" or "Cell Phone" (based on answeredOn field)

### Data Models

```typescript
interface Contact {
  id: string;
  name: string;
  phoneNumber: string;
  email?: string;
  notes?: string;
  createdAt: string;
  modifiedAt: string;
}

interface CallHistoryEntry {
  id?: string;
  startTime: string;
  endTime?: string;
  duration?: string;
  direction: string;       // "Incoming" | "Outgoing"
  phoneNumber: string;
  answeredOn?: string;     // "RotaryPhone" | "CellPhone"
  phoneId?: string;
}

interface SystemStatus {
  platform: string;
  isRaspberryPi: boolean;
  bluetoothEnabled: boolean;
  bluetoothConnected: boolean;
  bluetoothDeviceAddress: string | null;
  sipListening: boolean;
  sipListenAddress: string | null;
  sipPort: number;
  ht801IpAddress: string | null;
  ht801Reachable: boolean | null;
}
```

### API Endpoints (RotaryPhone.API)

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/phone/status` | GET | Current call state |
| `/api/phone/system-status` | GET | Hardware/platform info |
| `/api/phone/simulate/hook` | POST | Handset on/off-hook |
| `/api/phone/simulate/dial` | POST | Dial input |
| `/api/phone/simulate/incoming` | POST | Trigger incoming call |
| `/api/contacts` | GET/POST | List/create contacts |
| `/api/contacts/{id}` | GET/PUT/DELETE | Read/update/delete contact |
| `/api/callhistory` | GET/DELETE | List/clear history |

### SignalR Events (Server → Client)

- `CallStateChanged(phoneId, state)`
- `IncomingCall(phoneId, phoneNumber)`
- `CallHistoryUpdated(entry)`
- `SystemStatusChanged(status)`

---

## Port Summary

| Service | HTTP | Other |
|---------|------|-------|
| Radio.API | 5000 | Audio stream: 8080 |
| Radio.Web | 5002 | — |
| RotaryPhone.API | 5004 | SIP: 5060/UDP, RTP: 49000 |

---

## Phasing

| Phase | Repo | Scope |
|-------|------|-------|
| 1 | RotaryPhone | Linux port: BlueZ HFP, PipeWire audio, mgmt monitor, deploy scripts |
| 2 | Radio Console | Radio.Web UI: PhonePage, nav bar icon, SignalR client, config section |
| 3 | Both | Deploy both, integration test BT sharing, full call flow |

Phase 2 is self-contained in the Radio Console repo and can proceed independently of Phase 1 (using mock/stub responses from RotaryPhone.API during development).

---

## Startup Order

```
radio-api.service → rotary-phone.service → radio-web.service
```

Radio.Web depends on both APIs. RotaryPhone depends on Radio.API being up first (BlueZ agent registration).

## CORS

RotaryPhone.API allows origins: `http://localhost:5002`, `http://radio:5002`
