using UnityEngine;

public class MarkerTrackingBootstrapper : MonoBehaviour
{
    [SerializeField] private MarkerAnchorService anchorService;
    [SerializeField] private string anchorRegistryUrl = "anchor_registry.json";

    private async void Start()
    {
        await anchorService.Initialize(CreateProvider(), CreateDataSource());
    }

    private IMarkerTrackingProvider CreateProvider()
    {
#if MRBASE_QUEST
        return new QuestMarkerProvider();
#elif MRBASE_PICO
        return new PicoMarkerProvider();
#else
        Debug.LogError("[MarkerTrackingBootstrapper] 未识别到 MRBASE_QUEST 或 MRBASE_PICO 平台定义,部署配置错误。");
        throw new System.PlatformNotSupportedException("未设置平台 Scripting Define Symbol(MRBASE_QUEST / MRBASE_PICO)");
#endif
    }

    private IAnchorDataSource CreateDataSource()
    {
        // 还没有真实后端时先用本地 JSON;有后端后换成 HttpAnchorDataSource,这里是唯一要改的地方。
        return new LocalJsonAnchorDataSource(anchorRegistryUrl);
    }
}
