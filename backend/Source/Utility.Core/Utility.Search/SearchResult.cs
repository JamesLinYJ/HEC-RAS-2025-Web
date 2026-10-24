namespace Utility.Search;

public class SearchResult<T> : ISearchResult
{
	public SearchItem<T> Item { get; set; }

	public float Score { get; set; }

	ISearchItem ISearchResult.Item => Item;
}
