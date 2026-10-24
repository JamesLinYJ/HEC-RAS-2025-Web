using System;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class LazyLabelDefinition<T> : LabelDefinitionBase
{
	private string _cachedStr = string.Empty;

	private Func<T, string> _getString;

	private T _val;

	public override string Text
	{
		get
		{
			if (_cachedStr == string.Empty)
			{
				_cachedStr = _getString(_val);
			}
			return _cachedStr;
		}
	}

	public LazyLabelDefinition(Func<T, string> getString, T localValue, Point pt)
	{
		_getString = getString;
		_val = localValue;
		PointLocation = pt;
	}
}
