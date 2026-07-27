using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class MarkerAnchorService : MonoBehaviour
{
    [SerializeField] private PlatformOffsetConfig offsetConfig;

    private AnchorRegistry registry;
    private IContentLoader contentLoader;
    private IMarkerTrackingProvider provider;
    private MarkerStabilizer stabilizer;
    private readonly HashSet<string> activeAnchorIds = new HashSet<string>();

    public void SetDependenciesForTesting(AnchorRegistry registryOverride, PlatformOffsetConfig config, IContentLoader loader, MarkerStabilizer stabilizerOverride)
    {
        registry = registryOverride;
        offsetConfig = config;
        contentLoader = loader;
        stabilizer = stabilizerOverride;
        stabilizer.Stabilized += HandleStabilized;
    }

    public void BindProviderForTesting(IMarkerTrackingProvider trackingProvider)
    {
        provider = trackingProvider;
        provider.MarkerResolved += HandleMarkerResolved;
        provider.StartTracking();
    }

    public async Task Initialize(IMarkerTrackingProvider trackingProvider, IAnchorDataSource dataSource)
    {
        registry = new AnchorRegistry();
        await registry.LoadAsync(dataSource);

        contentLoader = new ImageContentLoader();
        stabilizer = new MarkerStabilizer();
        stabilizer.Stabilized += HandleStabilized;

        BindProviderForTesting(trackingProvider);
    }

    private void HandleMarkerResolved(string rawId, Pose rawPose)
    {
        stabilizer.Feed(rawId, rawPose, Time.deltaTime);
    }

    private void HandleStabilized(string rawId, Pose stablePose)
    {
        if (activeAnchorIds.Contains(rawId))
        {
            return;
        }

        if (!registry.TryResolve(rawId, out var data))
        {
            Debug.LogWarning($"[MarkerAnchorService] 识别到未匹配的标记ID: {rawId}");
            return;
        }

        Pose entityPose = PoseMath.Compose(stablePose, ResolvePlatformOffset());

        var entityObject = new GameObject($"AnchorEntity_{data.AnchorId}");
        entityObject.transform.SetPositionAndRotation(entityPose.position, entityPose.rotation);
        var entity = entityObject.AddComponent<AnchorEntity>();
        _ = entity.Create(data, contentLoader);

        activeAnchorIds.Add(rawId);
    }

    private Pose ResolvePlatformOffset()
    {
#if MRBASE_QUEST
        return offsetConfig.questMarkerToTargetOffset;
#elif MRBASE_PICO
        return offsetConfig.picoMarkerToTargetOffset;
#else
        return Pose.identity;
#endif
    }
}
