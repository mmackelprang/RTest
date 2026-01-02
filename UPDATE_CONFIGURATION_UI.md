# Configuration UI Enhancement Plan

## Overview

This document outlines a phased approach to enhance the Radio Console Web UI's System Configuration page with comprehensive configuration, preferences, and secrets management capabilities.

**Context:**
- Read `/design/CONFIGURATION.md` for the configuration infrastructure design
- Read `/SYSTEMCONFIGURATION.md` for configuration reference
- Read `/design/WEBUI.md` for UI design guidelines
- Existing implementation: `/src/Radio.Web/Components/Pages/SystemConfigPage.razor`

**Target:** Raspberry Pi 5 (Linux/Raspbian) + Windows development
**Framework:** Blazor Server (.NET 8+)
**UI Library:** MudBlazor with Material 3 design principles

---

## Architecture Overview

### Existing Infrastructure

The configuration infrastructure already exists and supports:
- **Dual Storage**: JSON files and SQLite database (togglable)
- **Secrets Management**: Encrypted secrets with tag-based substitution (`${secret:identifier}`)
- **Configuration Stores**: IConfigurationStore, IConfigurationManager interfaces
- **Backup/Restore**: IConfigurationBackupService
- **REST API**: `/api/configuration/*` endpoints via ConfigurationController
- **Web Service**: ConfigurationApiService in Radio.Web

### UI Enhancement Goals

1. **Preferences Management**: Easy access to all user preferences with auto-creation and logging
2. **Secrets Management**: Secure UI for managing secrets (SpotifySecrets, TTSSecrets, AcoustId)
3. **Configuration Options**: Comprehensive panels for all configuration classes
4. **Store Management**: Display/compare/reconcile JSON vs SQLite configurations
5. **Import/Export**: Backup and restore configuration data
6. **USB Port Configuration**: Add device configuration (vinyl turntable USB port)

---

## Implementation Summary

This plan is organized into 6 phases that build upon the existing configuration infrastructure to create a comprehensive Web UI for managing all aspects of the Radio Console configuration system.

**Key Deliverables:**
- Preferences UI for all preference types with auto-creation
- Secrets UI with proper masking and security
- Configuration panels for all option classes including USB device ports
- Store comparison and reconciliation tools
- Import/export functionality
- Comprehensive testing and documentation

**For detailed implementation instructions, see the phase sections below.**

---

## Current Implementation Status

### Phase 1: Infrastructure Analysis & API Enhancement ✅ COMPLETE

**Completed:**
- ✅ Generic configuration endpoints: `GET/POST /api/configuration/{section}` with validation
- ✅ Device Configuration DTOs created
- ✅ Preferences DTOs created
- ✅ Input validation for section names (alphanumeric, hyphens, underscores, dots)
- ✅ Proper HTTP status codes (404 for not found, 500 for errors)
- ✅ Configuration data validation (null checks, size limits, key validation)

**Pending (Phase 5 - Store Management):**
- ⏳ Get store metadata: `GET /api/configuration/store-info`
- ⏳ Compare stores: `GET /api/configuration/compare`
- ⏳ Reconcile stores: `POST /api/configuration/reconcile`
- ⏳ Import configuration: `POST /api/configuration/import`
- ⏳ Export configuration: `GET /api/configuration/export`

### Phase 2: Preferences Management UI ✅ COMPLETE

**Completed:**
- ✅ Preferences main tab with 5 sub-tabs
- ✅ Audio Preferences panel (CurrentSource, MasterVolume)
- ✅ Spotify Preferences panel (LastSongPlayed, SongPositionMs, Shuffle, Repeat)
- ✅ File Player Preferences panel (LastSongPlayed, SongPositionMs, Shuffle, Repeat)
- ✅ Radio Preferences panel (LastFrequency, LastBand, LastEQMode)
- ✅ Generic Source Preferences panel (USBPort)
- ✅ Auto-creation with LoadOrCreatePreferenceAsync method
- ✅ Logging when preferences are created with default values
- ✅ Save functionality for all preference types with StateHasChanged()
- ✅ User feedback via Snackbar notifications

### Phase 4 (Partial): Configuration Options UI ✅ COMPLETE

**Completed:**
- ✅ Device Options panel with Radio and Vinyl USB ports
- ✅ Cross-platform support (Linux/Windows paths)
- ✅ Save functionality with StateHasChanged()
- ✅ Comprehensive tests (15 tests passing)

