using UnityEngine;

public class MarkerTrackingBootstrapper : MonoBehaviour
{
    [SerializeField] private MarkerAnchorService anchorService;
    [SerializeField] private string anchorRegistryUrl = "anchor_registry.json";

    /// <summary>
    /// 本次会话的标记提供方。**在 Awake 里就绪**，其它消费方可以在自己的 Start 里安全取用。
    ///
    /// 暴露出来是因为标记识别是一份**硬件会话**，不该按消费方数量开多份：ITE 导览
    /// （<c>MRBase.Ite.Host.MarkerSourceAdapter</c>）与 <see cref="MarkerAnchorService"/>
    /// 是两桩业务，但共享同一个提供方。同时这也让平台 <c>#if</c> 继续只存在于此处一份。
    /// </summary>
    public IMarkerTrackingProvider Provider { get; private set; }

    private void Awake()
    {
        Provider = CreateProvider();
    }

    private async void Start()
    {
        await anchorService.Initialize(Provider, CreateDataSource());
    }

    private IMarkerTrackingProvider CreateProvider()
    {
#if MRBASE_QUEST
        return new QuestMarkerProvider();
#elif MRBASE_PICO && MRBASE_HAS_PICO_SDK
        return new PicoMarkerProvider();
#elif MRBASE_PICO
        // 构建意图是 PICO,但 com.unity.xr.picoxr 不在工程里。两条 define 轴是独立的,
        // 分开报错才能一眼看出是"包没装"而不是"平台没配"。
        Debug.LogError("[MarkerTrackingBootstrapper] 构建意图为 PICO,但未安装 com.unity.xr.picoxr。");
        throw new System.PlatformNotSupportedException("缺少 PICO Integration SDK(com.unity.xr.picoxr)");
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
