using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Metrics.Data;
using Radio.Infrastructure.Audio.Fingerprinting.Data;
using Radio.Tools.AudioUAT.Utilities;
using Spectre.Console;

namespace Radio.Tools.AudioUAT.Phases.Phase10;

public class BackupRestoreTests
{
  private readonly IServiceProvider _serviceProvider;

  public BackupRestoreTests(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      new DatabasePathResolutionTest(_serviceProvider),
      new CreateFullBackupTest(_serviceProvider),
      new RestoreBackupTest(_serviceProvider),
      new BackupCleanupTest(_serviceProvider),
    ];
  }
}

public class DatabasePathResolutionTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;
  public string TestId => "P10-001";
  public string TestName => "Database Path Resolution";
  public string Description => "Verify unified database path configuration";
  public int Phase => 10;

  public DatabasePathResolutionTest(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    try
    {
      var pathResolver = _serviceProvider.GetRequiredService<DatabasePathResolver>();
      var table = new Table().AddColumn("Database").AddColumn("Path");
      table.AddRow("Configuration (includes metrics)", pathResolver.GetConfigurationDatabasePath());
      table.AddRow("Fingerprinting", pathResolver.GetFingerprintingDatabasePath());
      table.AddRow("Backup Directory", pathResolver.GetBackupPath());
      AnsiConsole.Write(table);
      ConsoleUI.WriteSuccess("All paths resolved correctly");
      return TestResult.Pass(TestId, "Paths resolved");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message, exception: ex);
    }
  }
}

public class CreateFullBackupTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;
  public string TestId => "P10-002";
  public string TestName => "Create Full Backup";
  public string Description => "Create unified backup of all databases";
  public int Phase => 10;

  public CreateFullBackupTest(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    try
    {
      // Initialize databases with test data
      var configManager = _serviceProvider.GetRequiredService<IConfigurationManager>();
      var store = await configManager.CreateStoreAsync("backup-test");
      await store.SetEntryAsync("test-key", "test-value");
      await store.SaveAsync();

      using (var scope = _serviceProvider.CreateScope())
      {
        await scope.ServiceProvider.GetRequiredService<MetricsDbContext>().InitializeAsync(ct);
        await scope.ServiceProvider.GetRequiredService<FingerprintDbContext>().InitializeAsync(ct);
      }

      // Create backup
      var backupService = _serviceProvider.GetRequiredService<IUnifiedDatabaseBackupService>();
      var backup = await backupService.CreateFullBackupAsync("UAT Test Backup", ct);

      var table = new Table().AddColumn("Property").AddColumn("Value");
      table.AddRow("Backup ID", backup.BackupId);
      table.AddRow("Size", $"{backup.SizeBytes / 1024.0:F2} KB");
      table.AddRow("Databases", backup.IncludedDatabases.Count.ToString());
      AnsiConsole.Write(table);

      ConsoleUI.WriteSuccess($"Backup created: {backup.BackupId}");
      return TestResult.Pass(TestId, $"Created {backup.SizeBytes / 1024.0:F2} KB backup");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message, exception: ex);
    }
  }
}

public class RestoreBackupTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;
  public string TestId => "P10-003";
  public string TestName => "Restore from Backup";
  public string Description => "Restore databases from backup";
  public int Phase => 10;

  public RestoreBackupTest(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    try
    {
      var backupService = _serviceProvider.GetRequiredService<IUnifiedDatabaseBackupService>();
      var backups = await backupService.ListBackupsAsync(ct);
      
      if (backups.Count == 0)
        return TestResult.Fail(TestId, "No backups to restore");

      var backup = backups[0];
      ConsoleUI.WriteInfo($"Restoring: {backup.BackupId}");
      await backupService.RestoreBackupAsync(backup.BackupId, overwrite: true, ct);

      // Verify
      var configManager = _serviceProvider.GetRequiredService<IConfigurationManager>();
      var store = await configManager.GetStoreAsync("backup-test");
      var entry = await store.GetEntryAsync("test-key");
      
      if (entry?.Value != "test-value")
        return TestResult.Fail(TestId, "Verification failed");

      ConsoleUI.WriteSuccess("Backup restored and verified");
      return TestResult.Pass(TestId, "Restored successfully");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message, exception: ex);
    }
  }
}

public class BackupCleanupTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;
  public string TestId => "P10-004";
  public string TestName => "Backup Cleanup";
  public string Description => "Test automatic backup cleanup";
  public int Phase => 10;

  public BackupCleanupTest(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    try
    {
      var backupService = _serviceProvider.GetRequiredService<IUnifiedDatabaseBackupService>();
      var before = (await backupService.ListBackupsAsync(ct)).Count;
      var deleted = await backupService.CleanupOldBackupsAsync(ct);
      var after = (await backupService.ListBackupsAsync(ct)).Count;
      
      ConsoleUI.WriteInfo($"Before: {before}, Deleted: {deleted}, After: {after}");
      ConsoleUI.WriteSuccess("Cleanup completed");
      return TestResult.Pass(TestId, $"Removed {deleted} old backup(s)");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Failed: {ex.Message}");
      return TestResult.Fail(TestId, ex.Message, exception: ex);
    }
  }
}
