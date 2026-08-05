using System.Reflection;
using NUnit.Framework;
using Uality.IteTour.Components;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 组件类型键 → MonoBehaviour 类型的注册表。
    ///
    /// 源实现是个 <c>StaticInstance&lt;ComponentsUtils&gt;</c> 单例 MonoBehaviour，靠
    /// <c>executionOrder: 500</c> 保证在别人用它之前 <c>Awake</c> 完——一张**编译期
    /// 就确定**的常量表，却被绑在场景对象的生命周期上（design D23）。
    ///
    /// 键取自服务端约定，改错了组件就静默消失。
    /// </summary>
    public class ComponentRegistryTests
    {
        [TestCase(ComponentType.EMWModelRender, typeof(EMWModelRenderUnityComponent))]
        [TestCase(ComponentType.RichText, typeof(RichTextUnityComponent))]
        [TestCase(ComponentType.VideoPlane, typeof(VideoPlaneUnityComponent))]
        [TestCase(ComponentType.PrimitiveModelRender, typeof(PrimitiveModelRenderUnityComponent))]
        [TestCase(ComponentType.LoadTrigger, typeof(LoadTriggerUnityComponent))]
        [TestCase(ComponentType.TapTrigger, typeof(TapTriggerUnityComponent))]
        [TestCase(ComponentType.ApproximateTrigger, typeof(ApproximateTriggerUnityComponent))]
        [TestCase(ComponentType.PlayAudioAction, typeof(PlayAudioActionUnityComponent))]
        [TestCase(ComponentType.SpinAction, typeof(SpinActionUnityComponent))]
        [TestCase(ComponentType.ToggleVisibilityAction, typeof(ToggleVisibilityActionUnityComponent))]
        [TestCase(ComponentType.PlayAnimationAction, typeof(PlayAnimationActionUnityComponent))]
        public void Resolve_MapsEachKeyToItsComponent(string key, System.Type expected)
        {
            Assert.That(ComponentRegistry.Resolve(key), Is.EqualTo(expected));
        }

        [TestCase("VolumeTriggerComponent")]
        [TestCase("")]
        [TestCase(null)]
        public void Resolve_ReturnsNullForUnknownKeys(string key)
        {
            Assert.That(ComponentRegistry.Resolve(key), Is.Null);
        }

        /// <summary>
        /// 每个 <c>ComponentType</c> 常量都必须有一行注册。加了常量却忘了登记，
        /// 表现是「这类组件在场景里静默不出现」——没有报错，最难查的那种。
        /// </summary>
        [Test]
        public void EveryComponentTypeConstantIsRegistered()
        {
            var constants = typeof(ComponentType).GetFields(BindingFlags.Public | BindingFlags.Static);
            Assert.That(constants, Is.Not.Empty);

            foreach (var field in constants)
            {
                var key = (string)field.GetValue(null);
                Assert.That(ComponentRegistry.Resolve(key), Is.Not.Null, $"{field.Name} 没有注册");
            }
        }
    }
}
