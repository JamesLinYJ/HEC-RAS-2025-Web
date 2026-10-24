using System;

namespace Geospatial.IO;

public class TypedBandedRasterWriterConfig<T> : BandedRasterWriterConfig where T : unmanaged, IEquatable<T>
{
	public T? OverrideSourceNoData;

	public T? DestinationNoData;

	public new static TypedBandedRasterWriterConfig<T> Default()
	{
		TypedBandedRasterWriterConfig<T> typedBandedRasterWriterConfig = new TypedBandedRasterWriterConfig<T>();
		typedBandedRasterWriterConfig.InitDefault();
		return typedBandedRasterWriterConfig;
	}

	public static TypedBandedRasterWriterConfig<T> Default(T dstNoData)
	{
		TypedBandedRasterWriterConfig<T> typedBandedRasterWriterConfig = Default();
		typedBandedRasterWriterConfig.DestinationNoData = dstNoData;
		return typedBandedRasterWriterConfig;
	}

	protected override void InitDefault()
	{
		base.InitDefault();
		OverrideSourceNoData = null;
		DestinationNoData = null;
	}
}
