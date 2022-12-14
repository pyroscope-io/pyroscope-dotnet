// <copyright file="TufRootBase64Converter.cs" company="Datadog">
// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2017 Datadog, Inc.
// </copyright>

using System;
using System.IO;
using System.Text;
using Datadog.Trace.RemoteConfigurationManagement.Protocol.Tuf;
using Datadog.Trace.Vendors.Newtonsoft.Json;

namespace Datadog.Trace.RemoteConfigurationManagement.Json
{
    internal class TufRootBase64Converter : JsonConverter<TufRoot>
    {
        public override TufRoot ReadJson(JsonReader reader, Type objectType, TufRoot existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.Value == null || reader.ValueType != typeof(string))
            {
                return null;
            }

            var contentDecode = Convert.FromBase64String((string)reader.Value);

            using var stream = new MemoryStream(contentDecode);
            using var streamReader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(streamReader);
            return serializer.Deserialize<TufRoot>(jsonReader);
        }

        public override void WriteJson(JsonWriter writer, TufRoot value, JsonSerializer serializer)
        {
            var encodedContent = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value)));
            writer.WriteValue(encodedContent);
        }
    }
}
