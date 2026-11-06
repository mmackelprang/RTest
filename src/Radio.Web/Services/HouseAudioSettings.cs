using System.Globalization;

namespace Radio.Web.Services;

/// <summary>
/// The <c>HouseAudio</c> configuration section. An unusable value falls back to the default rather
/// than throwing, because these are read where a panel creates its poller, inside <c>/system</c>.
/// </summary>
internal static class HouseAudioSettings
{
  private const string DefaultBaseUrl = "http://homeaudio.local:3001";
  private const int DefaultPollSeconds = 3;

  internal static Uri BaseUrl(IConfiguration configuration) =>
    Uri.TryCreate(configuration["HouseAudio:BaseUrl"], UriKind.Absolute, out Uri? baseUrl)
      ? baseUrl
      : new Uri(DefaultBaseUrl);

  internal static TimeSpan PollInterval(IConfiguration configuration)
  {
    bool usable = int.TryParse(
      configuration["HouseAudio:PollSeconds"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
      && seconds > 0;
    return TimeSpan.FromSeconds(usable ? seconds : DefaultPollSeconds);
  }
}
