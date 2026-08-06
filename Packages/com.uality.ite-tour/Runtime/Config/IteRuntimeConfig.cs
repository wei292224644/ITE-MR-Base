using UnityEngine;

namespace Uality.IteTour.Config
{
    /// <summary>
    /// ITE 运行时配置。资产实例放在宿主 <c>Assets/</c>，prefab 默认指向包内
    /// <c>Runtime/Prefabs/</c>，宿主可替换。
    ///
    /// 源工程里这些散在 <c>MainConstants</c> 与 <c>SettingsManager.Gameplay</c> 两个宿主单例上，
    /// 版本查询地址还硬编码在 <c>IteSpaceManagerAssets</c> 里。
    ///
    /// 不含 <c>ItePropertiesUrl</c>（`projectDirectory.json`）：它的唯一消费方是宿主的
    /// 空间场景选择下拉框，挑哪个场景属宿主 UX，包的职责从「加载这个 sceneName」开始。
    /// 宿主要拉那份目录时可直接复用包内的 <see cref="Data.IteSpaces"/> 模型。
    /// </summary>
    [CreateAssetMenu(fileName = "IteRuntimeConfig", menuName = "ITE Tour/Runtime Config")]
    public class IteRuntimeConfig : ScriptableObject
    {
        [Header("内容服务")]
        [SerializeField]
        [Tooltip("空间场景包的根地址；实际请求为 {base}/{sceneName}.zip")]
        private string spaceSceneBaseUrl = "https://ite-spatial-config.uality.cn";

        [SerializeField]
        [Tooltip("Tour 内容包地址模板，{0} 为 tourId")]
        private string tourPackageUrlTemplate = "https://ite-pkg.uality.cn/{0}/{0}_wx.zip";

        [SerializeField]
        [Tooltip("Tour 最新版本查询地址模板，{0} 为 tourId。源工程中这条是硬编码在代码里的")]
        private string tourLatestVersionUrlTemplate = "https://api.uality.cn/ITE/Tour/LatestVersion?tourId={0}";

        [Header("加载目标")]
        [SerializeField]
        [Tooltip("要加载的空间场景名，对应 {base}/{sceneName}.zip")]
        private string sceneName = "";

        [Header("Prefab")]
        [SerializeField]
        [Tooltip("Tour 载体预制体。元素预制体挂在它自己身上，不在这里配——见 design D20")]
        private GameObject tourObjectPrefab;

        public string SpaceSceneBaseUrl => spaceSceneBaseUrl;
        public string SceneName => sceneName;

        public GameObject TourObjectPrefab => tourObjectPrefab;

        public string BuildSpaceSceneUrl(string scene) => $"{spaceSceneBaseUrl}/{scene}.zip";

        public string BuildTourPackageUrl(string tourId) => string.Format(tourPackageUrlTemplate, tourId);

        public string BuildTourLatestVersionUrl(string tourId) => string.Format(tourLatestVersionUrlTemplate, tourId);
    }
}
