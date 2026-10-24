using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class PrioritizedResampleCollection : IResample<float>, ISurface<float>, ISpatialVersion
{
	private long _version = 1L;

	public List<IResample<float>> ResampleCollection { get; } = new List<IResample<float>>();

	public float? NoDataValue => -9999f;

	public Extent ValidExtent
	{
		get
		{
			if (ResampleCollection.Count == 0)
			{
				return Extent.NullExtent;
			}
			return Extent.Union(ResampleCollection.Select((IResample<float> ir) => ir.ValidExtent));
		}
	}

	public long VersionXY => _version;

	public void FreezeOne()
	{
		throw new NotImplementedException();
	}

	public void ThawOne()
	{
		throw new NotImplementedException();
	}

	public PrioritizedResampleCollection(params IResample<float>[] rasters)
	{
		foreach (IResample<float> raster in rasters)
		{
			Add(raster);
		}
	}

	public PrioritizedResampleCollection(IEnumerable<IResample<float>> rasters)
	{
		foreach (IResample<float> raster in rasters)
		{
			Add(raster);
		}
	}

	public void Add(IResample<float> raster)
	{
		ResampleCollection.Add(raster);
		Interlocked.Increment(ref _version);
	}

	public void Remove(IResample<float> raster)
	{
		ResampleCollection.Remove(raster);
		Interlocked.Increment(ref _version);
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		if (ResampleCollection.Count != 0)
		{
			ResampleArgs args2 = args.CloneWithoutBufferClear();
			for (int i = 0; i < ResampleCollection.Count; i++)
			{
				ResampleCollection[i].Resample(args2, outData);
			}
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		throw new NotImplementedException();
	}
}
