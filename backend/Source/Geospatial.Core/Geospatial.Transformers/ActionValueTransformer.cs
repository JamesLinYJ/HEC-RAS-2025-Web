using System;

namespace Geospatial.Transformers;

public class ActionValueTransformer<T> : IValueTransformer<T> where T : unmanaged
{
	private readonly Action<Memory<T>, T?> _transformer;

	public ActionValueTransformer(Action<Memory<T>, T?> transformer)
	{
		_transformer = transformer;
	}

	public void Transform(Memory<T> data, T? noDataValue)
	{
		_transformer(data, noDataValue);
	}
}
