using System;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

public class InterpolatedTransformedResampler : IMultiBandResampler<float>, ISurface<float>
{
	private class Interpolator : ISpatialTransformer
	{
		private readonly Point _initCoM;

		private readonly Point _newCoM;

		private readonly Point _newBasis;

		private readonly float _fractionAlong;

		private readonly ISpatialTransformer _baseTransformer;

		private readonly TransformInterpolationScheme _scheme;

		public bool IsReversible => false;

		public Interpolator(ISpatialTransformer baseTransformer, Point initCoM, Point newCoM, float fractionAlong, TransformInterpolationScheme scheme)
		{
			_initCoM = initCoM;
			_newCoM = newCoM;
			_baseTransformer = baseTransformer;
			_fractionAlong = fractionAlong;
			_newBasis = _initCoM.Translate(_initCoM.To(_newCoM).Ratio(fractionAlong));
			_scheme = scheme;
		}

		public void Transform(Memory<Point> points, Memory<Point> transformed)
		{
			Point[] transform = _baseTransformer.Transform(points);
			Span<Point> pointsSpan = points.Span;
			Span<Point> transformedSpan = transformed.Span;
			for (int i = 0; i < points.Length; i++)
			{
				Point other = pointsSpan[i];
				Point other2 = transform[i];
				Vector vector = _initCoM.To(other);
				Vector vec = _newCoM.To(other2);
				Vector vec2;
				if (_scheme == TransformInterpolationScheme.LinearDxDy)
				{
					double deltaXDifference = vec.DeltaX - vector.DeltaX;
					double deltaYDifference = vec.DeltaY - vector.DeltaY;
					vec2 = new Vector(vector.DeltaX + deltaXDifference * (double)_fractionAlong, vector.DeltaY + deltaYDifference * (double)_fractionAlong);
				}
				else
				{
					double length = vector.Length;
					double lengthDifference = vec.Length - length;
					double angleBetweenRads = vector.AngleBetweenRads(in vec);
					if (angleBetweenRads > Math.PI)
					{
						angleBetweenRads -= Math.PI * 2.0;
					}
					double newLength = length + lengthDifference * (double)_fractionAlong;
					double angleRads = angleBetweenRads * (double)_fractionAlong;
					vec2 = vector.Rotate(angleRads).ScaleTo(newLength);
				}
				transformedSpan[i] = _newBasis.Translate(vec2);
			}
		}

		public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
		{
			throw new NotImplementedException();
		}
	}

	private readonly IResample<float> _baseData;

	private readonly ISpatialTransformer _transformer;

	private readonly ISpatialTransformer _reversed;

	private Point _baseCoM;

	private Point _newCoM;

	private TransformInterpolationScheme _scheme;

	public int ProfileCount => 101;

	public Extent ValidExtent => _baseData.ValidExtent;

	public float? NoDataValue => _baseData.NoDataValue;

	public InterpolatedTransformedResampler(IResample<float> baseData, ISpatialTransformer transformer, TransformInterpolationScheme scheme = TransformInterpolationScheme.LinearDxDy)
	{
		_baseData = baseData;
		_transformer = transformer;
		_reversed = transformer.GetInvertedTransformer();
		Init(_baseData.ValidExtent);
		_scheme = scheme;
	}

	private void Init(Extent ext)
	{
		_baseCoM = ext.Center;
		Point[] borderPoints = ext.GetBorderPoints();
		Polygon polygon = Polygon.CreateWithBuffer(_transformer.Transform(borderPoints));
		_newCoM = polygon.ComputeCentroid();
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<float> outData, ResampleCache cache = null)
	{
		args.EnsureBuffer(outData, NoDataValue);
		switch (bandIndex)
		{
		case 0:
			_baseData.Resample(args, outData);
			return;
		case 100:
		{
			if (!args.TryTransform(_reversed, out var warpedArgs))
			{
				return;
			}
			Memory<float> mem;
			using (SharedMemoryPool<float>.Rent(warpedArgs.Raster.CellCount, out mem))
			{
				_baseData.Resample(warpedArgs, mem);
				RasterOperations.Warp(warpedArgs.Raster, mem, args.Raster, outData, _reversed);
				return;
			}
		}
		}
		float fractionAlong = (float)bandIndex / 100f;
		Interpolator interpolator = new Interpolator(_reversed, _baseCoM, _newCoM, fractionAlong, _scheme);
		if (!args.TryTransform(interpolator, out var warpedArgs2))
		{
			return;
		}
		Memory<float> mem2;
		using (SharedMemoryPool<float>.Rent(warpedArgs2.Raster.CellCount, out mem2))
		{
			_baseData.Resample(warpedArgs2, mem2);
			RasterOperations.Warp(warpedArgs2.Raster, mem2, args.Raster, outData, interpolator);
		}
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<float> outData)
	{
		throw new NotImplementedException();
	}
}
