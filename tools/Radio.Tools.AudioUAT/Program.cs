using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.Audio.Visualization;
using Radio.Infrastructure.DependencyInjection;
using Radio.Tools.AudioUAT;
using Radio.Tools.AudioUAT.Phases.Phase2;
using Radio.Tools.AudioUAT.Phases.Phase3;
using Radio.Tools.AudioUAT.Phases.Phase4;
using Radio.Tools.AudioUAT.Phases.Phase5;
using Radio.Tools.AudioUAT.Phases.Phase6;
using Radio.Tools.AudioUAT.Phases.Phase7;
using Radio.Tools.AudioUAT.Phases.Phase8;
using Radio.Tools.AudioUAT.Phases.Phase9;
using Radio.Tools.AudioUAT.Phases.Phase10;
using Radio.Tools.AudioUAT.Results;
using Radio.Tools.AudioUAT.Utilities;
using Spectre.Console;

// Check for command-line arguments
if (args.Length > 0 && (args.Contains("--help") || args.Contains("-h")))
{
  ShowHelp();
  return 0;
}

// Build configuration
var configuration = new ConfigurationBuilder()
  .SetBasePath(Directory.GetCurrentDirectory())
  .AddJsonFile("appsettings.json", optional: true)
  .AddEnvironmentVariables()
  .Build();

// Build the host
var host = Host.CreateDefaultBuilder(args)
  .ConfigureServices((context, services) =>
  {
    // Configure logging
    services.AddLogging(builder =>
    {
      builder.SetMinimumLevel(LogLevel.Warning);
      builder.AddConsole();
    });

    // Configure options
    services.Configure<AudioEngineOptions>(configuration.GetSection(AudioEngineOptions.SectionName));

    // Register audio services
    services.AddSingleton<SoundFlowDeviceManager>();
    services.AddSingleton<IAudioDeviceManager>(sp => sp.GetRequiredService<SoundFlowDeviceManager>());
    services.AddSingleton<SoundFlowMasterMixer>();
    services.AddSingleton<SoundFlowAudioEngine>();
    services.AddSingleton<IAudioEngine>(sp => sp.GetRequiredService<SoundFlowAudioEngine>());

    // Register TTS services for Phase 4
    services.Configure<TTSOptions>(configuration.GetSection(TTSOptions.SectionName));
    services.Configure<TTSSecrets>(configuration.GetSection(TTSSecrets.SectionName));
    services.AddSingleton<TTSFactory>();
    services.AddSingleton<ITTSFactory>(sp => sp.GetRequiredService<TTSFactory>());
    services.AddSingleton<AudioFileEventSourceFactory>();

    // Register Ducking service for Phase 5
    services.Configure<AudioOptions>(configuration.GetSection(AudioOptions.SectionName));
    services.AddSingleton<DuckingService>();
    services.AddSingleton<IDuckingService>(sp => sp.GetRequiredService<DuckingService>());

    // Register Audio Output services for Phase 6
    services.Configure<AudioOutputOptions>(configuration.GetSection(AudioOutputOptions.SectionName));
    services.AddSingleton<LocalAudioOutput>();
    services.AddSingleton<GoogleCastOutput>();
    services.AddSingleton<HttpStreamOutput>();

    // Register Visualization services for Phase 7
    services.Configure<VisualizerOptions>(configuration.GetSection(VisualizerOptions.SectionName));
    services.AddSingleton<VisualizerService>();
    services.AddSingleton<IVisualizerService>(sp => sp.GetRequiredService<VisualizerService>());

    // Register Fingerprinting services for Phase 9
    services.AddFingerprinting(configuration);

    // Register Configuration and Metrics services for Phase 10 (Backup/Restore)
    services.AddManagedConfiguration(configuration, useSqliteSecrets: true);
    services.AddMetrics(configuration);

    // Register UAT services
    services.AddSingleton<TestResultsManager>();
    services.AddSingleton<TestRunner>();
    services.AddSingleton<CoreAudioEngineTests>();
    services.AddSingleton<PrimaryAudioSourceTests>();
    services.AddSingleton<EventAudioSourceTests>();
    services.AddSingleton<DuckingPriorityTests>();
    services.AddSingleton<AudioOutputTests>();
    services.AddSingleton<VisualizationTests>();
    services.AddSingleton<ApiSignalRTests>();
    services.AddSingleton<FingerprintingTests>();
    services.AddSingleton<BackupRestoreTests>();
  })
  .Build();

var serviceProvider = host.Services;

// Check for automated test runs
if (args.Length > 0)
{
  return await RunAutomatedTests(args, serviceProvider);
}

// Run interactive mode
await RunInteractiveMode(serviceProvider);
return 0;

static void ShowHelp()
{
  AnsiConsole.MarkupLine("[bold blue]Radio Audio UAT Tool[/]");
  AnsiConsole.WriteLine();
  AnsiConsole.MarkupLine("[bold]Usage:[/]");
  AnsiConsole.MarkupLine("  dotnet run                        Run in interactive mode");
  AnsiConsole.MarkupLine("  dotnet run -- --all               Run all tests");
  AnsiConsole.MarkupLine("  dotnet run -- --phase 2           Run all Phase 2 tests");
  AnsiConsole.MarkupLine("  dotnet run -- --test P2-001       Run specific test");
  AnsiConsole.MarkupLine("  dotnet run -- --output results.json  Export results to file");
  AnsiConsole.MarkupLine("  dotnet run -- --help              Show this help");
  AnsiConsole.WriteLine();
  AnsiConsole.MarkupLine("[bold]Options:[/]");
  AnsiConsole.MarkupLine("  --all             Run all available tests");
  AnsiConsole.MarkupLine("  --phase <n>       Run all tests in phase n");
  AnsiConsole.MarkupLine("  --test <id>       Run specific test by ID");
  AnsiConsole.MarkupLine("  --output <file>   Export results to JSON file");
  AnsiConsole.MarkupLine("  --help, -h        Show help");
}

