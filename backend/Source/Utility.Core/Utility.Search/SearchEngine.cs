using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Utility.Search;

public class SearchEngine<T> : ISearchEngine
{
	private List<SearchItem<T>> _searchItems;

	private Dictionary<string, HashSet<string>> _partialStrings;

	private Dictionary<string, HashSet<SearchItem<T>>> _fullStringMap;

	public static float TitleImportanceRatio { get; set; } = 3f;

	public SearchEngine()
	{
		_searchItems = new List<SearchItem<T>>();
	}

	public void Add(string title, string subhead, T payload)
	{
		SearchItem<T> searchItem = new SearchItem<T>();
		searchItem.Title = title;
		searchItem.Subheading = subhead;
		searchItem.Payload = payload;
		Add(searchItem);
	}

	public void Add(SearchItem<T> searchItem)
	{
		_searchItems.Add(searchItem);
	}

	public virtual void Initialize()
	{
	}

	public ValueTask<IEnumerable<ISearchResult>> Search(string searchTerm)
	{
		List<ISearchResult> iSearchResults = new List<ISearchResult>();
		if (string.IsNullOrEmpty(searchTerm))
		{
			return ValueTask.FromResult((IEnumerable<ISearchResult>)iSearchResults);
		}
		HashSet<string> tokenize = SearchEngineHelpers.Tokenize(searchTerm);
		for (int i = 0; i < _searchItems.Count; i++)
		{
			SearchItem<T> searchItem = _searchItems[i];
			Func<bool> makesSense = searchItem.MakesSense;
			if (makesSense != null && !makesSense())
			{
				continue;
			}
			float score = 0f;
			foreach (string tokenize2 in tokenize)
			{
				score += SearchEngineHelpers.GetScore(tokenize2, searchItem.Title) * TitleImportanceRatio;
				score += SearchEngineHelpers.GetScore(tokenize2, searchItem.Subheading);
			}
			if (score != 0f)
			{
				SearchResult<T> searchResult = new SearchResult<T>();
				searchResult.Item = searchItem;
				searchResult.Score = score;
				iSearchResults.Add(searchResult);
			}
		}
		return ValueTask.FromResult(iSearchResults.OrderByDescending((ISearchResult a) => a.Score).AsEnumerable());
	}

	public void Reindex()
	{
		_partialStrings = new Dictionary<string, HashSet<string>>(SearchEngineHelpers.DefaultStringComparer);
		_fullStringMap = new Dictionary<string, HashSet<SearchItem<T>>>(SearchEngineHelpers.DefaultStringComparer);
		for (int i = 0; i < _searchItems.Count; i++)
		{
			SearchItem<T> searchItem = _searchItems[i];
			AddWholeWords(searchItem, searchItem.Title);
			AddWholeWords(searchItem, searchItem.Subheading);
		}
		foreach (KeyValuePair<string, HashSet<SearchItem<T>>> fullStringMap in _fullStringMap)
		{
			string key = fullStringMap.Key;
			for (int j = 1; j < key.Length - 1; j++)
			{
				string key2 = key.Substring(0, j);
				if (_partialStrings.TryGetValue(key2, out var partialStrings))
				{
					partialStrings.Add(key);
					continue;
				}
				partialStrings = new HashSet<string>(SearchEngineHelpers.DefaultStringComparer);
				_partialStrings[key2] = partialStrings;
				partialStrings.Add(key);
			}
		}
		void AddWholeWords(SearchItem<T> searchItem2, ReadOnlySpan<char> localValue)
		{
			int unity = -1;
			for (int k = 0; k < localValue.Length; k++)
			{
				if (SearchEngineHelpers.IsWordBreak(localValue[k]))
				{
					if (unity != -1)
					{
						string slice = localValue.Slice(unity, k - unity).ToString();
						unity = -1;
						if (!SearchEngineHelpers.ShouldIgnore(slice))
						{
							if (_fullStringMap.TryGetValue(slice, out var fullStringMap2))
							{
								fullStringMap2.Add(searchItem2);
							}
							else
							{
								fullStringMap2 = new HashSet<SearchItem<T>> { searchItem2 };
								_fullStringMap[slice] = fullStringMap2;
							}
						}
					}
				}
				else if (unity == -1)
				{
					unity = k;
				}
			}
		}
	}
}
