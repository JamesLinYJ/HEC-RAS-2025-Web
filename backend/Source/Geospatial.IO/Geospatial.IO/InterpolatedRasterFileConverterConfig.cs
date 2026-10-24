using System.Numerics;
using Geospatial.Rasters;
using Geospatial.Transformers;

namespace Geospatial.IO;

public class InterpolatedRasterFileConverterConfig<T> : RasterFileConverterConfig where T : unmanaged, IBinaryFloatingPointIeee754<T>
{
	public T? OverrideSourceNoData;

	public T? DestinationNoData;

	public T? ConvertScaleFactor;

	public RoundBase? RoundingBase;

	public int RoundingDigits;

	public new static InterpolatedRasterFileConverterConfig<T> Default()
	{
		return new InterpolatedRasterFileConverterConfig<T>
		{
			GenerateOverviews = true,
			DestinationNoData = DefaultNoDataValue()
		};
	}

	protected InterpolatedRasterFileConverterConfig()
	{
	}

	public static T DefaultNoDataValue()
	{
		return T.CreateChecked(-9999f);
	}

	public void UseRounding(RoundTo? rounding)
	{
		if (!rounding.HasValue || rounding == RoundTo.None)
		{
			RoundingBase = null;
			RoundingDigits = 0;
			return;
		}
		(RoundBase, int) tuple = RoundingConverters.Convert(rounding.Value);
		RoundBase roundingBase = tuple.Item1;
		int roundingDigits = tuple.Item2;
		RoundingBase = roundingBase;
		RoundingDigits = roundingDigits;
	}
}
