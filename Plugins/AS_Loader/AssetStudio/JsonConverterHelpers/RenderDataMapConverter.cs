using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AssetStudio
{
    public static partial class JsonConverterHelper
    {
        public class RenderDataMapConverter : JsonConverter<Dictionary<KeyValuePair<Guid, long>, SpriteAtlasData>>
        {
            public override Dictionary<KeyValuePair<Guid, long>, SpriteAtlasData> ReadJson(
                JsonReader reader,
                Type objectType,
                Dictionary<KeyValuePair<Guid, long>, SpriteAtlasData> existingValue,
                bool hasExistingValue,
                JsonSerializer serializer)
            {
                var dataArray = serializer.Deserialize<List<Dictionary<string, object>>>(reader);
                var renderDataMap = new Dictionary<KeyValuePair<Guid, long>, SpriteAtlasData>();

                foreach (var item in dataArray)
                {
                    if (item.TryGetValue("Key", out var keyObj) &&
                        item.TryGetValue("Value", out var valueObj))
                    {
                        var keyJObject = JObject.FromObject(keyObj);
                        var firstJObject = keyJObject["first"] as JObject;
                        var second = (long)keyJObject["second"];

                        var data0 = (uint)firstJObject["data[0]"];
                        var data1 = (uint)firstJObject["data[1]"];
                        var data2 = (uint)firstJObject["data[2]"];
                        var data3 = (uint)firstJObject["data[3]"];

                        var guidBytes = new byte[16];
                        BitConverter.GetBytes(data0).CopyTo(guidBytes, 0);
                        BitConverter.GetBytes(data1).CopyTo(guidBytes, 4);
                        BitConverter.GetBytes(data2).CopyTo(guidBytes, 8);
                        BitConverter.GetBytes(data3).CopyTo(guidBytes, 12);
                        var guid = new Guid(guidBytes);

                        var spriteAtlasData = valueObj is JObject valueJObject
                            ? valueJObject.ToObject<SpriteAtlasData>(serializer)
                            : JsonConvert.DeserializeObject<SpriteAtlasData>(valueObj.ToString());

                        renderDataMap.Add(new KeyValuePair<Guid, long>(guid, second), spriteAtlasData);
                    }
                }

                return renderDataMap;
            }

            public override void WriteJson(
                JsonWriter writer,
                Dictionary<KeyValuePair<Guid, long>, SpriteAtlasData> value,
                JsonSerializer serializer)
            {
                var result = new List<object>();

                foreach (var kvp in value)
                {
                    var guidBytes = kvp.Key.Key.ToByteArray();
                    var guidObject = new
                    {
                        data0 = BitConverter.ToUInt32(guidBytes, 0),
                        data1 = BitConverter.ToUInt32(guidBytes, 4),
                        data2 = BitConverter.ToUInt32(guidBytes, 8),
                        data3 = BitConverter.ToUInt32(guidBytes, 12)
                    };

                    var keyObject = new
                    {
                        first = guidObject,
                        second = kvp.Key.Value
                    };

                    var item = new
                    {
                        Key = keyObject,
                        Value = kvp.Value
                    };

                    result.Add(item);
                }

                serializer.Serialize(writer, result);
            }
        }

        private class GUID
        {
            [JsonProperty("data[0]")]
            public uint data0 { get; set; }

            [JsonProperty("data[1]")]
            public uint data1 { get; set; }

            [JsonProperty("data[2]")]
            public uint data2 { get; set; }

            [JsonProperty("data[3]")]
            public uint data3 { get; set; }

            public Guid Convert()
            {
                var guidBytes = new byte[16];
                BitConverter.GetBytes(data0).CopyTo(guidBytes, 0);
                BitConverter.GetBytes(data1).CopyTo(guidBytes, 4);
                BitConverter.GetBytes(data2).CopyTo(guidBytes, 8);
                BitConverter.GetBytes(data3).CopyTo(guidBytes, 12);
                return new Guid(guidBytes);
            }
        }
    }
}