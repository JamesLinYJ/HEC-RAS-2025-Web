using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial;
using Geospatial.IO;
using Geospatial.IO.ORM;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using H5Assist;
using Utility;
using Utility.Files;
using Utility.Hashing;
using Utility.Memory;
using Utility.ORM;

namespace Ras.Layers.Classifiers;

public class RasterClassificationInput : IClassificationInput, IDataHash, ISpatialVersion, IDisposable
{
	public const string TypeNameStatic = "Raster";

	private bool _fileFoundOnLoad;

	private bool _isInMemory;

	private bool _isDisposed;

	private IDisposable _cleanup;

	private readonly object _loadLock = new object();

	private IRasterPyramid<int> _baseRasterizer;

	private Dictionary<int, int> _keyMap;

	private RemapRasterPyramid _convertRasterizer;

	private VersionTracker _classificationVersionTracker;

	private Classification _classification = new Classification();

	private Freezer _freezer;

	private long _versionXy = 1L;

	private readonly Versioned<IRasterPyramid<int>> _resamplerVersioned = new Versioned<IRasterPyramid<int>>();

	public string Name { get; set; }

	public ClassificationLayer ClassificationLayer { get; private set; }

	[Metadata("Type")]
	public string TypeName => "Raster";

	[Metadata("Absolute Path")]
	public string Filename { get; set; }

	[Metadata("Relative Path")]
	private string PathRelFilename { get; set; }

	[ComplexMetadata]
	public FileSignature Signature { get; set; }