### Phase 3: Secrets Management UI ✅ COMPLETE

**Completed:**
- ✅ Secrets main tab with security warning
- ✅ Spotify Secrets panel (ClientID, ClientSecret, RefreshToken)
- ✅ TTS Secrets panel (GoogleAPIKey, AzureAPIKey, AzureRegion)
- ✅ AcoustID Secret panel (ApiKey)
- ✅ Password fields with visibility toggle icons
- ✅ Save functionality with StateHasChanged()
- ✅ Clear functionality with confirmation dialogs
- ✅ Logging when secrets are updated (without logging values)
- ✅ User feedback via Snackbar notifications
- ✅ Auto-hide secrets after save
- ✅ bUnit tests for Secrets UI (22 tests passing)

### Next Steps: Phase 5 - Configuration Store Management UI

---

## Phase 1: Infrastructure Analysis & API Enhancement (2-3 days)

### Objectives
- Ensure all required API endpoints exist
- Create missing DTOs for preferences, secrets, and configuration options
- Add endpoints for configuration store comparison and reconciliation

### Tasks

#### 1.1: Audit Existing API Endpoints

Review `/src/Radio.API/Controllers/ConfigurationController.cs` and verify endpoints for:
- ✓ Get/Update generic configuration: `GET/POST /api/configuration/{section}`
- ✓ List configuration files: `GET /api/configuration/stores`
- ? Get store metadata (type, location, size, modified): `GET /api/configuration/store-info`
- ? Compare stores: `GET /api/configuration/compare`
- ? Reconcile stores: `POST /api/configuration/reconcile`
- ? Import configuration: `POST /api/configuration/import`
- ? Export configuration: `GET /api/configuration/export`

#### 1.2: Create Missing DTOs

The following DTOs need to be created in `/src/Radio.API/Models/ConfigurationModels.cs`:

**Store Management DTOs:**
- `ConfigurationStoreInfoDto` - Store metadata (type, location, size, modified date, entry count)
- `ConfigurationComparisonDto` - Comparison results between JSON and SQLite stores
- `ConfigurationDifferenceDto` - Individual difference between stores
- `ReconcileConfigurationRequestDto` - Request for copying values between stores

**Preferences DTOs:**
- `SpotifyPreferencesDto` - Spotify playback preferences
- `FilePlayerPreferencesDto` - File player preferences
- `RadioPreferencesDto` - Radio tuner preferences
- `GenericSourcePreferencesDto` - USB source preferences
- `AudioPreferencesDto` - General audio preferences

**Secrets DTOs:**
- `SpotifySecretsDto` - Spotify API credentials
- `TTSSecretsDto` - TTS service API keys
- `AcoustIdSecretDto` - AcoustID API key

**Device Configuration DTOs:**
- `DeviceOptionsDto` - USB port configuration for radio and vinyl

#### 1.3: Add Missing API Endpoints

Extend `/src/Radio.API/Controllers/ConfigurationController.cs` with:

```csharp
[HttpGet("store-info")]
public async Task<ActionResult<ConfigurationStoreInfoDto>> GetStoreInfo([FromQuery] string storeType = "current")

[HttpGet("compare")]
public async Task<ActionResult<ConfigurationComparisonDto>> CompareStores()

[HttpPost("reconcile")]
public async Task<IActionResult> ReconcileStores([FromBody] ReconcileConfigurationRequestDto request)

[HttpPost("import")]
public async Task<IActionResult> ImportConfiguration([FromForm] IFormFile file, [FromQuery] string targetStore = "current")

[HttpGet("export")]
public async Task<IActionResult> ExportConfiguration([FromQuery] string sourceStore = "current", [FromQuery] string format = "json")
```

#### 1.4: Update Web Service

Extend `/src/Radio.Web/Services/ApiClients/ConfigurationApiService.cs` with corresponding methods.

### Success Criteria
- All necessary DTOs created and documented
- API endpoints implemented and tested with unit tests
- Web service methods added and functional
- Postman/curl tests verify endpoints work correctly

---

## Phase 2: Preferences Management UI (3-4 days)

### Objectives
- Create tabbed interface for all preference types
- Implement auto-creation of missing preferences with default values
- Add logging when preferences are created
- Ensure preferences persist correctly

### Tasks

#### 2.1: Add Preferences Tab to SystemConfigPage

Modify `/src/Radio.Web/Components/Pages/SystemConfigPage.razor` to add a new main tab called "Preferences" with sub-tabs for:
- Spotify
- File Player
- Radio
- Generic Source
- Audio

