using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Uality.IteTour.Components;

namespace Uality.IteTour.Serialization
{
    /// <summary>
    /// 按 <c>ComponentType</c> 字段把组件反序列化成对应数据类。
    /// </summary>
    public class ComponentConverter : JsonConverter<Component>
    {
        public override Component ReadJson(JsonReader reader, Type objectType, Component existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject obj = JObject.Load(reader);

            string type = obj["ComponentType"]?.ToString();

            // 注意：未知类型这里得到 null，随后 Populate(reader, null) 会抛异常，
            // 于是整个 Tour 加载失败。这是迁移前既有的行为，按"行为等价优先"原样保留。
            // 但它是一颗前向兼容的地雷：服务端新增任何组件类型都会让已发布的客户端
            // 整体加载不出内容。已记入 TODO，修法是加一句 null 判断。
            Component result = type switch
            {
                ComponentType.EMWModelRender => new EMWModelRender(),
                ComponentType.RichText => new RichText(),
                ComponentType.VideoPlane => new VideoPlane(),
                ComponentType.PrimitiveModelRender => new PrimitiveModelRender(),

                ComponentType.LoadTrigger => new LoadTrigger(),
                ComponentType.TapTrigger => new TapTrigger(),
                ComponentType.ApproximateTrigger => new ApproximateTrigger(),

                ComponentType.PlayAnimationAction => new PlayAnimationAction(),
                ComponentType.PlayAudioAction => new PlayAudioAction(),
                ComponentType.SpinAction => new SpinAction(),
                ComponentType.ToggleVisibilityAction => new ToggleVisibilityAction(),

                _ => null
            };

            serializer.Populate(obj.CreateReader(), result);
            return result;
        }

        // 源工程只处理了 EMWModelRender 一种，其余类型会静默丢失 ComponentType 字段。
        // 内容管线只做反序列化，这条路径没有消费方，原样保留并记入 TODO。
        public override void WriteJson(JsonWriter writer, Component value, JsonSerializer serializer)
        {
            JObject obj = JObject.FromObject(value);
            switch (value)
            {
                case EMWModelRender:
                    obj.AddFirst(new JProperty("ComponentType", ComponentType.EMWModelRender));
                    break;
            }
            obj.WriteTo(writer);
        }
    }
}
