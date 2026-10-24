using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.DataStructures;
using Utility.Filtering;

namespace Visual.Observables.Filtering;

public class ObservableEnumFilter<T, TEnum> : ObservableObject, IFilter<T>, IFilter where TEnum : Enum
{
	private readonly Func<T, TEnum> _getEnumForObj;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<TEnum> SearchEnums
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<AdvancedCollection<TEnum>>.Default.Equals(field, value))
			{
				OnPropertyChanging("SearchEnums");
				field = value;
				OnPropertyChanged("SearchEnums");
			}
		}
	} = new AdvancedCollection<TEnum>();

	public event IFilter.FilterStatusChangedEvent FilterStatusChanged;

	public ObservableEnumFilter(Func<T, TEnum> getEnumForObj)
	{
		_getEnumForObj = getEnumForObj;
		SearchEnums.CollectionChanged += (object? s, NotifyCollectionChangedEventArgs e) =>
		{
			FilterStatusChanged?.Invoke(this);
		};
	}

	public IEnumerable<T> Filter(IEnumerable<T> source)
	{
		if (SearchEnums.Count() == 0)
		{
			return source;
		}
		return source.Where((T localValue) => SearchEnums.Contains(_getEnumForObj(localValue)));
	}

	public void Reset()
	{
		SearchEnums.Clear();
	}
}
