using System;
using CyberSource.Model;
using Newtonsoft.Json;

namespace VirtoCommerce.CyberSourcePayment.Data.Services
{
    public class Notificationsubscriptionsv1webhooksNotificationScopeJsonConverter : JsonConverter<Notificationsubscriptionsv1webhooksNotificationScope>
    {
        public override void WriteJson(JsonWriter writer, Notificationsubscriptionsv1webhooksNotificationScope value,
            JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }

        public override Notificationsubscriptionsv1webhooksNotificationScope ReadJson(JsonReader reader, Type objectType,
            Notificationsubscriptionsv1webhooksNotificationScope existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                return new Notificationsubscriptionsv1webhooksNotificationScope { Scope = reader.Value.ToString() };
            }
            if (reader.TokenType == JsonToken.StartObject)
            {
                return serializer.Deserialize<Notificationsubscriptionsv1webhooksNotificationScope>(reader);
            }

            throw new JsonSerializationException($"Unexpected token {reader.TokenType} when parsing Notificationsubscriptionsv1webhooksNotificationScope");

        }
    }
}
