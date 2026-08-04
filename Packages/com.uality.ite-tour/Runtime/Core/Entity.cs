using System;
using UnityEngine;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// Tour 场景里的一个实体。
    ///
    /// 显隐切换作用在子物体 <see cref="Root"/> 而不是实体自身：组件挂在实体上，
    /// 关掉实体等于连带停掉动作组件的协程与事件订阅。多一层 Root 之后，
    /// 内容可以隐藏而逻辑照常运行。
    /// </summary>
    public class Entity : MonoBehaviour
    {
        /// <summary>切换可见性**之前**触发，供动作组件复位（如停旋转、停动画）。</summary>
        public Action OnPreEnable;

        public Action OnPreDisable;

        private GameObject _root;

        public GameObject Root => _root;

        void Awake()
        {
            _root = new GameObject("Root");
            _root.transform.SetParent(transform, false);
        }

        public void SetActive(bool active)
        {
            if (_root != null)
            {
                _root.SetActive(active);
            }
        }
    }
}
