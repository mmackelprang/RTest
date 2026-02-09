#r "nuget: Newtonsoft.Json, 13.0.3"
#r "C:/Users/mark/.nuget/packages/sharpcaster/1.1.18/lib/netstandard2.0/Sharpcaster.dll"

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Runtime.Serialization;
using Sharpcaster.Models.Media;

var media = new Media
{
    ContentUrl = "http://192.168.86.55:8080/stream/audio/raw",
    ContentType = "audio/wav",
    StreamType = StreamType.Live
};

// Test 1: Default Newtonsoft settings
Console.WriteLine("=== Default Newtonsoft ===");
Console.WriteLine(JsonConvert.SerializeObject(media, Formatting.Indented));

// Test 2: With CamelCase resolver
Console.WriteLine("\n=== CamelCase resolver ===");
var camelSettings = new JsonSerializerSettings
{
    ContractResolver = new CamelCasePropertyNamesContractResolver()
};
Console.WriteLine(JsonConvert.SerializeObject(media, Formatting.Indented, camelSettings));

// Test 3: With DataContract support (default in Newtonsoft)
Console.WriteLine("\n=== DataContract default ===");
var dcSettings = new JsonSerializerSettings
{
    ContractResolver = new DefaultContractResolver()
};
Console.WriteLine(JsonConvert.SerializeObject(media, Formatting.Indented, dcSettings));

// Test 4: DataContractJsonSerializer (System.Runtime.Serialization)
Console.WriteLine("\n=== DataContractJsonSerializer ===");
var dcjs = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Media));
using var ms = new System.IO.MemoryStream();
dcjs.WriteObject(ms, media);
Console.WriteLine(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
