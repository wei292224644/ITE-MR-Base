using GLTFast;
using UnityEngine;

namespace Uality.IteTour.Data.Assets
{
    [System.Serializable]
    public class EMWModelAsset : Asset
    {
        [System.Serializable]
        public class PublishJson
        {
            [System.Serializable]
            public class EMWModelRenderAudio
            {
                public string path;
                public AudioClip audioClip;
            }

            [System.Serializable]
            public class EMWModelRenderAnimationAudio
            {
                public int audio;
                public float volume;
            }

            public string id;
            public string name;
            public EMWModelRenderAudio[] audio;
            public EMWModelRenderAnimationAudio[] animationAudio;
        }

        public string RepoId;
        public string SceneId;
        public int Version;

        /// <summary>运行时由内容管线填入，不来自 JSON。</summary>
        public GltfImport GltfImport;

        /// <summary>运行时由内容管线填入，不来自 JSON。</summary>
        public PublishJson Json;
    }

    [System.Serializable]
    public class RichTextAsset : Asset
    {
        public string ImgSrc;
        public string RasterSrc;
        public int RasterOpacity;
        public string AudioSrc;
        public bool AudioOnly;

        /// <summary>运行时由内容管线填入，不来自 JSON。</summary>
        public AudioClip audioInstance;

        /// <summary>运行时由内容管线填入，不来自 JSON。</summary>
        public Sprite spriteInstance;
    }

    [System.Serializable]
    public class VideoAsset : Asset
    {
        public string Src;
        public string Status;

        /// <summary>运行时由内容管线填入，不来自 JSON。</summary>
        public string videoAbsoluteUrl;
    }
}

namespace Uality.IteTour.Components
{
    [System.Serializable]
    public class EMWModelRender : Component
    {
        public string Asset;
        public bool CastGroundShadow;
        public bool OcclusionMode;
        public string ComponentType { get; set; }
    }

    [System.Serializable]
    public class RichText : Component
    {
        public string Asset;
        public bool DoubleSided;
        public float CornerRadius;
        public bool Loop;
        public bool AutoPlay;
        public string ComponentType { get; set; }
    }

    [System.Serializable]
    public class VideoPlane : Component
    {
        public string Asset;
        public bool DoubleSided;
        public float CornerRadius;
        public bool Loop;
        public bool AutoPlay;
        public string ComponentType { get; set; }
    }

    [System.Serializable]
    public class PrimitiveModelRender : Component
    {
        /// <summary>模型类型，目前支持 box / plane / sphere。</summary>
        public string PrimitiveType;

        /// <summary>是否在模型底面投射阴影。bool，默认 true。</summary>
        public bool CastGroundShadow;

        /// <summary>box 的长。float，0&lt;x&lt;100，默认 1。</summary>
        public float boxWidth;

        /// <summary>box 的高。float，0&lt;x&lt;100，默认 1。</summary>
        public float boxHeight;

        /// <summary>box 的宽。float，0&lt;x&lt;100，默认 1。</summary>
        public float boxDepth;

        /// <summary>box 边角圆角的弧度。float，0~1，默认 1。</summary>
        public float boxCornerRadius;

        /// <summary>plane 的宽。float，0&lt;x&lt;100，默认 1。</summary>
        public float planeWidth;

        /// <summary>plane 的高。float，0&lt;x&lt;100，默认 1。</summary>
        public float planeHeight;

        /// <summary>plane 边角圆角的弧度。float，0~1，默认 1。</summary>
        public float planeCornerRadius;

        /// <summary>sphere 的半径。float，1~100，默认 1。</summary>
        public float sphereRadius;

        public string ComponentType { get; set; }
    }

    public partial class ComponentType
    {
        public const string EMWModelRender = "EMWModelRender";
        public const string RichText = "RichText";
        public const string VideoPlane = "VideoPlane";
        public const string PrimitiveModelRender = "PrimitiveModelRender";
    }
}
