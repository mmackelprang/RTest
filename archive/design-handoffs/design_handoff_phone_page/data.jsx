// Mock data + tiny icon set used across components
// Icons are rendered as inline SVG so we don't depend on a font.

const ICONS = {
  home: 'M3 12 12 3l9 9v9a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1z',
  queue: 'M3 6h13v2H3zm0 5h13v2H3zm0 5h8v2H3zm15-9v9.18a3 3 0 1 1-2-2.83V4h6v2z',
  metrics: 'M3 13h4v8H3zm7-9h4v17h-4zm7 5h4v12h-4z',
  devices: 'M4 6h7V4H4v2zm0 14h7v-2H4v2zm0-7h7v-2H4v2zm14-7v12h2V6zm-3 12h2V6h-2z',
  history: 'M13 3a9 9 0 1 0 9 9h-2a7 7 0 1 1-7-7V3zm-1 5v5l4 2 .7-1.4L13 11.7V8z',
  settings: 'M19.4 13a7.5 7.5 0 0 0 0-2l2-1.5-2-3.5-2.4 1a7.6 7.6 0 0 0-1.7-1L15 3h-4l-.3 2.5a7.6 7.6 0 0 0-1.7 1l-2.4-1-2 3.5L6.6 11a7.5 7.5 0 0 0 0 2l-2 1.5 2 3.5 2.4-1a7.6 7.6 0 0 0 1.7 1L11 21h4l.3-2.5a7.6 7.6 0 0 0 1.7-1l2.4 1 2-3.5zM13 15a3 3 0 1 1 0-6 3 3 0 0 1 0 6z',
  phone: 'M6.6 10.8c1.4 2.8 3.8 5.2 6.6 6.6l2.2-2.2c.3-.3.7-.4 1-.2 1.1.4 2.3.6 3.5.6.6 0 1 .4 1 1V20c0 .6-.4 1-1 1A17 17 0 0 1 3 4c0-.6.4-1 1-1h3.5c.6 0 1 .4 1 1 0 1.3.2 2.4.6 3.5.1.3 0 .7-.2 1z',
  phoneIn: 'M19 1l-4 4h3v4h2V5h3l-4-4zM6.6 10.8c1.4 2.8 3.8 5.2 6.6 6.6l2.2-2.2c.3-.3.7-.4 1-.2 1.1.4 2.3.6 3.5.6.6 0 1 .4 1 1V20c0 .6-.4 1-1 1A17 17 0 0 1 3 4c0-.6.4-1 1-1h3.5c.6 0 1 .4 1 1 0 1.3.2 2.4.6 3.5.1.3 0 .7-.2 1z',
  phoneTalk: 'M20 15.5c-1.3 0-2.5-.2-3.6-.6-.3-.1-.7 0-1 .2l-2.2 2.2c-2.8-1.4-5.2-3.8-6.6-6.6l2.2-2.2c.2-.3.3-.7.2-1A11 11 0 0 1 8.5 4c0-.6-.5-1-1-1H4c-.5 0-1 .4-1 1A17 17 0 0 0 20 21c.6 0 1-.5 1-1v-3.5c0-.5-.4-1-1-1zM12 3v10l3-3h6V3z',
  phoneOff: 'M4.4 4.6 3 6l3 3a17 17 0 0 0 8.5 8.6L17 20c0 .5.4 1 1 1A17 17 0 0 0 21 18l-1.4-1.4 1.4-1.4-5.6-5.6 1.4-1.4-1.4-1.4-1.4 1.4-2.2-2.2-1.4-1.4-1.4 1.4-1.4-1.4-1.4 1.4z',
  ringVolume: 'M7 4 5.5 2.5 1.5 6.5 3 8zM11 11h2V5h-2zm6 0 4-4-1.5-1.5-4 4zM21 16.5c-1.3 0-2.5-.2-3.7-.6h-.3c-.3 0-.5.1-.7.3l-2.2 2.2c-2.8-1.4-5.2-3.8-6.6-6.6L9.7 9.5c.3-.3.4-.7.3-1A12 12 0 0 1 9.5 4.7c0-.5-.4-1-1-1H5c-.5 0-1 .5-1 1A17 17 0 0 0 21 21.5c.5 0 1-.5 1-1v-3.5c0-.5-.4-1-1-1z',
  dial: 'M12 1.7 1.3 4l1.4 8.9a3 3 0 0 0 .9 1.7l8.4 8.4c.8.8 2 .8 2.8 0l7.5-7.5a2 2 0 0 0 0-2.8L13.9 4.3a3 3 0 0 0-1.7-.9zM7 8a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3z',
  bluetooth: 'm6.5 17 5.5-5.5L17.5 17 12 22.5 6.5 17zm0-10L12 1.5 17.5 7 12 12.5 6.5 7zm5.5 4.5L9 9l3 3 3-3z',
  sip: 'M21 17a6 6 0 0 1-6-6 6 6 0 0 1 6-6V3a8 8 0 0 0-8 8 8 8 0 0 0 8 8zM3 7l-2-2v6h6L4.5 8.5A6 6 0 0 1 12 3v-2A8 8 0 0 0 3 7zm14-1a3 3 0 0 0-3 3 3 3 0 0 0 3 3 3 3 0 0 0 3-3 3 3 0 0 0-3-3z',
  globe: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm6.9 6h-3a16 16 0 0 0-1.6-3.9 8 8 0 0 1 4.6 3.9zM12 4c.9 1.4 1.6 3 2 4h-4c.4-1 1.1-2.6 2-4zM4 14a8 8 0 0 1 0-4h3.4l-.2 2 .2 2zM4.6 16h3.3a16 16 0 0 0 1.7 4 8 8 0 0 1-5-4zm3.3-8H4.6a8 8 0 0 1 5-4 16 16 0 0 0-1.7 4zM12 20c-.9-1.4-1.6-3-2-4h4c-.4 1-1.1 2.6-2 4zm2.4-6H9.6l-.3-2 .3-2h4.8l.3 2zm.3 5.9A16 16 0 0 0 16 16h3a8 8 0 0 1-4.4 4zM16.4 14l.2-2-.2-2H20a8 8 0 0 1 0 4z',
  search: 'M15.5 14h-.8l-.3-.3a6.5 6.5 0 1 0-.7.7l.3.3v.8l5 5 1.5-1.5zM10 14a4 4 0 1 1 0-8 4 4 0 0 1 0 8z',
  add: 'M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6z',
  refresh: 'M17.7 6.3A8 8 0 1 0 20 12h-2a6 6 0 1 1-1.7-4.3L13 11h7V4z',
  edit: 'M3 17.2V21h3.8L17.8 9.9 14 6 3 17.2zM20.7 7.3a1 1 0 0 0 0-1.4l-2.6-2.6a1 1 0 0 0-1.4 0L15 5l3.8 3.8 1.9-1.5z',
  trash: 'M6 19a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V7H6zM19 4h-3.5L14.5 3h-5L8.5 4H5v2h14z',
  contacts: 'M20 0H4v2h16zM4 24h16v-2H4zM20 4H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2zm-8 2.8a2.8 2.8 0 1 1 0 5.6 2.8 2.8 0 0 1 0-5.6zM18 17H6v-1.2c0-1.9 3.9-2.8 6-2.8 2 0 6 .9 6 2.8z',
  smartphone: 'M17 1H7a2 2 0 0 0-2 2v18a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V3a2 2 0 0 0-2-2zm0 18H7V5h10z',
  dashboard: 'M3 13h8V3H3zm0 8h8v-6H3zm10 0h8V11h-8zm0-18v6h8V3z',
  callIn: 'M20 15.5c-1.3 0-2.5-.2-3.6-.6h-.3a1 1 0 0 0-.7.3l-2.2 2.2A15 15 0 0 1 6.6 11l2.2-2.2c.3-.3.4-.7.2-1a11.4 11.4 0 0 1-.6-3.6c0-.6-.4-1-1-1H4a1 1 0 0 0-1 1A17 17 0 0 0 20 21c.6 0 1-.5 1-1v-3.5c0-.5-.4-1-1-1zM18 11h2V3h-8v2h4.6L13 8.6 14.4 10 18 6.4z',
  callOut: 'M21 16.5c-1.3 0-2.5-.2-3.7-.6h-.3a1 1 0 0 0-.7.3l-2.2 2.2A15 15 0 0 1 7.6 12l2.2-2.2c.3-.3.4-.7.2-1A12 12 0 0 1 9.5 5.2c0-.5-.4-1-1-1H5a1 1 0 0 0-1 1A17 17 0 0 0 21 22c.6 0 1-.4 1-1v-3.5c0-.6-.4-1-1-1zM14 11h6V5l-2 2-3.5-3.5L13 5l3.5 3.5L14 11z',
  callMiss: 'M19.6 13.4 18.1 14 14 9.9V12h-2V7h5v2h-2.1l4 4 4.6-4.6L24 9.8 19.6 13.4zM12 13a16 16 0 0 0-5.6 5.5l-2.4-2.4a1 1 0 0 1 0-1.4A11 11 0 0 1 12 11z',
  chevron: 'M9 6 7.6 7.4 12.2 12l-4.6 4.6L9 18l6-6z',
  speaker: 'M3 9v6h4l5 5V4L7 9H3zm7-.2-3.5 3.5L3 12v0l3.5-.3L10 15.2zm6 3.2c0-1.8-1-3.3-2.5-4v8a4.5 4.5 0 0 0 2.5-4zM14 3.2v2A7 7 0 0 1 14 19v2a9 9 0 0 0 0-17.8z',
  mic: 'M12 14a3 3 0 0 0 3-3V5a3 3 0 0 0-6 0v6a3 3 0 0 0 3 3zm5.3-3a5.3 5.3 0 0 1-10.6 0H5a7 7 0 0 0 6 6.9V21h2v-3.1A7 7 0 0 0 19 11z',
  dialpad: 'M6 6a2 2 0 1 1-4 0 2 2 0 0 1 4 0zm6 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0zm6 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0zM6 12a2 2 0 1 1-4 0 2 2 0 0 1 4 0zm6 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0zm6 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0zM6 18a2 2 0 1 1-4 0 2 2 0 0 1 4 0zm6 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0zm6 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0z',
  swap: 'M16 17.5V15h-5v-2h5v-2.5l3.5 3.5L16 17.5zM8 6.5V9h5v2H8v2.5L4.5 10 8 6.5z',
  sleep: 'M20.4 13.4A8 8 0 1 1 10.6 3.6a7 7 0 0 0 9.8 9.8z',
  caret: 'M7 10 5.6 11.4 12 17.8l6.4-6.4L17 10l-5 5z',
};

