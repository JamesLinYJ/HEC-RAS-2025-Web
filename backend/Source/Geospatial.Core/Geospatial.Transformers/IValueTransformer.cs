using System;

namespace Geospatial.Transformers;

public interface IValueTransformer<T> where T : unmanaged
{
	void Transform(Memory<T> data, T? noDataValue);
}
