using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AssetStudio
{
    public static partial class JsonConverterHelper
    {
        public class ByteArrayConverter : JsonConverter<byte[]>
        {
            public override byte[] ReadJson(JsonReader reader, Type objectType, byte[] existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.StartArray)
                {
                    var byteList = new List<byte>();
                    while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                    {
                        if (reader.TokenType == JsonToken.Integer)
                        {
                            byteList.Add(Convert.ToByte(reader.Value));
                        }
                        else if (reader.TokenType == JsonToken.String)
                        {
                            byteList.Add(Convert.ToByte(reader.Value));
                        }
                    }
                    return byteList.ToArray();
                }
                else if (reader.TokenType == JsonToken.String)
                {
                    try
                    {
                        return Convert.FromBase64String((string)reader.Value);
                    }
                    catch (FormatException)
                    {
                        return serializer.Deserialize<byte[]>(reader);
                    }
                }
                else
                {
                    return serializer.Deserialize<byte[]>(reader);
                }
            }

            public override void WriteJson(JsonWriter writer, byte[] value, JsonSerializer serializer)
            {
                writer.WriteValue(Convert.ToBase64String(value));
            }

            public override bool CanRead => true;
            public override bool CanWrite => true;
        }
    }
}
