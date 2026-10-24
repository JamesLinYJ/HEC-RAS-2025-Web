using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Geospatial;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.IO.ORM;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Layers.Terrains;
using TiffBinaryReader;
using Utility;
using Utility.Extensions;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Layers;

public class Terrain : CoreLayerBase, IResample<float>, ISurface<float>, IDisposable, ISpatialVersion
{
	private new static readonly ILogger _logger = LogHelper.GetLogger("Terrain");

	private const string TerrainResampleLockFailureLogMessage = "Terrain {TerrainName} failed to acquire lock. This can happen when modifying a terrain layer during a resampling operation.";

	public const string HDF5Group = "/Terrain";

	public static readonly string FileVersion = Project.DevFileVersion;

	public const string AttributeDatasetName = "Attributes";

	public const string Extension = ".h5";

	public const string S3Extension = ".s3";

	public const string S3ExtensionSearchPattern = "*.s3";

	private readonly Versioned<Extent> _extVer = new Versioned<Extent>();

	private CollectionVersionTracker _inputsVersionTracker = new CollectionVersionTracker();

	private readonly List<ITerrainInput> _layers = new List<ITerrainInput>();

	private long _versionXy;

	private Freezer _freezer;

	private int _layerInputHash;

	public Extent ValidExtent => _extVer.Get(VersionXY, Layers, (IReadOnlyList<ITerrainInput> lyrs) => Extent.Union(lyrs.Select((ITerrainInput l) => l.GetResampler()?.ValidExtent ?? Extent.NullExtent)));

	public float? NoDataValue => -9999f;

	public IReadOnlyList<ITerrainInput> Layers => _layers;

	public int LayerCount => Layers.Count;

	public LayerFailureBehavior ResampleFailureBehavior { get; set; }

	public long VersionXY
	{
		get
		{
			if (!_freezer.IsFrozen)
			{
				return _versionXy + _inputsVersionTracker.GetVersion(_layers);
			}
			return _freezer.FrozenVersionXY;
		}
	}

	public static string MakeFilename(string projectDir, string terrName)
	{
		return Project.MakeFilename<Terrain>(projectDir, terrName, ".h5");
	}

	protected override string GetHDF5Group()
	{
		return "/Terrain";
	}

	public override string GetCurrentFileFormatVersion()
	{
		return FileVersion;
	}

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	private Terrain(InstantiationMode mode)
		: base(mode)
	{
	}

	public Terrain(string filename, Project project = null)
		: base(filename, project)
	{
	}

	public static Terrain CreateInMemory(string name = "")
	{
		return new Terrain(InstantiationMode.InMemory)
		{
			_name = name
		};
	}

	public static Terrain CreateNew(string filename)
	{
		CoreLayerBase.VerifyCanCreateNew(filename);
		Terrain terrain = new Terrain(InstantiationMode.CreateNewFile);
		terrain.InitializeAndSaveAsNew(filename);
		return terrain;
	}

	public static Terrain FromS3(string s3File)
	{
		throw new NotImplementedException();
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

	public static Terrain Load(NativeResourceWrapper<H5Reader> reader, string name = "")
	{
		Terrain terrain = new Terrain(InstantiationMode.LoadFromDataProvider);
		terrain._h5Wrapper = reader;
		terrain.InitializeData();
		if (!string.IsNullOrEmpty(name))
		{
			terrain._name = name;
		}
		return terrain;
	}

	protected override void LoadInternal(H5Reader h5r)
	{
		if (IsV6TerrainFile(h5r))
		{
			Parse_V6(h5r);
			return;
		}
		foreach (TerrainInputDefinition read in FeatureIO.Read<TerrainInputDefinition>(h5r, "/Terrain"))
		{
			ITerrainInput terrainInput = read.MakeInput(this, h5r);
			if (terrainInput == null)
			{
				_logger.LogWarning("Could not parse/read terrain input {Name}, {Type}, {Location}", read.Name, read.Type, read.Location);
			}
			else
			{
				Add(terrainInput);
			}
		}
		ScanLayerLoadErrors();
	}

	private void ScanLayerLoadErrors()
	{
		ClearLoadErrorsOf(FileMessageCodes.FileNotFound);
		foreach (ITerrainInput layer in Layers)
		{
			if (layer is RasterTerrainInput rasterTerrainInput && !File.Exists(rasterTerrainInput.Filename))
			{
				AddLoadError(FileMessageCodes.GetFileNotFound(rasterTerrainInput.Filename));
			}
		}
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
			combine = HashCode.Combine(combine, dataHash);
		}
		return combine;
	}

