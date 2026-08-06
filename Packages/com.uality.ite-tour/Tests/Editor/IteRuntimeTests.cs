using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Uality.IteTour.Config;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 对外 API 面的装配与校验。运行时的加载流程需要网络与磁盘，不在这里测。
    /// </summary>
    public class IteRuntimeTests
    {
        [Test]
        public void MissingRequired_EmptyBootstrap_NamesEveryRequiredField()
        {
            var missing = new IteBootstrap().MissingRequired();

            CollectionAssert.AreEquivalent(
                new[] { "Config", "AnchorRoot", "TourRoot", "Camera" },
                missing.ToArray());
        }

        [Test]
        public void Create_MissingRequired_RefusesAndNamesEachMissingItem()
        {
            LogAssert.Expect(LogType.Error, new Regex("Config.*AnchorRoot.*TourRoot.*Camera"));

            Assert.IsNull(IteRuntime.Create(new IteBootstrap()));
        }

        /// <summary>
        /// Tour 预制体是装配的一部分，只是它挂在 Config 上。少了它同样要拒绝启动，
        /// 而不是等 <c>IteTourAssembler</c> 的构造函数抛 <c>ArgumentNullException</c>。
        /// </summary>
        [Test]
        public void Create_ConfigWithoutTourObjectPrefab_RefusesInsteadOfThrowing()
        {
            var config = ScriptableObject.CreateInstance<IteRuntimeConfig>();
            var host = new GameObject("host");

            try
            {
                LogAssert.Expect(LogType.Error, new Regex("Config\\.TourObjectPrefab"));

                Assert.IsNull(IteRuntime.Create(new IteBootstrap
                {
                    Config = config,
                    AnchorRoot = host.transform,
                    TourRoot = host.transform,
                    Camera = host.transform,
                }));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(config);
            }
        }
    }
}
