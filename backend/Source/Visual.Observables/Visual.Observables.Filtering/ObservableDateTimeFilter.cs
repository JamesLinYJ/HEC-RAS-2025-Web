using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.Filtering;

namespace Visual.Observables.Filtering;

public class ObservableDateTimeFilter<T> : ObservableObject, IFilter<T>, IFilter
{
	private Func<T, DateTime> _getDateTimeForObj;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime SearchDate
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<DateTime>.Default.Equals(field, value))
			{
				OnPropertyChanging("SearchDate");
				field = value;
				OnSearchDateChanged(value);
				OnPropertyChanged("SearchDate");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public InequalityCheck SearchCriteria
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<InequalityCheck>.Default.Equals(field, value))
			{
				OnPropertyChanging("SearchCriteria");
				field = value;
				OnSearchCriteriaChanged(value);
				OnPropertyChanged("SearchCriteria");
			}
		}
	} = InequalityCheck.GreaterThanOrEqual;

	public event IFilter.FilterStatusChangedEvent FilterStatusChanged;

	public ObservableDateTimeFilter(Func<T, DateTime> getDateTimeForObj)
	{
		_getDateTimeForObj = getDateTimeForObj;
	}

	public IEnumerable<T> Filter(IEnumerable<T> source)
	{
		return source.Where((T localValue) =>
		{
			DateTime dateTime = _getDateTimeForObj(localValue);
			return SearchCriteria switch
			{
				InequalityCheck.GreaterThan => dateTime > SearchDate, 
				InequalityCheck.GreaterThanOrEqual => dateTime >= SearchDate, 
				InequalityCheck.LessThan => dateTime < SearchDate, 
				InequalityCheck.LessThanOrEqual => dateTime <= SearchDate, 
				InequalityCheck.Equal => dateTime.Date == SearchDate.Date, 
				_ => throw new NotImplementedException("Inequality not expected."), 
			};
		});
	}

	public void Reset()
	{
		SearchDate = default;
		SearchCriteria = InequalityCheck.GreaterThanOrEqual;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSearchDateChanged(DateTime dateTime)
	{
		FilterStatusChanged?.Invoke(this);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSearchCriteriaChanged(InequalityCheck inequalityCheck)
	{
		FilterStatusChanged?.Invoke(this);
	}
}
