using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Uality.IteTour.Components;

namespace Uality.IteTour.Serialization
{
    /// <summary>
    /// 按 <c>Action</c> 字段把触发器派发的动作反序列化成带参数的具体类型。
    /// </summary>
    public class ComponentActionConverter : JsonConverter<ComponentAction>
    {
        public override ComponentAction ReadJson(JsonReader reader, Type objectType, ComponentAction existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject obj = JObject.Load(reader);

            string type = obj["Action"]?.ToString();

            ComponentAction result = type switch
            {
                ComponentActionName.PlayAnimationAction => new PlayAnimationActionParameters(),
                ComponentActionName.PlayAudioAction => new PlayAudioActionParameters(),
                ComponentActionName.SpinAction => new SpinActionParameters(),
                ComponentActionName.ToggleVisibilityAction => new ToggleVisibilityActionParameters(),
                _ => new ComponentAction()
            };

            serializer.Populate(obj.CreateReader(), result);
            return result;
        }

        // 源工程写回的键是 "type"，而读取时用的是 "Action"，两边并不对称。
        // 内容管线只做反序列化，这条路径没有消费方，原样保留并记入 TODO。
        public override void WriteJson(JsonWriter writer, ComponentAction value, JsonSerializer serializer)
        {
            JObject obj = JObject.FromObject(value);
            switch (value)
            {
                case PlayAnimationActionParameters:
                    obj.AddFirst(new JProperty("type", ComponentActionName.PlayAnimationAction));
                    break;
                case PlayAudioActionParameters:
                    obj.AddFirst(new JProperty("type", ComponentActionName.PlayAudioAction));
                    break;
                case SpinActionParameters:
                    obj.AddFirst(new JProperty("type", ComponentActionName.SpinAction));
                    break;
                case ToggleVisibilityActionParameters:
                    obj.AddFirst(new JProperty("type", ComponentActionName.ToggleVisibilityAction));
                    break;
                default:
                    obj.AddFirst(new JProperty("type", "ComponentAction"));
                    break;
            }
            obj.WriteTo(writer);
        }
    }
}