const Icon = ({ name, size = 16, color = 'currentColor', style }) => {
  const path = ICONS[name];
  if (!path) return null;
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill={color} style={style} aria-hidden="true">
      <path d={path} />
    </svg>
  );
};

// ─── Mock data ─────────────────────────────────────────────────────
const MOCK_SOURCES = [
  { id: 'radio',    label: 'FM/AM Radio', icon: 'globe', accent: 'radio',    active: true },
  { id: 'vinyl',    label: 'Vinyl (Phono)', icon: 'speaker', accent: 'vinyl' },
  { id: 'file',     label: 'File Player', icon: 'queue', accent: 'file' },
  { id: 'usb',      label: 'USB Audio', icon: 'devices', accent: 'usb' },
  { id: 'bluetooth',label: 'Bluetooth', icon: 'bluetooth', accent: 'bluetooth' },
];

const MOCK_CONTACTS = [
  { id: '1', name: 'Anderson, Carol',   phone: '(919) 555-0142', source: 'manual', email: 'carol@example.com',   favorite: true },
  { id: '2', name: 'Anderson, Frank',   phone: '(919) 555-2871', source: 'pbap',   email: null },
  { id: '3', name: 'Bryant, Marcus',    phone: '(984) 555-9043', source: 'manual', email: 'marcus.b@example.org' },
  { id: '4', name: 'Coleman, Ruth',     phone: '(704) 555-1190', source: 'pbap',   email: null },
  { id: '5', name: 'Davis, Henry',      phone: '(252) 555-4422', source: 'manual', email: 'henry.d@radio.net' },
  { id: '6', name: 'Edwards, Patricia', phone: '(910) 555-7763', source: 'pbap',   email: null },
  { id: '7', name: 'Foster, James',     phone: '(828) 555-0034', source: 'pbap',   email: null },
  { id: '8', name: 'Gardner, Linda',    phone: '(336) 555-3398', source: 'manual', email: 'linda.gardner@example.com' },
  { id: '9', name: 'Hayes, Robert',     phone: '(919) 555-6612', source: 'pbap',   email: null },
  { id: '10',name: 'Ingram, Susan',     phone: '(252) 555-8801', source: 'manual', email: 'susan.i@example.com' },
  { id: '11',name: 'Jenkins, William',  phone: '(704) 555-5527', source: 'pbap',   email: null },
  { id: '12',name: 'Klein, Margaret',   phone: '(919) 555-0089', source: 'manual', email: 'mk@example.com',     favorite: true },
];