#### 2.2: Implement Individual Preference Panels

Create UI panels for each preference type using MudBlazor components:

**Spotify Preferences:**
- LastSongPlayed (URI, read-only)
- SongPositionMs (numeric field)
- Shuffle (checkbox)
- RepeatMode (select: Off/One/All)

**File Player Preferences:**
- Same fields as Spotify

**Radio Preferences:**
- LastFrequency (numeric)
- LastBand (select: AM/FM/WB/VHF/SW)
- LastEQMode (text field)

**Generic Source Preferences:**
- USBPort (text field)

**Audio Preferences:**
- CurrentSource (select: Spotify/Radio/FilePlayer/Vinyl)
- MasterVolume (numeric, 0-100)

#### 2.3: Implement Auto-Creation Logic

Add a reusable method `LoadOrCreatePreferenceAsync<T>()` that:
1. Attempts to load preference from API
2. If null, creates with default values
3. Logs the creation with default values
4. Saves to API
5. Returns the preference

Example log message: "Preference SpotifyPreferences not found, creating with default values: {@DefaultValue}"

#### 2.4: Add Save Functionality

Each preference panel needs a "Save" button that:
- Calls `ConfigApi.UpdateConfigurationAsync(section, dto)`
- Shows success/error snackbar notification
- Disables button during save operation

### Success Criteria
- All preference panels display and edit correctly
- Missing preferences auto-create with defaults and log creation
- Changes persist correctly to active configuration store
- Validation prevents invalid values
- UI is responsive and user-friendly

---

## Phase 3: Secrets Management UI (3-4 days)

### Objectives
- Create secure UI for viewing and editing secrets
- Mask secret values by default with show/hide toggle
- Support creating, updating, and deleting secrets
- Handle secret tags properly (${secret:identifier})

### Tasks

#### 3.1: Add Secrets Tab to SystemConfigPage

Add a new main tab called "Secrets" with:
- Warning alert about secret security
- Sub-tabs for: Spotify, TTS Services, AcoustID

#### 3.2: Implement Spotify Secrets Panel

Create fields for:
- ClientID (password field with visibility toggle)
- ClientSecret (password field with visibility toggle)
- RefreshToken (password field with visibility toggle)

Each field should:
- Use InputType.Password by default
- Have a visibility icon button to toggle to InputType.Text
- Include helper text explaining what the field is for

#### 3.3: Implement TTS Secrets Panel

Create fields for:
- GoogleAPIKey (password field with visibility toggle)
- AzureAPIKey (password field with visibility toggle)
- AzureRegion (password field with visibility toggle)

#### 3.4: Implement AcoustID Secret Panel

Create field for:
- ApiKey (password field with visibility toggle)

#### 3.5: Add Secret Management Actions

Each secrets panel needs:
- "Save Secrets" button (saves to API, then hides all fields)
- "Clear All Secrets" button (with confirmation dialog)

Clearing sets all fields to empty strings and saves.

#### 3.6: Security Considerations

- Never log secret values (log only that a secret was updated)
- Mask values by default
- Hide values after save
- Use HTTPS in production
- Validate that secrets are properly encrypted server-side

### Success Criteria
- All secret fields mask values by default
- Show/hide toggle works for each field
- Secrets save and encrypt correctly
- Clear functionality works with confirmation
- No plain-text secrets logged or displayed in network requests
- Secret tags (${secret:identifier}) handled properly

---

## Phase 4: Configuration Options UI (4-5 days)

### Objectives
- Add comprehensive panels for all configuration option classes
- Ensure USB port configuration for vinyl turntable is included
- Provide validation and helpful descriptions
- Make it easy to add new configuration classes in the future

### Tasks

#### 4.1: Extend Configuration Tab

The existing "Configuration" tab already has sub-tabs for Audio, Visualizer, and Output.
Add additional sub-tabs for:
- TTS (TTSOptions)
- File Player (FilePlayerOptions)
- Radio (RadioOptions)
- Audio Engine (AudioEngineOptions)
- Google Cast (GoogleCastOptions)
- HTTP Stream (HttpOutputStreamOptions)
- **Devices (DeviceOptions) - PRIORITY**
- Fingerprinting (FingerprintingOptions)
- Metrics (MetricsOptions)
- Database (DatabaseOptions)
- System (SystemConfiguration)

#### 4.2: Implement Device Options Panel (PRIORITY)

This is specifically called out in the issue requirements.

