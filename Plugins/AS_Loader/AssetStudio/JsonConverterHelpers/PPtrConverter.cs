using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AssetStudio
{
    public static partial class JsonConverterHelper
    {
        public static SerializedFile AssetsFile { get; set; }

        public class PPtrConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                if (!objectType.IsGenericType)
                    return false;

                var generic = objectType.GetGenericTypeDefinition();
                return generic == typeof(PPtr<>);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var elementType = objectType.GetGenericArguments()[0];

                var pptrInstance = Activator.CreateInstance(objectType);

                var jObject = JObject.Load(reader);

                var tempType = typeof(PPtr<>).MakeGenericType(elementType);
                var tempSerializer = JsonSerializer.CreateDefault();
                tempSerializer.ContractResolver = serializer.ContractResolver;

                using (var tempReader = jObject.CreateReader())
                {
                    tempSerializer.Populate(tempReader, pptrInstance);
                }

                var assetsFileField = objectType.GetField("AssetsFile");
                if (assetsFileField != null)
                {
                    assetsFileField.SetValue(pptrInstance, AssetsFile);
                }

                return pptrInstance;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }

            public override bool CanWrite => false;
        }

        public class PPtrConverter<T> : JsonConverter<PPtr<T>> where T : Object
        {
            public override PPtr<T> ReadJson(JsonReader reader, Type objectType, PPtr<T> existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                var pptr = new PPtr<T>();
                serializer.Populate(reader, pptr);

                pptr.AssetsFile = AssetsFile;

                return pptr;
            }

            public override void WriteJson(JsonWriter writer, PPtr<T> value, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }

            public override bool CanWrite => false;
        }
    }
}