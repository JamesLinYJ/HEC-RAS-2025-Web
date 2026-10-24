using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Utility.DataStructures;

namespace Geospatial.Vectors;

public class BandedTin : IMultiBandResampler<float>, ISurface<float>
{
	private readonly Tin _tin;

	private readonly IList<Any<float[]>> _bands;

	public int ProfileCount => _bands.Count;

	public Extent ValidExtent => _tin.Extent;

	public float? NoDataValue => -9999f;

	public BandedTin(Tin tin, IList<Any<float[]>> bands)
	{
		_tin = tin ?? throw new ArgumentNullException("tin");
		_bands = bands ?? throw new ArgumentNullException("bands");
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<float> outData, ResampleCache cache = null)
	{
		args.EnsureBuffer(outData, NoDataValue);
		float[] zValues = _bands[bandIndex].Value;
		_tin.Rasterize(args.Raster, (int px, int triIdx, BarycentricWeights bary) =>
		{
			Span<float> outDataSpan = outData.Span;
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[triIdx];
			outDataSpan[px] = bary.Interpolate(zValues[triangleDefinition.p0], zValues[triangleDefinition.p1], zValues[triangleDefinition.p2]);
		});
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<float>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<float>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<float> outData)
	{
		throw new NotImplementedException();
	}
}
