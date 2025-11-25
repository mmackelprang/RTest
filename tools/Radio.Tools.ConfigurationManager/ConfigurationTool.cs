using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;
using Spectre.Console;

using IRadioConfigurationManager = Radio.Infrastructure.Configuration.Abstractions.IConfigurationManager;

namespace Radio.Tools.ConfigurationManager;

/// <summary>
/// Interactive configuration management tool using Spectre.Console.
/// </summary>
public sealed class ConfigurationTool
{
  private readonly IServiceProvider _serviceProvider;
  private readonly IRadioConfigurationManager _configManager;
  private readonly ISecretsProvider _secretsProvider;
  private readonly IConfiguration _appConfiguration;
  private bool _rawViewMode;
  private string? _selectedStoreId;

  public ConfigurationTool(IServiceProvider serviceProvider, IConfiguration appConfiguration)
  {
    _serviceProvider = serviceProvider;
    _configManager = serviceProvider.GetRequiredService<IRadioConfigurationManager>();
    _secretsProvider = serviceProvider.GetRequiredService<ISecretsProvider>();
    _appConfiguration = appConfiguration;
  }

  public async Task RunAsync()
  {
    AnsiConsole.Clear();
    DisplayHeader();

    while (true)
    {
      try
      {
        var action = await ShowMainMenuAsync();
        if (action == MenuAction.Exit)
        {
          AnsiConsole.MarkupLine("[grey]Goodbye![/]");
          break;
        }

        await ExecuteActionAsync(action);
      }
      catch (Exception ex)
      {
        AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        WaitForKeypress();
      }
    }
  }

  private void DisplayHeader()
  {
    var rule = new Rule("[bold blue]Radio Configuration Manager[/]");
    rule.Justification = Justify.Center;
    AnsiConsole.Write(rule);
    AnsiConsole.WriteLine();
  }

  private async Task<MenuAction> ShowMainMenuAsync()
  {
    AnsiConsole.WriteLine();

    // Display status bar
    var storeType = _configManager.CurrentStoreType;
    var storeCount = (await _configManager.ListStoresAsync()).Count;
    var viewMode = _rawViewMode ? "Raw" : "Normal";

    var table = new Table().Border(TableBorder.Rounded).Expand();
    table.AddColumn(new TableColumn("[bold]Status[/]").Centered());
    table.AddRow($"[cyan]Store Type:[/] [yellow]{storeType}[/]  |  " +
                 $"[cyan]Stores:[/] [yellow]{storeCount}[/]  |  " +
                 $"[cyan]View Mode:[/] [yellow]{viewMode}[/]  |  " +
                 $"[cyan]Selected:[/] [yellow]{_selectedStoreId ?? "(none)"}[/]");
    AnsiConsole.Write(table);
    AnsiConsole.WriteLine();

    // Show menu options
    AnsiConsole.MarkupLine("[bold]Options:[/]");
    AnsiConsole.MarkupLine("  [yellow]L[/] - List configuration stores");
    AnsiConsole.MarkupLine("  [yellow]S[/] - Select a store");
    AnsiConsole.MarkupLine("  [yellow]V[/] - View entries in selected store");
    AnsiConsole.MarkupLine("  [yellow]T[/] - Toggle view mode (Raw/Normal)");
    AnsiConsole.MarkupLine("  [yellow]A[/] - Add new store");
    AnsiConsole.MarkupLine("  [yellow]D[/] - Delete store");
    AnsiConsole.MarkupLine("  [yellow]E[/] - Edit entry in selected store");
    AnsiConsole.MarkupLine("  [yellow]N[/] - Add new entry");
    AnsiConsole.MarkupLine("  [yellow]R[/] - Remove entry");
    AnsiConsole.MarkupLine("  [yellow]K[/] - View secret keys");
    AnsiConsole.MarkupLine("  [yellow]X[/] - Add/Update secret");
    AnsiConsole.MarkupLine("  [yellow]Z[/] - Delete secret");
    AnsiConsole.MarkupLine("  [yellow]Q[/] - Quit");
    AnsiConsole.WriteLine();

    AnsiConsole.Markup("[bold]Press a key: [/]");

    // Read single keypress (no Enter required)
    var key = Console.ReadKey(intercept: true);
    AnsiConsole.WriteLine();

    return key.Key switch
    {
      ConsoleKey.L => MenuAction.ListStores,
      ConsoleKey.S => MenuAction.SelectStore,
      ConsoleKey.V => MenuAction.ViewEntries,
      ConsoleKey.T => MenuAction.ToggleViewMode,
      ConsoleKey.A => MenuAction.AddStore,
      ConsoleKey.D => MenuAction.DeleteStore,
      ConsoleKey.E => MenuAction.EditEntry,
      ConsoleKey.N => MenuAction.AddEntry,
      ConsoleKey.R => MenuAction.RemoveEntry,
      ConsoleKey.K => MenuAction.ViewSecretKeys,
      ConsoleKey.X => MenuAction.AddUpdateSecret,
      ConsoleKey.Z => MenuAction.DeleteSecret,
      ConsoleKey.Q => MenuAction.Exit,
      ConsoleKey.Escape => MenuAction.Exit,
      _ => MenuAction.None
    };
  }

