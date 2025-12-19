# Web UI TODO Items

**Document Version:** 1.0  
**Created:** December 19, 2024  
**Purpose:** Comprehensive audit of Web UI for TODO items, stubbed functionality, and future enhancements

---

## Overview

This document catalogs all identified TODO items, stubbed functionality, "in the future" features, and implementation notes found in the Radio Console Blazor Web UI codebase.

## Status Summary

| Category | Count | Status |
|----------|-------|--------|
| TODO Comments | 0 | ✅ None found |
| Stubbed Functionality | 1 | ⚠️ Needs completion |
| Future Enhancements | 15+ | 📋 Documented below |
| Phase 13 Deferred Items | 2 | ⏳ Pending Phase 13 |

---

## 1. Stubbed Functionality (Needs Implementation)

### 1.1 Log Export Feature
**Location:** `/src/Radio.Web/Components/Pages/SystemConfigPage.razor` (line 453)  
**Current State:** Placeholder implementation  
**Issue:** Export logs functionality currently only shows a notification with file info instead of actually downloading the file  
**Code:**
```csharp
// Note: This is a placeholder. Actual file download in Blazor requires JavaScript interop
Snackbar.Add($"Export functionality: {fileName} ({content.Length} characters)", Severity.Info);
```

**Required Implementation:**
1. Add JavaScript interop file (`wwwroot/js/fileDownload.js`)
2. Implement `downloadFile(filename, content)` JavaScript function
3. Inject `IJSRuntime` into SystemConfigPage component
4. Call JS interop to trigger browser download
5. Add proper error handling for JS interop failures

**Priority:** Medium  
**Estimated Effort:** 1-2 hours  
**Dependencies:** None

---

## 2. Phase 13 Deferred Items (From UIPHASEDPLAN.md)

### 2.1 Queue Drag-and-Drop Reordering
**Location:** `/src/Radio.Web/Components/Pages/QueuePage.razor`  
**Current State:** Click to jump/play works, but drag-and-drop is deferred  
**Phase:** Deferred from Phase 4 to Phase 13  
**Status:** 95% of Phase 4 complete (missing only this feature)

**Required Implementation:**
1. Use MudBlazor MudDropZone or HTML5 drag API
2. Add visual feedback during drag (cursor change, item highlighting)
3. Call `POST /api/queue/move` with `fromIndex` and `toIndex` on drop
4. Enable only if `source.CanReorderQueue` capability is true
5. Test on touchscreen with 72px row height for easy touch interaction
6. Ensure smooth drag experience on Raspberry Pi hardware

**Priority:** High (Phase 13 core feature)  
**Estimated Effort:** 4-6 hours  
**Dependencies:** None  
**Testing:** bUnit tests for drag-drop logic, E2E tests for user interaction

### 2.2 Page Transition Animations
**Location:** `/src/Radio.Web/Components/Layout/MainLayout.razor`  
**Current State:** Navigation works but no transition animations  
**Phase:** Deferred from Phase 2 to Phase 13  
**Status:** All navigation functional, just missing animations

**Required Implementation:**
1. Horizontal slide animation between pages (250ms duration)
2. Smooth easing function (ease-in-out)
3. Use Blazor PageTransition component or custom CSS transitions
4. Apply to navigation between: Home, Queue, Spotify, Files, Radio, System, Metrics, Visualizer, Devices, History
5. Ensure 60fps performance on Raspberry Pi
6. Test with touch gestures (swipe navigation if time permits)

**Priority:** Medium (Polish feature)  
**Estimated Effort:** 3-4 hours  
**Dependencies:** None  
**Testing:** E2E tests for smooth transitions, performance testing on target hardware

---

## 3. Future Enhancements (Not Blocking, Documented in UIPHASEDPLAN.md)

### 3.1 Phase 1: Foundation & Infrastructure
- [ ] Polly retry policies for API resilience (optional, not required for core functionality)

