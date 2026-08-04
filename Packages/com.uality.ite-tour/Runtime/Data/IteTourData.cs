using System.Collections.Generic;
using UnityEngine;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Data.Assets
{
    public class AssetType
    {
        public const string EMWModel = "EMWModel";
        public const string RichText = "RichText";
        public const string Video = "Video";
    }

    /// <summary>
    /// 资源基类。具体子类（EMWModelAsset / RichTextAsset / VideoAsset）
    /// 随各自的元素组件定义，由 AssetConverter 按 Type 字段多态分派。
    /// </summary>
    [System.Serializable]
    public class Asset
    {
        public string Type;
        public string Name;
        public string Id;
        public string Description;
        public long CreatedAtDate;
        public long UpdatedAtDate;
    }
}

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 组件类型键。每个组件文件以 partial 追加自己的常量。
    /// </summary>
    [System.Serializable]
    public partial class ComponentType
    {
    }

    public interface Component
    {
        string ComponentType { get; set; }
    }

    [System.Serializable]
    public class ComponentAction
    {
        public string EntityId;
        public string Action;
    }

    /// <summary>
    /// 动作名。每个动作组件文件以 partial 追加自己的常量。
    /// </summary>
    public partial class ComponentActionName
    {
    }
}

namespace Uality.IteTour.Data
{
    public class IteTour
    {
        public string Id;
        public string Type;

        public Dictionary<string, Assets.Asset> Assets;

        public Dictionary<string, Scene> Scenes;

        public string[] ScenesOrder;
    }

    [System.Serializable]
    public class Scene
    {
        public string Description;
        public string Title;
        public Dictionary<string, Entity> Entities;
    }

    [System.Serializable]
    public class Entity
    {
        public string Id;
        public string name;
        public bool enable;
        public Vector3 pos;
        public Vector3 rot;
        public Vector3 scale;
        public List<Components.Component> Components;

        private Matrix4x4 _matrix4X4;

        /// <summary>
        /// 注意：这里在 ConvertToLeftHanded 之后还做了 FlipRotY，
        /// 而 <see cref="IteSpaceScene.Tour.Matrix4X4"/> 没有。
        /// 这处不一致是迁移前即存在的行为，原样保留，由快照测试锁定。
        /// </summary>
        public Matrix4x4 Matrix4X4
        {
            get
            {
                if (_matrix4X4 == default)
                {
                    _matrix4X4 = Matrix4x4.TRS(pos, Quaternion.Euler(rot * Mathf.Rad2Deg), scale);
                    _matrix4X4 = _matrix4X4.ConvertToLeftHanded();
                    _matrix4X4 = _matrix4X4.FlipRotY();
                }
                return _matrix4X4;
            }
        }
    }
}
