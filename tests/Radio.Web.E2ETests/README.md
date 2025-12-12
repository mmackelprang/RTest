# Radio Console E2E Tests

End-to-end tests for the Radio Console Web UI using Playwright.

## Prerequisites

1. Install Playwright browsers:
   ```bash
   pwsh bin/Debug/net10.0/playwright.ps1 install
   ```
   
   Or on Linux/Mac:
   ```bash
   ./bin/Debug/net10.0/playwright.sh install
   ```

2. The Radio Console application must be running before executing E2E tests:
   ```bash
   cd ../../src/Radio.Web
   dotnet run
   ```

## Running the Tests

From the E2E test directory:

```bash
dotnet test
```

Or with more verbose output:

```bash
dotnet test --verbosity normal
```

## Test Configuration

- **Base URL**: Tests default to `http://localhost:5000`
- To change the URL, modify the `BaseUrl` constant in test files or set via environment variable

## Test Coverage

### HomePageE2ETests
- Page loads successfully
- Now Playing card displays
- Transport controls present (play, pause, next, previous)
- Volume control visible
- Navigation bar rendered
- Responsive layout (1920×576px) verified

## Notes

- These tests require the backend API to be running
- Tests run in headless browser mode by default
- For debugging, you can run in headed mode by modifying Playwright configuration
- Tests are parallelizable for faster execution

## Troubleshooting

If tests fail:
1. Verify the application is running at the configured URL
2. Check that all Playwright browsers are installed
3. Review test output for specific assertion failures
4. Run tests with `--verbosity detailed` for more information
