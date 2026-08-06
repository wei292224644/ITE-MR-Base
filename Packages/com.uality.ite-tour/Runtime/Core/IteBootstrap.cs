using System;
using System.Collections.Generic;
using UnityEngine;
using Uality.IteTour.Config;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 宿主装配 ITE 运行时所需的一切。字段而非接口——四个必需项都是「宿主已经有的东西」，
    /// 不是「宿主要实现的行为」（design D3）。
    /// </summary>
    public class IteBootstrap
    {
        /// <summary>内容服务地址与场景名。必需。</summary>
        public IteRuntimeConfig Config;

        /// <summary>锚定目标，扫码后被移动到标记位姿。原 tag <c>AnchorObject</c>。必需。</summary>
        public Transform AnchorRoot;

        /// <summary>Tour 实例的父节点。原 tag <c>AnchorOffsetObject</c>。必需。</summary>
        public Transform TourRoot;

        /// <summary>用于识别谁进出触发体积。原 tag <c>ARCamera</c>。必需。</summary>
        public Transform Camera;

        /// <summary>
        /// 有没有网。可选，缺省按「有网」处理——离线是宿主的产品策略，不配就不启用。
        /// </summary>
        public Func<bool> IsNetworkAvailable;

        /// <summary>
        /// 缺失的必需项名。空列表表示可以启动。
        ///
        /// 逐项报名而不是「装配不完整」一句话：装配错误只在真机上现形，
        /// 报错时省下的一个字段名，找起来要花一次打包。
        /// </summary>
        public List<string> MissingRequired()
        {
            var missing = new List<string>();

            if (Config == null) missing.Add(nameof(Config));
            // Tour 预制体也是装配的一部分，只是它挂在 Config 上
            else if (Config.TourObjectPrefab == null) missing.Add("Config.TourObjectPrefab");

            if (AnchorRoot == null) missing.Add(nameof(AnchorRoot));
            if (TourRoot == null) missing.Add(nameof(TourRoot));
            if (Camera == null) missing.Add(nameof(Camera));

            return missing;
        }
    }
}
