using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Geospatial.Rasters;
using Geospatial.Rendering.Recolor;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class ImageRenderLayer : RenderLayer
{
	private readonly IMultiBandResampler<byte> _raster;

	private readonly byte?[] _rgbaNoDataValues;

	private long _version = 1L;

	private byte _opacity = byte.MaxValue;

	public override Extent ValidExtent { get; }

	public long Version => _version;

	public byte Opacity
	{
		get
		{
			return _opacity;
		}
		set
		{
			_opacity = value;
			Interlocked.Increment(ref _version);
		}
	}

	public IRecolorer Recolorer { get; set; }

	public ImageRenderLayer(IMultiBandResampler<byte> raster)
		: this(raster, null)
	{
	}

	public ImageRenderLayer(IMultiBandResampler<byte> raster, IReadOnlyList<byte?> rgbaNoDataValues)
	{
		if (rgbaNoDataValues != null)
		{
			if (raster.ProfileCount != 4 || rgbaNoDataValues.Count != 4)
			{
				throw new ArgumentException("RGBA NoData metadata requires exactly four bands.", nameof(rgbaNoDataValues));
			}
			_rgbaNoDataValues = new byte?[4];
			for (int band = 0; band < _rgbaNoDataValues.Length; band++)
			{
				_rgbaNoDataValues[band] = rgbaNoDataValues[band];
			}
		}
		ValidExtent = raster.ValidExtent;
		_raster = raster;
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		if (!request.RasterQuery.Extent.Overlaps(ValidExtent))
		{
			return false;
		}
		if (_raster.ProfileCount == 1)
		{
			RenderSingleBandInternal(request);
			return true;
		}
		if (_raster.ProfileCount == 3)
		{
			Render3BandDataInternal(request);
			return true;
		}
		if (_raster.ProfileCount == 4)
		{
			Render4BandDataInternal(request);
			return true;
		}
		return false;
	}

	private void RenderSingleBandInternal(PlotRequest request)
	{
		ResampleArgs args = new ResampleArgs(request.RasterQuery);
		RasterDefinition rasterQuery = request.RasterQuery;
		int cellCount = rasterQuery.CellCount;
		PixelRange pixelRange = rasterQuery.GetPixelRange(ValidExtent).ClipTo(rasterQuery);
		Memory<byte> mem;
		using (SharedMemoryPool<byte>.Rent(cellCount, out mem))
		{
			_raster.Resample(args, 0, mem);
			Memory<int> mem2;
			using (SharedMemoryPool<int>.Rent(cellCount, 0, out mem2))
			{
				Span<int> mem2Span = mem2.Span;
				if (Recolorer == null)
				{
					if (!pixelRange.IsNull())
					{
						Span<byte> memSpan = mem.Span;
						int opacityTerm = Opacity << 24;
						int cols = rasterQuery.Cols;
						for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
						{
							int colsTerm = i * cols;
							for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
							{
								int index = colsTerm + j;
								byte b = memSpan[index];
								mem2Span[index] = opacityTerm | (b << 16) | (b << 8) | b;
							}
						}
					}
					request.Splat(mem2Span);
				}
				else
				{
					Recolorer.RecolorBrightness(mem, mem2);
					RasterOperations.PreserveRegion(rasterQuery, mem2, ValidExtent, 0);
					request.SplatPremultiplied(mem2Span);
				}
			}
		}
	}

	private void Render3BandDataInternal(PlotRequest request)
	{
		ResampleArgs args = new ResampleArgs(request.RasterQuery);
		RasterDefinition rasterQuery = request.RasterQuery;
		int cellCount = rasterQuery.CellCount;
		PixelRange pixelRange = rasterQuery.GetPixelRange(ValidExtent).ClipTo(rasterQuery);
		Memory<byte>[] byteSpanArray = new Memory<byte>[3];
		using IMemoryOwner<byte> memoryOwner = SharedMemoryPool<byte>.Rent(_raster.ProfileCount * cellCount);
		byteSpanArray[0] = memoryOwner.Memory.Slice(0, cellCount);
		byteSpanArray[1] = memoryOwner.Memory.Slice(cellCount, cellCount);
		byteSpanArray[2] = memoryOwner.Memory.Slice(cellCount * 2, cellCount);
		_raster.Resample(args, 0, 3, byteSpanArray);
		Memory<int> mem;
		using (SharedMemoryPool<int>.Rent(cellCount, 0, out mem))
		{
			Span<int> memSpan = mem.Span;
			Span<byte> byteSpan = byteSpanArray[0].Span;
			Span<byte> byteSpan2 = byteSpanArray[1].Span;
			Span<byte> byteSpan3 = byteSpanArray[2].Span;
			if (Recolorer == null)
			{
				if (!pixelRange.IsNull())
				{
					int opacityTerm = Opacity << 24;
					int cols = rasterQuery.Cols;
					for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
					{
						int colsTerm = i * cols;
						for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
						{
							int index = colsTerm + j;
							memSpan[index] = opacityTerm | (byteSpan[index] << 16) | (byteSpan2[index] << 8) | byteSpan3[index];
						}
					}
				}
				request.Splat(memSpan);
			}
			else
			{
				for (int k = 0; k < cellCount; k++)
				{
					int ordinal = byteSpan[k];
					int ordinal2 = byteSpan2[k];
					int ordinal3 = byteSpan3[k];
					memSpan[k] = (ordinal << 16) | (ordinal2 << 8) | ordinal3;
				}
				Recolorer.RecolorARGB(mem, mem);
				RasterOperations.PreserveRegion(rasterQuery, mem, ValidExtent, 0);
				request.SplatPremultiplied(memSpan);
			}
		}
	}

	private void Render4BandDataInternal(PlotRequest request)
	{
		ResampleArgs args = new ResampleArgs(request.RasterQuery);
		RasterDefinition rasterQuery = request.RasterQuery;
		int cellCount = rasterQuery.CellCount;
		PixelRange pixelRange = rasterQuery.GetPixelRange(ValidExtent).ClipTo(rasterQuery);
		Memory<byte>[] byteSpanArray = new Memory<byte>[4];
		using IMemoryOwner<byte> memoryOwner = SharedMemoryPool<byte>.Rent(_raster.ProfileCount * cellCount);
		for (int band = 0; band < byteSpanArray.Length; band++)
		{
			byteSpanArray[band] = memoryOwner.Memory.Slice(band * cellCount, cellCount);
		}
		_raster.Resample(args, 0, 4, byteSpanArray);
		Memory<int> mem;
		using (SharedMemoryPool<int>.Rent(cellCount, 0, out mem))
		{
			Span<int> memSpan = mem.Span;
			Span<byte> red = byteSpanArray[0].Span;
			Span<byte> green = byteSpanArray[1].Span;
			Span<byte> blue = byteSpanArray[2].Span;
			Span<byte> sourceAlpha = byteSpanArray[3].Span;
			int cols = rasterQuery.Cols;
			byte opacity = Opacity;

			if (Recolorer == null)
			{
				if (!pixelRange.IsNull())
				{
					for (int row = pixelRange.MinRow; row <= pixelRange.MaxRow; row++)
					{
						int rowOffset = row * cols;
						for (int col = pixelRange.MinCol; col <= pixelRange.MaxCol; col++)
						{
							int index = rowOffset + col;
							if (IsRgbaNoData(red[index], green[index], blue[index], sourceAlpha[index]))
							{
								memSpan[index] = 0;
								continue;
							}
							int alpha = (sourceAlpha[index] * opacity + 127) / byte.MaxValue;
							memSpan[index] = (alpha << 24) | (red[index] << 16) | (green[index] << 8) | blue[index];
						}
					}
				}
				RasterOperations.PreserveRegion(rasterQuery, mem, ValidExtent, 0);
				request.Splat(memSpan);
				return;
			}

			// Recolorers consume RGB as an opaque color and return premultiplied ARGB.
			// The plot buffer contract is straight ARGB, so restore RGB before applying
			// source alpha and layer opacity to the returned alpha.
			for (int index = 0; index < cellCount; index++)
			{
				memSpan[index] = (red[index] << 16) | (green[index] << 8) | blue[index];
			}
			Recolorer.RecolorARGB(mem, mem);
			if (!pixelRange.IsNull())
			{
				for (int row = pixelRange.MinRow; row <= pixelRange.MaxRow; row++)
				{
					int rowOffset = row * cols;
					for (int col = pixelRange.MinCol; col <= pixelRange.MaxCol; col++)
					{
						int index = rowOffset + col;
						if (IsRgbaNoData(red[index], green[index], blue[index], sourceAlpha[index]))
						{
							memSpan[index] = 0;
							continue;
						}
						int coverage = (sourceAlpha[index] * opacity + 127) / byte.MaxValue;
						int recolored = memSpan[index];
						int recoloredAlpha = (int)((uint)recolored >> 24);
						int alpha = (recoloredAlpha * coverage + 127) / byte.MaxValue;
						if (alpha == 0 || recoloredAlpha == 0)
						{
							memSpan[index] = 0;
							continue;
						}
						int redComponent = Math.Min(255, (((recolored >> 16) & 0xFF) * byte.MaxValue + recoloredAlpha / 2) / recoloredAlpha);
						int greenComponent = Math.Min(255, (((recolored >> 8) & 0xFF) * byte.MaxValue + recoloredAlpha / 2) / recoloredAlpha);
						int blueComponent = Math.Min(255, ((recolored & 0xFF) * byte.MaxValue + recoloredAlpha / 2) / recoloredAlpha);
						memSpan[index] = (alpha << 24) | (redComponent << 16) | (greenComponent << 8) | blueComponent;
					}
				}
			}
			RasterOperations.PreserveRegion(rasterQuery, mem, ValidExtent, 0);
			request.Splat(memSpan);
		}
	}

	private bool IsRgbaNoData(byte red, byte green, byte blue, byte alpha)
	{
		byte?[] noData = _rgbaNoDataValues;
		if (noData == null)
		{
			return false;
		}
		if (noData[3].HasValue && alpha == noData[3].Value)
		{
			return true;
		}
		return noData[0].HasValue && noData[1].HasValue && noData[2].HasValue &&
			red == noData[0].Value && green == noData[1].Value && blue == noData[2].Value;
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + _version;
	}
}
