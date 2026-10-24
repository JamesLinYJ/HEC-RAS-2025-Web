using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Filtering;

public interface IFilter
{
	public delegate void FilterStatusChangedEvent(IFilter sender);

	event FilterStatusChangedEvent FilterStatusChanged;

	IEnumerable Filter(IEnumerable source);

	void Reset();
}
public interface IFilter<T> : IFilter
{
	IEnumerable<T> Filter(IEnumerable<T> source);

	IEnumerable IFilter.Filter(IEnumerable source)
	{
		return Filter(source.Cast<T>());
	}
}
