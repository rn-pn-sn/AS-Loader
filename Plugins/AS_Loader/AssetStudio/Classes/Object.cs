using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace AssetStudio
{
    public class Object
    {
        [JsonIgnore]
        public SerializedFile assetsFile;
        [JsonIgnore]
        public ObjectReader reader;
        public long m_PathID;
        [JsonIgnore]
        public UnityVersion version;
        [JsonIgnore]
        public BuildTarget platform;
        [JsonConverter(typeof(StringEnumConverter))]
        public ClassIDType type;
        [JsonIgnore]
        public SerializedType serializedType;
        public int classID;
        public uint byteSize;
        [JsonIgnore]
        public string Name;

        private static readonly JsonSerializerSettings jsonSettings;

        static Object()
        {
            jsonSettings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>
                {
                    new JsonConverterHelper.FloatConverter(),
                    new StringEnumConverter()
                },
                StringEscapeHandling = StringEscapeHandling.Default,
                FloatParseHandling = FloatParseHandling.Decimal,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new IncludeFieldsContractResolver(),
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate
            };
        }

        public Object() { }

        public Object(ObjectReader reader)
        {
            this.reader = reader;
            reader.Reset();
            assetsFile = reader.assetsFile;
            type = reader.type;
            m_PathID = reader.m_PathID;
            version = reader.version;
            platform = reader.platform;
            serializedType = reader.serializedType;
            classID = reader.classID;
            byteSize = reader.byteSize;

            if (platform == BuildTarget.NoTarget)
            {
                var m_ObjectHideFlags = reader.ReadUInt32();
            }
        }

        public string DumpObject()
        {
            string str = null;
            try
            {
                if (this is Mesh m_Mesh)
                {
                    m_Mesh.ProcessData();
                }

                str = JsonConvert.SerializeObject(this, Formatting.Indented, jsonSettings)
                    .Replace("  ", "    ");
            }
            catch
            {
                // ignore
            }

            return str;
        }

        public string DumpObjectCompact()
        {
            string str = null;
            try
            {
                if (this is Mesh m_Mesh)
                {
                    m_Mesh.ProcessData();
                }

                var compactSettings = new JsonSerializerSettings
                {
                    Converters = jsonSettings.Converters,
                    ContractResolver = jsonSettings.ContractResolver,
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    Formatting = Formatting.None
                };

                str = JsonConvert.SerializeObject(this, compactSettings);
            }
            catch
            {
                // ignore
            }

            return str;
        }

        public string Dump(TypeTree m_Type = null)
        {
            m_Type = m_Type ?? serializedType?.m_Type;
            if (m_Type == null)
                return null;

            return TypeTreeHelper.ReadTypeString(m_Type, reader);
        }

        public OrderedDictionary ToType(TypeTree m_Type = null)
        {
            m_Type = m_Type ?? serializedType?.m_Type;
            if (m_Type == null)
                return null;

            return TypeTreeHelper.ReadType(m_Type, reader);
        }

        public JToken ToJToken(TypeTree m_Type = null)
        {
            var typeDict = ToType(m_Type);
            try
            {
                if (typeDict != null)
                {
                    return JToken.FromObject(typeDict, JsonSerializer.Create(jsonSettings));
                }

                if (this is Mesh m_Mesh)
                {
                    m_Mesh.ProcessData();
                }

                return JToken.FromObject(this, JsonSerializer.Create(jsonSettings));
            }
            catch
            {
                // ignore
            }

            return null;
        }

        public JObject ToJObject(TypeTree m_Type = null)
        {
            var token = ToJToken(m_Type);
            return token as JObject;
        }

        public string ToJson(TypeTree m_Type = null)
        {
            var token = ToJToken(m_Type);
            return token?.ToString(Formatting.Indented);
        }

        public byte[] GetRawData()
        {
            reader.Reset();
            return reader.ReadBytes((int)byteSize);
        }

        // Helper method to get JSON with custom settings
        public string GetJson(JsonSerializerSettings customSettings = null)
        {
            try
            {
                if (this is Mesh m_Mesh)
                {
                    m_Mesh.ProcessData();
                }

                var settings = customSettings ?? jsonSettings;
                return JsonConvert.SerializeObject(this, settings);
            }
            catch
            {
                return null;
            }
        }
    }

    // Custom contract resolver to include fields
    public class IncludeFieldsContractResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            var properties = base.CreateProperties(type, memberSerialization);

            // Include public fields
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Select(f => CreateProperty(f, memberSerialization))
                .Where(p => p != null)
                .ToList();

            // Combine properties and fields
            var allMembers = new List<JsonProperty>();
            if (properties != null)
                allMembers.AddRange(properties);
            allMembers.AddRange(fields);

            return allMembers;
        }

        protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (member is System.Reflection.FieldInfo)
            {
                // Make fields writable and readable
                property.Writable = true;
                property.Readable = true;

                // You can customize field naming if needed
                // property.PropertyName = JsonNamingStrategy.CamelCase.ConvertName(member.Name);
            }

            return property;
        }
    }
}