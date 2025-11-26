using Spectre.Console;
using Radio.Tools.AudioUAT.Results;

namespace Radio.Tools.AudioUAT.Utilities;

/// <summary>
/// Utility class for Spectre.Console UI elements.
/// </summary>
public static class ConsoleUI
{
  /// <summary>
  /// Writes the welcome banner.
  /// </summary>
  public static void WriteWelcomeBanner()
  {
    AnsiConsole.Clear();
    var rule = new Rule("[blue]Radio Audio UAT Tool[/]")
    {
      Justification = Justify.Center,
      Style = Style.Parse("blue")
    };
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();

    var panel = new Panel(
      "[bold]Use this tool to validate audio functionality.[/]\n" +
      "For help, press [blue]?[/] at any menu or run with [yellow]--help[/]")
    {
      Border = BoxBorder.Rounded,
      Padding = new Padding(2, 1)
    };
    AnsiConsole.Write(panel);
    AnsiConsole.WriteLine();
  }

  /// <summary>
  /// Displays a menu and returns the selected option.
  /// </summary>
  /// <param name="title">The menu title.</param>
  /// <param name="options">The available options.</param>
  /// <returns>The selected option.</returns>
  public static string ShowMenu(string title, params string[] options)
  {
    var selection = AnsiConsole.Prompt(
      new SelectionPrompt<string>()
        .Title(title)
        .PageSize(15)
        .HighlightStyle(Style.Parse("cyan"))
        .AddChoices(options));

    return selection;
  }

  /// <summary>
  /// Displays test results in a table.
  /// </summary>
  /// <param name="results">The test results to display.</param>
  public static void DisplayTestResults(IReadOnlyList<TestResult> results)
  {
    var table = new Table()
      .Border(TableBorder.Rounded)
      .AddColumn(new TableColumn("[bold]Test ID[/]").Centered())
      .AddColumn(new TableColumn("[bold]Status[/]").Centered())
      .AddColumn(new TableColumn("[bold]Duration[/]").Centered())
      .AddColumn(new TableColumn("[bold]Message[/]"));

    foreach (var result in results)
    {
      var status = result.Skipped
        ? "[yellow]SKIPPED[/]"
        : result.Passed
          ? "[green]PASSED[/]"
          : "[red]FAILED[/]";

      var message = result.Message ?? "";
      if (message.Length > 50)
      {
        message = message[..47] + "...";
      }

      table.AddRow(
        result.TestId,
        status,
        $"{result.Duration.TotalMilliseconds:F0}ms",
        message);
    }

    AnsiConsole.Write(table);
  }

  /// <summary>
  /// Displays a summary of test results.
  /// </summary>
  /// <param name="summary">The summary to display.</param>
  public static void DisplaySummary(TestSummary summary)
  {
    var grid = new Grid()
      .AddColumn()
      .AddColumn();

    grid.AddRow("[bold]Total Tests:[/]", summary.TotalTests.ToString());
    grid.AddRow("[bold green]Passed:[/]", summary.PassedTests.ToString());
    grid.AddRow("[bold red]Failed:[/]", summary.FailedTests.ToString());
    grid.AddRow("[bold yellow]Skipped:[/]", summary.SkippedTests.ToString());
    grid.AddRow("[bold]Pass Rate:[/]", $"{summary.PassRate:F1}%");
    grid.AddRow("[bold]Total Duration:[/]", $"{summary.TotalDuration.TotalSeconds:F2}s");

    var panel = new Panel(grid)
    {
      Header = new PanelHeader("[blue]Test Summary[/]"),
      Border = BoxBorder.Rounded
    };

    AnsiConsole.Write(panel);
  }

  /// <summary>
  /// Displays a progress bar for long-running operations.
  /// </summary>
  /// <param name="description">The operation description.</param>
  /// <param name="action">The action to execute with progress reporting.</param>
  public static async Task WithProgressAsync(string description, Func<ProgressTask, Task> action)
  {
    await AnsiConsole.Progress()
      .AutoClear(false)
      .Columns(
        new TaskDescriptionColumn(),
        new ProgressBarColumn(),
        new PercentageColumn(),
        new RemainingTimeColumn())
      .StartAsync(async ctx =>
      {
        var task = ctx.AddTask(description);
        await action(task);
      });
  }

  /// <summary>
  /// Displays a status spinner during an operation.
  /// </summary>
  /// <param name="statusText">The status text.</param>
  /// <param name="action">The async action to execute.</param>
  public static async Task WithStatusAsync(string statusText, Func<StatusContext, Task> action)
  {
    await AnsiConsole.Status()
      .Spinner(Spinner.Known.Dots)
      .SpinnerStyle(Style.Parse("blue"))
      .StartAsync(statusText, action);
  }

  /// <summary>
  /// Prompts the user for a yes/no confirmation.
  /// </summary>
  /// <param name="question">The question to ask.</param>
  /// <returns>True if the user answered yes.</returns>
  public static bool Confirm(string question)
  {
    return AnsiConsole.Confirm(question);
  }

  /// <summary>
  /// Prompts the user to press any key to continue.
  /// </summary>
  public static void PressAnyKeyToContinue()
  {
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
    Console.ReadKey(true);
  }

  /// <summary>
  /// Displays a success message.
  /// </summary>
  /// <param name="message">The message to display.</param>
  public static void WriteSuccess(string message)
  {
    AnsiConsole.MarkupLine($"[green]✓[/] {message}");
  }

