using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class HybridSurface<T> : IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private readonly List<IResample<T>> _surfaces;

	public List<IResample<T>> Surfaces => _surfaces;

	public Extent ValidExtent => Extent.Union(Surfaces.Select((IResample<T> s) => s.ValidExtent));

	public T? NoDataValue { get; }

	public HybridSurface(T? noDataValue)
	{
		NoDataValue = noDataValue;
		_surfaces = new List<IResample<T>>();
	}

	public HybridSurface(IList<IResample<T>> surfaces)
	{
		NoDataValue = surfaces.First().NoDataValue;
		_surfaces = surfaces.ToList();
	}

	public void Add(IResample<T> surface)
	{
		_surfaces.Add(surface);
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		args = args.CloneWithoutBufferClear();
		for (int i = 0; i < _surfaces.Count; i++)
		{
			_surfaces[i].Resample(args, outData);
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		throw new NotImplementedException();
	}
}
