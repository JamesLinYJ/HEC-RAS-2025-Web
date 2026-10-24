using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class RegisteredTileProvider<T> : ITileProvider<T>, IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition where T : unmanaged, IEquatable<T>
{
	public List<ITileProvider<T>> InputSources { get; set; }

	public int TileSize
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public RasterDefinition TiledRasterDefinition
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public BlockSize BlockSize
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public RasterDefinition RasterDefinition
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public Extent ValidExtent
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public T? NoDataValue
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public static List<RegisteredTileProvider<T>> TryIdentifyRegisteredSources(IList<ITileProvider<T>> inputs)
	{
		throw new NotImplementedException();
	}

	public bool ReadBlock(long blockIndex, Memory<T> block)
	{
		throw new NotImplementedException();
	}
}