  /// <summary>
  /// Displays an error message.
  /// </summary>
  /// <param name="message">The message to display.</param>
  public static void WriteError(string message)
  {
    AnsiConsole.MarkupLine($"[red]✗[/] {message}");
  }

  /// <summary>
  /// Displays a warning message.
  /// </summary>
  /// <param name="message">The message to display.</param>
  public static void WriteWarning(string message)
  {
    AnsiConsole.MarkupLine($"[yellow]![/] {message}");
  }

  /// <summary>
  /// Displays an info message.
  /// </summary>
  /// <param name="message">The message to display.</param>
  public static void WriteInfo(string message)
  {
    AnsiConsole.MarkupLine($"[blue]ℹ[/] {message}");
  }

  /// <summary>
  /// Displays a header rule.
  /// </summary>
  /// <param name="title">The header title.</param>
  public static void WriteHeader(string title)
  {
    AnsiConsole.WriteLine();
    var rule = new Rule($"[cyan]{title}[/]")
    {
      Justification = Justify.Left,
      Style = Style.Parse("grey")
    };
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();
  }

  /// <summary>
  /// Creates a volume slider for interactive volume testing.
  /// </summary>
  /// <param name="initialValue">The initial volume value (0-100).</param>
  /// <param name="onValueChanged">Callback when value changes.</param>
  /// <returns>The final selected volume.</returns>
  public static int VolumeSlider(int initialValue, Action<int> onValueChanged)
  {
    AnsiConsole.MarkupLine("[grey]Use UP/DOWN arrows to adjust, ENTER to confirm[/]");
    AnsiConsole.WriteLine();

    var value = initialValue;
    ConsoleKeyInfo key;

    do
    {
      // Clear line and redraw
      AnsiConsole.Cursor.MoveUp(1);
      AnsiConsole.Write(new Rule());
      AnsiConsole.Cursor.MoveUp(1);

      var barLength = value / 5;
      var bar = new string('█', barLength) + new string('░', 20 - barLength);
      AnsiConsole.MarkupLine($"Volume: [cyan]{bar}[/] [bold]{value}%[/]");

      key = Console.ReadKey(true);

      var previousValue = value;
      value = key.Key switch
      {
        ConsoleKey.UpArrow => Math.Min(100, value + 5),
        ConsoleKey.DownArrow => Math.Max(0, value - 5),
        ConsoleKey.PageUp => Math.Min(100, value + 10),
        ConsoleKey.PageDown => Math.Max(0, value - 10),
        ConsoleKey.Home => 100,
        ConsoleKey.End => 0,
        _ => value
      };

      if (value != previousValue)
      {
        onValueChanged(value);
      }
    } while (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Escape);

    return value;
  }

  // Balance slider constants
  private const int BalanceSliderWidth = 20;
  private const int BalanceValueRange = 200; // -100 to 100
  private const int BalanceStep = BalanceValueRange / BalanceSliderWidth;

  /// <summary>
  /// Creates a balance slider for interactive balance testing.
  /// </summary>
  /// <param name="initialValue">The initial balance value (-100 to 100).</param>
  /// <param name="onValueChanged">Callback when value changes.</param>
  /// <returns>The final selected balance.</returns>
  public static int BalanceSlider(int initialValue, Action<int> onValueChanged)
  {
    AnsiConsole.MarkupLine("[grey]Use LEFT/RIGHT arrows to adjust, ENTER to confirm[/]");
    AnsiConsole.WriteLine();

    var value = initialValue;
    ConsoleKeyInfo key;

    do
    {
      AnsiConsole.Cursor.MoveUp(1);
      AnsiConsole.Write(new Rule());
      AnsiConsole.Cursor.MoveUp(1);

      var position = (value + 100) / BalanceStep;
      var bar = new string(' ', position) + "●" + new string(' ', BalanceSliderWidth - 1 - position);
      var label = value == 0 ? "CENTER" : value < 0 ? $"LEFT {-value}%" : $"RIGHT {value}%";
      AnsiConsole.MarkupLine($"Balance: [cyan]L[{bar}]R[/] [bold]{label}[/]");

      key = Console.ReadKey(true);

      var previousValue = value;
      value = key.Key switch
      {
        ConsoleKey.RightArrow => Math.Min(100, value + 10),
        ConsoleKey.LeftArrow => Math.Max(-100, value - 10),
        ConsoleKey.Home => -100,
        ConsoleKey.End => 100,
        ConsoleKey.D0 or ConsoleKey.NumPad0 => 0,
        _ => value
      };

      if (value != previousValue)
      {
        onValueChanged(value);
      }
    } while (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Escape);

    return value;
  }

  /// <summary>
  /// Displays device list in a formatted table.
  /// </summary>
  /// <param name="devices">The devices to display.</param>
  public static void DisplayDeviceList(IEnumerable<(string Id, string Name, string Type, bool IsDefault, bool IsUSB)> devices)
  {
    var table = new Table()
      .Border(TableBorder.Rounded)
      .AddColumn(new TableColumn("[bold]ID[/]"))
      .AddColumn(new TableColumn("[bold]Name[/]"))
      .AddColumn(new TableColumn("[bold]Type[/]").Centered())
      .AddColumn(new TableColumn("[bold]Default[/]").Centered())
      .AddColumn(new TableColumn("[bold]USB[/]").Centered());

    foreach (var device in devices)
    {
      table.AddRow(
        device.Id,
        device.Name,
        device.Type,
        device.IsDefault ? "[green]✓[/]" : "",
        device.IsUSB ? "[cyan]✓[/]" : "");
    }

    AnsiConsole.Write(table);
  }
}
