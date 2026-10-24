using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.Filtering;
using Utility.Search;

namespace Visual.Observables.Filtering;

public class ObservableStringFilter<T> : ObservableObject, IStringFilter<T>, IFilter<T>, IFilter, IStringFilter
{
	private Func<T, string> _getStringForObj;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SearchText
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				string oldValue = field;
				OnPropertyChanging("SearchText");
				field = value;
				OnSearchTextChanged(oldValue, value);
				OnPropertyChanged("SearchText");
			}
		}
	}

	public event IFilter.FilterStatusChangedEvent FilterStatusChanged;

	public ObservableStringFilter(Func<T, string> getStringForObj)
	{
		_getStringForObj = getStringForObj;
	}

	public IEnumerable<T> Filter(IEnumerable<T> source)
	{
		if (string.IsNullOrEmpty(SearchText))
		{
			return source;
		}
		HashSet<string> tokens = GetTokens();
		return from item in source
			where Filter(item, tokens)
			orderby CalculateScore(_getStringForObj(item), tokens) descending
			select item;
	}

	public bool Filter(T localValue, HashSet<string> tokensCache = null)
	{
		return Filter(_getStringForObj(localValue));
	}

	public bool Filter(string itemStr, HashSet<string> tokensCache = null)
	{
		if (string.IsNullOrEmpty(SearchText))
		{
			return true;
		}
		tokensCache = GetTokens();
		return CalculateScore(itemStr, tokensCache) != 0f;
	}

	public HashSet<string> GetTokens()
	{
		return SearchEngineHelpers.Tokenize(SearchText);
	}

	public static float CalculateScore(string stringValue, HashSet<string> tokens)
	{
		float scoreSum = 0f;
		foreach (string token in tokens)
		{
			scoreSum += SearchEngineHelpers.GetScore(token, stringValue);
		}
		return scoreSum;
	}

	public void Reset()
	{
		SearchText = string.Empty;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSearchTextChanged(string oldValue, string newValue)
	{
		FilterStatusChanged?.Invoke(this);
	}
}
