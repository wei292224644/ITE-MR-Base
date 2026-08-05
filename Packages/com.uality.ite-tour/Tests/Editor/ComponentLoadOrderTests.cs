using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Uality.IteTour.Components;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 一个实体上的组件按什么次序实例化。次序不是美观问题：触发器要在
    /// <c>Constructor</c> 里 <c>GetComponent&lt;BaseElementComponent&gt;()</c>，
    /// 动作要 <c>GetComponent&lt;T&gt;()</c> 拿元素——**元素必须先在**。
    ///
    /// 源实现把这个次序写成 <c>CreateTourScene</c> 里的一个三层嵌套循环，与
    /// GameObject 创建、<c>AddComponent</c>、<c>await</c> 混在一起，无法单独验证。
    /// </summary>
    public class ComponentLoadOrderTests
    {
        private class Fake : Component
        {
            public string ComponentType { get; set; }
        }

        private static Fake C(string type) => new Fake { ComponentType = type };

        private static List<string> Types(IEnumerable<Component> components)
            => components.Select(c => c.ComponentType).ToList();

        [Test]
        public void Sort_PutsElementsBeforeTriggersBeforeActions()
        {
            var entityComponents = new List<Component>
            {
                C(ComponentType.SpinAction),
                C(ComponentType.TapTrigger),
                C(ComponentType.RichText),
            };

            var ordered = ComponentLoadOrder.Sort(entityComponents);

            Assert.That(Types(ordered), Is.EqualTo(new[]
            {
                ComponentType.RichText,
                ComponentType.TapTrigger,
                ComponentType.SpinAction,
            }));
        }

        [Test]
        public void Sort_SkipsUnregisteredComponentTypes()
        {
            var entityComponents = new List<Component>
            {
                C("VolumeTriggerComponent"),
                C(ComponentType.RichText),
            };

            Assert.That(Types(ComponentLoadOrder.Sort(entityComponents)),
                Is.EqualTo(new[] { ComponentType.RichText }));
        }

        /// <summary>
        /// 同一类型出现多次时只取第一个——源实现用的是 <c>Find</c>，多出来的静默丢弃。
        /// </summary>
        [Test]
        public void Sort_KeepsOnlyTheFirstComponentOfEachType()
        {
            var first = C(ComponentType.RichText);
            var second = C(ComponentType.RichText);

            var ordered = ComponentLoadOrder.Sort(new List<Component> { first, second });

            Assert.That(ordered.Count, Is.EqualTo(1));
            Assert.That(ordered[0], Is.SameAs(first));
        }

        /// <summary>
        /// 注册表里的每个类型都必须出现在次序表里。漏一个的表现是「这类组件
        /// 静默不实例化」——注册了却排不上队，比忘记注册更难查。
        /// </summary>
        [Test]
        public void EveryRegisteredComponentTypeHasAPlaceInTheOrder()
        {
            var constants = typeof(ComponentType)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Select(f => (string)f.GetValue(null))
                .ToList();

            foreach (var key in constants)
            {
                var ordered = ComponentLoadOrder.Sort(new List<Component> { C(key) });
                Assert.That(ordered, Is.Not.Empty, $"{key} 不在实例化次序表里");
            }
        }

        [Test]
        public void Sort_ReturnsEmptyForNullOrEmptyInput()
        {
            Assert.That(ComponentLoadOrder.Sort(null), Is.Empty);
            Assert.That(ComponentLoadOrder.Sort(new List<Component>()), Is.Empty);
        }
    }
}
