using System;
using System.IO;
using Geospatial;
using Geospatial.IO;
using Geospatial.IO.ORM;
using Geospatial.Rasters;
using H5Assist;
using Utility;
using Utility.Files;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers.Terrains;

public class RasterTerrainInput : ITerrainInput, IDataHash, IDisposable, ISpatialVersion
{
	public const string TypeNameStatic = "Raster File";

	private bool _fileFoundOnLoad;

	private TiffDataSource<float> _ds;

	private readonly object _loadLock = new object();

	private IRasterPyramid<float> _rasterizer;

	[Metadata("Name")]
	public string Name { get; set; }

	public Terrain Terrain { get; private set; }

	[Metadata("Type")]
	public string TypeName => "Raster";

	[Metadata("Absolute Path")]
	public string Filename { get; set; }

	[Metadata("Relative Path")]
	private string PathRelFilename { get; set; }

	[ComplexMetadata]
	public FileSignature Signature { get; set; }

	public long VersionXY => 1L;

	public RasterTerrainInput(Terrain tr, string filename)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException("Terrain raster file not found: " + filename, filename);
		}
		Terrain = tr;
		Filename = filename;
		Name = ExtractName(tr.Name, Filename);
		_fileFoundOnLoad = true;
		if (!Terrain.IsInMemory)
		{
			PathRelFilename = FileHelpers.MakeRelative(Terrain.SourceDirectory, Filename);
		}
	}

	public static RasterTerrainInput CreateAndScan(Terrain tr, string filename)
	{
		return new RasterTerrainInput(tr, filename)
		{
			Signature = FileSignature.LoadDefault(filename)
		};
	}

	private RasterTerrainInput(Terrain tr)
	{
		Terrain = tr;
	}

	public RasterTerrainInput()
	{
	}

	public static RasterTerrainInput CreateInMemory(Terrain tr, TiffDataSource<float> ds)
	{
		return new RasterTerrainInput(tr)
		{
			_ds = ds,
			_rasterizer = ds.AsRasterizer(),
			_fileFoundOnLoad = true
		};
	}

	public bool IsValid(out string err)
	{
		if (_fileFoundOnLoad)
		{
			err = "";
			return true;
		}
		err = "File not found: " + Filename;
		return false;
	}

	IResample<float> ITerrainInput.GetResampler()
	{
		return GetResampler();
	}

	public IRasterPyramid<float> GetResampler()
	{
		if (!_fileFoundOnLoad)
		{
			return null;
		}
		return LazyThreadsafe<IRasterPyramid<float>>.AllocateOnce(ref _rasterizer, _loadLock, this, (RasterTerrainInput model) =>
		{
			model._ds = TiffDataSource<float>.TryLoad(model.Filename);
			return (model._ds == null) ? null : model._ds.AsRasterizer();
		});
	}

	public void Dispose()
	{
		_ds?.Dispose();
	}

	public void Save(H5Writer hw, string h5Path)
	{
		GeospatialH5Metadata.Write(this, hw, h5Path);
	}

	public void RelocateSource(string filename)
	{
		if (!File.Exists(filename)) throw new FileNotFoundException("Terrain raster not found.", filename);
		lock (_loadLock)
		{
			_ds?.Dispose(); _ds = null; _rasterizer = null;
			Filename = Path.GetFullPath(filename);
			PathRelFilename = FileHelpers.MakeRelative(Terrain.SourceDirectory, Filename);
			_fileFoundOnLoad = true;
		}
	}

	public bool TryInit(Terrain terrain, H5Reader hr, string h5Path)
	{
		if (!hr.ExistsAsGroup(h5Path))
		{
			return false;
		}
		Terrain = terrain;
		GeospatialH5Metadata.Read(this, hr, h5Path);
		if (string.IsNullOrEmpty(Filename) && string.IsNullOrEmpty(PathRelFilename))
		{
			return false;
		}
		InitSource();
		return true;
	}

	private static string ExtractName(string trName, string filename)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
		if (string.IsNullOrEmpty(trName))
		{
			return fileNameWithoutExtension;
		}
		if (fileNameWithoutExtension.StartsWith(trName) && fileNameWithoutExtension.Length > trName.Length + 1)
		{
			fileNameWithoutExtension = fileNameWithoutExtension.Substring(trName.Length + 1);
		}
		return fileNameWithoutExtension;
	}

	private void InitSource()
	{
		if (Terrain.IsInMemory)
		{
			if (File.Exists(Filename))
			{
				_fileFoundOnLoad = true;
			}
			else
			{
				_fileFoundOnLoad = false;
			}
		}
		else
		{
			string absPath = Filename;
			string relPath = PathRelFilename;
			if (ExternalConnection.TryReconcile(ref absPath, ref relPath, Terrain.SourceDirectory))
			{
				Filename = absPath;
				PathRelFilename = relPath;
				_fileFoundOnLoad = true;
			}
			else
			{
				_fileFoundOnLoad = false;
			}
		}
		if (string.IsNullOrEmpty(Name))
		{
			Name = ExtractName(Terrain.Name, Filename);
		}
	}

	public int GetDataHash()
	{
		return StableHash.Combine(Name?.GetHashCode() ?? 0, Filename?.GetHashCode() ?? 0);
	}

	public void FreezeOne()
	{
	}

	public void ThawOne()
	{
	}
}
