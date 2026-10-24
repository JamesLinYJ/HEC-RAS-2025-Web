using System.Collections.Generic;
using System.Threading.Tasks;
using Utility.Search;

public interface ISearchEngine
{
	ValueTask<IEnumerable<ISearchResult>> Search(string searchTerm);
}