Create panel with fields for:
- **RadioUSBPort** (text field, default: /dev/ttyUSB0)
- **VinylUSBPort** (text field, default: /dev/ttyUSB1) - NEW REQUIREMENT
- CastDefaultDevice (text field, optional)

Helper text should explain:
- Linux/Raspberry Pi USB port paths
- How to find available ports (ls /dev/ttyUSB*)
- Windows equivalents (COM ports) for development

#### 4.3: Implement Other Configuration Panels

Create UI panels following the same pattern as existing Audio/Visualizer/Output panels:

**TTSOptions:**
- DefaultEngine (select: ESpeak, Google, Azure)
- DefaultVoice
- DefaultPitch (0.5-2.0)
- DefaultSpeed (0.5-2.0)
- ESpeakPath
- GenerationTimeoutSeconds

**FilePlayerOptions:**
- RootDirectory
- SupportedExtensions (multi-line text or chip list)

**RadioOptions:**
- DefaultBand (select: AM, FM, WB, VHF, SW)
- DefaultFrequency
- DefaultStepSize

**AudioEngineOptions:**
- SampleRate
- Channels
- BufferSize
- HotPlugIntervalSeconds
- OutputBufferSizeSeconds
- EnableHotPlugDetection (checkbox)

**FingerprintingOptions:**
- Enabled (checkbox)
- SampleDurationSeconds
- IdentificationIntervalSeconds
- MinimumConfidenceThreshold (0.0-1.0)
- DuplicateSuppressionMinutes
- DatabasePath
- AcoustId settings (ApiKey as read-only reference, other settings)
- MusicBrainz settings

**MetricsOptions:**
- Enabled (checkbox)
- FlushIntervalSeconds
- RetentionMinuteData
- RetentionHourData
- RetentionDayData
- RollupIntervalMinutes

**DatabaseOptions:**
- RootPath
- ConfigurationSubdirectory
- MetricsSubdirectory
- FingerprintingSubdirectory
- BackupSubdirectory

**SystemConfiguration:**
- RootDir (base path for all resources)
- Other system-level settings

#### 4.4: Add Validation

Each field should have appropriate validation:
- Numeric fields: min/max ranges
- Path fields: valid path format
- Percentage fields: 0-100 range
- Duration fields: positive values

### Success Criteria
- All configuration panels display correctly
- USB port configuration for vinyl is editable and saved
- All fields have appropriate validation
- Changes persist to active configuration store
- Helpful tooltips/descriptions provided
- Device Options panel prioritized and fully functional

---

## Phase 5: Configuration Store Management (3-4 days)

### Objectives
- Display current configuration store type (JSON vs SQLite)
- Show store metadata (location, size, modified date, entry count)
- Implement import/export functionality
- Add store comparison tool
- Add reconciliation tool to copy values between stores

### Tasks

#### 5.1: Add Store Management Tab

Create a new main tab called "Store Management" with sections for:
- Current Store Info (card displaying metadata)
- Import/Export (buttons for import/export operations)
- Compare Stores (comparison table with differences)
- Reconciliation (UI for copying values between stores)

#### 5.2: Implement Store Info Display

Card showing:
- Store Type (JSON or SQLite)
- Location (full file path)
- Size (formatted as KB/MB)
- Last Modified (formatted datetime)
- Entry Count

Data loaded from `ConfigApi.GetStoreInfoAsync("current")`

#### 5.3: Implement Import/Export

**Export Buttons:**
- "Export Configuration (JSON)" - downloads .json file
- "Export Backup (.radiobak)" - downloads .radiobak file

Both use `ConfigApi.ExportConfigurationAsync()` and trigger browser download via JSRuntime.

**Import:**
- MudFileUpload component accepting .json and .radiobak files
- Confirmation dialog before import
- Calls `ConfigApi.ImportConfigurationAsync(stream, fileName)`
- Reloads all configuration after successful import

#### 5.4: Implement Store Comparison

**UI Elements:**
- "Refresh" button to trigger comparison
- Alert showing summary (entry counts, difference count)
- Table with columns: Checkbox, Key, JSON Value, SQLite Value, Status
- "Select All" checkbox in header
- Status chips with colors: OnlyInJson (Primary), OnlyInSqlite (Secondary), Different (Warning), Same (Success)
- Action buttons: "Copy Selected: JSON → SQLite" and "Copy Selected: SQLite → JSON"

