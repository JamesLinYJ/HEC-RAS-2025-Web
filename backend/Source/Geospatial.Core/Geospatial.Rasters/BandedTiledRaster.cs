using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class BandedTiledRaster<T> : IMultiBandRaster<T>, IMultiBandResampler<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandTileProvider<T> _tileProvider;

	public Extent ValidExtent => _tileProvider.RasterDefinition.Extent;

	public T? NoDataValue => _tileProvider.NoDataValue;

	public RasterDefinition RasterDefinition => _tileProvider.RasterDefinition;

	public RasterDefinition TiledRasterDefinition => _tileProvider.TiledRasterDefinition;

	public RasterDefinition WorldRasterDefinition => _tileProvider.RasterDefinition;

	public IMultiBandTileProvider<T> TileProvider => _tileProvider;

	public int ProfileCount => _tileProvider.ProfileCount;

	public BandedTiledRaster(IMultiBandTileProvider<T> tileProvider)
	{
		_tileProvider = tileProvider ?? throw new ArgumentNullException("tileProvider");
	}

	public RasterDefinition GetTileRasterDefinition(Pixel tileIdx)
	{
		return new RasterDefinition(TiledRasterDefinition.CellExtent(tileIdx), WorldRasterDefinition.CellSize);
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<T> outData, ResampleCache cache = null)
	{
		_tileProvider.AsSingleProfileProviderRef(bandIndex).AsRasterizer().Resample(args, outData);
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		this.Resample_NaiiveLoop(args, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> retn)
	{
		_tileProvider.AsSingleProfileProviderRef(bandIndex).AsRasterizer().SamplePoints(points, retn);
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		this.SamplePoints_NaiiveLoop(points, startBandIndex, bandCount, outData);
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
	{
		throw new NotImplementedException();
	}
}