  private async Task ExecuteActionAsync(MenuAction action)
  {
    switch (action)
    {
      case MenuAction.ListStores:
        await ListStoresAsync();
        break;
      case MenuAction.SelectStore:
        await SelectStoreAsync();
        break;
      case MenuAction.ViewEntries:
        await ViewEntriesAsync();
        break;
      case MenuAction.ToggleViewMode:
        ToggleViewMode();
        break;
      case MenuAction.AddStore:
        await AddStoreAsync();
        break;
      case MenuAction.DeleteStore:
        await DeleteStoreAsync();
        break;
      case MenuAction.EditEntry:
        await EditEntryAsync();
        break;
      case MenuAction.AddEntry:
        await AddEntryAsync();
        break;
      case MenuAction.RemoveEntry:
        await RemoveEntryAsync();
        break;
      case MenuAction.ViewSecretKeys:
        await ViewSecretKeysAsync();
        break;
      case MenuAction.AddUpdateSecret:
        await AddUpdateSecretAsync();
        break;
      case MenuAction.DeleteSecret:
        await DeleteSecretAsync();
        break;
    }
  }

  private async Task ListStoresAsync()
  {
    var stores = await _configManager.ListStoresAsync();

    if (stores.Count == 0)
    {
      AnsiConsole.MarkupLine("[yellow]No configuration stores found.[/]");
      WaitForKeypress();
      return;
    }

    var table = new Table().Border(TableBorder.Rounded).Expand();
    table.AddColumn("[bold]Store ID[/]");
    table.AddColumn("[bold]Type[/]");
    table.AddColumn("[bold]Entries[/]");
    table.AddColumn("[bold]Size[/]");
    table.AddColumn("[bold]Last Modified[/]");

    foreach (var store in stores)
    {
      table.AddRow(
        store.StoreId,
        store.StoreType.ToString(),
        store.EntryCount.ToString(),
        FormatSize(store.SizeBytes),
        store.LastModifiedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
      );
    }

    AnsiConsole.Write(table);
    WaitForKeypress();
  }

  private async Task SelectStoreAsync()
  {
    var stores = await _configManager.ListStoresAsync();

    if (stores.Count == 0)
    {
      AnsiConsole.MarkupLine("[yellow]No configuration stores found. Create one first.[/]");
      WaitForKeypress();
      return;
    }

    var storeIds = stores.Select(s => s.StoreId).ToList();
    storeIds.Add("(Cancel)");

    var selected = AnsiConsole.Prompt(
      new SelectionPrompt<string>()
        .Title("[bold]Select a configuration store:[/]")
        .PageSize(10)
        .AddChoices(storeIds)
        .HighlightStyle(new Style(Color.Yellow))
    );

    if (selected != "(Cancel)")
    {
      _selectedStoreId = selected;
      AnsiConsole.MarkupLine($"[green]Selected store: {_selectedStoreId}[/]");
    }

    WaitForKeypress();
  }