**Logic:**
- Load comparison on button click via `ConfigApi.CompareStoresAsync()`
- Track selected differences in HashSet
- Enable action buttons only when selections exist

#### 5.5: Implement Reconciliation

When user clicks action button:
1. Show confirmation dialog with count and direction
2. Build `ReconcileConfigurationRequestDto` with selected keys
3. Call `ConfigApi.ReconcileStoresAsync(request)`
4. Show success/error message
5. Reload comparison to show updated state
6. Clear selections

### Success Criteria
- Current store type and metadata display correctly
- Export creates downloadable JSON and .radiobak files
- Import uploads and applies configuration successfully
- Comparison shows differences between JSON and SQLite accurately
- Reconciliation copies selected values between stores correctly
- UI updates after import/reconciliation
- Confirmation dialogs prevent accidental data loss

---

## Phase 6: Testing & Documentation (2-3 days)

### Objectives
- Create comprehensive tests for new UI components
- Test all CRUD operations
- Validate import/export functionality
- Document new features
- Take screenshots for documentation

### Tasks

#### 6.1: Unit Tests for API Endpoints

Create tests in `/tests/Radio.API.Tests/Controllers/ConfigurationControllerTests.cs`:

Test coverage should include:
- GetStoreInfo returns current store metadata
- CompareStores returns differences correctly
- ReconcileStores copies values successfully
- ExportConfiguration returns file data
- ImportConfiguration applies configuration
- All DTOs serialize/deserialize correctly

#### 6.2: Component Tests

Create tests in `/tests/Radio.Web.Tests/Components/Pages/SystemConfigPageTests.cs`:

Test coverage should include:
- Each preferences tab loads and displays data
- Preferences save correctly
- Secrets tab masks values by default
- Show/hide toggle works for secrets
- Device options panel saves USB port configuration
- Configuration panels save correctly
- Store comparison displays differences
- Reconciliation UI functions correctly
- Import/export operations work

#### 6.3: Integration Testing

Manual testing checklist:

**Preferences:**
- [ ] Load all preference tabs without errors
- [ ] Edit and save Spotify preferences
- [ ] Edit and save File Player preferences
- [ ] Edit and save Radio preferences
- [ ] Edit and save Generic Source preferences (USB port)
- [ ] Edit and save Audio preferences
- [ ] Verify auto-creation logging for missing preferences

**Secrets:**
- [ ] All secrets masked by default
- [ ] Show/hide toggle works for each field
- [ ] Save Spotify secrets
- [ ] Save TTS secrets
- [ ] Save AcoustID secret
- [ ] Clear secrets with confirmation

**Configuration:**
- [ ] Edit and save Device Options (Radio and Vinyl USB ports)
- [ ] Edit and save all other configuration panels
- [ ] Verify validation on numeric fields
- [ ] Verify path validation

**Store Management:**
- [ ] View current store info
- [ ] Export configuration as JSON
- [ ] Export configuration as .radiobak backup
- [ ] Import configuration from file
- [ ] Compare JSON vs SQLite stores
- [ ] Select differences and reconcile JSON → SQLite
- [ ] Select differences and reconcile SQLite → JSON
- [ ] Verify changes persist after reconciliation

#### 6.4: Documentation

**Update `/SYSTEMCONFIGURATION.md`:**
- Add "Configuration UI Management" section
- Document how to access and use new UI features
- Include screenshots of key features

**Create `/docs/configuration-ui-guide.md`:**

User guide covering:
- Overview of configuration UI
- How to access SystemConfigPage
- Preferences management walkthrough
- Secrets management walkthrough
- Configuration options walkthrough
- Store comparison and reconciliation guide
- Import/export instructions
- Best practices and tips
- Troubleshooting common issues

#### 6.5: Screenshots

Capture screenshots for:
1. SystemConfigPage main view with all tabs
2. Preferences tab showing Spotify preferences
3. Secrets tab showing masked secrets with toggle
4. Device Options panel highlighting vinyl USB port
5. Store Management showing current store info
6. Comparison view with differences highlighted
7. Reconciliation with selections
8. Export configuration dialog
9. Import confirmation dialog

Screenshots should be:
- High resolution (at least 1920x1080)
- Annotated with arrows/highlights where helpful
- Saved in `/docs/images/configuration-ui/`

### Success Criteria
- All unit tests pass (minimum 80% code coverage)
- All component tests pass
- Manual testing checklist 100% complete
- Documentation updated with clear instructions
- Screenshots captured and embedded in docs
- No console errors or warnings
- Performance acceptable (page loads < 2 seconds, operations < 5 seconds)
- Code review completed and approved