static async Task<int> RunAutomatedTests(string[] args, IServiceProvider services)
{
  var resultsManager = services.GetRequiredService<TestResultsManager>();
  var runner = services.GetRequiredService<TestRunner>();
  var phase2Tests = services.GetRequiredService<CoreAudioEngineTests>();
  var phase3Tests = services.GetRequiredService<PrimaryAudioSourceTests>();
  var phase4Tests = services.GetRequiredService<EventAudioSourceTests>();
  var phase5Tests = services.GetRequiredService<DuckingPriorityTests>();
  var phase6Tests = services.GetRequiredService<AudioOutputTests>();
  var phase7Tests = services.GetRequiredService<VisualizationTests>();
  var phase8Tests = services.GetRequiredService<ApiSignalRTests>();
  var phase9Tests = services.GetRequiredService<FingerprintingTests>();
  var phase10Tests = services.GetRequiredService<BackupRestoreTests>();

  var testsToRun = new List<IPhaseTest>();

  // Parse arguments
  var runAll = args.Contains("--all");
  var outputFile = GetArgValue(args, "--output");

  if (runAll || args.Contains("--phase"))
  {
    var phaseStr = GetArgValue(args, "--phase");
    if (runAll || phaseStr == "2")
    {
      testsToRun.AddRange(phase2Tests.GetAllTests());
    }
    if (runAll || phaseStr == "3")
    {
      testsToRun.AddRange(phase3Tests.GetAllTests());
    }
    if (runAll || phaseStr == "4")
    {
      testsToRun.AddRange(phase4Tests.GetAllTests());
    }
    if (runAll || phaseStr == "5")
    {
      testsToRun.AddRange(phase5Tests.GetAllTests());
    }
    if (runAll || phaseStr == "6")
    {
      testsToRun.AddRange(phase6Tests.GetAllTests());
    }
    if (runAll || phaseStr == "7")
    {
      testsToRun.AddRange(phase7Tests.GetAllTests());
    }
    if (runAll || phaseStr == "8")
    {
      testsToRun.AddRange(phase8Tests.GetAllTests());
    }
    if (runAll || phaseStr == "9")
    {
      testsToRun.AddRange(phase9Tests.GetAllTests());
    }
    if (runAll || phaseStr == "10")
    {
      testsToRun.AddRange(phase10Tests.GetAllTests());
    }
  }
  else if (args.Contains("--test"))
  {
    var testId = GetArgValue(args, "--test");
    if (!string.IsNullOrEmpty(testId))
    {
      // Search in all phases
      var allTests = phase2Tests.GetAllTests()
        .Concat(phase3Tests.GetAllTests())
        .Concat(phase4Tests.GetAllTests())
        .Concat(phase5Tests.GetAllTests())
        .Concat(phase6Tests.GetAllTests())
        .Concat(phase7Tests.GetAllTests())
        .Concat(phase8Tests.GetAllTests())
        .Concat(phase9Tests.GetAllTests())
        .Concat(phase10Tests.GetAllTests())
        .ToList();
      var test = allTests.FirstOrDefault(t => t.TestId == testId);
      if (test != null)
      {
        testsToRun.Add(test);
      }
      else
      {
        AnsiConsole.MarkupLine($"[red]Test '{testId}' not found[/]");
        return 1;
      }
    }
  }

  if (testsToRun.Count == 0)
  {
    AnsiConsole.MarkupLine("[yellow]No tests to run[/]");
    return 0;
  }

  AnsiConsole.MarkupLine($"[blue]Running {testsToRun.Count} test(s)...[/]");
  AnsiConsole.WriteLine();

  var results = await runner.RunAllTestsAsync(testsToRun);

  // Display results
  ConsoleUI.DisplayTestResults(results);
  ConsoleUI.DisplaySummary(resultsManager.GetSummary());

  // Export if requested
  if (!string.IsNullOrEmpty(outputFile))
  {
    await resultsManager.ExportToJsonAsync(outputFile);
    AnsiConsole.MarkupLine($"[green]Results exported to {outputFile}[/]");
  }

  return resultsManager.GetSummary().FailedTests > 0 ? 1 : 0;
}

static string? GetArgValue(string[] args, string argName)
{
  var index = Array.IndexOf(args, argName);
  if (index >= 0 && index + 1 < args.Length)
  {
    return args[index + 1];
  }
  return null;
}

