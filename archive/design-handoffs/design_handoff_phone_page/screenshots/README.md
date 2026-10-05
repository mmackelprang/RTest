# Screenshots — reference renders of the prototype

These are static captures of `Phone Page Redesign.html` at native 1920×720. Use them as
side-by-side targets when implementing the Razor version.

| File | Description |
|---|---|
| `01-dashboard-idle.png` | Dashboard tab, **Idle** state — empty hint, "New Call" / "Pick Contact" actions. |
| `02-dashboard-ringing.png` | Dashboard tab, **Ringing** state — amber hero, pulsing animation, Answer/Reject/Silence actions. |
| `03-dashboard-incall.png` | Dashboard tab, **InCall** state — green hero, LED duration counter, Hang Up/Mute/Keypad/Move-to-Soundbar actions. |
| `04-dashboard-dialing.png` | Dashboard tab, **Dialing** state — blue hero, Cancel/Speaker actions. |
| `05-dashboard-dev-tray-expanded.png` | Dashboard tab, dev tray expanded — Handset/Network/Dialer columns visible. |
| `06-contacts.png` | Contacts tab — list + detail rail. |
| `07-history.png` | Call History tab — filter pills + stats rail. |

When verifying the Razor implementation, compare 1:1 against these renders. Any pixel-scale
divergence (font weight, spacing, colour) is a regression to fix before merging.
