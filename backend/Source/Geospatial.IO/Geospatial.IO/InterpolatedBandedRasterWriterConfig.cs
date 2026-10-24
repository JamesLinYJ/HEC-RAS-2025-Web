using System.Numerics;
using Geospatial.Transformers;

namespace Geospatial.IO;

public class InterpolatedBandedRasterWriterConfig<T> : TypedBandedRasterWriterConfig<T> where T : unmanaged, IBinaryFloatingPointIeee754<T>
{
	public T? ConvertScaleFactor;

	public RoundBase? RoundingBase;

	public int RoundingDigits;

	public new static InterpolatedBandedRasterWriterConfig<T> Default()
	{
		return new InterpolatedBandedRasterWriterConfig<T>();
	}
}
