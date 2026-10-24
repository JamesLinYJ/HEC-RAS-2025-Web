using System.Collections;
using System.Collections.Generic;

namespace RasMapperLib;

public class PlotOptionCollection : IEnumerable<PlotOption>
{
	private HashSet<PlotOption> _options;

	public bool this[string name]
	{
		get
		{
			return Contains(name);
		}
		set
		{
			if (value)
			{
				Add(name);
			}
			else
			{
				Remove(name);
			}
		}
	}

	public PlotOptionCollection()
	{
		_options = new HashSet<PlotOption>();
	}

	public void Add(PlotOption po)
	{
		_options.Add(po);
	}

	public void Add(string po)
	{
		_options.Add(new PlotOption(po));
	}

	public bool Contains(PlotOption po)
	{
		return _options.Contains(po);
	}

	public bool Contains(string po)
	{
		return _options.Contains(new PlotOption(po));
	}

	public void Remove(string po)
	{
		_options.Remove(new PlotOption(po));
	}

	public void Remove(PlotOption po)
	{
		_options.Remove(po);
	}

	public IEnumerator<PlotOption> GetEnumerator()
	{
		foreach (PlotOption option in _options)
		{
			yield return option;
		}
	}

	IEnumerator<PlotOption> IEnumerable<PlotOption>.GetEnumerator()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetEnumerator
		return this.GetEnumerator();
	}

	private IEnumerator IEnumerable_GetEnumerator()
	{
		return GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		//ILSpy generated this explicit interface implementation from .override directive in IEnumerable_GetEnumerator
		return this.IEnumerable_GetEnumerator();
	}
}
