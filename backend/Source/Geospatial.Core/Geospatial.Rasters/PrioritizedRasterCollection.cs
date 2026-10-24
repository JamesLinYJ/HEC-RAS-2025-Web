using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class PrioritizedRasterCollection : IPrioritizedRasterCollection<float>, IResample<float>, ISurface<float>
{
	public List<IRaster<float>> RasterCollection { get; }

	public float? NoDataValue => -9999f;

	public Extent ValidExtent
	{
		get
		{
			if (RasterCollection.Count == 0)
			{
				return Extent.NullExtent;
			}
			return Extent.Union(RasterCollection.Select((IRaster<float> ir) => ir.RasterDefinition.Extent));
		}
	}

	public PrioritizedRasterCollection()
	{
		RasterCollection = new List<IRaster<float>>();
	}

	public PrioritizedRasterCollection(params IRaster<float>[] rasters)
		: this()
	{
		foreach (IRaster<float> raster in rasters)
		{
			Add(raster);
		}
	}

	public PrioritizedRasterCollection(IEnumerable<IRaster<float>> rasters)
		: this()
	{
		foreach (IRaster<float> raster in rasters)
		{
			Add(raster);
		}
	}

	public void Add(IRaster<float> raster)
	{
		RasterCollection.Add(raster);
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		if (RasterCollection.Count != 0)
		{
			ResampleArgs args2 = args.CloneWithoutBufferClear();
			for (int i = 0; i < RasterCollection.Count; i++)
			{
				RasterCollection[i].Resample(args2, outData);
			}
		}
	}

	public Histogram ComputeHistogramFromPyramidLevel(int pyramidLevel, int binCt)
	{
		List<Histogram> histograms = new List<Histogram>();
		foreach (IRaster<float> rasterCollection in RasterCollection)
		{
			double cellSize = rasterCollection.RasterDefinition.CellSize * Math.Pow(2.0, pyramidLevel);
			histograms.Add(Histogram.ComputeFrom(rasterCollection, cellSize, binCt));
		}
		return Histogram.MergeHistograms(histograms);
	}

	public Histogram ComputeHistogram(double targetCellSize, int binCt)
	{
		List<Histogram> histograms = new List<Histogram>();
		foreach (IRaster<float> rasterCollection in RasterCollection)
		{
			histograms.Add(Histogram.ComputeFrom(rasterCollection, targetCellSize, binCt));
		}
		return Histogram.MergeHistograms(histograms);
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		throw new NotImplementedException();
	}
}