---

## Implementation Timeline

**Total Estimated Time: 17-22 days**

| Phase | Estimated Time | Dependencies |
|-------|----------------|--------------|
| Phase 1: Infrastructure & API | 2-3 days | None |
| Phase 2: Preferences UI | 3-4 days | Phase 1 complete |
| Phase 3: Secrets UI | 3-4 days | Phase 1 complete |
| Phase 4: Configuration Options UI | 4-5 days | Phase 1 complete |
| Phase 5: Store Management UI | 3-4 days | Phase 1 complete |
| Phase 6: Testing & Documentation | 2-3 days | All phases complete |

**Parallelization Opportunities:**
- Phases 2, 3, and 4 can be worked on in parallel after Phase 1
- Phase 5 can start once Phase 1 API endpoints are complete
- Phase 6 testing can begin incrementally as each phase completes

**Critical Path:**
- Phase 1 (API) must complete first
- Phase 6 (Testing) must complete last
- Device Options (Phase 4) is high priority per requirements

---

## Technical Considerations

### Security
- **Secrets**: Never log or display plain-text secrets
- **Validation**: Validate all user input on both client and server
- **Authentication**: Ensure SystemConfigPage requires authentication in production
- **CSRF Protection**: ASP.NET Core provides built-in CSRF protection

### Performance
- **Lazy Loading**: Load configuration sections on-demand (tab activation)
- **Debouncing**: Debounce save operations for text fields
- **Caching**: Cache configuration on client-side
- **Pagination**: Implement if comparison shows hundreds of differences

### Accessibility
- **ARIA Labels**: Ensure all form fields have proper labels
- **Keyboard Navigation**: Test tab navigation through forms
- **Screen Reader Support**: Use semantic HTML and ARIA attributes
- **Focus Management**: Return focus appropriately after dialogs

### Cross-Platform
- **Path Separators**: Handle Windows vs Linux paths correctly
- **USB Ports**: /dev/ttyUSB* on Linux, COM* on Windows
- **File Permissions**: Ensure config files are readable/writable

### Error Handling
- **Network Failures**: Handle API failures gracefully with retries
- **Validation Errors**: Display clear, actionable error messages
- **Concurrency**: Handle multiple users editing configuration
- **Partial Failures**: Report what succeeded/failed

---

## Future Enhancements

Consider for future iterations:

1. **Configuration History**: Track changes over time with audit log
2. **Rollback**: Ability to rollback to previous configuration snapshot
3. **Configuration Templates**: Save and load configuration profiles
4. **Bulk Operations**: Apply same value to multiple keys at once
5. **Search/Filter**: Search configuration keys and values
6. **Validation Rules**: Define custom validation rules
7. **Configuration Diff View**: Visual diff between two configurations
8. **Real-time Sync**: SignalR updates when configuration changes externally
9. **Configuration Groups**: Organize related configuration items
10. **Expert Mode**: Raw JSON editor for advanced users

---

## Key Requirements Summary

From the issue description, this plan specifically addresses:

✅ **UI preferences with auto-creation**: Phase 2 implements LoadOrCreatePreferenceAsync with logging
✅ **Display current configuration type/location/info**: Phase 5 implements store info display
✅ **Import/export functions**: Phase 5 implements import/export for all config types
✅ **Compare and reconcile JSON vs SQLite**: Phase 5 implements comparison and reconciliation
✅ **USB Port for Vinyl Record Player**: Phase 4 prioritizes Device Options with VinylUSBPort
✅ **Secrets management**: Phase 3 implements SpotifySecrets, TTSSecrets, AcoustId APIKey
✅ **Preferences support**: Phase 2 implements all preference types
✅ **Configuration options support**: Phase 4 implements all configuration option types

All requirements from the issue have been incorporated into this phased plan.

---

## Conclusion

This comprehensive plan provides a roadmap for enhancing the Radio Console Web UI with full configuration management capabilities. The implementation prioritizes:

1. **Security**: Proper handling of secrets with encryption and masking
2. **Usability**: Intuitive UI with clear organization and descriptions
3. **Flexibility**: Easy to add new configuration classes as system grows
4. **Reliability**: Comprehensive testing and error handling
5. **Maintainability**: Well-documented code and user guides

By following this plan, the Radio Console will have a production-ready configuration UI that meets all requirements specified in the issue and provides a solid foundation for future enhancements.