const MOCK_HISTORY = [
  { id: 'h1', dir: 'in',   name: 'Anderson, Carol',  number: '(919) 555-0142', when: 'Today 14:22', duration: '4:18',  answeredOn: 'RotaryPhone' },
  { id: 'h2', dir: 'miss', name: 'Bryant, Marcus',   number: '(984) 555-9043', when: 'Today 11:05', duration: '—',     answeredOn: null },
  { id: 'h3', dir: 'out',  name: 'Anderson, Frank',  number: '(919) 555-2871', when: 'Today 09:40', duration: '12:55', answeredOn: 'GVBrowser' },
  { id: 'h4', dir: 'in',   name: 'Davis, Henry',     number: '(252) 555-4422', when: 'Yesterday 19:11', duration: '2:08',  answeredOn: 'RotaryPhone' },
  { id: 'h5', dir: 'miss', name: 'Unknown',          number: '(800) 555-9921', when: 'Yesterday 16:34', duration: '—',     answeredOn: null },
  { id: 'h6', dir: 'out',  name: 'Foster, James',    number: '(828) 555-0034', when: 'Yesterday 12:01', duration: '0:47',  answeredOn: 'RotaryPhone' },
  { id: 'h7', dir: 'in',   name: 'Klein, Margaret',  number: '(919) 555-0089', when: 'Feb 19 · 20:48',  duration: '23:11', answeredOn: 'RotaryPhone' },
  { id: 'h8', dir: 'out',  name: 'Hayes, Robert',    number: '(919) 555-6612', when: 'Feb 19 · 14:22',  duration: '6:34',  answeredOn: 'GVBrowser' },
  { id: 'h9', dir: 'miss', name: 'Gardner, Linda',   number: '(336) 555-3398', when: 'Feb 18 · 09:15',  duration: '—',     answeredOn: null },
  { id: 'h10',dir: 'in',   name: 'Ingram, Susan',    number: '(252) 555-8801', when: 'Feb 17 · 17:50',  duration: '1:42',  answeredOn: 'RotaryPhone' },
];

const initials = (name) => {
  const parts = name.split(/[ ,]+/).filter(Boolean);
  if (!parts.length) return '?';
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[1][0] + parts[0][0]).toUpperCase();
};

Object.assign(window, { ICONS, Icon, MOCK_SOURCES, MOCK_CONTACTS, MOCK_HISTORY, initials });