### 3.2 Phase 4: Queue Management
- ✅ Core queue operations complete
- [x] Drag-and-drop reordering (deferred to Phase 13 - see section 2.1 above)

### 3.3 Phase 7: Radio Controls & Presets
**Status:** 85% complete

**Missing Features:**
- [ ] Long-press scan control (continuous scan while holding button)
- [ ] Advanced power management UI for RTLSDRCore
- [ ] Visual frequency spectrum display during scan
- [ ] Preset import/export functionality
- [ ] Preset grouping/categories

**Priority:** Low to Medium  
**Estimated Effort:** 6-10 hours total

### 3.4 Phase 8: System Configuration & Management
**Current State:** 100% core features complete

**Future Enhancements:**
- [ ] Configuration editing capability (currently read-only display)
  - Inline editing for simple values
  - Edit dialogs for complex settings
  - Validation for configuration values
- [ ] Log export to file (requires JavaScript interop - see section 1.1)
- [ ] Auto-refresh toggle for logs
- [ ] Time range filter for logs (maxAgeMinutes parameter already supported by API)
- [ ] Configuration backup/restore UI
- [ ] System shutdown/restart buttons with confirmation dialogs

**Priority:** Low to Medium  
**Estimated Effort:** 8-12 hours total

### 3.5 Phase 9: Metrics Dashboard
**Current State:** 100% core features complete

**Future Enhancements:**
- [ ] Time-series line charts (requires Chart.js or similar JavaScript charting library)
- [ ] Multiple metrics on same chart with multi-axis support
- [ ] Chart zoom and pan capabilities
- [ ] Metric comparison views (side-by-side)
- [ ] Custom metric selection and dashboard customization (save preferred layout)
- [ ] Export metrics data to CSV
- [ ] Metric alert thresholds and notifications

**Priority:** Low  
**Estimated Effort:** 12-16 hours total  
**Dependencies:** Chart.js or similar charting library

### 3.6 Phase 10: Audio Visualization
**Current State:** 95% complete

**Future Enhancements:**
- [ ] Configuration integration for default visualization mode (via Configuration REST API)
- [ ] Sensitivity/color scheme customization UI
- [ ] Performance throttling for low-end devices (auto-detect and adapt)
- [ ] FPS monitoring and optimization
- [ ] requestAnimationFrame optimization (currently using direct rendering)
- [ ] OffscreenCanvas for better performance (browser support permitting)

**Priority:** Low  
**Estimated Effort:** 4-6 hours total

### 3.7 Phase 11: Device Management
**Current State:** 100% core features complete

**Future Enhancements:**
- [ ] Auto-refresh every 30 seconds (optional feature, can be toggled)
- [ ] Device configuration dialogs for advanced settings
- [ ] USB port selection UI for specific devices
- [ ] Hot-plug event notifications with real-time SignalR updates
- [ ] Device details view with full capabilities display
- [ ] Configuration persistence via Configuration REST API (device preferences)

**Priority:** Low  
**Estimated Effort:** 6-8 hours total

### 3.8 Phase 12: Play History & Analytics
**Current State:** 100% core features complete

**Future Enhancements:**
- [ ] Manual entry form (POST /api/playhistory - API ready)
- [ ] Visual charts for statistics (bar charts, pie charts for genre/source breakdown)
- [ ] Export history to CSV
- [ ] Bulk delete operations (select multiple items)
- [ ] Advanced search with multiple criteria (genre, date range, duration range)
- [ ] Sort column headers (client-side sorting)
- [ ] Configuration persistence via Configuration REST API (default filters, items per page)
- [ ] Today quick filter button (API endpoint ready)
- [ ] Date range picker UI (start and end dates)

**Priority:** Low to Medium  
**Estimated Effort:** 8-10 hours total

---

## 4. "Last Used" Preferences Implementation

### Status: ⚠️ Needs Implementation

