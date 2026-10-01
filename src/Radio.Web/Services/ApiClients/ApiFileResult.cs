namespace Radio.Web.Services.ApiClients;

/// <summary>
/// Outcome of fetching a file-shaped diagnostic from radio-api for the DevTray (UI-26). On success
/// <see cref="Content"/> holds the body and <see cref="FileName"/> a name to save it under; on failure
/// <see cref="Error"/> holds a short, display-ready reason and <see cref="StatusCode"/> the HTTP status,
/// or null when no response arrived at all (API unreachable or the call timed out).
/// </summary>
public sealed record ApiFileResult(bool Success, int? StatusCode, string? FileName, byte[]? Content, string? Error)
{
  public static ApiFileResult Ok(int statusCode, string fileName, byte[] content) =>
    new(true, statusCode, fileName, content, null);

  public static ApiFileResult Failed(int? statusCode, string error) =>
    new(false, statusCode, null, null, error);
}
