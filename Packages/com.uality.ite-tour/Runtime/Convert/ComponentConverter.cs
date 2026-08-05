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

            // 未知类型跳过（design D24）。源实现此处直接 Populate(reader, null) 抛
            // ArgumentNullException，整个 Tour 加载失败——服务端上线任何新组件类型，
            // 已发布的客户端就整体加载不出内容。版本化线格式的标准做法是跳过未知项。
            if (result == null)
            {
                return null;
            }

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