  private async Task ViewEntriesAsync()
  {
    if (_selectedStoreId == null)
    {
      AnsiConsole.MarkupLine("[yellow]No store selected. Press 'S' to select one.[/]");
      WaitForKeypress();
      return;
    }

    try
    {
      var store = await _configManager.GetStoreAsync(_selectedStoreId);
      var mode = _rawViewMode ? ConfigurationReadMode.Raw : ConfigurationReadMode.Resolved;
      var entries = await store.GetAllEntriesAsync(mode);

      if (entries.Count == 0)
      {
        AnsiConsole.MarkupLine($"[yellow]No entries in store '{_selectedStoreId}'.[/]");
        WaitForKeypress();
        return;
      }

      var viewModeText = _rawViewMode ? "Raw" : "Normal";
      AnsiConsole.MarkupLine($"[bold]Entries in '{_selectedStoreId}' ({viewModeText} view):[/]");
      AnsiConsole.WriteLine();

      var table = new Table().Border(TableBorder.Rounded).Expand();
      table.AddColumn("[bold]Key[/]");
      table.AddColumn("[bold]Value[/]");
      table.AddColumn("[bold]Secret?[/]");
      table.AddColumn("[bold]Last Modified[/]");

      foreach (var entry in entries)
      {
        var value = entry.Value.Length > 50 ? entry.Value[..50] + "..." : entry.Value;
        var secretIndicator = entry.ContainsSecret ? "[red]Yes[/]" : "[grey]No[/]";
        var modified = entry.LastModified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "-";

        table.AddRow(
          entry.Key,
          Markup.Escape(value),
          secretIndicator,
          modified
        );
      }

      AnsiConsole.Write(table);
    }
    catch (Exception ex)
    {
      AnsiConsole.MarkupLine($"[red]Error viewing entries: {ex.Message}[/]");
    }

    WaitForKeypress();
  }

  private void ToggleViewMode()
  {
    _rawViewMode = !_rawViewMode;
    var mode = _rawViewMode ? "Raw" : "Normal";
    AnsiConsole.MarkupLine($"[green]View mode changed to: {mode}[/]");
    WaitForKeypress();
  }

  private async Task AddStoreAsync()
  {
    var storeId = AnsiConsole.Ask<string>("[bold]Enter new store ID (or empty to cancel):[/]");

    if (string.IsNullOrWhiteSpace(storeId))
    {
      AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
      WaitForKeypress();
      return;
    }

    // Validate store ID
    if (!IsValidStoreId(storeId))
    {
      AnsiConsole.MarkupLine("[red]Invalid store ID. Use only letters, numbers, hyphens, and underscores.[/]");
      WaitForKeypress();
      return;
    }

    try
    {
      await _configManager.CreateStoreAsync(storeId);
      _selectedStoreId = storeId;
      AnsiConsole.MarkupLine($"[green]Store '{storeId}' created and selected.[/]");
    }
    catch (Exception ex)
    {
      AnsiConsole.MarkupLine($"[red]Error creating store: {ex.Message}[/]");
    }

    WaitForKeypress();
  }

