using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Sharpcaster.Models.Media;

var media = new Media
{
  ContentUrl = "http://192.168.86.55:8080/stream/audio/raw",
  ContentType = "audio/wav",
  StreamType = StreamType.Live
};

Console.WriteLine("=== Default Newtonsoft (respects [DataMember]) ===");
Console.WriteLine(JsonConvert.SerializeObject(media, Formatting.Indented));

Console.WriteLine("\n=== CamelCase resolver ===");
var camelSettings = new JsonSerializerSettings
{
  ContractResolver = new CamelCasePropertyNamesContractResolver()
};
Console.WriteLine(JsonConvert.SerializeObject(media, Formatting.Indented, camelSettings));

Console.WriteLine("\n=== CamelCase + StringEnumConverter ===");
var camelEnumSettings = new JsonSerializerSettings
{
  ContractResolver = new CamelCasePropertyNamesContractResolver(),
  Converters = { new StringEnumConverter() }
};
Console.WriteLine(JsonConvert.SerializeObject(media, Formatting.Indented, camelEnumSettings));

Console.WriteLine("\n=== StringEnumConverter (default, PascalCase) ===");
var defaultEnumSettings = new JsonSerializerSettings
{
  Converters = { new StringEnumConverter() }
};
Console.WriteLine(JsonConvert.SerializeObject(media, Formatting.Indented, defaultEnumSettings));

Console.WriteLine("\n=== StringEnumConverter (CamelCase) ===");
var camelEnumOnly = new JsonSerializerSettings
{
  Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) }
};
Console.WriteLine(JsonConvert.SerializeObject(media, Formatting.Indented, camelEnumOnly));

// Check what DataContractJsonSerializer produces
Console.WriteLine("\n=== DataContractJsonSerializer ===");
var dcjs = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Media));
using var ms = new System.IO.MemoryStream();
dcjs.WriteObject(ms, media);
Console.WriteLine(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
