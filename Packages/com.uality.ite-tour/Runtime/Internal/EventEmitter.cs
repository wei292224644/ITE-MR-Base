using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uality.IteTour.Internal
{
    /// <summary>
    /// 实体级事件通道。触发器组件通过它向同一实体上的动作组件派发动作。
    /// </summary>
    public class EventEmitter : MonoBehaviour
    {
        private Dictionary<string, List<Action<object>>> _eventCallbacks = new Dictionary<string, List<Action<object>>>();

        /// <summary>
        /// 监听事件，可接收动态参数
        /// </summary>
        public void On(string name, Action<object> callback)
        {
            if (!_eventCallbacks.ContainsKey(name))
            {
                _eventCallbacks[name] = new List<Action<object>>();
            }
            _eventCallbacks[name].Add(callback);
        }

        /// <summary>
        /// 调用事件，可传动态参数（可选）
        /// </summary>
        public void Invoke(string name, object data = null)
        {
            if (_eventCallbacks.ContainsKey(name))
            {
                foreach (var callback in _eventCallbacks[name])
                {
                    callback?.Invoke(data);
                }
            }
        }

        public void Off(string name, Action<object> callback)
        {
            if (_eventCallbacks.ContainsKey(name))
            {
                _eventCallbacks[name].Remove(callback);
            }
        }
    }
}
