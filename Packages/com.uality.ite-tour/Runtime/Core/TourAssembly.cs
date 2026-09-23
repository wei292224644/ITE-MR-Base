using System.Collections.Generic;
using Uality.IteTour.Data;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 装配哪些 Tour、谁该有触发体积。纯决策。
    /// JSON 缺 <c>isEnabled</c> 时按启用（字段默认 true），只有显式 false 才跳过。
    /// </summary>
    public static class TourAssembly
    {
        public static bool ShouldAssemble(IteSpaceScene.Tour tour)
            => tour != null && tour.isEnabled;

        public static List<IteSpaceScene.Tour> EnabledTours(IteSpaceScene.Tour[] tours)
        {
            var enabled = new List<IteSpaceScene.Tour>();
            if (tours == null)
            {
                return enabled;
            }

            for (int i = 0; i < tours.Length; i++)
            {
                if (ShouldAssemble(tours[i]))
                {
                    enabled.Add(tours[i]);
                }
            }

            return enabled;
        }

        public static bool AllowsTriggerVolume(IteSpaceScene.Tour.DisplayType displayType)
            => displayType != IteSpaceScene.Tour.DisplayType.alwaysDisplayed;

        /// <summary>停用当前导览时仍保留内容树。只有卸载才拆。</summary>
        public static bool RetainsSceneWhenDeactivated(IteSpaceScene.Tour.DisplayType displayType)
            => displayType == IteSpaceScene.Tour.DisplayType.alwaysDisplayed;

        /// <summary>
        /// 被选为当前 Tour 后是否立即播放（ite-current-tour D5）：regionalTrigger 一进区域就播；
        /// normal 要扫到它的码才播——碰撞永远不激活 normal。
        /// </summary>
        public static bool PlaysOnSelect(IteSpaceScene.Tour.DisplayType displayType)
            => displayType == IteSpaceScene.Tour.DisplayType.regionalTrigger;

        /// <summary>
        /// 能不能当当前 Tour（ite-current-tour D10、I5）。当前 Tour 只在人离开它的体积时才换，没有体积的
        /// alwaysDisplayed 一旦当上就永远离不开；它的显示由导览状态决定（ite-current-tour D8），不需要当当前 Tour。
        /// </summary>
        public static bool CanBeCurrent(IteSpaceScene.Tour.DisplayType displayType)
            => AllowsTriggerVolume(displayType);
    }
}