  private async Task DeleteStoreAsync()
  {
    var stores = await _configManager.ListStoresAsync();

    if (stores.Count == 0)
    {
      AnsiConsole.MarkupLine("[yellow]No configuration stores to delete.[/]");
      WaitForKeypress();
      return;
    }

    var storeIds = stores.Select(s => s.StoreId).ToList();
    storeIds.Add("(Cancel)");

    var selected = AnsiConsole.Prompt(
      new SelectionPrompt<string>()
        .Title("[bold]Select store to delete:[/]")
        .PageSize(10)
        .AddChoices(storeIds)
        .HighlightStyle(new Style(Color.Red))
    );

    if (selected == "(Cancel)")
    {
      AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
      WaitForKeypress();
      return;
    }

    var confirm = AnsiConsole.Confirm($"[red]Are you sure you want to delete '{selected}'?[/]", false);

    if (!confirm)
    {
      AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
      WaitForKeypress();
      return;
    }

    try
    {
      var deleted = await _configManager.DeleteStoreAsync(selected);
      if (deleted)
      {
        AnsiConsole.MarkupLine($"[green]Store '{selected}' deleted.[/]");
        if (_selectedStoreId == selected)
        {
          _selectedStoreId = null;
        }
      }
      else
      {
        AnsiConsole.MarkupLine($"[yellow]Store '{selected}' not found or could not be deleted.[/]");
      }
    }
    catch (Exception ex)
    {
      AnsiConsole.MarkupLine($"[red]Error deleting store: {ex.Message}[/]");
    }

    WaitForKeypress();
  }

  private async Task EditEntryAsync()
  {
    if (_selectedStoreId == null)
    {
      AnsiConsole.MarkupLine("[yellow]No store selected. Press 'S' to select one.[/]");
      WaitForKeypress();
      return;
    }

    try
    {
      var store = await _configManager.GetStoreAsync(_selectedStoreId);
      var entries = await store.GetAllEntriesAsync(ConfigurationReadMode.Raw);

      // Filter out entries with secrets (can't edit secrets directly)
      var editableEntries = entries.Where(e => !e.ContainsSecret).ToList();

      if (editableEntries.Count == 0)
      {
        AnsiConsole.MarkupLine("[yellow]No non-secret entries to edit. Use 'X' to manage secrets.[/]");
        WaitForKeypress();
        return;
      }

      var keys = editableEntries.Select(e => e.Key).ToList();
      keys.Add("(Cancel)");

      var selectedKey = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
          .Title("[bold]Select entry to edit:[/]")
          .PageSize(10)
          .AddChoices(keys)
          .HighlightStyle(new Style(Color.Yellow))
      );

      if (selectedKey == "(Cancel)")
      {
        AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
        WaitForKeypress();
        return;
      }

      var existingEntry = editableEntries.First(e => e.Key == selectedKey);
      AnsiConsole.MarkupLine($"[grey]Current value: {Markup.Escape(existingEntry.Value)}[/]");

      var newValue = AnsiConsole.Ask<string>("[bold]Enter new value (or empty to cancel):[/]");

      if (string.IsNullOrEmpty(newValue))
      {
        AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
        WaitForKeypress();
        return;
      }

      await _configManager.SetValueAsync(_selectedStoreId, selectedKey, newValue);
      AnsiConsole.MarkupLine($"[green]Entry '{selectedKey}' updated.[/]");
    }
    catch (Exception ex)
    {
      AnsiConsole.MarkupLine($"[red]Error editing entry: {ex.Message}[/]");
    }

