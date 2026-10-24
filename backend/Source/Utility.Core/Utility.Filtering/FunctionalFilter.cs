using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Filtering;

public class FunctionalFilter<T> : IFilter<T>, IFilter
{
	private Func<T, bool> _isValid;

	public event IFilter.FilterStatusChangedEvent FilterStatusChanged;

	public FunctionalFilter(Func<T, bool> isValid)
	{
		_isValid = isValid;
	}

	public IEnumerable<T> Filter(IEnumerable<T> source)
	{
		return source.Where(_isValid);
	}

	public void Reset()
	{
		FilterStatusChanged?.Invoke(this);
	}
}
