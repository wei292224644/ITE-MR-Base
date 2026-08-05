using System.Collections.Generic;
using UnityEngine;
using Uality.IteTour.Data.Assets;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 由发布描述与 glb 里的动画列表构建「动画名 → 配音」映射。纯函数，无副作用。
    ///
    /// 两个下标都来自服务端数据，越界的条目**跳过**而不是抛异常（design D18）：
    /// 源实现在这里越界会打死整个 Tour 的加载，而一条配错的配音不该有那么大杀伤力。
    /// </summary>
    public static class AnimationAudioMap
    {
        public static Dictionary<string, AudioData> Build(
            EMWModelAsset.PublishJson json, IReadOnlyList<AnimationClip> clips)
        {
            var map = new Dictionary<string, AudioData>();

            if (json?.animationAudio == null || json.audio == null || clips == null)
            {
                return map;
            }

            for (int i = 0; i < json.animationAudio.Length && i < clips.Count; i++)
            {
                var item = json.animationAudio[i];

                if (item == null || item.audio < 0 || item.audio >= json.audio.Length)
                {
                    continue;
                }

                var clip = clips[i];
                if (clip == null)
                {
                    continue;
                }

                map[clip.name] = new AudioData
                {
                    Clip = json.audio[item.audio].audioClip,
                    Volume = item.volume,
                };
            }

            return map;
        }
    }
}
