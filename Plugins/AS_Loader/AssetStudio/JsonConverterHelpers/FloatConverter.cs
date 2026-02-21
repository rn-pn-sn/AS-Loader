using System;
using System.Globalization;
using Newtonsoft.Json;

namespace AssetStudio
{
    public static partial class JsonConverterHelper
    {
        public class FloatConverter : JsonConverter<float>
        {
            public override float ReadJson(JsonReader reader, Type objectType, float existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    var stringValue = reader.Value.ToString();
                    if (string.Equals(stringValue, "NaN", StringComparison.OrdinalIgnoreCase))
                        return float.NaN;
                    if (string.Equals(stringValue, "Infinity", StringComparison.OrdinalIgnoreCase))
                        return float.PositiveInfinity;
                    if (string.Equals(stringValue, "-Infinity", StringComparison.OrdinalIgnoreCase))
                        return float.NegativeInfinity;

                    if (float.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                        return result;
                }
                else if (reader.TokenType == JsonToken.Float || reader.TokenType == JsonToken.Integer)
                {
                    return Convert.ToSingle(reader.Value);
                }

                return serializer.Deserialize<float>(reader);
            }

            public override void WriteJson(JsonWriter writer, float value, JsonSerializer serializer)
            {
                if (float.IsNaN(value))
                {
                    writer.WriteValue("NaN");
                }
                else if (float.IsPositiveInfinity(value))
                {
                    writer.WriteValue("Infinity");
                }
                else if (float.IsNegativeInfinity(value))
                {
                    writer.WriteValue("-Infinity");
                }
                else
                {
                    writer.WriteRawValue(value.ToString("G9", CultureInfo.InvariantCulture));
                }
            }

            public override bool CanRead => true;
            public override bool CanWrite => true;
        }
    }
}