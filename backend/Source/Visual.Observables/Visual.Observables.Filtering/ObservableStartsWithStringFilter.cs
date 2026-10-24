using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.Extensions;
using Utility.Filtering;

namespace Visual.Observables.Filtering;

public class ObservableStartsWithStringFilter<T> : ObservableObject, IStringFilter<T>, IFilter<T>, IFilter, IStringFilter
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

	public ObservableStartsWithStringFilter(Func<T, string> getStringForObj)
	{
		_getStringForObj = getStringForObj;
	}

	public IEnumerable<T> Filter(IEnumerable<T> source)
	{
		if (string.IsNullOrEmpty(SearchText))
		{
			return source;
		}
		return source.Where((T localValue) => _getStringForObj(localValue).RoughlyStartsWith(SearchText));
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