	public Classification Classification
	{
		get
		{
			return _classification;
		}
		set
		{
			_classificationVersionTracker.Assign(ref _classification, value);
		}
	}

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			return _versionXy + _classificationVersionTracker.GetVersion(_classification);
		}
	}

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public static RasterClassificationInput CreateAndScan(ClassificationLayer lyr, string filename, Classification classifier)
	{
		return new RasterClassificationInput(lyr, filename, classifier)
		{
			Signature = FileSignature.LoadDefault(filename)
		};
	}

	public RasterClassificationInput(ClassificationLayer lyr, string filename, Classification classifier)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		ClassificationLayer = lyr;
		Filename = filename;
		Classification = classifier;
		Name = ExtractName(lyr.Name, Filename);
		_fileFoundOnLoad = true;
		if (!ClassificationLayer.IsInMemory)
		{
			PathRelFilename = FileHelpers.MakeRelative(ClassificationLayer.SourceDirectory, Filename);
		}
	}

	private RasterClassificationInput(ClassificationLayer lyr, Classification classifier)
	{
		ClassificationLayer = lyr;
		Classification = classifier;
	}

	public RasterClassificationInput()
	{
	}

	public static RasterClassificationInput CreateInMemory(ClassificationLayer lyr, IRasterPyramid<int> rasterizer, Classification classifier)
	{
		return new RasterClassificationInput(lyr, classifier)
		{
			_baseRasterizer = rasterizer,
			_isInMemory = true
		};
	}

	private static string ExtractName(string trName, string filename)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
		if (fileNameWithoutExtension.StartsWith(trName) && fileNameWithoutExtension.Length > trName.Length + 1)
		{
			fileNameWithoutExtension = fileNameWithoutExtension.Substring(trName.Length + 1);
		}
		return fileNameWithoutExtension;
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

	public IRasterPyramid<int> GetResampler()
	{
		return _resamplerVersioned.Get(VersionXY, BuildResampler);
	}

	IResample<int> IClassificationInput.GetResampler()
	{
		return GetResampler();
	}

	private IRasterPyramid<int> BuildResampler()
	{
		if (!_isInMemory && !_fileFoundOnLoad)
		{
			return null;
		}
		if (_baseRasterizer == null)
		{
			LazyThreadsafe<IRasterPyramid<int>>.AllocateOnce(ref _baseRasterizer, _loadLock, Filename, (string fn) => ((IPyramidTileProvider<int>)(_cleanup = TiffDataSource<int>.TryLoad(fn)))?.AsRasterizer());
		}
		if (_baseRasterizer == null)
		{
			return null;
		}
		_keyMap = BuildDictionary();
		if (_keyMap.Count == 0)
		{
			return _baseRasterizer;
		}
		_convertRasterizer = new RemapRasterPyramid(_baseRasterizer, _keyMap);
		return _convertRasterizer;
	}

	private Dictionary<int, int> BuildDictionary()
	{
		Dictionary<int, int> intByInt = new Dictionary<int, int>();
		UniqueClassificationMap classificationMap = ClassificationLayer.ClassificationMap;
		foreach (int iD in Classification.IDs)
		{
			string name = Classification.GetName(iD);
			if (classificationMap.TryGetID(name, out var localValue))
			{
				if (iD != localValue)
				{
					intByInt[iD] = localValue;
				}
			}
			else
			{
				intByInt[iD] = 9999;
			}
		}
		return intByInt;
	}

	public void Save(H5Writer hw, string h5Path)
	{
		GeospatialH5Metadata.Write(this, hw, h5Path);
		SaveTable(hw, h5Path, Classification);
	}

	public void RelocateSource(string filename)
	{
		if (!File.Exists(filename)) throw new FileNotFoundException("Classification raster not found.", filename);
		lock (_loadLock)
		{
			_cleanup?.Dispose(); _cleanup = null; _baseRasterizer = null; _convertRasterizer = null; _keyMap = null;
			Filename = Path.GetFullPath(filename);
			PathRelFilename = FileHelpers.MakeRelative(ClassificationLayer.SourceDirectory, Filename);
			_fileFoundOnLoad = true; _versionXy++;
		}
	}

	private Classification LoadTable(H5Reader h5r, string h5Path)
	{
		string pathCombine = H5Reader.PathCombine(h5Path, "Attributes");
		Classification classification = new Classification();
		classification.Set(0, "NoData");
		if (h5r.ExistsAsDataset(pathCombine))
		{
			Table table = h5r.ReadCompoundElementsAsTable(pathCombine);
			int columnIndex = table.GetColumnIndex("ID");
			int columnIndex2 = table.GetColumnIndex("Name");
			if (columnIndex == -1 || columnIndex2 == -1)
			{
				throw new Exception("Malformed ID/Name column on ClassificationLayer.");
			}
			foreach (TableRow row in table.Rows)
			{
				int valueOrDefault = (row[columnIndex] as int?).GetValueOrDefault();
				string name = row[columnIndex2].ToString();
				classification.Set(valueOrDefault, name);
			}
		}
		return classification;
	}

	private void SaveTable(H5Writer hw, string h5Path, Classification cls)
	{
		string datasetName = H5Reader.PathCombine(h5Path, "Attributes");
		Table table = new Table();
		table.AddColumn("ID", typeof(int));
		table.AddColumn("Name", typeof(string));
		int[] iDs = cls.IDs.ToArray();
		string[] classes = cls.Classes.ToArray();
		for (int i = 0; i < cls.Count; i++)
		{
			TableRow tableRow = table.AddNewRow();
			tableRow[0] = iDs[i];
			tableRow[1] = classes[i];
		}
		hw.WriteCompoundTable(datasetName, table);
	}

	public bool TryInit(ClassificationLayer cl, H5Reader hr, string h5Path)
	{
		if (!hr.ExistsAsGroup(h5Path))
		{
			return false;
		}
		ClassificationLayer = cl;
		GeospatialH5Metadata.Read(this, hr, h5Path);
		Classification = LoadTable(hr, h5Path);
		if (string.IsNullOrEmpty(Filename) && string.IsNullOrEmpty(PathRelFilename))
		{
			return false;
		}
		InitSource();
		return true;
	}

	private void InitSource()
	{
		if (ClassificationLayer.IsInMemory)
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
			if (ExternalConnection.TryReconcile(ref absPath, ref relPath, ClassificationLayer.SourceDirectory))
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
		Name = ExtractName(ClassificationLayer.Name, Filename);
	}

	public int GetDataHash()
	{
		HashCode hash = new HashCode();
		hash.Add(Filename); hash.Add(Name);
		foreach (KeyValuePair<int, string> entry in Classification.GetAllClassifications().OrderBy(entry => entry.Key))
		{
			hash.Add(entry.Key); hash.Add(entry.Value);
		}
		return hash.ToHashCode();
	}

	public Classification GetClassifier()
	{
		return Classification;
	}

	IClassificationInput IClassificationInput.CloneWith(ClassificationLayer newClass)
	{
		throw new NotSupportedException("Cannot clone Raster inputs - see ExportClipped");
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				_cleanup?.Dispose();
				_cleanup = null;
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