	protected override bool NeedsSaveInternal()
	{
		return LoadLayerHash() != _layerInputHash;
	}

	public void CleanTerrain(TerrainInputDefinition[] defs, H5Writer hw)
	{
		List<string> stringList = new List<string>();
		foreach (TerrainInputDefinition terrainInputDefinition in defs)
		{
			if (terrainInputDefinition != null)
			{
				string name = terrainInputDefinition.Name;
				stringList.Add(name);
			}
		}
		foreach (string fullyQualifiedGroupName in hw.GetFullyQualifiedGroupNames("/Terrain"))
		{
			if (TerrainInputDefinition.CheckValidGroup(hw.ReadAllAttributes(fullyQualifiedGroupName)))
			{
				stringList.Add(fullyQualifiedGroupName);
			}
		}
		hw.CleanGroup("/Terrain", stringList);
	}

	protected override void SaveInternal(H5Writer hw)
	{
		CheckHealRasterFileSignaturesInternal(forceRecomputeHash: false, out var _);
		TerrainInputDefinition[] fromInputs = TerrainInputDefinition.FromInputs("/Terrain", _layers);
		CleanTerrain(fromInputs, hw);
		List<Exception> failures = new List<Exception>();
		for (int i = 0; i < _layers.Count; i++)
		{
			ITerrainInput terrainInput = _layers[i];
			try
			{
				string location = fromInputs[i].Location;
				terrainInput.Save(hw, location);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Layer failed to save: {LayerName}", terrainInput.Name);
				failures.Add(new IOException("Failed to save input " + terrainInput.Name, exception));
			}
		}
		if (failures.Count > 0) throw new AggregateException("One or more model inputs failed to save.", failures);
		FeatureIO.Write(hw, "/Terrain", fromInputs);
		_layerInputHash = LoadLayerHash();
	}

	public void ExportFullCopy(string filename)
	{
		if (File.Exists(filename))
		{
			throw new Exception("File " + filename + " already exists.");
		}
		string directoryName = Path.GetDirectoryName(filename);
		SaveAs(filename);
		foreach (RasterTerrainInput allRasterInput in AllRasterInputs())
		{
			string destFileName = Path.Combine(directoryName, Path.GetFileName(allRasterInput.Filename));
			File.Copy(allRasterInput.Filename, destFileName);
		}
	}

