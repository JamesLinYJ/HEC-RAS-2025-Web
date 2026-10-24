using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Rendering.TiledRenderingSystem;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Memory;
using Utility.Web;

namespace Geospatial.IO.TileMapService;

public class NetworkTileProvider : IMultiBandPyramidTileProvider<byte>, ISurface<byte>
{
	public delegate string FormatXYZForRequestURL(int x, int y, int z);

	private class NetworkTileData
	{
		public bool Success;

		public TileKey TileKey;

		public Memory<byte> TileData;

		public int BandCount;
	}

	private class WebMercPyramidLevel : IMultiBandTileProvider<byte>, IMultiBandRasterProvider<byte>, IBlockedRasterDefinition, IRasterDefinition, ISurface<byte>, ITiledRasterDefinition
	{
		private struct ReadBlockData
		{
			public string BaseURL;

			public TileKey Key;

			public FormatXYZForRequestURL GetUrlEndingForTile;
		}

		private readonly int _pyramidLevel;

		private readonly CacheManager<TileKey, NetworkTileData> _cache;

		private readonly string _baseUrl;

		protected HashSet<TileKey> _badResponse = new HashSet<TileKey>(new TileKey.Comparer());

		public const int BandCount = 4;

		private FormatXYZForRequestURL _getUrlEndingForTile;

		private const int DefaultBufferSize = 30720;

		private readonly ThreadLocal<MemoryStream> _memStreams = new ThreadLocal<MemoryStream>(() => new MemoryStream(30720));

		public BlockSize BlockSize => new BlockSize(256, 256);

		public int ProfileCount => 3;

		public BlockSize VerticallyChunkedBlockSize
		{
			get
			{
				throw new NotImplementedException();
			}
		}

		public RasterDefinition RasterDefinition { get; }

		public Extent ValidExtent => MercatorTiledRegion.FullExtent();

		public byte? NoDataValue => null;

		public int TileSize => 256;

		public RasterDefinition TiledRasterDefinition { get; }

		public WebMercPyramidLevel(int pLevel, string baseUrl, FormatXYZForRequestURL getUrlEndingForTile, CacheManager<TileKey, NetworkTileData> cache, HashSet<TileKey> badResponse)
		{
			_getUrlEndingForTile = getUrlEndingForTile;
			TiledRasterDefinition = MercatorTiledRegion.GetTileDefinitionForPyramidLevel(pLevel);
			RasterDefinition = MercatorTiledRegion.GetRasterDefinitionForPyramidLevel(pLevel);
			_pyramidLevel = pLevel;
			_baseUrl = baseUrl;
			_cache = cache;
			_badResponse = badResponse;
		}

		public bool ReadBlock(int profileIndex, long blockIndex, Memory<byte> block)
		{
			if (!TiledRasterDefinition.IsValid())
			{
				return false;
			}
			int pyramidLevel = _pyramidLevel;
			Pixel pixel = TiledRasterDefinition.CellIndex2D(blockIndex);
			int x = pixel.X;
			int y = pixel.Y;
			int numTilesWide = MercatorTiledRegion.NumTilesWide(pyramidLevel);
			if (x >= numTilesWide || y >= numTilesWide)
			{
				_logger.LogWarning("Querying tile that cannot exist: {Z}, ({X},{Y})", pyramidLevel, x, y);
				return false;
			}
			TileKey tileKey = new TileKey(pyramidLevel, new Pixel(y, x));
			lock (_badResponse)
			{
				if (_badResponse.Contains(tileKey))
				{
					return false;
				}
			}
			ReadBlockData model = new ReadBlockData
			{
				BaseURL = _baseUrl,
				Key = tileKey,
				GetUrlEndingForTile = _getUrlEndingForTile
			};
			NetworkTileData orMake = _cache.GetOrMake(tileKey, model, (ReadBlockData readBlockData) =>
			{
				string downloadUrl = readBlockData.BaseURL + readBlockData.GetUrlEndingForTile?.Invoke(readBlockData.Key.TileIndex.X, readBlockData.Key.TileIndex.Y, readBlockData.Key.Level);
				NetworkTileData networkTileData = new NetworkTileData
				{
					Success = false,
					TileKey = readBlockData.Key
				};
				WebHelper.RentedMemoryNetworkResult rentedMemoryNetworkResult = WebHelper.DownloadMemoryEfficientSync(downloadUrl);
				try
				{
					if (rentedMemoryNetworkResult.IsEmpty())
					{
						return networkTileData;
					}
					Memory<byte> slice = rentedMemoryNetworkResult.MemoryOwner.Memory.Slice(0, rentedMemoryNetworkResult.Length);
					try
					{
						using GDALRaster gDALRaster = GDALRaster.FromMemory(slice.Span);
						networkTileData.BandCount = gDALRaster.GetBandCount();
						byte[] tileData = new byte[65536 * networkTileData.BandCount];
						gDALRaster.ReadRaster<byte>(tileData);
						networkTileData.TileData = tileData;
						networkTileData.Success = true;
					}
					catch (Exception exception)
					{
						_logger.LogWarning(exception, "Error parsing network image.");
					}
					return networkTileData;
				}
				finally
				{
					((IDisposable)rentedMemoryNetworkResult/*cast due to constrained. prefix*/).Dispose();
				}
			});
			if (orMake.Success)
			{
				orMake.TileData.Slice(65536 * profileIndex, 65536).CopyTo(block);
			}
			if (!orMake.Success)
			{
				lock (_badResponse)
				{
					_badResponse.Add(tileKey);
				}
			}
			return orMake.Success;
		}

