# Anderson Console — brand guide

**Proposed display name:** Anderson Console
**Tagline:** *Grandpa's radio, reborn.*

## Why this name

The project's full title is *"Grandpa Anderson's Console Radio Remade"* — the repo name `RTest`
says nothing, and the code already calls itself Radio Console. **Anderson Console** keeps the
family name in the product, which is the whole point of the restoration, and reads like a
mid-century radio marque (Philco, Zenith, *Anderson*).

**Alternates considered:** *Radio Console* (current de-facto name; descriptive, anonymous),
*Cathedral* (radio-shape nostalgia, obscure), *Heirloom Audio* (sentiment without specificity).

## The mark

A tabletop radio front — tuning dial, speaker grille, two knobs, brass trim — under a gable
line: the console in the house it belongs to. Warm cream-on-walnut, deliberately un-digital for
a project whose UI is Blazor but whose soul is 1940.

## Palette

| Color | Hex | Role |
|---|---|---|
| Walnut | `#5C3A21` | Background / primary brand color |
| Cream | `#F3E9DC` | Cabinet, cards, text on dark |
| Brass | `#C9A227` | Dial, trim, accents |

## Voice

Warm and mechanical: "tune", "dial", "warm up" instead of "configure", "select", "initialize".
The Web UI could adopt the walnut/cream/brass palette as its MudBlazor theme.

## Files in this directory

| File | Use |
|---|---|
| `logo.svg` | Full lockup (mark + wordmark + tagline) for README headers and docs |
| `favicon.svg` | Square app mark, scales from 16px to full size |
| `favicon.ico` | Legacy multi-size favicon (16/32/48) for browsers that want `.ico` |
| `favicon-32.png` | 32px PNG favicon |
| `apple-touch-icon.png` | 180px iOS home-screen icon |
| `icon-512.png` | Large raster for app manifests, social cards, stores |

### Wiring the favicon into a web page

```html
<link rel="icon" href="/branding/favicon.svg" type="image/svg+xml">
<link rel="icon" href="/branding/favicon.ico" sizes="16x16 32x32 48x48">
<link rel="apple-touch-icon" href="/branding/apple-touch-icon.png">
```

### README header

```markdown
<p align="center"><img src="branding/logo.svg" alt="Anderson Console" width="520"></p>
```

## Typography

Wordmark: **Montserrat Bold** (falls back to Segoe UI / system sans). Body text: the platform
default sans. For code-adjacent surfaces, any monospace at hand — the brand doesn't pin one.

The logo's wordmark is live SVG text, so it renders with whatever sans is installed; if you want
it pixel-identical everywhere, convert the text to outlines in any SVG editor and re-save.

## Dark and light backgrounds

The tile carries its own background, so both `logo.svg` and `favicon.svg` work unchanged on
light or dark pages. The wordmark in `logo.svg` is dark ink — on a dark page, either rely on the
tile alone (use `favicon.svg`) or restyle the two `<text>` fills to `#F0F2F5`.

---
*Generated as a proposal — names, colors, and marks are suggestions to accept, tweak, or reject.*
