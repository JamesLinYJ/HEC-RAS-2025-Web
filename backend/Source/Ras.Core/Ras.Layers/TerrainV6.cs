using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Geospatial;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using H5Assist.Caching;
using Microsoft.Extensions.Logging;
using TiffBinaryReader;
using Utility;
using Utility.Communication;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;
using Utility.ORM;

namespace Ras.Layers;

public class TerrainV6 : IResample<float>, ISurface<float>, IDisposable, ISpatialVersion
{
	private enum TileMaskState
	{
		Complete,
		None,
		PartialOrUnknown
	}

	public static readonly string CurrentVersion = "1.0";

	public const string Extension = ".h5";

	public const string S3Extension = ".s3";

	public const string RootTerrainGroup = "/Terrain";

	private static readonly ILogger _logger = LogHelper.GetLogger("TerrainV6");

	private NativeResourceWrapper<H5Reader> _h5Wrapper;

	private List<string> _rasterFiles;

	private List<TiffDataSource<float>> _rasterInputs;

	private List<IResample<float>> _layers;

	private RasterFileInfo[] _rasterFilesInfo;

	public List<PolygonRasterModificationResampler> VectorMods = new List<PolygonRasterModificationResampler>();

	private string _name;

	private readonly Versioned<Extent> _rasterExtCache = new Versioned<Extent>();

	private readonly Versioned<Extent> _vecExtCache = new Versioned<Extent>();

	private static List<S3Client> _clients;

	private bool _isDisposed;

	public IReadOnlyList<IResample<float>> Layers => _layers;

	public int LayerCount => _layers?.Count ?? 0;

	public RasterFileInfo[] RasterFilesInfo => _rasterFilesInfo;

	public bool HasLoadErrors { get; private set; }

