namespace Utility.Search;

public interface ISearchResult
{
	ISearchItem Item { get; }

	float Score { get; set; }
}