static async Task RunInteractiveMode(IServiceProvider services)
{
  var resultsManager = services.GetRequiredService<TestResultsManager>();
  var runner = services.GetRequiredService<TestRunner>();
  var phase2Tests = services.GetRequiredService<CoreAudioEngineTests>();
  var phase3Tests = services.GetRequiredService<PrimaryAudioSourceTests>();
  var phase4Tests = services.GetRequiredService<EventAudioSourceTests>();
  var phase5Tests = services.GetRequiredService<DuckingPriorityTests>();
  var phase6Tests = services.GetRequiredService<AudioOutputTests>();
  var phase7Tests = services.GetRequiredService<VisualizationTests>();
  var phase8Tests = services.GetRequiredService<ApiSignalRTests>();
  var phase9Tests = services.GetRequiredService<FingerprintingTests>();
  var phase10Tests = services.GetRequiredService<BackupRestoreTests>();

  ConsoleUI.WriteWelcomeBanner();

  var exit = false;
  while (!exit)
  {
    var summary = resultsManager.GetSummary();
    var statusLine = summary.TotalTests > 0
      ? $"Tests: {summary.TotalTests} | Pass: {summary.PassedTests} | Fail: {summary.FailedTests}"
      : "No tests run yet";

    AnsiConsole.WriteLine();
    var rule = new Rule($"[grey]{statusLine}[/]")
    {
      Justification = Justify.Right,
      Style = Style.Parse("grey")
    };
    AnsiConsole.Write(rule);

    var choice = ConsoleUI.ShowMenu(
      "[bold]Main Menu[/]",
      "Phase 2: Core Audio Engine Tests",
      "Phase 3: Primary Audio Sources Tests",
      "Phase 4: Event Audio Sources Tests",
      "Phase 5: Ducking & Priority Tests",
      "Phase 6: Audio Outputs Tests",
      "Phase 7: Visualization & Monitoring Tests",
      "Phase 8: API & SignalR Tests",
      "Phase 9: Audio Fingerprinting Tests",
      "Phase 10: Database Backup & Restore Tests",
      "View Test Results",
      "Export Results to JSON",
      "Clear Results",
      "Quit"
    );

    switch (choice)
    {
      case "Phase 2: Core Audio Engine Tests":
        await RunPhase2Menu(services, runner, phase2Tests);
        break;

      case "Phase 3: Primary Audio Sources Tests":
        await RunPhase3Menu(services, runner, phase3Tests);
        break;

      case "Phase 4: Event Audio Sources Tests":
        await RunPhase4Menu(services, runner, phase4Tests);
        break;

      case "Phase 5: Ducking & Priority Tests":
        await RunPhase5Menu(services, runner, phase5Tests);
        break;

      case "Phase 6: Audio Outputs Tests":
        await RunPhase6Menu(services, runner, phase6Tests);
        break;

      case "Phase 7: Visualization & Monitoring Tests":
        await RunPhase7Menu(services, runner, phase7Tests);
        break;

      case "Phase 8: API & SignalR Tests":
        await RunPhase8Menu(services, runner, phase8Tests);
        break;

      case "Phase 9: Audio Fingerprinting Tests":
        await RunPhase9Menu(services, runner, phase9Tests);
        break;

      case "Phase 10: Database Backup & Restore Tests":
        await RunPhase10Menu(services, runner, phase10Tests);
        break;

      case "View Test Results":
        ViewResults(resultsManager);
        break;

      case "Export Results to JSON":
        await ExportResults(resultsManager);
        break;

      case "Clear Results":
        resultsManager.Clear();
        ConsoleUI.WriteSuccess("Results cleared");
        ConsoleUI.PressAnyKeyToContinue();
        break;

      case "Quit":
        exit = true;
        break;
    }
  }

  // Cleanup
  var engine = services.GetService<IAudioEngine>();
  if (engine != null)
  {
    await engine.DisposeAsync();
  }

  AnsiConsole.MarkupLine("[blue]Goodbye![/]");
}

static async Task RunPhase2Menu(IServiceProvider services, TestRunner runner, CoreAudioEngineTests tests)
{
  var exit = false;
  while (!exit)
  {
    AnsiConsole.Clear();

    var rule = new Rule("[cyan]Phase 2: Core Audio Engine Tests[/]")
    {
      Justification = Justify.Center
    };
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();

    var allTests = tests.GetAllTests();
    var menuItems = new List<string>
    {
      "Run All Phase 2 Tests",
      "---",
    };

    foreach (var test in allTests)
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("Return to Main Menu");

    var choice = ConsoleUI.ShowMenu("[bold]Select Test[/]", menuItems.ToArray());

    if (choice == "Return to Main Menu")
    {
      exit = true;
      continue;
    }

    if (choice == "---")
    {
      continue;
    }

    if (choice == "Run All Phase 2 Tests")
    {
      AnsiConsole.Clear();
      ConsoleUI.WriteHeader("Running All Phase 2 Tests");

      await AnsiConsole.Progress()
        .AutoClear(false)
        .Columns(
          new TaskDescriptionColumn(),
          new ProgressBarColumn(),
          new PercentageColumn(),
          new SpinnerColumn())
        .StartAsync(async ctx =>
        {
          var task = ctx.AddTask("Running tests...", maxValue: allTests.Count);

          foreach (var test in allTests)
          {
            task.Description = $"Running {test.TestId}...";
            await runner.RunTestAsync(test);
            task.Increment(1);
          }
        });

      var resultsManager = services.GetRequiredService<TestResultsManager>();
      ConsoleUI.DisplayTestResults(resultsManager.GetResultsForPhase(2));
      ConsoleUI.DisplaySummary(resultsManager.GetSummaryForPhase(2));
      ConsoleUI.PressAnyKeyToContinue();
    }
    else
    {
      // Extract test ID from choice
      var testIdMatch = System.Text.RegularExpressions.Regex.Match(choice, @"\[([^\]]+)\]");
      if (testIdMatch.Success)
      {
        var testId = testIdMatch.Groups[1].Value;
        var test = allTests.FirstOrDefault(t => t.TestId == testId);
        if (test != null)
        {
          AnsiConsole.Clear();
          var result = await runner.RunTestAsync(test);

          AnsiConsole.WriteLine();
          if (result.Passed)
          {
            ConsoleUI.WriteSuccess($"Test {test.TestId} PASSED");
          }
          else if (result.Skipped)
          {
            ConsoleUI.WriteWarning($"Test {test.TestId} SKIPPED: {result.Message}");
          }
          else
          {
            ConsoleUI.WriteError($"Test {test.TestId} FAILED: {result.Message}");
          }

          ConsoleUI.PressAnyKeyToContinue();
        }
      }
    }
  }
}