**Requirement:** As users interact with screens that have changeable parameters, preferences should be saved for "last used" values, and parameters with no stored value should use a valid default (and persist it).

### 4.1 Pages Requiring Preference Persistence

#### Home Page (Playback Controls)
**Parameters:**
- Volume level
- Balance setting
- Repeat mode (Off/One/All)
- Shuffle state (on/off)

**Implementation:**
- Load on `OnInitializedAsync`
- Save via `POST /api/configuration` after each user change
- Configuration section: `"ui.playback"`

#### Queue Page
**Parameters:**
- Sort order
- Column visibility

**Implementation:**
- Configuration section: `"ui.queue"`

#### Spotify Page
**Parameters:**
- Default search filters (Music, Playlists, Podcasts, Albums, Artists, Audiobooks)
- Last search query (optional)

**Implementation:**
- Configuration section: `"ui.spotify"`

#### File Browser Page
**Parameters:**
- Default path
- View mode (list/grid)
- Sort order
- Last visited directory

**Implementation:**
- Configuration section: `"ui.filebrowser"`

#### Radio Page
**Parameters:**
- Default band (AM/FM/WB/VHF/SW)
- Default step size
- LED colors
- Preset sort order

**Implementation:**
- Configuration section: `"ui.radio"`

#### Metrics Dashboard
**Parameters:**
- Selected metrics for display
- Time range selection
- Chart types

**Implementation:**
- Configuration section: `"ui.metrics"`

#### Visualizer Page
**Parameters:**
- Default visualization mode (VU Meter/Waveform/Spectrum)
- Color scheme (if customizable in future)

**Implementation:**
- Configuration section: `"ui.visualizer"`

#### Device Management Page
**Parameters:**
- Auto-refresh toggle state (if implemented)

**Implementation:**
- Configuration section: `"ui.devices"`

#### Play History Page
**Parameters:**
- Default filter (All/Today/Date Range/Source)
- Items per page
- Sort order

**Implementation:**
- Configuration section: `"ui.history"`

### 4.2 Implementation Pattern

Each page should follow this pattern:

```csharp
@inject ConfigurationApiService ConfigApi

private async Task LoadPreferencesAsync()
{
    try
    {
        var prefs = await ConfigApi.GetConfigurationAsync<Dictionary<string, object>>("ui.pagename");
        if (prefs != null)
        {
            // Apply loaded preferences
            _parameter1 = prefs.GetValueOrDefault("parameter1", defaultValue);
            _parameter2 = prefs.GetValueOrDefault("parameter2", defaultValue);
        }
        else
        {
            // Save defaults
            await SavePreferenceAsync("parameter1", defaultValue);
            await SavePreferenceAsync("parameter2", defaultValue);
        }
    }
    catch (Exception ex)
    {
        Logger.LogWarning(ex, "Failed to load preferences for {Page}", "PageName");
        // Use defaults silently
    }
}

private async Task SavePreferenceAsync(string key, object value)
{
    try
    {
        await ConfigApi.UpdateConfigurationAsync("ui.pagename", key, value.ToString());
    }
    catch (Exception ex)
    {
        Logger.LogWarning(ex, "Failed to save preference {Key}", key);
        // Fail silently, don't interrupt user workflow
    }
}

// Call SavePreferenceAsync whenever user changes a parameter
private async Task OnParameterChanged(object newValue)
{
    _parameter = newValue;
    await SavePreferenceAsync("parameter", newValue);
    StateHasChanged();
}
```

**Priority:** High (User experience requirement)  
**Estimated Effort:** 6-8 hours total  
**Dependencies:** ConfigurationApiService (already implemented)

---

## 5. Testing Requirements

### 5.1 bUnit Tests for New Features

**Phase 13 Features:**
- [ ] QueuePage drag-and-drop tests (mock drag events, verify API calls)
- [ ] Page transition tests (verify animations trigger correctly)
- [ ] Preference persistence tests for all pages

**Test Coverage Goal:** 80%+ for UI components

