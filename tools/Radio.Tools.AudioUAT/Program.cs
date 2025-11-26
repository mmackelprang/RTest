using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Tools.AudioUAT;
using Radio.Tools.AudioUAT.Phases.Phase2;
using Radio.Tools.AudioUAT.TestResults;
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

    // Register UAT services
    services.AddSingleton<TestResultsManager>();
    services.AddSingleton<TestRunner>();
    services.AddSingleton<CoreAudioEngineTests>();
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
  }
  else if (args.Contains("--test"))
  {
    var testId = GetArgValue(args, "--test");
    if (!string.IsNullOrEmpty(testId))
    {
      var test = phase2Tests.GetAllTests().FirstOrDefault(t => t.TestId == testId);
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
