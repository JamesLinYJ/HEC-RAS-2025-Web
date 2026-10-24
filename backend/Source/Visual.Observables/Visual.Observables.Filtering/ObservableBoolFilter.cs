using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.Filtering;

namespace Visual.Observables.Filtering;

public class ObservableBoolFilter<T> : ObservableObject, IFilter<T>, IFilter
{
	private Func<T, bool> _getValueForObj;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public BoolSearchCriteria SearchCriteria
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<BoolSearchCriteria>.Default.Equals(field, value))
			{
				OnPropertyChanging("SearchCriteria");
				field = value;
				OnSearchCriteriaChanged(value);
				OnPropertyChanged("SearchCriteria");
			}
		}
	} = BoolSearchCriteria.Both;

	public event IFilter.FilterStatusChangedEvent FilterStatusChanged;

	public ObservableBoolFilter(Func<T, bool> getValueForObj)
	{
		_getValueForObj = getValueForObj;
	}

	public IEnumerable<T> Filter(IEnumerable<T> source)
	{
		if (SearchCriteria == BoolSearchCriteria.Both)
		{
			return source;
		}
		return source.Where((T localValue) => (SearchCriteria == BoolSearchCriteria.True) ? _getValueForObj(localValue) : (!_getValueForObj(localValue)));
	}

	public void Reset()
	{
		SearchCriteria = BoolSearchCriteria.Both;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSearchCriteriaChanged(BoolSearchCriteria boolSearchCriteria)
	{
		FilterStatusChanged?.Invoke(this);
	}
}
