namespace Radio.Configuration.Bridge;

using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Custom <see cref="ConfigurationProvider"/> backed by the SQLite configuration
/// store. Reads all key/value entries from the Config_{storeId} table and
/// populates them into .NET's configuration pipeline so that
/// <c>IOptions&lt;T&gt;</c> / <c>IOptionsMonitor&lt;T&gt;</c> bindings
/// automatically reflect runtime config changes.
///
/// JSON object/array values are flattened into hierarchical keys using ':'
/// as the separator (matching .NET configuration conventions).
/// </summary>
public sealed class SqliteConfigurationProvider : ConfigurationProvider
{
  private readonly string _connectionString;
  private readonly string _tableName;

  public SqliteConfigurationProvider(string connectionString, string tableName)
  {
    _connectionString = connectionString;
    _tableName = tableName;
  }

  /// <summary>
  /// Reads all entries from the SQLite table and populates the Data dictionary.
  /// JSON values are flattened into hierarchical keys.
  /// Handles missing database or table gracefully (leaves Data empty).
  /// </summary>
  public override void Load()
  {
    var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    try
    {
      // Extract DB path from connection string to check existence
      var builder = new SqliteConnectionStringBuilder(_connectionString);
      if (!File.Exists(builder.DataSource))
        return;

      using var connection = new SqliteConnection(_connectionString);
      connection.Open();

      // Check if the table exists before querying
      if (!TableExists(connection))
        return;

      using var cmd = connection.CreateCommand();
      cmd.CommandText = $"SELECT Key, Value FROM {_tableName}";

      using var reader = cmd.ExecuteReader();
      while (reader.Read())
      {
        var key = reader.GetString(0);
        var value = reader.GetString(1);

        // Convert config store key separators (colon-delimited) to
        // .NET configuration key format (also colon-delimited — same convention)
        var configKey = key;

        // If the value looks like a JSON object or array, flatten it
        if (IsJsonObjectOrArray(value))
        {
          FlattenJson(configKey, value, data);
        }
        else
        {
          data[configKey] = value;
        }
      }
    }
    catch (SqliteException)
    {
      // Database might be locked, corrupt, or inaccessible.
      // Fall through with empty data — appsettings defaults will apply.
    }
    finally
    {
      Data = data;
    }
  }

  /// <summary>
  /// Re-reads from SQLite and triggers IOptionsMonitor change tokens.
  /// </summary>
  public void Reload()
  {
    Load();
    OnReload();
  }

  private bool TableExists(SqliteConnection connection)
  {
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@Name LIMIT 1";
    cmd.Parameters.AddWithValue("@Name", _tableName);
    return cmd.ExecuteScalar() != null;
  }

  private static bool IsJsonObjectOrArray(string value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return false;

    var trimmed = value.TrimStart();
    return trimmed.StartsWith('{') || trimmed.StartsWith('[');
  }

  /// <summary>
  /// Flattens a JSON object/array value into hierarchical configuration keys.
  /// E.g., key="devices:Radio", value={"USBPort":"AB13X"}
  ///   → data["devices:Radio:USBPort"] = "AB13X"
  /// </summary>
  private static void FlattenJson(string prefix, string json, Dictionary<string, string?> data)
  {
    try
    {
      using var doc = JsonDocument.Parse(json);
      FlattenElement(prefix, doc.RootElement, data);
    }
    catch (JsonException)
    {
      // Not valid JSON despite starting with { or [. Store as-is.
      data[prefix] = json;
    }
  }

  private static void FlattenElement(string prefix, JsonElement element, Dictionary<string, string?> data)
  {
    switch (element.ValueKind)
    {
      case JsonValueKind.Object:
        foreach (var property in element.EnumerateObject())
        {
          var childKey = string.IsNullOrEmpty(prefix)
            ? property.Name
            : $"{prefix}:{property.Name}";
          FlattenElement(childKey, property.Value, data);
        }
        break;

      case JsonValueKind.Array:
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
          var childKey = $"{prefix}:{index}";
          FlattenElement(childKey, item, data);
          index++;
        }
        break;

      case JsonValueKind.Null:
      case JsonValueKind.Undefined:
        data[prefix] = null;
        break;

      default:
        // String, Number, True, False — store raw text
        data[prefix] = element.ToString();
        break;
    }
  }
}