	public long VersionXY => VectorMods.Sum((PolygonRasterModificationResampler vm) => vm.VersionXY);

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			if (!IsInMemory)
			{
				throw new NotSupportedException("Terrain must be in-memory to be renamed this way. Please use Save-As");
			}
			_name = value;
		}
	}

	public string Filename { get; private set; }

	public bool IsInMemory => string.IsNullOrEmpty(Filename);

	[Metadata("Description")]
	public string Description { get; set; }

	public int RasterFileCount => RasterFilenames.Count;

	public IReadOnlyList<string> RasterFilenames => _rasterFiles;

	public Extent ValidExtent
	{
		get
		{
			Extent extent = _vecExtCache.Get(VersionXY, ComputeVectorExtents);
			Extent other = _rasterExtCache.Get(VersionXY, ComputeRasterExtents);
			return extent.Union(other);
		}
	}

	public float? NoDataValue { get; private set; } = -9999f;

	public void AddLayer(IResample<float> layer)
	{
		_layers.Add(layer);
	}

	public void FreezeOne()
	{
	}

	public void ThawOne()
	{
	}

	private Extent ComputeVectorExtents()
	{
		return Extent.Union(VectorMods.Select((PolygonRasterModificationResampler s) => s.ValidExtent));
	}

	private Extent ComputeRasterExtents()
	{
		return Extent.Union(Layers.Select((IResample<float> l) => l.ValidExtent));
	}

	public Projection TryGetProjection()
	{
		return GDALRaster.GetProjection(RasterFilenames[0]);
	}

	private TerrainV6()
	{
	}

	public TerrainV6(string filename)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		Filename = filename;
		string fileName = Path.GetFileName(filename);
		if (fileName.RoughlyEndsWith(".h5"))
		{
			string fileName2 = fileName;
			int length = ".h5".Length;
			fileName = fileName2.Substring(0, fileName2.Length - length);
		}
		_name = fileName;
		_h5Wrapper = H5ResourceWrapper.KeepOpen(filename);
		Parse();
	}

	public static TerrainV6 CreateNew(string filename)
	{
		throw new NotImplementedException();
	}

	public static TerrainV6 FromS3(string s3File)
	{
		try
		{
			if (!File.Exists(s3File))
			{
				throw new FileNotFoundException(s3File);
			}
			if (_clients == null)
			{
				_clients = S3Helpers.TryParseCredentialFile("C:\\Temp\\s3cred.txt");
			}
			if (_clients.IsNullOrEmpty())
			{
				return null;
			}
			if (!S3Helpers.TryParseKeyFile(s3File, out var server, out var bucket, out var key))
			{
				return null;
			}
			S3Client client = _clients.FirstOrDefault((S3Client c) => c.ServerUrl.RoughlyEquals(server));
			if (client == null)
			{
				_logger.LogInformation("No S3 client found that matches {Server}", server);
				return null;
			}
			TiffFile reader = new TiffFile(new Pool<Stream>(() => new S3BucketStream(client, bucket, key)));
			TerrainV6 terrainV = new TerrainV6
			{
				_name = Path.GetFileNameWithoutExtension(s3File),
				Filename = s3File,
				_rasterFiles = new List<string> { key }
			};
			ArrayCacheManager<PyramidTileIndex, float> arrayCacheManager = new ArrayCacheManager<PyramidTileIndex, float>();
			arrayCacheManager.CacheManager.MaxCacheSizeBytes = 52428800;
			TiffDataSource<float> tiffDataSource = TiffDataSource<float>.Give(reader, arrayCacheManager);
			IRasterPyramid<float> rasterPyramid = tiffDataSource.AsRasterizer();
			rasterPyramid.PyramidSelectionRatio = 2f;
			terrainV._rasterInputs = new List<TiffDataSource<float>> { tiffDataSource };
			terrainV._layers = new List<IResample<float>> { rasterPyramid };
			return terrainV;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Could not create terrain from S3");
			return null;
		}
	}

	public static TerrainV6 CreateInMemory(string name = "")
	{
		TerrainV6 terrainV = new TerrainV6();
		terrainV.InitEmpty();
		terrainV.Name = name;
		return terrainV;
	}

	private void InitEmpty()
	{
		_rasterFiles = new List<string>();
		_rasterInputs = new List<TiffDataSource<float>>();
		_layers = new List<IResample<float>>();
	}

	public static string CreateSingleFileTerrain(string tiffFilename, string newTerrainName)
	{
		string path = Path.Combine(Path.GetDirectoryName(tiffFilename), newTerrainName + ".h5");
		File.Delete(path);
		using H5Writer h5Writer = new H5Writer(path);
		h5Writer.CreateGroup("/Terrain");
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(tiffFilename);
		string fileNameWithoutExtensionTerm = "/Terrain/" + fileNameWithoutExtension;
		h5Writer.CreateGroup(fileNameWithoutExtensionTerm);
		h5Writer.WriteAttribute(fileNameWithoutExtensionTerm, "File", Path.GetFileName(tiffFilename));
		h5Writer.WriteAttribute(fileNameWithoutExtensionTerm, "File Last-Write", "N/A");
		h5Writer.WriteAttribute(fileNameWithoutExtensionTerm, "Priority", 0);
		return path;
	}

	public void CopyTo(string dir)
	{
		File.Copy(Filename, Path.Combine(dir, Path.GetFileName(Filename)));
		foreach (string rasterFilename in RasterFilenames)
		{
			File.Copy(rasterFilename, Path.Combine(dir, Path.GetFileName(rasterFilename)));
		}
	}

	private void Parse()
	{
		_h5Wrapper.UseResource((H5Reader hr) =>
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			if (!hr.ExistsAsGroup("/Terrain"))
			{
				throw new Exception("Not a valid HEC-RAS HDF5 Terrain file.");
			}
			List<Tuple<string, int>> valueTupleList = new List<Tuple<string, int>>();
			List<string> fullyQualifiedGroupNames = hr.GetFullyQualifiedGroupNames("/Terrain");
			int ordinal = 0;
			foreach (string fullyQualifiedGroupName in fullyQualifiedGroupNames)
			{
				string tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar(fullyQualifiedGroupName, "File");
				if (!string.IsNullOrEmpty(tryReadStringAttributeScalar))
				{
					if (hr.TryReadAttributeScalar(fullyQualifiedGroupName, "Priority", out int ordinal2))
					{
						valueTupleList.Add(new Tuple<string, int>(tryReadStringAttributeScalar, ordinal2));
					}
					else
					{
						if (ordinal == 0)
						{
							_logger.LogWarning("Error reading Terrain: Priority tag does not exist on {grp}. This is likely an old Terrain, from before 5.0 was released. The terrain files may not be parsed in the correct order.", fullyQualifiedGroupName);
						}
						valueTupleList.Add(new Tuple<string, int>(tryReadStringAttributeScalar, ordinal));
						ordinal++;
					}
				}
			}
			string dir = Path.GetDirectoryName(Filename);
			_rasterFiles = (from a in valueTupleList
				orderby a.Item2
				select Path.Combine(dir, a.Item1)).ToList();
			_rasterInputs = TiffDataSource<float>.TryLoad(_rasterFiles, useSharedCache: true);
			_layers = new List<IResample<float>>();
			_layers.AddRange(_rasterInputs.Select((TiffDataSource<float> a) => a.AsRasterizer()).Reverse());
			ReadRasterFilesInfo();
			stopwatch.Stop();
			_logger?.LogInformation("Loaded \"{Name}\" in {Duration} ms.", Path.GetFileName(Filename), stopwatch.ElapsedMilliseconds);
		});
	}

	private void ReadRasterFilesInfo()
	{
		try
		{
			_h5Wrapper.UseResource((H5Reader h5r) =>
			{
				string directoryName = Path.GetDirectoryName(Filename);
				List<string> groupNames = h5r.GetGroupNames("Terrain");
				_rasterFilesInfo = new RasterFileInfo[groupNames.Count];
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(Filename);
				bool condition = false;
				for (int i = 0; i < groupNames.Count; i++)
				{
					RasterFileInfo rasterFileInfo = new RasterFileInfo(h5r, directoryName, fileNameWithoutExtension, groupNames[i]);
					if (rasterFileInfo.Priority == -1)
					{
						rasterFileInfo.Priority = i;
						if (!condition)
						{
							condition = true;
							_logger.LogInformation("Loading old terrain file. File priority may be incorrect. \"{Name}\"", Path.GetFileName(Filename));
						}
					}
					_rasterFilesInfo[i] = rasterFileInfo;
				}
				_rasterFilesInfo = _rasterFilesInfo.OrderBy((RasterFileInfo rfi) => rfi.Priority).ToArray();
			});
		}
		catch (Exception exception)
		{
			_rasterFilesInfo = new RasterFileInfo[0];
			_logger.LogWarning(exception, "Error reading terrain file '{Filename}", Filename);
		}
	}

	public Projection GetProjection()
	{
		if (RasterFilenames.Count == 0)
		{
			return null;
		}
		return GDALRaster.GetProjection(RasterFilenames[0]);
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		if (LayerCount == 0)
		{
			return;
		}
		ResampleArgs args2 = args.CloneWithoutBufferClear();
		for (int i = 0; i < LayerCount; i++)
		{
			if (args.Reporter.CancelRequested)
			{
				break;
			}
			_layers[i].Resample(args2, outData);
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		if (Layers.Count == 0)
		{
			outData.Span.Fill(NoDataValue.GetValueOrDefault());
			return;
		}
		if (Layers.Count == 1)
		{
			Layers[0].SamplePoints(points, outData);
			return;
		}
		for (int i = 0; i < LayerCount; i++)
		{
			_layers[i].SamplePoints(points, outData);
		}
	}

	public Histogram ComputeHistogram(int pixelCount, int binCount)
	{
		return Histogram.ComputeTiled(this, pixelCount, binCount);
	}

	public void Save()
	{
		throw new NotImplementedException();
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				_h5Wrapper.Dispose();
			}
			_isDisposed = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
