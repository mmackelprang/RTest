# Prompt for Claude Code CLI

Copy-paste the prompt below into Claude Code. Run it from the `RTest/` repo root with the
`design_handoff_phone_page/` folder placed somewhere accessible (e.g. unzipped to your
desktop, or already inside the repo).

---

## Prompt

> I'm handing off a design redesign for the `/phone` page in the Radio.Web app. The handoff
> bundle is at `~/Downloads/design_handoff_phone_page/` (adjust path as needed). Please:
>
> 1. **Read the handoff bundle first**, in this order:
>    - `design_handoff_phone_page/README.md` — overview, scope, design tokens, icon map.
>    - `design_handoff_phone_page/IMPLEMENTATION.md` — section-by-section change script.
>    - `design_handoff_phone_page/Phone Page Redesign.html` — open this in a browser at
>      1920×720. Toggle Tweaks in the toolbar to cycle through the four call states (Idle,
>      Ringing, InCall, Dialing) and to see Contacts / Call History tabs. This is the
>      source of truth for layout, colour, and spacing.
>    - `design_handoff_phone_page/screenshots/` — pre-captured PNGs of every state at native
>      1920×720. Use them as visual targets when comparing your Razor output against the
>      design. See `screenshots/README.md` for the file index.
>    - `design_handoff_phone_page/styles.css` and the `phone-*.jsx` files for the exact CSS
>      values and component structures to mirror.
>
> 2. **Read the existing project files** the handoff references:
>    - `RTest/CLAUDE.md`
>    - `src/Radio.Web/Components/Pages/PhonePage.razor` and `PhonePage.razor.css`
>    - `src/Radio.Web/wwwroot/css/design-system.css` (especially §2 tokens, §5 topbar, §7
>      nav pills — the prototype reuses these)
>    - `docs/design-handoffs/design_handoff_radio_console/IMPLEMENTATION.md` for prior
>      patterns and the project's standard handoff voice.
>
> 3. **Implement the redesign** by following `IMPLEMENTATION.md` section by section (P0·1
>    through P0·7). Each section is a small landable PR-sized chunk. Stop after each
>    section and run `dotnet build` + `dotnet test --filter Radio.Web` to make sure nothing
>    broke before moving on.
>
> 4. **Critical constraints:**
>    - This is a **Blazor Server + Radzen Blazor** UI. The HTML/JSX in the handoff is a
>      design reference, NOT code to port directly. Recreate the design using Razor
>      components and existing Radzen primitives (RadzenIcon, RadzenButton, RadzenBadge,
>      etc.) where they exist. Use the icon mapping table in `README.md` to translate
>      prototype SVG names to Radzen Material icon names.
>    - The viewport is a fixed **1920×720 kiosk**. The content area is exactly **600px
>      tall** (after the 120px top bar). The redesigned page MUST fit without vertical
>      scrollbars on any of the three tabs at default state.
>    - Reuse design tokens from `design-system.css`. **Do not invent new colours or fonts.**
>      The handoff explicitly lists which new selectors to add (in P0·1) and they are all
>      composed from existing tokens.
>    - **Do not rewrite the SignalR / API integration** in `PhonePage.razor` — the
>      `_callState`, `_systemStatus`, `_gvActiveMode`, etc. state machine stays. Only the
>      markup that consumes those values changes.
>    - **Do not duplicate** what's already in `design-system.css`. The README lists the
>      tokens and rules that are already there.
>
> 5. **Ask before any of these:**
>    - Adding new API methods to `PhoneApiService` (e.g. dedicated `AnswerAsync` / `HangupAsync`
>      if they don't exist) — see the "Risks / open questions" section in `IMPLEMENTATION.md`.
>    - Changing the existing PBAP sync dropdown UX vs. the simpler one-tap variant in the
>      prototype.
>    - Wiring the "Move to Soundbar" button if the underlying transfer feature isn't built —
>      default behaviour is to disable it with a tooltip.
>    - Cleanups beyond the Phone page (the README's "Out of scope" section explicitly lists
>      Bluetooth and System pages as follow-up work, not this PR).
>
> 6. **Verify each PR-sized chunk** before moving to the next:
>    - Build: `dotnet build --configuration Release`
>    - Tests: `dotnet test --filter "FullyQualifiedName~PhonePage" --verbosity normal`
>    - Eyeball: deploy locally (`dotnet run --project src/Radio.Web`) and open
>      `http://localhost:5002/phone` at 1920×720. Compare side-by-side with the prototype
>      HTML.
>
> 7. **When everything in `IMPLEMENTATION.md` P0·1 through P0·7 is landed:**
>    - Run the full test suite once more.
>    - Take a fresh `screenshots/phone.png` of the kiosk at 1920×720 so the next design pass
>      starts from an updated baseline.
>    - Write a brief PR description summarising what changed, with before/after screenshots,
>      mapped to the P0 sections.
>
> The cross-service boundary in `CLAUDE.md` does NOT apply to this work — the Phone page
> redesign is pure UI inside Radio.Web. No `bluetoothctl` or WirePlumber changes are needed.
>
> Begin with P0·1 (CSS additions), pause when complete, show me a diff, and we'll iterate
> from there.

---

## Notes for the human handing this off

- The prompt assumes the agent has filesystem access to both the repo and the handoff
  bundle. Adjust the bundle path to wherever you've unzipped it.
- If you want the agent to land all P0 sections in one shot rather than pausing per
  section, change "Begin with P0·1, pause when complete" to "Land P0·1 through P0·7
  as a single PR, then summarise".
- The agent will likely ask about the API surface for Answer / Hang Up endpoints. Be
  ready to answer:
  - **Option A** (safer): keep the existing simulate-only endpoints and gate the Hero
    action buttons behind feature flags until proper endpoints exist.
  - **Option B** (preferred): land the new API endpoints as part of this PR, in
    `PhoneApiService` + the corresponding controller.
- If the agent's first diff produces > 1000 lines of changes, push back — `IMPLEMENTATION.md`
  is designed for ~150 LOC per section. A larger diff usually means duplicate CSS or
  bypassed Radzen components.
