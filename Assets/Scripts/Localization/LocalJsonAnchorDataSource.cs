using System.Collections.Generic;
using System.Threading.Tasks;

[System.Serializable]
public class AnchorEntityDataListWrapper
{
    public List<AnchorEntityData> Anchors;
}

public class LocalJsonAnchorDataSource : IAnchorDataSource
{
    private readonly string relativeJsonPath;

    public LocalJsonAnchorDataSource(string relativeJsonPath)
    {
        this.relativeJsonPath = relativeJsonPath;
    }

    public async Task<List<AnchorEntityData>> FetchAsync()
    {
        var wrapper = await FileUtils.Instance.LoadJsonByUrlAsync<AnchorEntityDataListWrapper>(relativeJsonPath);
        return wrapper?.Anchors ?? new List<AnchorEntityData>();
    }
}