		public bool ReadVerticalBlock(int verticalBlockIndex, Memory<byte> block)
		{
			throw new NotImplementedException();
		}

		public void EvictCache()
		{
		}
	}

	private ConcurrentDictionary<int, IPyramidTileProvider<byte>> _cachedBands = new ConcurrentDictionary<int, IPyramidTileProvider<byte>>();

	private static readonly ILogger _logger = LogHelper.GetLogger("NetworkTileProvider");

	public const int TileWidth = 256;

	public static string GoogleName = "Google Hybrid";

	public static string GoogleBaseAddress = "http://mt.google.com/";

	public static int GoogleMaxPyramidLevel = 22;

	public static string USGSShadedReliefName = "USGS Shaded Relief";

	public static string USGSShadedReliefAddress = "https://basemap.nationalmap.gov/arcgis/rest/services/USGSShadedReliefOnly/MapServer/WMTS/tile/1.0.0/USGSShadedReliefOnly/default/GoogleMapsCompatible/";

	public static int USGSShadedReliefMaxPyramidLevel = 23;

	private static object _shadedReliefInstanceLock = new object();

	private static NetworkTileProvider _shadedReliefInstance;

	private static object _satelliteInstanceLock = new object();

	private static NetworkTileProvider _satelliteInstance;

	private readonly HashSet<TileKey> _badResponse = new HashSet<TileKey>(new TileKey.Comparer());

	private readonly CacheManager<TileKey, NetworkTileData> _cache;

	private string _name;

	private string _baseUrl;

	private int _maxPyramidLevel;

	private FormatXYZForRequestURL _getUrlEndingForTile;

	ConcurrentDictionary<int, IPyramidTileProvider<byte>> IMultiBandPyramidTileProvider<byte>.CachedBands => _cachedBands;

	public static FormatXYZForRequestURL GoogleGetTile => (int x, int y, int z) => $"vt/lyrs=s&x={x}&y={y}&z={z}";

	public static FormatXYZForRequestURL USGSShadedReliefGetTile => (int x, int y, int z) => $"{z}/{y}/{x}";

	public static NetworkTileProvider ShadedReliefInstance
	{
		get
		{
			lock (_shadedReliefInstanceLock)
			{
				if (_shadedReliefInstance == null)
				{
					_shadedReliefInstance = new NetworkTileProvider(USGSShadedReliefName, USGSShadedReliefAddress, USGSShadedReliefGetTile, USGSShadedReliefMaxPyramidLevel, 1);
				}
				return _shadedReliefInstance;
			}
		}
	}

	public static NetworkTileProvider SatelliteInstance
	{
		get
		{
			lock (_satelliteInstanceLock)
			{
				if (_satelliteInstance == null)
				{
					_satelliteInstance = new NetworkTileProvider(GoogleName, GoogleBaseAddress, GoogleGetTile, GoogleMaxPyramidLevel);
				}
				return _satelliteInstance;
			}
		}
	}

	public IMultiBandTileProvider<byte>[] PyramidLevel { get; }

	public int ProfileCount { get; init; }

	public Extent ValidExtent => MercatorTiledRegion.FullExtent();

	public byte? NoDataValue => null;

	private NetworkTileProvider(string name, string baseUrl, FormatXYZForRequestURL getUrlEndingForTile, int maxPyramidLevel, int profileCount = 3)
	{
		ProfileCount = profileCount;
		_name = name;
		_maxPyramidLevel = maxPyramidLevel;
		_baseUrl = baseUrl;
		_getUrlEndingForTile = getUrlEndingForTile;
		_cache = new CacheManager<TileKey, NetworkTileData>((NetworkTileData data) => data.BandCount * 256 * 256, new TileKey.Comparer());
		_cache.OnItemEvicted += _cache_OnItemEvicted;
		_cache.MaxCacheSizeBytes = 268435456;
		_cache.LogIdentifier = name;
		PyramidLevel = new IMultiBandTileProvider<byte>[_maxPyramidLevel + 1];
		for (int intervalIndex = 0; intervalIndex <= _maxPyramidLevel; intervalIndex++)
		{
			int pLevel = _maxPyramidLevel - intervalIndex - 1;
			PyramidLevel[intervalIndex] = new WebMercPyramidLevel(pLevel, _baseUrl, getUrlEndingForTile, _cache, _badResponse);
		}
	}

	private void _cache_OnItemEvicted(TileKey key, NetworkTileData networkTileData)
	{
	}
}
