using System.Collections.Generic;
using System.Threading.Tasks;

public class AnchorRegistry
{
    private List<AnchorEntityData> entities = new List<AnchorEntityData>();

    public async Task LoadAsync(IAnchorDataSource dataSource)
    {
        entities = await dataSource.FetchAsync() ?? new List<AnchorEntityData>();
    }

    public bool TryResolve(string rawId, out AnchorEntityData data)
    {
        foreach (var entity in entities)
        {
            if (entity == null) continue;
            if (entity.QuestPayload == rawId || entity.PicoMarkerId.ToString() == rawId)
            {
                data = entity;
                return true;
            }
        }

        data = null;
        return false;
    }

    public void SetEntitiesForTesting(List<AnchorEntityData> list) => entities = list;
}