static void ViewResults(TestResultsManager resultsManager)
{
  AnsiConsole.Clear();
  ConsoleUI.WriteHeader("Test Results");

  var results = resultsManager.GetAllResults();
  if (results.Count == 0)
  {
    ConsoleUI.WriteWarning("No test results available");
  }
  else
  {
    ConsoleUI.DisplayTestResults(results);
    AnsiConsole.WriteLine();
    ConsoleUI.DisplaySummary(resultsManager.GetSummary());
  }

  ConsoleUI.PressAnyKeyToContinue();
}

static async Task RunPhase3Menu(IServiceProvider services, TestRunner runner, PrimaryAudioSourceTests tests)
{
  var exit = false;
  while (!exit)
  {
    AnsiConsole.Clear();

    var rule = new Rule("[cyan]Phase 3: Primary Audio Sources Tests[/]")
    {
      Justification = Justify.Center
    };
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();

    var allTests = tests.GetAllTests();
    var menuItems = new List<string>
    {
      "Run All Phase 3 Tests",
      "---",
      "[RADIO TESTS]",
    };

    // Group tests by category
    foreach (var test in allTests.Where(t => t.TestId.StartsWith("P3-00")))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[VINYL TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P3-005" or "P3-006" or "P3-007"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[SPOTIFY TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P3-008" or "P3-009" or "P3-010"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[MULTI-SOURCE TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P3-011" or "P3-012" or "P3-013" or "P3-014"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("Return to Main Menu");

    var choice = ConsoleUI.ShowMenu("[bold]Select Test[/]", menuItems.ToArray());

    if (choice == "Return to Main Menu")
    {
      exit = true;
      continue;
    }

    if (choice.StartsWith("---") || choice.StartsWith("[RADIO") || choice.StartsWith("[VINYL") ||
        choice.StartsWith("[SPOTIFY") || choice.StartsWith("[MULTI"))
    {
      continue;
    }

    if (choice == "Run All Phase 3 Tests")
    {
      AnsiConsole.Clear();
      ConsoleUI.WriteHeader("Running All Phase 3 Tests");

      await AnsiConsole.Progress()
        .AutoClear(false)
        .Columns(
          new TaskDescriptionColumn(),
          new ProgressBarColumn(),
          new PercentageColumn(),
          new SpinnerColumn())
        .StartAsync(async ctx =>
        {
          var task = ctx.AddTask("Running tests...", maxValue: allTests.Count);

          foreach (var test in allTests)
          {
            task.Description = $"Running {test.TestId}...";
            await runner.RunTestAsync(test);
            task.Increment(1);
          }
        });

      var resultsManager = services.GetRequiredService<TestResultsManager>();
      ConsoleUI.DisplayTestResults(resultsManager.GetResultsForPhase(3));
      ConsoleUI.DisplaySummary(resultsManager.GetSummaryForPhase(3));
      ConsoleUI.PressAnyKeyToContinue();
    }
    else
    {
      // Extract test ID from choice
      var testIdMatch = System.Text.RegularExpressions.Regex.Match(choice, @"\[([^\]]+)\]");
      if (testIdMatch.Success)
      {
        var testId = testIdMatch.Groups[1].Value;
        var test = allTests.FirstOrDefault(t => t.TestId == testId);
        if (test != null)
        {
          AnsiConsole.Clear();
          var result = await runner.RunTestAsync(test);

          AnsiConsole.WriteLine();
          if (result.Passed)
          {
            ConsoleUI.WriteSuccess($"Test {test.TestId} PASSED");
          }
          else if (result.Skipped)
          {
            ConsoleUI.WriteWarning($"Test {test.TestId} SKIPPED: {result.Message}");
          }
          else
          {
            ConsoleUI.WriteError($"Test {test.TestId} FAILED: {result.Message}");
          }

          ConsoleUI.PressAnyKeyToContinue();
        }
      }
    }
  }
}

static async Task ExportResults(TestResultsManager resultsManager)
{
  var results = resultsManager.GetAllResults();
  if (results.Count == 0)
  {
    ConsoleUI.WriteWarning("No test results to export");
    ConsoleUI.PressAnyKeyToContinue();
    return;
  }

  var filename = AnsiConsole.Ask<string>("Enter filename:", "test-results.json");
  await resultsManager.ExportToJsonAsync(filename);
  ConsoleUI.WriteSuccess($"Results exported to {filename}");
  ConsoleUI.PressAnyKeyToContinue();
}

static async Task RunPhase4Menu(IServiceProvider services, TestRunner runner, EventAudioSourceTests tests)
{
  var exit = false;
  while (!exit)
  {
    AnsiConsole.Clear();

    var rule = new Rule("[cyan]Phase 4: Event Audio Sources Tests[/]")
    {
      Justification = Justify.Center
    };
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();

    var allTests = tests.GetAllTests();
    var menuItems = new List<string>
    {
      "Run All Phase 4 Tests",
      "---",
      "[SOUND EFFECTS]",
    };

    // Group tests by category
    foreach (var test in allTests.Where(t => t.TestId is "P4-001" or "P4-002" or "P4-003" or "P4-004"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[NOTIFICATIONS]");
    foreach (var test in allTests.Where(t => t.TestId is "P4-005" or "P4-006" or "P4-007"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[CHIMES & SCHEDULED]");
    foreach (var test in allTests.Where(t => t.TestId is "P4-008" or "P4-011" or "P4-012"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[TEXT-TO-SPEECH]");
    foreach (var test in allTests.Where(t => t.TestId is "P4-009" or "P4-010"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[STRESS TESTS]");
    foreach (var test in allTests.Where(t => t.TestId == "P4-013"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("Return to Main Menu");

    var choice = ConsoleUI.ShowMenu("[bold]Select Test[/]", menuItems.ToArray());

    if (choice == "Return to Main Menu")
    {
      exit = true;
      continue;
    }

    if (choice.StartsWith("---") || choice.StartsWith("[SOUND") || choice.StartsWith("[NOTIFICATIONS") ||
        choice.StartsWith("[CHIMES") || choice.StartsWith("[TEXT") || choice.StartsWith("[STRESS"))
    {
      continue;
    }

    if (choice == "Run All Phase 4 Tests")
    {
      AnsiConsole.Clear();
      ConsoleUI.WriteHeader("Running All Phase 4 Tests");

      await AnsiConsole.Progress()
        .AutoClear(false)
        .Columns(
          new TaskDescriptionColumn(),
          new ProgressBarColumn(),
          new PercentageColumn(),
          new SpinnerColumn())
        .StartAsync(async ctx =>
        {
          var task = ctx.AddTask("Running tests...", maxValue: allTests.Count);

          foreach (var test in allTests)
          {
            task.Description = $"Running {test.TestId}...";
            await runner.RunTestAsync(test);
            task.Increment(1);
          }
        });

      var resultsManager = services.GetRequiredService<TestResultsManager>();
      ConsoleUI.DisplayTestResults(resultsManager.GetResultsForPhase(4));
      ConsoleUI.DisplaySummary(resultsManager.GetSummaryForPhase(4));
      ConsoleUI.PressAnyKeyToContinue();
    }
    else
    {
      // Extract test ID from choice
      var testIdMatch = System.Text.RegularExpressions.Regex.Match(choice, @"\[([^\]]+)\]");
      if (testIdMatch.Success)
      {
        var testId = testIdMatch.Groups[1].Value;
        var test = allTests.FirstOrDefault(t => t.TestId == testId);
        if (test != null)
        {
          AnsiConsole.Clear();
          var result = await runner.RunTestAsync(test);

          AnsiConsole.WriteLine();
          if (result.Passed)
          {
            ConsoleUI.WriteSuccess($"Test {test.TestId} PASSED");
          }
          else if (result.Skipped)
          {
            ConsoleUI.WriteWarning($"Test {test.TestId} SKIPPED: {result.Message}");
          }
          else
          {
            ConsoleUI.WriteError($"Test {test.TestId} FAILED: {result.Message}");
          }

          ConsoleUI.PressAnyKeyToContinue();
        }
      }
    }
  }
}

static async Task RunPhase5Menu(IServiceProvider services, TestRunner runner, DuckingPriorityTests tests)
{
  var exit = false;
  while (!exit)
  {
    AnsiConsole.Clear();

    var rule = new Rule("[cyan]Phase 5: Ducking & Priority Tests[/]")
    {
      Justification = Justify.Center
    };
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();

    var allTests = tests.GetAllTests();
    var menuItems = new List<string>
    {
      "Run All Phase 5 Tests",
      "---",
      "[PRIORITY TESTS]",
    };

    // Group tests by category
    foreach (var test in allTests.Where(t => t.TestId is "P5-001" or "P5-002" or "P5-003"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[DUCKING TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P5-004" or "P5-005" or "P5-006" or "P5-007" or "P5-008"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[PRIORITY OVERRIDE TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P5-009" or "P5-010"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[CONFIGURATION TESTS]");
    foreach (var test in allTests.Where(t => t.TestId == "P5-011"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[INTEGRATION TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P5-012" or "P5-013"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("Return to Main Menu");

    var choice = ConsoleUI.ShowMenu("[bold]Select Test[/]", menuItems.ToArray());

    if (choice == "Return to Main Menu")
    {
      exit = true;
      continue;
    }

    if (choice.StartsWith("---") || choice.StartsWith("[PRIORITY") || choice.StartsWith("[DUCKING") ||
        choice.StartsWith("[CONFIGURATION") || choice.StartsWith("[INTEGRATION"))
    {
      continue;
    }

    if (choice == "Run All Phase 5 Tests")
    {
      AnsiConsole.Clear();
      ConsoleUI.WriteHeader("Running All Phase 5 Tests");

      await AnsiConsole.Progress()
        .AutoClear(false)
        .Columns(
          new TaskDescriptionColumn(),
          new ProgressBarColumn(),
          new PercentageColumn(),
          new SpinnerColumn())
        .StartAsync(async ctx =>
        {
          var task = ctx.AddTask("Running tests...", maxValue: allTests.Count);

          foreach (var test in allTests)
          {
            task.Description = $"Running {test.TestId}...";
            await runner.RunTestAsync(test);
            task.Increment(1);
          }
        });

      var resultsManager = services.GetRequiredService<TestResultsManager>();
      ConsoleUI.DisplayTestResults(resultsManager.GetResultsForPhase(5));
      ConsoleUI.DisplaySummary(resultsManager.GetSummaryForPhase(5));
      ConsoleUI.PressAnyKeyToContinue();
    }
    else
    {
      // Extract test ID from choice
      var testIdMatch = System.Text.RegularExpressions.Regex.Match(choice, @"\[([^\]]+)\]");
      if (testIdMatch.Success)
      {
        var testId = testIdMatch.Groups[1].Value;
        var test = allTests.FirstOrDefault(t => t.TestId == testId);
        if (test != null)
        {
          AnsiConsole.Clear();
          var result = await runner.RunTestAsync(test);

          AnsiConsole.WriteLine();
          if (result.Passed)
          {
            ConsoleUI.WriteSuccess($"Test {test.TestId} PASSED");
          }
          else if (result.Skipped)
          {
            ConsoleUI.WriteWarning($"Test {test.TestId} SKIPPED: {result.Message}");
          }
          else
          {
            ConsoleUI.WriteError($"Test {test.TestId} FAILED: {result.Message}");
          }

          ConsoleUI.PressAnyKeyToContinue();
        }
      }
    }
  }
}

static async Task RunPhase6Menu(IServiceProvider services, TestRunner runner, AudioOutputTests tests)
{
  var exit = false;
  while (!exit)
  {
    AnsiConsole.Clear();

    var rule = new Rule("[cyan]Phase 6: Audio Outputs Tests[/]")
    {
      Justification = Justify.Center
    };
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();

    var allTests = tests.GetAllTests();
    var menuItems = new List<string>
    {
      "Run All Phase 6 Tests",
      "---",
      "[MULTI-DEVICE TESTS]",
    };

    // Group tests by category
    foreach (var test in allTests.Where(t => t.TestId is "P6-001" or "P6-002" or "P6-003"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[CHROMECAST TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P6-004" or "P6-005" or "P6-006" or "P6-007"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[HTTP STREAMING TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P6-008" or "P6-009" or "P6-010" or "P6-011"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[DIAGNOSTICS TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P6-012" or "P6-013" or "P6-014"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("Return to Main Menu");

    var choice = ConsoleUI.ShowMenu("[bold]Select Test[/]", menuItems.ToArray());

    if (choice == "Return to Main Menu")
    {
      exit = true;
      continue;
    }

    if (choice.StartsWith("---") || choice.StartsWith("[MULTI") || choice.StartsWith("[CHROMECAST") ||
        choice.StartsWith("[HTTP") || choice.StartsWith("[DIAGNOSTICS"))
    {
      continue;
    }

    if (choice == "Run All Phase 6 Tests")
    {
      AnsiConsole.Clear();
      ConsoleUI.WriteHeader("Running All Phase 6 Tests");

      await AnsiConsole.Progress()
        .AutoClear(false)
        .Columns(
          new TaskDescriptionColumn(),
          new ProgressBarColumn(),
          new PercentageColumn(),
          new SpinnerColumn())
        .StartAsync(async ctx =>
        {
          var task = ctx.AddTask("Running tests...", maxValue: allTests.Count);

          foreach (var test in allTests)
          {
            task.Description = $"Running {test.TestId}...";
            await runner.RunTestAsync(test);
            task.Increment(1);
          }
        });

      var resultsManager = services.GetRequiredService<TestResultsManager>();
      ConsoleUI.DisplayTestResults(resultsManager.GetResultsForPhase(6));
      ConsoleUI.DisplaySummary(resultsManager.GetSummaryForPhase(6));
      ConsoleUI.PressAnyKeyToContinue();
    }
    else
    {
      // Extract test ID from choice
      var testIdMatch = System.Text.RegularExpressions.Regex.Match(choice, @"\[([^\]]+)\]");
      if (testIdMatch.Success)
      {
        var testId = testIdMatch.Groups[1].Value;
        var test = allTests.FirstOrDefault(t => t.TestId == testId);
        if (test != null)
        {
          AnsiConsole.Clear();
          var result = await runner.RunTestAsync(test);

          AnsiConsole.WriteLine();
          if (result.Passed)
          {
            ConsoleUI.WriteSuccess($"Test {test.TestId} PASSED");
          }
          else if (result.Skipped)
          {
            ConsoleUI.WriteWarning($"Test {test.TestId} SKIPPED: {result.Message}");
          }
          else
          {
            ConsoleUI.WriteError($"Test {test.TestId} FAILED: {result.Message}");
          }

          ConsoleUI.PressAnyKeyToContinue();
        }
      }
    }
  }
}

static async Task RunPhase7Menu(IServiceProvider services, TestRunner runner, VisualizationTests tests)
{
  var exit = false;
  while (!exit)
  {
    AnsiConsole.Clear();

    var rule = new Rule("[cyan]Phase 7: Visualization & Monitoring Tests[/]")
    {
      Justification = Justify.Center
    };
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();

    var allTests = tests.GetAllTests();
    var menuItems = new List<string>
    {
      "Run All Phase 7 Tests",
      "---",
      "[SPECTRUM ANALYZER TESTS]",
    };

    // Group tests by category
    foreach (var test in allTests.Where(t => t.TestId is "P7-001" or "P7-002" or "P7-003" or "P7-004"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[LEVEL METER TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P7-005" or "P7-006" or "P7-007" or "P7-008" or "P7-009"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[WAVEFORM TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P7-010" or "P7-011" or "P7-012"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[INTEGRATION TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P7-013" or "P7-014" or "P7-015"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("Return to Main Menu");

    var choice = ConsoleUI.ShowMenu("[bold]Select Test[/]", menuItems.ToArray());

    if (choice == "Return to Main Menu")
    {
      exit = true;
      continue;
    }

    if (choice.StartsWith("---") || choice.StartsWith("[SPECTRUM") || choice.StartsWith("[LEVEL") ||
        choice.StartsWith("[WAVEFORM") || choice.StartsWith("[INTEGRATION"))
    {
      continue;
    }

    if (choice == "Run All Phase 7 Tests")
    {
      AnsiConsole.Clear();
      ConsoleUI.WriteHeader("Running All Phase 7 Tests");

      await AnsiConsole.Progress()
        .AutoClear(false)
        .Columns(
          new TaskDescriptionColumn(),
          new ProgressBarColumn(),
          new PercentageColumn(),
          new SpinnerColumn())
        .StartAsync(async ctx =>
        {
          var task = ctx.AddTask("Running tests...", maxValue: allTests.Count);

          foreach (var test in allTests)
          {
            task.Description = $"Running {test.TestId}...";
            await runner.RunTestAsync(test);
            task.Increment(1);
          }
        });

      var resultsManager = services.GetRequiredService<TestResultsManager>();
      ConsoleUI.DisplayTestResults(resultsManager.GetResultsForPhase(7));
      ConsoleUI.DisplaySummary(resultsManager.GetSummaryForPhase(7));
      ConsoleUI.PressAnyKeyToContinue();
    }
    else
    {
      // Extract test ID from choice
      var testIdMatch = System.Text.RegularExpressions.Regex.Match(choice, @"\[([^\]]+)\]");
      if (testIdMatch.Success)
      {
        var testId = testIdMatch.Groups[1].Value;
        var test = allTests.FirstOrDefault(t => t.TestId == testId);
        if (test != null)
        {
          AnsiConsole.Clear();
          var result = await runner.RunTestAsync(test);

          AnsiConsole.WriteLine();
          if (result.Passed)
          {
            ConsoleUI.WriteSuccess($"Test {test.TestId} PASSED");
          }
          else if (result.Skipped)
          {
            ConsoleUI.WriteWarning($"Test {test.TestId} SKIPPED: {result.Message}");
          }
          else
          {
            ConsoleUI.WriteError($"Test {test.TestId} FAILED: {result.Message}");
          }

          ConsoleUI.PressAnyKeyToContinue();
        }
      }
    }
  }
}

static async Task RunPhase8Menu(IServiceProvider services, TestRunner runner, ApiSignalRTests tests)
{
  var exit = false;
  while (!exit)
  {
    AnsiConsole.Clear();

    var rule = new Rule("[cyan]Phase 8: API & SignalR Tests[/]")
    {
      Justification = Justify.Center
    };
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();

    var allTests = tests.GetAllTests();
    var menuItems = new List<string>
    {
      "Run All Phase 8 Tests",
      "---",
      "[REST API TESTS]",
    };

    // Group tests by category
    foreach (var test in allTests.Where(t => t.TestId is "P8-001" or "P8-002" or "P8-003" or "P8-004" or "P8-005"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[SIGNALR TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P8-006" or "P8-007" or "P8-008"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[AUDIO STREAMING TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P8-009" or "P8-010"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[INTEGRATION TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P8-011" or "P8-012"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("Return to Main Menu");

    var choice = ConsoleUI.ShowMenu("[bold]Select Test[/]", menuItems.ToArray());

    if (choice == "Return to Main Menu")
    {
      exit = true;
      continue;
    }

    if (choice.StartsWith("---") || choice.StartsWith("[REST") || choice.StartsWith("[SIGNALR") ||
        choice.StartsWith("[AUDIO") || choice.StartsWith("[INTEGRATION"))
    {
      continue;
    }

    if (choice == "Run All Phase 8 Tests")
    {
      AnsiConsole.Clear();
      ConsoleUI.WriteHeader("Running All Phase 8 Tests");

      await AnsiConsole.Progress()
        .AutoClear(false)
        .Columns(
          new TaskDescriptionColumn(),
          new ProgressBarColumn(),
          new PercentageColumn(),
          new SpinnerColumn())
        .StartAsync(async ctx =>
        {
          var task = ctx.AddTask("Running tests...", maxValue: allTests.Count);

          foreach (var test in allTests)
          {
            task.Description = $"Running {test.TestId}...";
            await runner.RunTestAsync(test);
            task.Increment(1);
          }
        });

      var resultsManager = services.GetRequiredService<TestResultsManager>();
      ConsoleUI.DisplayTestResults(resultsManager.GetResultsForPhase(8));
      ConsoleUI.DisplaySummary(resultsManager.GetSummaryForPhase(8));
      ConsoleUI.PressAnyKeyToContinue();
    }
    else
    {
      // Extract test ID from choice
      var testIdMatch = System.Text.RegularExpressions.Regex.Match(choice, @"\[([^\]]+)\]");
      if (testIdMatch.Success)
      {
        var testId = testIdMatch.Groups[1].Value;
        var test = allTests.FirstOrDefault(t => t.TestId == testId);
        if (test != null)
        {
          AnsiConsole.Clear();
          var result = await runner.RunTestAsync(test);

          AnsiConsole.WriteLine();
          if (result.Passed)
          {
            ConsoleUI.WriteSuccess($"Test {test.TestId} PASSED");
          }
          else if (result.Skipped)
          {
            ConsoleUI.WriteWarning($"Test {test.TestId} SKIPPED: {result.Message}");
          }
          else
          {
            ConsoleUI.WriteError($"Test {test.TestId} FAILED: {result.Message}");
          }

          ConsoleUI.PressAnyKeyToContinue();
        }
      }
    }
  }
}

static async Task RunPhase9Menu(IServiceProvider services, TestRunner runner, FingerprintingTests tests)
{
  var exit = false;
  while (!exit)
  {
    AnsiConsole.Clear();

    var rule = new Rule("[cyan]Phase 9: Audio Fingerprinting Tests[/]")
    {
      Justification = Justify.Center
    };
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();

    var allTests = tests.GetAllTests();
    var menuItems = new List<string>
    {
      "Run All Phase 9 Tests",
      "---",
      "[DATABASE TESTS]",
    };

    // Group tests by category
    foreach (var test in allTests.Where(t => t.TestId is "P9-001" or "P9-003" or "P9-012"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[FINGERPRINT TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P9-002" or "P9-008" or "P9-009" or "P9-010"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[METADATA TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P9-004" or "P9-011"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("[PLAY HISTORY TESTS]");
    foreach (var test in allTests.Where(t => t.TestId is "P9-005" or "P9-006" or "P9-007"))
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("Return to Main Menu");

    var choice = ConsoleUI.ShowMenu("[bold]Select Test[/]", menuItems.ToArray());

    if (choice == "Return to Main Menu")
    {
      exit = true;
      continue;
    }

    if (choice.StartsWith("---") || choice.StartsWith("[DATABASE") || choice.StartsWith("[FINGERPRINT") ||
        choice.StartsWith("[METADATA") || choice.StartsWith("[PLAY"))
    {
      continue;
    }

    if (choice == "Run All Phase 9 Tests")
    {
      AnsiConsole.Clear();
      ConsoleUI.WriteHeader("Running All Phase 9 Tests");

      await AnsiConsole.Progress()
        .AutoClear(false)
        .Columns(
          new TaskDescriptionColumn(),
          new ProgressBarColumn(),
          new PercentageColumn(),
          new SpinnerColumn())
        .StartAsync(async ctx =>
        {
          var task = ctx.AddTask("Running tests...", maxValue: allTests.Count);

          foreach (var test in allTests)
          {
            task.Description = $"Running {test.TestId}...";
            await runner.RunTestAsync(test);
            task.Increment(1);
          }
        });

      var resultsManager = services.GetRequiredService<TestResultsManager>();
      ConsoleUI.DisplayTestResults(resultsManager.GetResultsForPhase(9));
      ConsoleUI.DisplaySummary(resultsManager.GetSummaryForPhase(9));
      ConsoleUI.PressAnyKeyToContinue();
    }
    else
    {
      // Extract test ID from choice
      var testIdMatch = System.Text.RegularExpressions.Regex.Match(choice, @"\[([^\]]+)\]");
      if (testIdMatch.Success)
      {
        var testId = testIdMatch.Groups[1].Value;
        var test = allTests.FirstOrDefault(t => t.TestId == testId);
        if (test != null)
        {
          AnsiConsole.Clear();
          var result = await runner.RunTestAsync(test);

          AnsiConsole.WriteLine();
          if (result.Passed)
          {
            ConsoleUI.WriteSuccess($"Test {test.TestId} PASSED");
          }
          else if (result.Skipped)
          {
            ConsoleUI.WriteWarning($"Test {test.TestId} SKIPPED: {result.Message}");
          }
          else
          {
            ConsoleUI.WriteError($"Test {test.TestId} FAILED: {result.Message}");
          }

          ConsoleUI.PressAnyKeyToContinue();
        }
      }
    }
  }
}

static async Task RunPhase10Menu(IServiceProvider services, TestRunner runner, BackupRestoreTests tests)
{
  var exit = false;
  while (!exit)
  {
    AnsiConsole.Clear();

    var rule = new Rule("[cyan]Phase 10: Database Backup & Restore Tests[/]")
    {
      Justification = Justify.Center
    };
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();

    var allTests = tests.GetAllTests();
    var menuItems = new List<string>
    {
      "Run All Phase 10 Tests",
      "---",
    };

    foreach (var test in allTests)
    {
      menuItems.Add($"[{test.TestId}] {test.TestName}");
    }

    menuItems.Add("---");
    menuItems.Add("Return to Main Menu");

    var choice = ConsoleUI.ShowMenu("[bold]Select Test[/]", menuItems.ToArray());

    if (choice == "Return to Main Menu")
    {
      exit = true;
      continue;
    }

    if (choice.StartsWith("---"))
    {
      continue;
    }

    if (choice == "Run All Phase 10 Tests")
    {
      AnsiConsole.Clear();
      ConsoleUI.WriteHeader("Running All Phase 10 Tests");

      await AnsiConsole.Progress()
        .StartAsync(async ctx =>
        {
          var task = ctx.AddTask("Running tests...", maxValue: allTests.Count);

          foreach (var test in allTests)
          {
            task.Description = $"Running {test.TestId}...";
            await runner.RunTestAsync(test);
            task.Increment(1);
          }
        });

      var resultsManager = services.GetRequiredService<TestResultsManager>();
      ConsoleUI.DisplayTestResults(resultsManager.GetResultsForPhase(10));
      ConsoleUI.DisplaySummary(resultsManager.GetSummaryForPhase(10));
      ConsoleUI.PressAnyKeyToContinue();
    }
    else
    {
      var testIdMatch = System.Text.RegularExpressions.Regex.Match(choice, @"\[([^\]]+)\]");
      if (testIdMatch.Success)
      {
        var testId = testIdMatch.Groups[1].Value;
        var test = allTests.FirstOrDefault(t => t.TestId == testId);
        if (test != null)
        {
          AnsiConsole.Clear();
          var result = await runner.RunTestAsync(test);

          AnsiConsole.WriteLine();
          if (result.Passed)
          {
            ConsoleUI.WriteSuccess($"Test {test.TestId} PASSED");
          }
          else if (result.Skipped)
          {
            ConsoleUI.WriteWarning($"Test {test.TestId} SKIPPED: {result.Message}");
          }
          else
          {
            ConsoleUI.WriteError($"Test {test.TestId} FAILED: {result.Message}");
          }

          ConsoleUI.PressAnyKeyToContinue();
        }
      }
    }
  }
}
