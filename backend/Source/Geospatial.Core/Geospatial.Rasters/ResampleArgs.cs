using System;
using Geospatial.Transformers;
using Utility;
using Utility.Progress;

namespace Geospatial.Rasters;

public class ResampleArgs
{
	public RasterDefinition Raster { get; set; }

	public ProgressReporter Reporter { get; set; } = ProgressReporter.None();

	public SamplingParams SampleParams { get; set; }

	public ResampleArgs(RasterDefinition queryRaster)
		: this(queryRaster, SamplingParams.Default)
	{
	}

	public ResampleArgs(RasterDefinition queryRaster, SamplingParams sampleParams)
	{
		Raster = queryRaster;
		SampleParams = sampleParams;
	}

	public ResampleArgs(RasterDefinition queryRaster, InterpolationMode interpMode)
		: this(queryRaster, new SamplingParams(interpMode, initBufferToNDV: true))
	{
	}

	public void EnsureBuffer<T>(Memory<T> outData, T? noData) where T : unmanaged
	{
		ThrowIfBadSize(outData);
		if (SampleParams.InitializeBufferToNoData && noData.HasValue)
		{
			outData.Span.Fill(noData.Value);
		}
	}

	public void ThrowIfBadSize<T>(Memory<T> data)
	{
		if (data.Length != Raster.CellCount)
		{
			throw new ArgumentException("Given buffer does not match the size of the requested raster.");
		}
	}

	public void EnsureTimeSeriesBuffer<T>(ref T[,] outData, int profileCount, T noData) where T : struct
	{
		Buffers.EnsureArraySize(ref outData, profileCount, Raster.CellCount);
		if (SampleParams.InitializeBufferToNoData)
		{
			Buffers.ArraySetAll(outData, noData);
		}
	}

	public ResampleArgs Clone()
	{
		return new ResampleArgs(Raster, SampleParams)
		{
			Reporter = Reporter
		};
	}

	public ResampleArgs Clone(RasterDefinition def)
	{
		return new ResampleArgs(def, SampleParams)
		{
			Reporter = Reporter
		};
	}

	public ResampleArgs CloneWith(SamplingParams sampleParams)
	{
		return new ResampleArgs(Raster, sampleParams)
		{
			Reporter = Reporter
		};
	}

	public ResampleArgs CloneWith(InterpolationMode mode)
	{
		return new ResampleArgs(Raster, new SamplingParams(mode, SampleParams.InitializeBufferToNoData, SampleParams.ResampleCellSizeRatio))
		{
			Reporter = Reporter
		};
	}

	public ResampleArgs CloneWithoutBufferClear()
	{
		return new ResampleArgs(Raster, SampleParams.CloneWithoutBufferClear())
		{
			Reporter = Reporter
		};
	}

	public ResampleArgs Transform(ISpatialTransformer transformer)
	{
		ResampleArgs resampleArgs = Clone();
		if (Raster.IsNull())
		{
			resampleArgs.Raster = Raster;
			return resampleArgs;
		}
		resampleArgs.Raster = Raster.Transform(transformer);
		return resampleArgs;
	}

	public bool TryTransform(ISpatialTransformer transformer, out ResampleArgs warpedArgs)
	{
		warpedArgs = Clone();
		if (Raster.TryTransform(transformer, out var res))
		{
			warpedArgs.Raster = res;
			return true;
		}
		return false;
	}
}
