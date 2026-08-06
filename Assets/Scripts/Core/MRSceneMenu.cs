using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 设备内的场景切换菜单。按钮按 <see cref="MRSceneDirector.ContentScenes"/> 在运行时生成。
///
/// 为什么运行时生成而不在场景里摆好按钮：清单来自 Build Settings，手摆的按钮会和它漂移，
/// 而漂移的表现是「菜单上有个按钮点了没反应」，得戴上头显才发现。
///
/// 摆位：挂在 XR 相机下方、略微朝上，像一块胸前面板 —— 低头可见，平视不挡。
/// 头锁但压低，是这里唯一不需要接线任何输入 action 就能一直可达的形态。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class MRSceneMenu : MonoBehaviour
{
    [Header("按钮模板")]
    [Tooltip("被复制的按钮。必须是本 Canvas 的子物体，且默认关闭。")]
    [SerializeField] Button buttonTemplate;

    [Header("跟随")]
    [Tooltip("相对 XR 相机的位置。y 为负表示在视线下方。")]
    [SerializeField] Vector3 localPosition = new Vector3(0f, -0.32f, 0.62f);

    [Tooltip("相对 XR 相机的朝向。绕 X 正角度 = 面板朝上，便于低头读。")]
    [SerializeField] Vector3 localEulerAngles = new Vector3(32f, 0f, 0f);

    readonly List<Button> m_Spawned = new();
    bool m_Attached;
    bool m_Built;

    void Start()
    {
        if (buttonTemplate != null)
            buttonTemplate.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!m_Built)
            TryBuild();
        if (!m_Attached)
            TryAttachToCamera();
    }

    /// <summary>
    /// Director 与本组件同在初始化场景，Awake 顺序不保证，所以在 Update 里等它就绪。
    /// 依赖 Awake 顺序是这类 bug 最常见的来源，而脚本执行顺序设置只是把约束藏起来。
    /// </summary>
    void TryBuild()
    {
        var director = MRSceneDirector.Instance;
        if (director == null || buttonTemplate == null)
            return;

        foreach (var sceneName in director.ContentScenes)
        {
            var button = Instantiate(buttonTemplate, buttonTemplate.transform.parent);
            button.name = "Button_" + sceneName;
            button.gameObject.SetActive(true);

            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = sceneName;

            // 闭包捕获循环变量：sceneName 是 foreach 的每轮新变量，C# 5 起可安全捕获。
            var target = sceneName;
            button.onClick.AddListener(() => director.Load(target));
            m_Spawned.Add(button);
        }

        m_Built = true;
    }

    void TryAttachToCamera()
    {
        var context = MRContext.Instance;
        var cam = context == null ? null : context.Camera;
        if (cam == null)
            return;

        transform.SetParent(cam.transform, false);
        transform.localPosition = localPosition;
        transform.localEulerAngles = localEulerAngles;
        m_Attached = true;
    }
}
