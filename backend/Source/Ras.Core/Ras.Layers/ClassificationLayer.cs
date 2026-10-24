using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Geospatial;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.IO.ORM;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Layers.Classifiers;
using Utility;
using Utility.Extensions;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Layers;

public class ClassificationLayer : CoreLayerBase, IResample<int>, ISurface<int>, IMutableSpatialVersion, ISpatialVersion
{
	public static class MessageCodes
	{
		public static readonly MessageClass InputLayerLoadError = new MessageClass("ClassificationLayer_InputLayerLoadError", MessageSeverity.Error, "{LayerName} did not load correctly. (Type: {LayerTypeName})");

		public static Message GetInputLayerLoadError(string layerName, string typeName)
		{
			return InputLayerLoadError.Create(layerName, typeName);
		}
	}

	private new static readonly ILogger _logger = LogHelper.GetLogger("ClassificationLayer");

	public const string HDF5Group = "/Classification";

	public const string AttributeDatasetName = "Attributes";

	public const string ClassificationDatasetName = "Class Names";

	public const string Extension = ".h5";

	public const string NoDataClass = "NoData";

	public const int DefaultNoDataValue = 0;

	public const int DefaultMissingValue = 9999;

	private int _layerInputHash;

	private CollectionVersionTracker _layersTracker;

	private readonly List<IClassificationInput> _layers = new List<IClassificationInput>();

	private long _versionXy;

	private Freezer _freezer;

	private Versioned<UniqueClassificationMap> _classificationMapVersioned = new Versioned<UniqueClassificationMap>();

	public Extent ValidExtent => Extent.Union(Layers.Select((IClassificationInput l) => l.GetResampler()?.ValidExtent ?? Extent.NullExtent));

	public int? NoDataValue => 0;

	public IReadOnlyList<IClassificationInput> Layers => _layers;

	public int LayerCount => Layers.Count;

