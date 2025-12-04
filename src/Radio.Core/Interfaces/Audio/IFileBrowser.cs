using Radio.Core.Models.Audio;

namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Service for browsing and listing audio files in the file system.
/// Used by FilePlayerAudioSource to discover and manage audio files.
/// </summary>
public interface IFileBrowser
{
  /// <summary>
  /// Lists audio files in the specified directory.
  /// </summary>
  /// <param name="path">
  /// The path relative to the configured root directory. 
  /// Empty or null returns files from the root directory.
  /// </param>
  /// <param name="recursive">
  /// If true, searches subdirectories recursively. Default is false.
  /// </param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of audio file information.</returns>
  Task<IReadOnlyList<AudioFileInfo>> ListFilesAsync(
    string? path = null, 
    bool recursive = false, 
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets information about a specific audio file.
  /// </summary>
  /// <param name="path">The path to the audio file relative to the root directory.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Audio file information, or null if file not found or not a supported audio format.</returns>
  Task<AudioFileInfo?> GetFileInfoAsync(
    string path, 
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Checks if a file is a supported audio format.
  /// </summary>
  /// <param name="filePath">The file path to check.</param>
  /// <returns>True if the file is a supported audio format, false otherwise.</returns>
  bool IsSupportedAudioFile(string filePath);

  /// <summary>
  /// Gets a list of supported audio file extensions.
  /// </summary>
  /// <returns>Array of supported extensions (e.g., ".mp3", ".flac", ".wav").</returns>
  string[] GetSupportedExtensions();
}
