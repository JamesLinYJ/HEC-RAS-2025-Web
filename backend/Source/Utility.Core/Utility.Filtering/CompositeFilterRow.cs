namespace Utility.Filtering;

public class CompositeFilterRow : IFilterRow
{
	private readonly IFilterRow[] _sources;

	public CompositeFilterRow(params IFilterRow[] sources)
	{
		_sources = sources;
	}

	public bool TryGetValue(string normalizedName, out object localValue)
	{
		IFilterRow[] sources = _sources;
		for (int i = 0; i < sources.Length; i++)
		{
			if (sources[i].TryGetValue(normalizedName, out localValue))
			{
				return true;
			}
		}
		localValue = null;
		return false;
	}
}
