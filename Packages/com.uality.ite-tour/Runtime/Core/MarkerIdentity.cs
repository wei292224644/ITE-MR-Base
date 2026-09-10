using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Uality.IteTour.Core
{
    /// <summary>一个 Tour 的标记绑定事实。刻意只带事实，不带 GameObject。</summary>
    public readonly struct MarkerBinding
    {
        public readonly string TourId;

        /// <summary>该 Tour 绑定的 AprilTag ID；null 表示不参与 AprilTag 反查。</summary>
        public readonly int? AprilTagId;

        public MarkerBinding(string tourId, int? aprilTagId)
        {
            TourId = tourId;
            AprilTagId = aprilTagId;
        }
    }

    public enum MarkerResolution
    {
        /// <summary>解析出 tourId，且该 Tour 在场景中。</summary>
        Resolved,

        /// <summary>payload 形状不符合该种类的解析规则。</summary>
        Unparsable,

        /// <summary>解析规则走通了，但没有对应的 Tour。</summary>
        UnknownTour,
    }

    /// <summary>
    /// 二维码文本 → tourId 的**唯一**解析点（design D8）。
    ///
    /// 单独一个类型而不是 <see cref="MarkerIdentity"/> 里的一段：二维码的内容归内容方所有，
    /// 形状会变（预期会变成一个地址）。换实现时只动这里，调用方与测试的其余部分不受波及。
    /// </summary>
    public static class QrPayloadFormat
    {
        /// <summary>当前印制格式。真机核实前沿用源工程的外壳。</summary>
        private static readonly Regex Shell = new Regex(@"^\*{6}(.*?)\*{6}$", RegexOptions.Compiled);

        public static bool TryParseTourId(string rawPayload, out string tourId)
        {
            tourId = null;
            if (string.IsNullOrEmpty(rawPayload))
            {
                return false;
            }

            var match = Shell.Match(rawPayload);
            if (!match.Success || match.Groups.Count < 2)
            {
                return false;
            }

            var parsed = match.Groups[1].Value;
            if (string.IsNullOrEmpty(parsed))
            {
                return false;
            }

            tourId = parsed;
            return true;
        }
    }

    /// <summary>
    /// 标记 payload 到 tourId 的解析（design D5）。纯函数：不碰 GameObject、不打日志、
    /// 不依赖帧或 async 时序——日志由调用方按 <see cref="MarkerResolution"/> 决定怎么说。
    ///
    /// 这段逻辑原先在宿主的一条正则里。移进包内的理由是它属于内容：payload 的形状由内容方
    /// 定义且会变，焊在宿主意味着内容每改一次码，宿主就要出一次包。
    /// </summary>
    public static class MarkerIdentity
    {
        public static MarkerResolution Resolve(
            MarkerKind kind,
            string rawPayload,
            IReadOnlyList<MarkerBinding> bindings,
            out string tourId)
        {
            tourId = null;

            switch (kind)
            {
                case MarkerKind.QrText:
                    if (!QrPayloadFormat.TryParseTourId(rawPayload, out string parsedTourId))
                    {
                        return MarkerResolution.Unparsable;
                    }

                    // 解析成功就带出 tourId，即使它不在场景中——日志要说清是谁。
                    tourId = parsedTourId;
                    return Contains(bindings, parsedTourId)
                        ? MarkerResolution.Resolved
                        : MarkerResolution.UnknownTour;

                case MarkerKind.AprilTagId:
                    if (string.IsNullOrEmpty(rawPayload)
                        || !int.TryParse(rawPayload, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tag))
                    {
                        return MarkerResolution.Unparsable;
                    }

                    // 反查不到时 tourId 留 null:tag 号本身不是 tourId,带不出来。
                    return TryFindByTag(bindings, tag, out tourId)
                        ? MarkerResolution.Resolved
                        : MarkerResolution.UnknownTour;

                default:
                    return MarkerResolution.Unparsable;
            }
        }

        private static bool Contains(IReadOnlyList<MarkerBinding> bindings, string tourId)
        {
            if (bindings == null)
            {
                return false;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].TourId == tourId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindByTag(IReadOnlyList<MarkerBinding> bindings, int tag, out string tourId)
        {
            tourId = null;
            if (bindings == null)
            {
                return false;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                // AprilTagId 为 null 的 Tour 不参与反查——tag 0 是合法 ID,
                // 不能让「没绑定」和「绑到 0」撞在一起。
                if (bindings[i].AprilTagId == tag)
                {
                    tourId = bindings[i].TourId;
                    return true;
                }
            }

            return false;
        }
    }
}
