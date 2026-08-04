using UnityEngine;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Data
{
    [System.Serializable]
    public class IteSpaces
    {
        [System.Serializable]
        public class Project
        {
            public string projectName;
        }

        public Project[] projects;
    }

    /// <summary>
    /// 空间场景描述：一个空间里有哪些 Tour、各自的位姿与触发体积。
    /// </summary>
    [System.Serializable]
    public class IteSpaceScene
    {
        [System.Serializable]
        public class Tour
        {
            [System.Serializable]
            public class TriggerVolume
            {
                public float width;
                public float height;
                public float depth;
                public Vector3 pos; // Position in local space
                public Vector3 rot; // Rotation in local Euler angles

                private Matrix4x4 _matrix4X4;
                public Matrix4x4 Matrix4X4
                {
                    get
                    {
                        if (_matrix4X4 == default)
                        {
                            _matrix4X4 = Matrix4x4.TRS(pos, Quaternion.Euler(rot * Mathf.Rad2Deg), Vector3.one);
                        }
                        return _matrix4X4;
                    }
                }
            }

            [System.Serializable]
            public enum DisplayType
            {
                normal,
                alwaysDisplayed,
                regionalTrigger
            }

            public string tourID;
            public bool isEnabled;
            public DisplayType displayType;
            public float[][] transform; // 4x4 matrix represented as a jagged array
            public TriggerVolume triggerVolume;

            private Matrix4x4 _matrix4X4;

            /// <summary>
            /// 注意：这里只做 ConvertToLeftHanded，而 <see cref="Entity.Matrix4X4"/>
            /// 还额外做了 FlipRotY。这处不一致是迁移前即存在的行为，原样保留，
            /// 由快照测试锁定。
            /// </summary>
            public Matrix4x4 Matrix4X4
            {
                get
                {
                    if (_matrix4X4 == default)
                    {
                        _matrix4X4 = new Matrix4x4();
                        _matrix4X4.SetColumn(0, new Vector4(transform[0][0], transform[0][1], transform[0][2], transform[0][3]));
                        _matrix4X4.SetColumn(1, new Vector4(transform[1][0], transform[1][1], transform[1][2], transform[1][3]));
                        _matrix4X4.SetColumn(2, new Vector4(transform[2][0], transform[2][1], transform[2][2], transform[2][3]));
                        _matrix4X4.SetColumn(3, new Vector4(transform[3][0], transform[3][1], transform[3][2], transform[3][3]));
                        _matrix4X4 = _matrix4X4.ConvertToLeftHanded();
                    }
                    return _matrix4X4;
                }
            }

            /// <summary>运行时由内容管线填入，不来自 JSON。</summary>
            public Sprite SpritePreviewImage;
        }

        public Tour[] tours;

        public string id;
        public string logo;
        public string name;

        /// <summary>运行时由内容管线填入，不来自 JSON。</summary>
        public Sprite SpriteLogo;
    }
}