    WaitForKeypress();
  }

  private async Task AddEntryAsync()
  {
    if (_selectedStoreId == null)
    {
      AnsiConsole.MarkupLine("[yellow]No store selected. Press 'S' to select one.[/]");
      WaitForKeypress();
      return;
    }

    var key = AnsiConsole.Ask<string>("[bold]Enter key (or empty to cancel):[/]");

    if (string.IsNullOrWhiteSpace(key))
    {
      AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
      WaitForKeypress();
      return;
    }

    var value = AnsiConsole.Ask<string>("[bold]Enter value:[/]");

    try
    {
      await _configManager.SetValueAsync(_selectedStoreId, key, value);
      AnsiConsole.MarkupLine($"[green]Entry '{key}' added.[/]");
    }
    catch (Exception ex)
    {
      AnsiConsole.MarkupLine($"[red]Error adding entry: {ex.Message}[/]");
    }

    WaitForKeypress();
  }

  private async Task RemoveEntryAsync()
  {
    if (_selectedStoreId == null)
    {
      AnsiConsole.MarkupLine("[yellow]No store selected. Press 'S' to select one.[/]");
      WaitForKeypress();
      return;
    }

    try
    {
      var store = await _configManager.GetStoreAsync(_selectedStoreId);
      var entries = await store.GetAllEntriesAsync(ConfigurationReadMode.Raw);

      // Filter out entries with secrets
      var nonSecretEntries = entries.Where(e => !e.ContainsSecret).ToList();

      if (nonSecretEntries.Count == 0)
      {
        AnsiConsole.MarkupLine("[yellow]No non-secret entries to remove. Use 'Z' to delete secrets.[/]");
        WaitForKeypress();
        return;
      }

      var keys = nonSecretEntries.Select(e => e.Key).ToList();
      keys.Add("(Cancel)");

      var selectedKey = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
          .Title("[bold]Select entry to remove:[/]")
          .PageSize(10)
          .AddChoices(keys)
          .HighlightStyle(new Style(Color.Red))
      );

      if (selectedKey == "(Cancel)")
      {
        AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
        WaitForKeypress();
        return;
      }

      var confirm = AnsiConsole.Confirm($"[red]Delete entry '{selectedKey}'?[/]", false);

      if (!confirm)
      {
        AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
        WaitForKeypress();
        return;
      }

      var deleted = await _configManager.DeleteValueAsync(_selectedStoreId, selectedKey);
      if (deleted)
      {
        AnsiConsole.MarkupLine($"[green]Entry '{selectedKey}' removed.[/]");
      }
      else
      {
        AnsiConsole.MarkupLine($"[yellow]Entry '{selectedKey}' not found.[/]");
      }
    }
    catch (Exception ex)
    {
      AnsiConsole.MarkupLine($"[red]Error removing entry: {ex.Message}[/]");
    }

    WaitForKeypress();
  }

  private async Task ViewSecretKeysAsync()
  {
    try
    {
      var tags = await _secretsProvider.ListTagsAsync();

      if (tags.Count == 0)
      {
        AnsiConsole.MarkupLine("[yellow]No secrets found.[/]");
        WaitForKeypress();
        return;
      }

      AnsiConsole.MarkupLine("[bold]Secret Keys (values are hidden):[/]");
      AnsiConsole.WriteLine();

      var table = new Table().Border(TableBorder.Rounded).Expand();
      table.AddColumn("[bold]Tag Identifier[/]");
      table.AddColumn("[bold]Full Tag Reference[/]");
      table.AddColumn("[bold]Value[/]");

      foreach (var tag in tags)
      {
        var fullTag = $"${{secret:{tag}}}";
        table.AddRow(tag, fullTag, "[grey]********[/]");
      }

      AnsiConsole.Write(table);
    }
    catch (Exception ex)
    {
      AnsiConsole.MarkupLine($"[red]Error listing secret keys: {ex.Message}[/]");
    }

    WaitForKeypress();
  }

  private async Task AddUpdateSecretAsync()
  {
    if (_selectedStoreId == null)
    {
      AnsiConsole.MarkupLine("[yellow]No store selected. Press 'S' to select one first.[/]");
      WaitForKeypress();
      return;
    }

    var choice = AnsiConsole.Prompt(
      new SelectionPrompt<string>()
        .Title("[bold]What would you like to do?[/]")
        .AddChoices(new[] { "Add new secret entry", "Update existing secret", "(Cancel)" })
        .HighlightStyle(new Style(Color.Yellow))
    );

    if (choice == "(Cancel)")
    {
      AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
      WaitForKeypress();
      return;
    }

    try
    {
      if (choice == "Add new secret entry")
      {
        var key = AnsiConsole.Ask<string>("[bold]Enter key for the secret entry (or empty to cancel):[/]");

        if (string.IsNullOrWhiteSpace(key))
        {
          AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
          WaitForKeypress();
          return;
        }

        var secretValue = AnsiConsole.Prompt(
          new TextPrompt<string>("[bold]Enter secret value:[/]")
            .Secret()
        );

        var tagRef = await _configManager.CreateSecretAsync(_selectedStoreId, key, secretValue);
        AnsiConsole.MarkupLine($"[green]Secret created with tag: {tagRef}[/]");
      }
      else // Update existing secret
      {
        var tags = await _secretsProvider.ListTagsAsync();

        if (tags.Count == 0)
        {
          AnsiConsole.MarkupLine("[yellow]No secrets to update.[/]");
          WaitForKeypress();
          return;
        }

        var tagList = tags.ToList();
        tagList.Add("(Cancel)");

        var selectedTag = AnsiConsole.Prompt(
          new SelectionPrompt<string>()
            .Title("[bold]Select secret to update:[/]")
            .PageSize(10)
            .AddChoices(tagList)
            .HighlightStyle(new Style(Color.Yellow))
        );

        if (selectedTag == "(Cancel)")
        {
          AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
          WaitForKeypress();
          return;
        }

        var newValue = AnsiConsole.Prompt(
          new TextPrompt<string>("[bold]Enter new secret value:[/]")
            .Secret()
        );

        var updated = await _configManager.UpdateSecretAsync(selectedTag, newValue);
        if (updated)
        {
          AnsiConsole.MarkupLine($"[green]Secret '{selectedTag}' updated.[/]");
        }
        else
        {
          AnsiConsole.MarkupLine($"[yellow]Secret '{selectedTag}' not found.[/]");
        }
      }
    }
    catch (Exception ex)
    {
      AnsiConsole.MarkupLine($"[red]Error managing secret: {ex.Message}[/]");
    }

    WaitForKeypress();
  }

  private async Task DeleteSecretAsync()
  {
    try
    {
      var tags = await _secretsProvider.ListTagsAsync();

      if (tags.Count == 0)
      {
        AnsiConsole.MarkupLine("[yellow]No secrets to delete.[/]");
        WaitForKeypress();
        return;
      }

      var tagList = tags.ToList();
      tagList.Add("(Cancel)");

      var selectedTag = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
          .Title("[bold]Select secret to delete:[/]")
          .PageSize(10)
          .AddChoices(tagList)
          .HighlightStyle(new Style(Color.Red))
      );

      if (selectedTag == "(Cancel)")
      {
        AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
        WaitForKeypress();
        return;
      }

      AnsiConsole.MarkupLine("[red]Warning: Deleting a secret will break any configuration entries that reference it![/]");
      var confirm = AnsiConsole.Confirm($"[red]Are you sure you want to delete secret '{selectedTag}'?[/]", false);

      if (!confirm)
      {
        AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
        WaitForKeypress();
        return;
      }

      var deleted = await _secretsProvider.DeleteSecretAsync(selectedTag);
      if (deleted)
      {
        AnsiConsole.MarkupLine($"[green]Secret '{selectedTag}' deleted.[/]");
      }
      else
      {
        AnsiConsole.MarkupLine($"[yellow]Secret '{selectedTag}' not found.[/]");
      }
    }
    catch (Exception ex)
    {
      AnsiConsole.MarkupLine($"[red]Error deleting secret: {ex.Message}[/]");
    }

    WaitForKeypress();
  }

  private static void WaitForKeypress()
  {
    AnsiConsole.WriteLine();
    AnsiConsole.Markup("[grey]Press any key to continue...[/]");
    Console.ReadKey(intercept: true);
    AnsiConsole.Clear();
  }

  private static string FormatSize(long bytes)
  {
    return bytes switch
    {
      < 1024 => $"{bytes} B",
      < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
      _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
  }

  private static bool IsValidStoreId(string storeId)
  {
    return !string.IsNullOrWhiteSpace(storeId) &&
           storeId.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
  }
}