### 5.2 E2E Tests

**Critical Scenarios:**
- [ ] Drag-and-drop queue reordering end-to-end
- [ ] Page transitions are smooth and complete
- [ ] Preferences persist across browser refresh
- [ ] Log export downloads file correctly (when implemented)

### 5.3 Manual Testing on Target Hardware

**Raspberry Pi 5 Testing:**
- [ ] Touch interactions for drag-and-drop feel natural
- [ ] Page transitions run at 60fps
- [ ] All preference changes save and load correctly
- [ ] No performance degradation from animations

---

## 6. Documentation Updates Required

### 6.1 UIPHASEDPLAN.md
- [ ] Update Phase 13 status from 0% to in-progress
- [ ] Mark Phase 13 tasks as complete when done
- [ ] Update overall progress percentage

### 6.2 README.md
- [ ] Update Phase 9 - UI status to "In Progress" or "Complete"
- [ ] Update project status table

### 6.3 API_REFERENCE.md
- [ ] Verify all API endpoints are documented
- [ ] Add examples for preference persistence endpoints

---

## 7. Priority Summary

### High Priority (Must Do for Phase 13 Completion)
1. ✅ **WEBUI_TODO.md creation** (This document)
2. ⏳ **Queue drag-and-drop reordering** (Core Phase 13 feature)
3. ⏳ **"Last used" preferences implementation** (User experience requirement)
4. ⏳ **Test runner scripts** (Development workflow requirement)
5. ⏳ **bUnit tests for Phase 13 features**

### Medium Priority (Polish and UX)
1. ⏳ **Page transition animations** (Phase 13 polish feature)
2. ⏳ **Log export implementation** (Stubbed functionality)
3. ⏳ **E2E tests for Phase 13 features**

### Low Priority (Future Enhancements)
1. All items listed in Section 3 (Future Enhancements)
2. Advanced features for Phase 7, 8, 9, 10, 11, 12

---

## 8. Completion Checklist

### Phase 13 Core Tasks
- [ ] Queue page drag-and-drop implemented and tested
- [ ] Page transition animations implemented and tested
- [ ] "Last used" preferences implemented for all pages
- [ ] Test runner scripts created (bash and PowerShell)
- [ ] bUnit tests for all Phase 13 features
- [ ] E2E tests for critical Phase 13 workflows
- [ ] UAT tests run with real radio hardware
- [ ] Manual testing on Raspberry Pi 5 complete

### Documentation Tasks
- [x] WEBUI_TODO.md created (this document)
- [ ] UIPHASEDPLAN.md updated with Phase 13 completion status
- [ ] README.md updated with current UI status
- [ ] All test scripts documented with usage examples

### Testing Tasks
- [ ] All new bUnit tests pass
- [ ] All E2E tests pass
- [ ] UAT tests pass with real hardware
- [ ] No regressions in existing tests
- [ ] Performance verified on target hardware

---

## Appendix A: Search Methodology

This document was created by searching the Web UI codebase for:
- TODO, HACK, FIXME, XXX comments (case-insensitive)
- "in the future", "future enhancement" phrases
- "deferred", "not implemented", "stub", "placeholder" keywords
- "in a real implementation" comments
- References to UIPHASEDPLAN.md for documented future work

**Search Commands Used:**
```bash
grep -rn "TODO\|HACK\|FIXME\|XXX" /src/Radio.Web --include="*.razor" --include="*.cs"
grep -rn "in the future\|future enhancement\|deferred\|not implemented\|stub\|placeholder\|in a real" /src/Radio.Web --include="*.razor" --include="*.cs" -i
```

**Files Reviewed:**
- All `.razor` component files
- All `.cs` C# code files in Radio.Web project
- UIPHASEDPLAN.md for documented deferred work

---

**Last Updated:** December 19, 2024  
**Next Review:** After Phase 13 completion  
**Maintainer:** Radio Console Development Team
