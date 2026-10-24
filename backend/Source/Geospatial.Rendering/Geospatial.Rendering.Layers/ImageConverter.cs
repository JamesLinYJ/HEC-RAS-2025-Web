using System;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Layers;

public class ImageConverter : IMultiBandResampler<byte>, ISurface<byte>
{
	private IRenderLayer _lyr;

	public int ProfileCount => 3;

	public Extent ValidExtent => _lyr.ValidExtent;

	public byte? NoDataValue => null;

	public ImageConverter(IRenderLayer layer)
	{
		_lyr = layer;
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<byte> outData, ResampleCache cache = null)
	{
		throw new NotImplementedException();
	}

	public unsafe void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<byte>[] outData)
	{
		if (startBandIndex != 0 || bandCount != 3)
		{
			throw new NotImplementedException();
		}
		using PlotRequest plotRequest = new PlotRequest(args.Raster);
		_lyr.Render(plotRequest);
		Span<byte> outDataSpan = outData[0].Span;
		Span<byte> outData2 = outData[1].Span;
		Span<byte> outData3 = outData[2].Span;
		Span<int> colorBufferARGB = plotRequest.ColorBufferARGB.Span;
		int length = colorBufferARGB.Length;
		fixed (int* colorBufferARGB2 = colorBufferARGB)
		{
			byte* colorBufferARGB3 = (byte*)colorBufferARGB2;
			for (int i = 0; i < length; i++)
			{
				outDataSpan[i] = colorBufferARGB3[i * 4 + 2];
				outData2[i] = colorBufferARGB3[i * 4 + 1];
				outData3[i] = colorBufferARGB3[i * 4];
			}
		}
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<byte>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<byte> outData)
	{
		throw new NotImplementedException();
	}
}
