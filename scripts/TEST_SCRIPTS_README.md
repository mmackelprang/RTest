# Test Runner Scripts

This directory contains test runner scripts for the Radio Console project. These scripts provide convenient ways to run different test suites.

## Available Scripts

All scripts are available in both **Bash** (`.sh` for Linux/macOS) and **PowerShell** (`.ps1` for Windows).

### 1. run-bunit-tests
**Purpose:** Run bUnit component tests only (Blazor UI components)

**Usage:**
```bash
# Linux/macOS
./run-bunit-tests.sh

# Windows
.\run-bunit-tests.ps1
```

**What it runs:** Tests in `tests/Radio.Web.Tests/`  
**Test count:** 100 component tests  
**Duration:** ~10 seconds

---

### 2. run-e2e-tests
**Purpose:** Run End-to-End tests only (Playwright browser tests)

**Usage:**
```bash
# Linux/macOS
./run-e2e-tests.sh

# Windows
.\run-e2e-tests.ps1
```

**What it runs:** Tests in `tests/Radio.Web.E2ETests/`  
**Test count:** 6 E2E tests  
**Duration:** ~30 seconds (browser automation)

**Requirements:**
- Playwright browsers installed
- Running instance of Radio.Web not required (TestServer used)

---

### 3. run-unit-tests-hardware
**Purpose:** Run all xUnit tests (excluding E2E and bUnit) that may require real radio hardware

**Usage:**
```bash
# Linux/macOS
./run-unit-tests-hardware.sh

# Windows
.\run-unit-tests-hardware.ps1
```

**What it runs:** 
- `tests/Radio.Core.Tests/`
- `tests/Radio.Infrastructure.Tests/`
- `tests/Radio.API.Tests/`
- `tests/RTLSDRCore.Tests/`

**⚠️ WARNING:** Some tests may interact with real USB radio devices (RTL-SDR, RF320). Ensure hardware is connected.

**Duration:** ~30-60 seconds

---

### 4. run-all-tests
**Purpose:** Run all test suites sequentially (bUnit → xUnit → E2E)

**Usage:**
```bash
# Linux/macOS
./run-all-tests.sh

# Windows
.\run-all-tests.ps1
```

**What it runs:** All test projects in the solution  
**Test count:** 100+ tests  
**Duration:** ~60-90 seconds  
**⚠️ WARNING:** Includes hardware tests - ensure radio devices are connected

**Output:** Test results saved to `./TestResults/` directories:
- `./TestResults/bUnit/`
- `./TestResults/xUnit/`
- `./TestResults/E2E/`

---

### 5. run-uat-interactive
**Purpose:** Run the Audio User Acceptance Testing (UAT) tool interactively

**Usage:**
```bash
# Linux/macOS
./run-uat-interactive.sh

# Windows
.\run-uat-interactive.ps1
```

**What it runs:** `tools/Radio.Tools.AudioUAT/`  
**Interface:** Interactive menu-driven console application  
**⚠️ IMPORTANT:**
- Requires real radio hardware (RTL-SDR, RF320, etc.)
- Requires audio output devices to be configured
- Will produce audible audio output
- Tests all audio subsystems interactively

**Features:**
- Phase 2: Core Audio Engine tests
- Phase 3: Primary Audio Sources tests  
- Phase 4: Event Audio Sources tests
- Phase 5: Ducking & Priority tests
- Phase 6: Audio Outputs tests
- Phase 7: Visualization tests
- Phase 8: API & SignalR tests
- Phase 9: Fingerprinting tests
- Phase 10: Backup & Restore tests

**Duration:** Interactive (user-controlled)

---

## Common Features

All test scripts include:

✅ **Build before test** - Ensures latest code is tested  
✅ **Code coverage** - Collects coverage data with `XPlat Code Coverage`  
✅ **Results directory** - Saves test results to `./TestResults/`  
✅ **Color-coded output** (PowerShell scripts)  
✅ **Progress indicators**  
✅ **Exit codes** - Non-zero on failure for CI/CD integration

---

## Test Results

Test results are saved to `./TestResults/` with subdirectories:
- `bUnit/` - bUnit test results
- `xUnit/` - xUnit test results
- `E2E/` - End-to-end test results

Coverage reports are in Cobertura XML format: `coverage.cobertura.xml`

---

## CI/CD Integration

All scripts are designed for CI/CD pipeline integration:

```yaml
# Example GitHub Actions workflow
- name: Run all tests
  run: ./run-all-tests.sh
  
# Or run specific test suites
- name: Run bUnit tests
  run: ./run-bunit-tests.sh
  
- name: Run E2E tests
  run: ./run-e2e-tests.sh
```

---

## Troubleshooting

### Bash scripts won't execute
```bash
chmod +x run-*.sh
```

### Tests fail with hardware errors
- Ensure USB radio devices are connected
- Check device permissions (udev rules on Linux)
- Verify audio output devices are configured
- Try running UAT tool to diagnose hardware issues

### Build fails
```bash
dotnet restore
dotnet build --configuration Release
```

### Test results not saved
- Check that `./TestResults/` directory has write permissions
- Verify `--results-directory` path is correct

---

## Additional Resources

- **UIPHASEDPLAN.md** - Comprehensive UI development plan with test strategy
- **WEBUI_TODO.md** - Audit of Web UI with remaining work
- **PHASE13_IMPLEMENTATION_SUMMARY.md** - Summary of Phase 13 progress
- **tools/AUDIO_UAT.md** - UAT test tool documentation
- **tests/Radio.Web.E2ETests/README.md** - E2E testing guide

---

## Test Status

As of December 19, 2024:

| Test Suite | Status | Count | Notes |
|------------|--------|-------|-------|
| bUnit (Web.Tests) | ✅ Passing | 100 | Component tests |
| E2E (Web.E2ETests) | ✅ Passing | 6 | Browser tests |
| xUnit (Core.Tests) | ✅ Passing | Various | Unit tests |
| xUnit (Infrastructure.Tests) | ✅ Passing | Various | Integration tests |
| xUnit (API.Tests) | ✅ Passing | Various | API tests |
| UAT (AudioUAT) | ✅ Working | Interactive | Manual testing |

**Total:** All automated tests passing (100+ tests)  
**Overall:** Zero failures, zero warnings

---

**Created:** December 19, 2024  
**Last Updated:** December 19, 2024  
**Maintainer:** Radio Console Development Team
