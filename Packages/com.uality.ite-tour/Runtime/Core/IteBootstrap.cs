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

        /// <summary>
        /// 锚定层级约束。空列表表示可以启动。
        ///
        /// 错误信息说清「为什么」而不只是「不满足」：层级搭错不会抛异常，
        /// 只会让内容出现在错误位置（design D4）。
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (AnchorRoot == null || TourRoot == null)
                return errors;

            if (TourRoot.parent != AnchorRoot)
            {
                errors.Add(
                    "TourRoot 必须是 AnchorRoot 的直接子物体。" +
                    "ChangeTourObjectTransform 把 TourRoot 的 local 设为被扫中 tour 的逆、" +
                    "把 AnchorRoot 的 local 设为标记世界位姿，两者相乘才让被扫中的 tour 落在标记上；" +
                    "中间夹任何节点等式即破。");
            }

            var parent = AnchorRoot.parent;
            if (parent != null && parent.localToWorldMatrix != Matrix4x4.identity)
            {
                errors.Add(
                    "AnchorRoot 的父级必须处于世界原点、无旋转、无缩放（根级物体即满足）。" +
                    "标记位姿是世界位姿，却以 SetLocalPositionAndRotation 写入 AnchorRoot；" +
                    "父级上的任何变换都会被二次施加，内容会出现在错误位置。");
            }

            return errors;
        }
    }
}
