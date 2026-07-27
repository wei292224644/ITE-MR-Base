using System.Collections.Generic;
using System.Threading.Tasks;

public interface IAnchorDataSource
{
    Task<List<AnchorEntityData>> FetchAsync();
}