	public LayerFailureBehavior ResampleFailureBehavior { get; set; } = LayerFailureBehavior.ThrowException;

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			return _versionXy + _layersTracker.GetVersion(_layers);
		}
	}

	public UniqueClassificationMap ClassificationMap => _classificationMapVersioned.Get(VersionXY, BuildClassificationMap);

	public static string MakeFilename(string projectDir, string className)
	{
		return Project.MakeFilename<ClassificationLayer>(projectDir, className, ".h5");
	}

	protected override string GetHDF5Group()
	{
		return "/Classification";
	}

	public override string GetCurrentFileFormatVersion()
	{
		return Project.DevFileVersion;
	}

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public void ForceSetVersion(long versionXy)
	{
		_versionXy = versionXy;
	}

	private ClassificationLayer(InstantiationMode mode)
		: base(mode)
	{
	}

	public ClassificationLayer(string filename, Project project = null)
		: base(filename, project)
	{
	}

	public static ClassificationLayer CreateInMemory(string name = "")
	{
		return new ClassificationLayer(InstantiationMode.InMemory)
		{
			_name = name
		};
	}

	public static ClassificationLayer CreateNew(string filename)
	{
		CoreLayerBase.VerifyCanCreateNew(filename);
		ClassificationLayer classificationLayer = new ClassificationLayer(InstantiationMode.CreateNewFile);
		classificationLayer.InitializeAndSaveAsNew(filename);
		return classificationLayer;
	}

	public static ClassificationLayer Load(NativeResourceWrapper<H5Reader> reader, string name = "")
	{
		ClassificationLayer classificationLayer = new ClassificationLayer(InstantiationMode.LoadFromDataProvider);
		classificationLayer._h5Wrapper = reader;
		classificationLayer.InitializeData();
		if (!string.IsNullOrEmpty(name))
		{
			classificationLayer._name = name;
		}
		return classificationLayer;
	}

	protected override void LoadInternal(H5Reader h5r)
	{
		foreach (ClassificationInputDefinition read in FeatureIO.Read<ClassificationInputDefinition>(h5r, "/Classification"))
		{
			IClassificationInput classificationInput = read.MakeInput(this, h5r);
			if (classificationInput == null)
			{
				AddLoadError(MessageCodes.GetInputLayerLoadError(read.Name, "NULL"));
			}
			else
			{
				if (!string.IsNullOrEmpty(read.Name)) classificationInput.Name = read.Name;
				Add(classificationInput);
			}
		}
	}

	public UniqueClassificationMap BuildClassificationMap()
	{
		UniqueClassificationMap uniqueClassificationMap = new UniqueClassificationMap();
		uniqueClassificationMap.Clear();
		uniqueClassificationMap.Set(0, "NoData");
		foreach (IClassificationInput layer in _layers)
		{
			Classification classifier = layer.GetClassifier();
			if (classifier == null)
			{
				AddLoadError(MessageCodes.GetInputLayerLoadError(layer.Name, layer.TypeName));
				continue;
			}
			foreach (string @class in classifier.Classes)
			{
				uniqueClassificationMap.TryAddClassifier(@class, out var _);
			}
		}
		return uniqueClassificationMap;
	}

	protected override void PostInitializeDataInternal()
	{
		_layerInputHash = LoadLayerHash();
	}

	private int LoadLayerHash()
	{
		int combine = 0;
		for (int i = 0; i < Layers.Count; i++)
		{
			int dataHash = Layers[i].GetDataHash();
			combine = HashCode.Combine(combine, Layers[i].Name, Layers[i].TypeName, dataHash);
		}
		return combine;
	}

	protected override bool NeedsSaveInternal()
	{
		return LoadLayerHash() != _layerInputHash;
	}

	protected override void SaveInternal(H5Writer hw)
	{
		ClassificationInputDefinition[] fromInputs = ClassificationInputDefinition.FromInputs("/Classification", _layers);
		List<Exception> failures = new List<Exception>();
		for (int i = 0; i < _layers.Count; i++)
		{
			IClassificationInput classificationInput = _layers[i];
			try
			{
				string location = fromInputs[i].Location;
				classificationInput.Save(hw, location);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Layer failed to save: {LayerName}", classificationInput.Name);
				failures.Add(new IOException("Failed to save input " + classificationInput.Name, exception));
			}
		}
		if (failures.Count > 0) throw new AggregateException("One or more model inputs failed to save.", failures);
		FeatureIO.Write(hw, "/Classification", fromInputs);
		_layerInputHash = LoadLayerHash();
	}

	public static bool IsValid(string fn)
	{
		try
		{
			if (!File.Exists(fn))
			{
				return false;
			}
			string extension = Path.GetExtension(fn);
			if (extension.RoughlyEquals(".h5") || extension.RoughlyEquals(".hdf"))
			{
				using (H5Reader hr = new H5Reader(fn))
				{
					return IsValid(hr);
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public static bool IsValid(H5Reader hr)
	{
		return hr.ExistsAsGroup("/Classification");
	}

	public string GetAvailableRasterFilename(string baseName)
	{
		if (IsInMemory)
		{
			throw new Exception("Cannot create new raster files on an in-memory classification layer.");
		}
		string sourceDirectory = SourceDirectory;
		string baseFilename = Name + "." + baseName;
		string extension = ".tif";
		return FileHelpers.UniqueFilename(sourceDirectory, baseFilename, extension);
	}

	public void Add(IClassificationInput input)
	{
		if (input.ClassificationLayer != this)
		{
			throw new Exception("Inputs can only belong to one classification layer. If intended, construct input layer in such a way that assigns its Classification layer.");
		}
		_layers.Add(input);
	}

	public void Insert(int index, IClassificationInput input)
	{
		if (input.ClassificationLayer != this)
		{
			throw new Exception("Inputs can only belong to one classification layer. If intended, construct input layer in such a way that assigns its Classification layer.");
		}
		_layers.Insert(index, input);
	}

	public void Remove(IClassificationInput input)
	{
		if (input.ClassificationLayer != this)
		{
			throw new Exception("Input layer doesn't point to this classification layer.");
		}
		if (!_layers.Remove(input))
		{
			throw new Exception("Input layer doesn't exist in this classification layer.");
		}
		_layersTracker.OnDeleted(input);
	}

	public void Reorder(int origIndex, int newIndex)
	{
		IClassificationInput layer = _layers[origIndex];
		_layers.RemoveAt(origIndex);
		_layers.Insert(newIndex, layer);
		Interlocked.Increment(ref _versionXy);
	}

	public RasterClassificationInput AddNewRasterLayer(string fn, Classification classifier)
	{
		RasterClassificationInput rasterClassificationInput = RasterClassificationInput.CreateAndScan(this, fn, classifier);
		Add(rasterClassificationInput);
		return rasterClassificationInput;
	}

	public PolygonClassificationInput AddNewPolygonLayer(string layerName)
	{
		PolygonClassificationInput polygonClassificationInput = new PolygonClassificationInput(this, layerName);
		Add(polygonClassificationInput);
		return polygonClassificationInput;
	}

	public List<RasterClassificationInput> AllRasterInputs()
	{
		return Layers.OfType<RasterClassificationInput>().ToList();
	}

	public void Resample(ResampleArgs args, Memory<int> outData)
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
			IResample<int> resampler = _layers[i].GetResampler();
			if (resampler == null)
			{
				if (ResampleFailureBehavior == LayerFailureBehavior.ThrowException)
				{
					throw new Exception("Layer failed to resample: " + _layers[i].Name);
				}
				if (ResampleFailureBehavior != LayerFailureBehavior.Ignore)
				{
					throw new NotImplementedException();
				}
			}
			else
			{
				resampler.Resample(args2, outData);
			}
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<int> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		if (sampleParams.IsNull())
		{
			sampleParams = SamplingParams.Default;
		}
		if (sampleParams.InitializeBufferToNoData)
		{
			outData.Span.Fill(NoDataValue.GetValueOrDefault());
		}
		SamplingParams sampleParams2 = sampleParams.CloneWithoutBufferClear();
		for (int i = 0; i < LayerCount; i++)
		{
			IResample<int> resampler = _layers[i].GetResampler();
			if (resampler == null)
			{
				if (ResampleFailureBehavior == LayerFailureBehavior.ThrowException)
				{
					throw new Exception("Layer failed to resample: " + _layers[i].Name);
				}
				if (ResampleFailureBehavior != LayerFailureBehavior.Ignore)
				{
					throw new NotImplementedException();
				}
			}
			else
			{
				resampler.SamplePoints(points, outData, sampleParams2);
			}
		}
	}

	public string Export(string directory)
	{
		if (IsInMemory)
		{
			throw new NotSupportedException("Not supported for in-memory datasets.");
		}
		if (NeedsSave())
		{
			_logger.LogWarning("Exporting from ClassificationLayer when NeedsSave() is true.");
		}
		if (!Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
		string fileName = Path.GetFileName(Filename);
		string path = Path.Combine(directory, fileName);
		File.Copy(Filename, path, overwrite: false);
		List<string> targets = new List<string>();
		Dictionary<string, string> copied = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (RasterClassificationInput input in AllRasterInputs())
		{
			string source = Path.GetFullPath(input.Filename);
			if (!copied.TryGetValue(source, out string target))
			{
				target = FileHelpers.UniqueFilename(Path.Combine(directory, Path.GetFileName(source)));
				File.Copy(source, target, overwrite: false); copied.Add(source, target);
			}
			targets.Add(target);
		}
		using (ClassificationLayer clone = new ClassificationLayer(path))
		{
			List<RasterClassificationInput> inputs = clone.AllRasterInputs();
			if (inputs.Count != targets.Count) throw new InvalidDataException("The copied classification inputs do not match the source.");
			for (int i = 0; i < inputs.Count; i++) inputs[i].RelocateSource(targets[i]);
			clone.Save();
		}
		return path;
	}

	public ClassificationLayer ExportClipped(string directory, Extent exportExt, ProgressReporter pr = null)
	{
		if (IsInMemory)
		{
			throw new NotSupportedException("Not supported for in-memory datasets.");
		}
		if (NeedsSave())
		{
			_logger.LogWarning("Exporting from ClassificationLayer when NeedsSave() is true.");
		}
		if (!Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
		string fileName = Path.GetFileName(Filename);
		string filename = Path.Combine(directory, fileName);
		Projection projectionMetadata = Project?.Projection;
		ClassificationLayer classificationLayer = CreateNew(filename);
		_ = classificationLayer.Name;
		foreach (IClassificationInput layer in Layers)
		{
			if (layer is RasterClassificationInput rasterClassificationInput)
			{
				string fileName2 = Path.GetFileName(rasterClassificationInput.Filename);
				string path = Path.Combine(directory, Path.GetFileName(fileName2));
				if (File.Exists(path))
				{
					throw new Exception("File already exists: " + path);
				}
				IRasterPyramid<int> resampler = rasterClassificationInput.GetResampler();
				if (exportExt.Overlaps(resampler.ValidExtent))
				{
					TiffExportEngine.ExportWithOverviews(path, resampler, new RasterDefinition(exportExt, resampler.RasterDefinition.CellSize), projectionMetadata);
					pr.ReportMessage("Clipped " + path.InDoubleQuotes());
					classificationLayer.Add(RasterClassificationInput.CreateAndScan(classificationLayer, path, rasterClassificationInput.Classification.Clone()));
				}
			}
			else
			{
				classificationLayer.Add(layer.CloneWith(classificationLayer));
			}
		}
		classificationLayer.Save();
		return classificationLayer;
	}

	public ClassificationSignature GetSignature()
	{
		ClassificationSignature classificationSignature = new ClassificationSignature();
		classificationSignature.Name = Name;
		foreach (IClassificationInput layer in Layers)
		{
			classificationSignature.LayerSignatures.Add(GetInputSignature(layer));
		}
		return classificationSignature;
	}

	public static ClassificationInputSignature GetInputSignature(IClassificationInput input, bool recomputeSignatureIfMissing = false)
	{
		ClassificationInputSignature classificationInputSignature = new ClassificationInputSignature();
		classificationInputSignature.Name = input.Name;
		classificationInputSignature.TypeName = input.TypeName;
		if (input is RasterClassificationInput rasterClassificationInput)
		{
			classificationInputSignature.FileSignature = rasterClassificationInput.Signature;
		}
		else
		{
			if (!(input is PolygonClassificationInput polygonClassificationInput))
			{
				throw new NotImplementedException();
			}
			classificationInputSignature.DataHash = polygonClassificationInput.GetDataHash();
		}
		return classificationInputSignature;
	}

	public override void DisposeAndDeleteAllBackingFiles()
	{
		base.DisposeAndDeleteAllBackingFiles();
		if (IsInMemory)
		{
			return;
		}
		foreach (RasterClassificationInput allRasterInput in AllRasterInputs())
		{
			allRasterInput.Dispose();
			if (!FileHelpers.TryDelete(allRasterInput.Filename))
			{
				_logger.LogWarning("Could not delete classification raster '{Filename}' for layer '{LayerName}'; it is still in use.", allRasterInput.Filename, Name);
			}
		}
	}
}
