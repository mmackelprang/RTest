# DEBUG_AND_FIX_PLAN.md Quick Reference

This is a quick reference guide to the comprehensive [DEBUG_AND_FIX_PLAN.md](./DEBUG_AND_FIX_PLAN.md).

## How to Use This Plan

Each phase in the plan contains:
- **Context**: Background and rationale for the fix
- **Issues to Fix**: Specific problems being addressed  
- **Tasks**: Numbered sub-tasks with Copilot-ready prompts
- **Verification Steps**: How to test the completed work

### Using Copilot to Implement

For each task, simply copy the "Copilot Prompt" section and paste it into GitHub Copilot. The prompts are designed to be complete and actionable.

## 12 Phases Overview

| Phase | Title | Priority | Complexity |
|-------|-------|----------|------------|
| 1 | Global Now Playing & Music Controls | High | Medium |
| 2 | Music Queue Navbar Visibility | High | Low |
| 3 | Now Playing Metadata Display | Critical | Medium |
| 4 | Fingerprinting Debug & Instrumentation | Medium | High |
| 5 | RTL-SDR Radio Audio Output | Medium | High |
| 6 | Queue Page Enhancements | High | High |
| 7 | Visualizer Graphics Improvements | Medium | Medium |
| 8 | Home Page Media Controls | Critical | Low |
| 9 | File Browser Network & Drive Access | Medium | High |
| 10 | Spotify Loopback Implementation | Medium | Very High |
| 11 | Queue Page Touch UX Improvements | High | Medium |
| 12 | Overall Material 3 Design | Medium | High |

## Recommended Implementation Order

Based on dependencies and impact:

1. **Phase 3** - Now Playing Metadata (foundational)
2. **Phase 1** - Global Now Playing Widget (high visibility)
3. **Phase 2** - Queue Navbar Visibility (quick win)
4. **Phase 8** - Home Page Controls (critical functionality)
5. **Phase 7** - Visualizer Improvements (good visual impact)
6. **Phase 4** - Fingerprinting Debug (diagnostic)
7. **Phase 5** - RTL-SDR Audio (hardware-specific)
8. **Phase 6** - Queue Enhancements (multiple sub-tasks)
9. **Phase 11** - Queue Touch UX (builds on Phase 6)
10. **Phase 9** - File Browser Network Access (standalone)
11. **Phase 10** - Spotify Loopback (complex, can be later)
12. **Phase 12** - Material 3 Polish (final refinement)

## Quick Start

To begin implementation:

```bash
# 1. Review the full plan
cat DEBUG_AND_FIX_PLAN.md | less

# 2. Start with Phase 3 (metadata foundation)
# Copy the prompt from Phase 3, Task 3.1 and give it to Copilot

# 3. After completing each task, run verification steps

# 4. Commit and push changes frequently
git add .
git commit -m "Phase 3.1: Debug metadata flow"
git push
```

## Phase Quick Links

Jump directly to each phase in the main document:

- [Phase 1: Global Now Playing & Music Controls](./DEBUG_AND_FIX_PLAN.md#phase-1-global-now-playing--music-controls)
- [Phase 2: Music Queue Navbar Visibility](./DEBUG_AND_FIX_PLAN.md#phase-2-music-queue-navbar-visibility)
- [Phase 3: Now Playing Metadata Display](./DEBUG_AND_FIX_PLAN.md#phase-3-now-playing-metadata-display)
- [Phase 4: Fingerprinting Debug](./DEBUG_AND_FIX_PLAN.md#phase-4-fingerprinting-debug--instrumentation)
- [Phase 5: RTL-SDR Radio Audio Output](./DEBUG_AND_FIX_PLAN.md#phase-5-rtl-sdr-radio-audio-output)
- [Phase 6: Queue Page Enhancements](./DEBUG_AND_FIX_PLAN.md#phase-6-queue-page-enhancements)
- [Phase 7: Visualizer Graphics](./DEBUG_AND_FIX_PLAN.md#phase-7-visualizer-graphics-improvements)
- [Phase 8: Home Page Media Controls](./DEBUG_AND_FIX_PLAN.md#phase-8-home-page-media-controls)
- [Phase 9: File Browser Network Access](./DEBUG_AND_FIX_PLAN.md#phase-9-file-browser-network--drive-access)
- [Phase 10: Spotify Loopback](./DEBUG_AND_FIX_PLAN.md#phase-10-spotify-loopback-implementation)
- [Phase 11: Queue Page Touch UX](./DEBUG_AND_FIX_PLAN.md#phase-11-queue-page-touch-ux-improvements)
- [Phase 12: Material 3 Design](./DEBUG_AND_FIX_PLAN.md#phase-12-overall-material-3-design--touch-optimization)

## Testing Checklist

After implementing phases, use the comprehensive testing checklist:

- [Manual Testing Checklist](./DEBUG_AND_FIX_PLAN.md#manual-testing-checklist)
- [Performance Metrics](./DEBUG_AND_FIX_PLAN.md#performance-metrics)

## Key Resources

- `/design/WEBUI.md` - Web UI design specifications
- `/design/AUDIO.md` - Audio system architecture  
- `/design/CONFIGURATION.md` - Configuration infrastructure
- `/SpotifyLoopback/` - Spotify integration examples
- [Material 3 Guidelines](https://m3.material.io/)
- [SoundFlow Documentation](https://lsxprime.github.io/soundflow-docs/)

## Progress Tracking

As you complete each phase, mark it here:

- [x] Phase 1: Global Now Playing & Music Controls (Task 1.1 completed)
- [x] Phase 2: Music Queue Navbar Visibility (Task 2.1 completed)
- [x] Phase 3: Now Playing Metadata Display (Tasks 3.1 & 3.2 completed)
- [ ] Phase 4: Fingerprinting Debug & Instrumentation
- [ ] Phase 5: RTL-SDR Radio Audio Output
- [ ] Phase 6: Queue Page Enhancements
- [x] Phase 7: Visualizer Graphics Improvements (Tasks 7.1, 7.3, 7.4 completed)
- [x] Phase 8: Home Page Media Controls (Tasks 8.1, 8.2, 8.3 completed)
- [ ] Phase 9: File Browser Network & Drive Access
- [ ] Phase 10: Spotify Loopback Implementation
- [ ] Phase 11: Queue Page Touch UX Improvements
- [ ] Phase 12: Overall Material 3 Design & Touch Optimization

---

**See [DEBUG_AND_FIX_PLAN.md](./DEBUG_AND_FIX_PLAN.md) for complete details on each phase.**