	public Terrain ExportClipped(string filename, Extent exportExt, float resampleToCellSize = 0f, ProgressReporter pr = null)
	{
		if (File.Exists(filename))
		{
			throw new Exception("File " + filename + " already exists.");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		Projection projectionMetadata = Project?.Projection;
		string directoryName = Path.GetDirectoryName(filename);
		Terrain terrain = CreateNew(filename);
		_ = terrain.Name;
		foreach (ITerrainInput layer in Layers)
		{
			if (layer is RasterTerrainInput rasterTerrainInput)
			{
				string fileName = Path.GetFileName(rasterTerrainInput.Filename);
				string path = Path.Combine(directoryName, Path.GetFileName(fileName));
				if (File.Exists(path))
				{
					throw new Exception("File already exists: " + path);
				}
				IRasterPyramid<float> resampler = rasterTerrainInput.GetResampler();
					if (resampler == null) throw new InvalidDataException("The terrain raster cannot be read: " + rasterTerrainInput.Filename);
				if (!exportExt.Overlaps(resampler.ValidExtent))
				{
					continue;
				}
				double cellSize = resampler.RasterDefinition.CellSize;
				if (resampleToCellSize > 0f)
				{
					cellSize = resampleToCellSize;
				}
				TiffExportEngine.ExportWithOverviews(path, resampler, new RasterDefinition(exportExt, cellSize), projectionMetadata);
				bool condition = false;
				using (TiffFile tiffFile = new TiffFile(path))
				{
					if (tiffFile.Directories[0].IsEntirelyNoData<float>())
					{
						condition = true;
					}
				}
				if (condition)
				{
					pr.ReportMessage("Clipped file " + path.InDoubleQuotes() + " was entirely NoData, deleting...");
					File.Delete(path);
				}
				else
				{
					pr.ReportMessage("Clipped " + path.InDoubleQuotes());
					terrain.Add(RasterTerrainInput.CreateAndScan(terrain, path));
				}
				continue;
			}
			throw new NotSupportedException();
		}
		terrain.Save();
		return terrain;
	}

	public string CopyAll(string targetDirectory)
	{
		Directory.CreateDirectory(targetDirectory);
		string path = Path.Combine(targetDirectory, Path.GetFileName(Filename));
		File.Copy(Filename, path, overwrite: false);
		List<string> targets = new List<string>();
		Dictionary<string, string> copied = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (RasterTerrainInput input in AllRasterInputs())
		{
			string source = Path.GetFullPath(input.Filename);
			if (!copied.TryGetValue(source, out string target))
			{
				target = FileHelpers.UniqueFilename(Path.Combine(targetDirectory, Path.GetFileName(source)));
				File.Copy(source, target, overwrite: false); copied.Add(source, target);
			}
			targets.Add(target);
		}
		using (Terrain clone = new Terrain(path))
		{
			List<RasterTerrainInput> inputs = clone.AllRasterInputs();
			if (inputs.Count != targets.Count) throw new InvalidDataException("The copied terrain inputs do not match the source.");
			for (int i = 0; i < inputs.Count; i++) inputs[i].RelocateSource(targets[i]);
			clone.Save();
		}
		return path;
	}

	public List<RasterTerrainInput> AllRasterInputs()
	{
		return Layers.OfType<RasterTerrainInput>().ToList();
	}

	public int IndexOf(ITerrainInput input)
	{
		return _layers.IndexOf(input);
	}

	public void Add(ITerrainInput input)
	{
		if (input.Terrain != this)
		{
			throw new Exception("Input layer doesn't point to this terrain.");
		}
		if (!_layers.Contains(input))
		{
			_layers.Add(input);
			Interlocked.Increment(ref _versionXy);
		}
	}

	public void Remove(ITerrainInput input)
	{
		if (input.Terrain != this)
		{
			throw new Exception("Input layer doesn't point to this terrain.");
		}
		_inputsVersionTracker.OnDeleted(input);
		if (!_layers.Remove(input))
		{
			throw new Exception("Input layer doesn't exist in this terrain.");
		}
		Interlocked.Increment(ref _versionXy);
	}

	public void DebugAdd(IResample<float> lyr)
	{
		if (!IsInMemory)
		{
			throw new NotSupportedException("Generic IResamplers cannot save to disk, so the terrain must be InMemory.");
		}
		_layers.Add(new DebugTerrainInput(this, lyr));
		Interlocked.Increment(ref _versionXy);
	}

	public void Reorder(int origIndex, int newIndex)
	{
		ITerrainInput layer = _layers[origIndex];
		_layers.RemoveAt(origIndex);
		_layers.Insert(newIndex, layer);
		Interlocked.Increment(ref _versionXy);
	}

	public void Import(IList<string> filenames, TerrainRasterImportConfig config = null, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (config == null)
		{
			config = TerrainRasterImportConfig.Default();
		}
		Projection projection = TryGetProjection();
		List<RasterTerrainInput> rasterTerrainInputs = new List<RasterTerrainInput>();
		if (!config.SmartMerge)
		{
			return;
		}
		ProgressReporter pr2 = pr.SubTask("Merging Rasters", 0f, 0.5f);
		List<(string, List<int>)> smartMerge = TiffExportEngine.SmartMerge(filenames, "", pr2);
		ProgressReporter progressReporter = pr.SubTask("Reprojecting Rasters", 0.5f, 0.5f);
		int count = smartMerge.Count;
		int ordinal = 0;
		foreach (var smartMerge2 in smartMerge)
		{
			ProgressReporter pr3 = progressReporter.SubTask("Importing...", (float)ordinal / (float)count, 1f / (float)count);
			ordinal++;
			string smartMerge2Term = smartMerge2.Item1;
			string textValue = "Merged";
			string uniqueFilename = FileHelpers.UniqueFilename(Path.Combine(SourceDirectory, textValue + ".tif"));
			double lengthScaleFactor = Projection.GetLengthScaleFactor(GDALRaster.GetProjection(smartMerge2Term), projection, config.FallbackSourceUnits);
			InterpolatedRasterFileConverterConfig<float> interpolatedRasterFileConverterConfig = InterpolatedRasterFileConverterConfig<float>.Default();
			interpolatedRasterFileConverterConfig.DestinationProjection = projection;
			interpolatedRasterFileConverterConfig.UseRounding(config.Rounding);
			interpolatedRasterFileConverterConfig.ConvertScaleFactor = (float)lengthScaleFactor;
			interpolatedRasterFileConverterConfig.DestinationNoData = -9999f;
			OperationResult operationResult = TiffExportEngine.ConvertFile(smartMerge2Term, uniqueFilename, interpolatedRasterFileConverterConfig, pr3);
			if (!operationResult)
			{
				_logger.LogWarning("Could not create merged TIFF {Message}", operationResult.GetConcatenatedMessages());
				pr.ReportError(operationResult.GetDiagnosticString());
				break;
			}
			RasterTerrainInput andScan = RasterTerrainInput.CreateAndScan(this, uniqueFilename);
			rasterTerrainInputs.Add(andScan);
		}
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
			IResample<float> resampler = _layers[i].GetResampler();
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

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		SamplePoints(LayerCount, points, outData, sampleParams);
	}

	public void SamplePoints(int tillLayer, Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
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
		for (int i = 0; i < tillLayer; i++)
		{
			IResample<float> resampler = _layers[i].GetResampler();
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

	public Histogram ComputeHistogram(int pixelCount, int binCount)
	{
		return Histogram.ComputeTiled(this, pixelCount, binCount);
	}

	public static OperationResult IsValidTerrainFile(string filename)
	{
		try
		{
			if (!Path.GetExtension(filename).RoughlyEquals(".h5"))
			{
				return OperationResult.Fail(FileMessageCodes.GetFileIncorrectExtension(filename, ".h5"));
			}
			using H5Reader h5r = new H5Reader(filename);
			return IsValid(h5r);
		}
		catch
		{
			return false;
		}
	}

	public static OperationResult IsValid(H5Reader h5r)
	{
		try
		{
			if (h5r.ExistsAsGroup("/Terrain"))
			{
				return true;
			}
			return OperationResult.Fail(H5Reader.MessageCodes.GetGroupNotFound(h5r.Filename, "/Terrain"));
		}
		catch (Exception ex)
		{
			return OperationResult.Fail("Opening and checking H5 file.", ex);
		}
	}

	public static OperationResult IsValidRasRaster(string filename)
	{
		TiffFile tiffFile = null;
		try
		{
			if (!File.Exists(filename))
			{
				return OperationResult.Fail(FileMessageCodes.GetFileNotFound(filename));
			}
			OperationResult operationResult = TiffFile.TryLoad(filename, out var file, out var _, out var canReadAllDirectories);
			tiffFile = file;
			if (!operationResult.Result)
			{
				return operationResult;
			}
			if (!canReadAllDirectories)
			{
				return OperationResult.Fail("Cannot read all overviews in TIFF.");
			}
			if (file.HasParentTiff)
			{
				return OperationResult.Fail("TIFF has co-dependency (is this an OVR?)");
			}
			if (!file.Directories.All((TiffDirectory td) => td.Metadata.IsTiled))
			{
				return OperationResult.Fail("TIFF is not tiled at all pyramid levels.");
			}
			IReadOnlyList<TiffDirectory> directories = file.Directories;
			if (directories[directories.Count - 1].Metadata.TileCount != 1)
			{
				return OperationResult.Fail("TIFF does not have sufficient pyramid levels.");
			}
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Error in IsValidRasRaster");
			return OperationResult.Fail("Parsing TIFF File", ex);
		}
		finally
		{
			tiffFile?.Dispose();
		}
	}

	public Projection TryGetProjection()
	{
		if (Project != null)
		{
			return Project.Projection;
		}
		return TryGetInternalProjection();
	}

	public Projection TryGetInternalProjection()
	{
		LoadProjectMetadataFromH5Header(out var _, out var projection, out var _);
		if (projection != null)
		{
			return projection;
		}
		string[] strings = (from rti in AllRasterInputs()
			select rti.Filename).ToArray();
		if (strings.Length == 0)
		{
			return null;
		}
		string[] stringsArray = strings;
		for (int intervalIndex = 0; intervalIndex < stringsArray.Length; intervalIndex++)
		{
			_ = stringsArray[intervalIndex];
			projection = GDALRaster.GetProjection(strings[0]);
			if (projection != null)
			{
				return projection;
			}
		}
		return null;
	}

	private void OnReadLockAcquisitionFailure()
	{
		_logger.LogWarning("Terrain {TerrainName} failed to acquire lock. This can happen when modifying a terrain layer during a resampling operation.", Name);
	}

	private static bool IsV6TerrainFile(H5Reader hr)
	{
		if (hr.ExistsAsGroup("/Terrain"))
		{
			return !hr.ExistsAsDataset("/Terrain/Attributes");
		}
		return false;
	}

	private void Parse_V6(H5Reader hr)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (!IsV6TerrainFile(hr))
		{
			throw new Exception("Not a valid HEC-RAS HDF5 Terrain file (V6).");
		}
		LoadedFileVersion = "6.0";
		List<Tuple<string, int>> valueTupleList = new List<Tuple<string, int>>();
		List<string> fullyQualifiedGroupNames = hr.GetFullyQualifiedGroupNames("/Terrain");
		int ordinal = 0;
		foreach (string fullyQualifiedGroupName in fullyQualifiedGroupNames)
		{
			string tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar(fullyQualifiedGroupName, "File");
			if (string.IsNullOrEmpty(tryReadStringAttributeScalar))
			{
				continue;
			}
			if (hr.TryReadAttributeScalar(fullyQualifiedGroupName, "Priority", out int ordinal2))
			{
				valueTupleList.Add(new Tuple<string, int>(tryReadStringAttributeScalar, ordinal2));
				continue;
			}
			if (ordinal == 0)
			{
				_logger.LogWarning("Error reading Terrain: Priority tag does not exist on {grp}. This is likely an old Terrain, from before 5.0 was released. The terrain files may not be parsed in the correct order.", fullyQualifiedGroupName);
				LoadedSoftwareVersion = "5.0-Beta";
			}
			valueTupleList.Add(new Tuple<string, int>(tryReadStringAttributeScalar, ordinal));
			ordinal++;
		}
		string dir = Path.GetDirectoryName(Filename);
		foreach (string textValue in (from a in valueTupleList
			orderby a.Item2
			select Path.Combine(dir, a.Item1)).ToList())
		{
			Add(RasterTerrainInput.CreateAndScan(this, textValue));
		}
		stopwatch.Stop();
		_logger?.LogInformation("Loaded (V6) \"{Name}\" in {Duration} ms.", Path.GetFileName(Filename), stopwatch.ElapsedMilliseconds);
	}

	public string GetAvailableRasterFilename(string baseName)
	{
		if (IsInMemory)
		{
			throw new Exception("Cannot create new raster files on an in-memory terrain.");
		}
		string sourceDirectory = SourceDirectory;
		string baseFilename = Name + "." + baseName;
		string extension = ".tif";
		return FileHelpers.UniqueFilename(sourceDirectory, baseFilename, extension);
	}

	public TerrainSignature GetSignature()
	{
		TerrainSignature terrainSignature = new TerrainSignature();
		terrainSignature.Name = Name;
		foreach (ITerrainInput layer in Layers)
		{
			terrainSignature.LayerSignatures.Add(GetTerrainInputSignature(layer));
		}
		return terrainSignature;
	}

	public TerrainSignature GetRecomputedSignature()
	{
		TerrainSignature terrainSignature = new TerrainSignature();
		terrainSignature.Name = Name;
		foreach (ITerrainInput layer in Layers)
		{
			TerrainInputSignature terrainInputSignature = GetTerrainInputSignature(layer);
			if (layer is RasterTerrainInput rasterTerrainInput && File.Exists(rasterTerrainInput.Filename))
			{
				terrainInputSignature.FileSignature = FileSignature.LoadDefault(rasterTerrainInput.Filename);
			}
			terrainSignature.LayerSignatures.Add(terrainInputSignature);
		}
		return terrainSignature;
	}

	public OperationResult CheckHealRasterFileSignatures(bool forceRecomputeHash = false, bool saveIfNeeded = true)
	{
		OperationResult operationResult = CheckHealRasterFileSignaturesInternal(forceRecomputeHash, out var needsSave);
		if (!operationResult)
		{
			return operationResult;
		}
		if (needsSave & saveIfNeeded)
		{
			Save();
		}
		return true;
	}

	private OperationResult CheckHealRasterFileSignaturesInternal(bool forceRecomputeHash, out bool needsSave)
	{
		needsSave = false;
		foreach (ITerrainInput layer in Layers)
		{
			if (layer is RasterTerrainInput rasterTerrainInput)
			{
				if (!File.Exists(rasterTerrainInput.Filename))
				{
					return OperationResult.Fail(FileMessageCodes.GetFileNotFound(rasterTerrainInput.Filename));
				}
				string fileName = Path.GetFileName(rasterTerrainInput.Filename);
				FileInfo fileInfo = new FileInfo(rasterTerrainInput.Filename);
				FileSignature signature = rasterTerrainInput.Signature;
				if (signature == null || signature.Name != fileName || signature.DateLastModifiedUTC != fileInfo.LastWriteTimeUtc || signature.FileSize != fileInfo.Length)
				{
					rasterTerrainInput.Signature = FileSignature.LoadDefault(rasterTerrainInput.Filename);
					needsSave = true;
				}
				else if (forceRecomputeHash && FileHelpers.ComputeRashXX3(rasterTerrainInput.Filename) != signature.RashXX3)
				{
					rasterTerrainInput.Signature = FileSignature.LoadDefault(rasterTerrainInput.Filename);
					needsSave = true;
				}
			}
		}
		return true;
	}

	public static TerrainInputSignature GetTerrainInputSignature(ITerrainInput input, bool recomputeSignatureIfMissing = false)
	{
		if (input is RasterTerrainInput rasterTerrainInput)
		{
			return new TerrainInputSignature
			{
				Name = rasterTerrainInput.Name,
				TypeName = "Raster File",
				FileSignature = rasterTerrainInput.Signature
			};
		}
		if (input is ConstantTerrainInput constantTerrainInput)
		{
			return new TerrainInputSignature
			{
				Name = constantTerrainInput.Name,
				TypeName = constantTerrainInput.TypeName,
				DataHash = constantTerrainInput.GetDataHash()
			};
		}
		if (input is DebugTerrainInput debugTerrainInput)
		{
			return new TerrainInputSignature
			{
				Name = "DEBUG INPUT - NO HASH VERIFIER",
				TypeName = debugTerrainInput.TypeName,
				DataHash = 0
			};
		}
		if (input is PolygonRasterModTerrainInput polygonRasterModTerrainInput)
		{
			return new TerrainInputSignature
			{
				Name = polygonRasterModTerrainInput.Name,
				TypeName = polygonRasterModTerrainInput.TypeName,
				DataHash = polygonRasterModTerrainInput.GetDataHash()
			};
		}
		if (input is ChannelModTerrainInput channelModTerrainInput)
		{
			return new TerrainInputSignature
			{
				Name = channelModTerrainInput.Name,
				TypeName = channelModTerrainInput.TypeName,
				DataHash = channelModTerrainInput.GetDataHash()
			};
		}
		if (input is FunctionalTerrainInput functionalTerrainInput)
		{
			return new TerrainInputSignature
			{
				Name = functionalTerrainInput.Name,
				TypeName = functionalTerrainInput.TypeName,
				DataHash = functionalTerrainInput.GetDataHash()
			};
		}
		if (input is LeveeModTerrainInput leveeModTerrainInput)
		{
			return new TerrainInputSignature
			{
				Name = leveeModTerrainInput.Name,
				TypeName = leveeModTerrainInput.TypeName,
				DataHash = leveeModTerrainInput.GetDataHash()
			};
		}
		throw new NotImplementedException();
	}

	public override void DisposeAndDeleteAllBackingFiles()
	{
		base.DisposeAndDeleteAllBackingFiles();
		if (IsInMemory)
		{
			return;
		}
		foreach (RasterTerrainInput allRasterInput in AllRasterInputs())
		{
			FileHelpers.TryDelete(allRasterInput.Filename);
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		foreach (ITerrainInput layer in Layers)
		{
			layer?.Dispose();
		}
	}
}
