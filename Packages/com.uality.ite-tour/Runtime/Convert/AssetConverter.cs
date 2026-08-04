using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Uality.IteTour.Data.Assets;

namespace Uality.IteTour.Serialization
{
    /// <summary>
    /// 按 <c>Type</c> 字段把 Asset 反序列化成对应子类。
    /// </summary>
    public class AssetConverter : JsonConverter<Asset>
    {
        public override Asset ReadJson(JsonReader reader, Type objectType, Asset existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject obj = JObject.Load(reader);

            string type = obj["Type"]?.ToString();

            Asset result = type switch
            {
                AssetType.EMWModel => new EMWModelAsset(),
                AssetType.RichText => new RichTextAsset(),
                AssetType.Video => new VideoAsset(),
                _ => new Asset()
            };

            serializer.Populate(obj.CreateReader(), result);
            return result;
        }

        // 源工程写回的键是小写 "type"，而读取时用的是 "Type"，两边并不对称。
        // 内容管线只做反序列化，这条路径没有消费方，原样保留并记入 TODO。
        public override void WriteJson(JsonWriter writer, Asset value, JsonSerializer serializer)
        {
            JObject obj = JObject.FromObject(value);
            switch (value)
            {
                case EMWModelAsset:
                    obj.AddFirst(new JProperty("type", AssetType.EMWModel));
                    break;
                case RichTextAsset:
                    obj.AddFirst(new JProperty("type", AssetType.RichText));
                    break;
                case VideoAsset:
                    obj.AddFirst(new JProperty("type", AssetType.Video));
                    break;
                default:
                    obj.AddFirst(new JProperty("type", "Asset"));
                    break;
            }
            obj.WriteTo(writer);
        }
    }
}
