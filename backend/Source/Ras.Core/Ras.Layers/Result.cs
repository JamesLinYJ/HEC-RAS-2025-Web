using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Geospatial.IO.ORM;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using H5Assist;
using H5Assist.Caching;
using H5Assist.Identifiers;
using Microsoft.Extensions.Logging;
using Ras.Engine;
using Ras.Engine.Native;
using Ras.Hydraulics;
using Ras.Layers.Results;
using Ras.Mapping;
using Ras.Output;
using Utility;
using Utility.Extensions;
using Utility.Files;
using Utility.Interfaces;
using Utility.Logging;
using Utility.Matrices;
using Utility.ORM;
using Utility.Progress;
using Utility.Threading;
using Utility.Units;

namespace Ras.Layers;

[DebuggerDisplay("{Name}")]
public class Result : IDisposable, IReadOnlyDisplayName
{
	public static class SolverNames
	{
		public static readonly string CPUExplicit = "CPU_Explicit";

		public static readonly string CPUImplicit = "CPU_Implicit";

		public static readonly string GPUExplicit = "GPU_Explicit";

		public static string ForCPU(SolverControl.EquationSet equationSet, SolverControl.TimeStepping timeStepping)
		{
			if (equationSet != SolverControl.EquationSet.DWE && timeStepping != SolverControl.TimeStepping.Implicit)
			{
				return CPUExplicit;
			}
			return CPUImplicit;
		}
	}

	public static class MessageCodes
	{
		public static readonly MessageClass NeedsMigrationWarning = new MessageClass("Result_NeedsMigration", MessageSeverity.Warning, "Result {ResultName} needs migration to V1 format");

		public static Message GetNeedsMigrationWarning(string resultName)
		{
			return NeedsMigrationWarning.Create(resultName);
		}
	}

	public enum Solvers
	{
		CPUExplicit,
		CPUImplicit,
		GPUExplicit
	}

	public delegate void TimeStepCompletedEvent(Result sender, MeshTimestepStats stats);

	public const string Extension = ".h5";

	public const string BaseOutputBlockName = "Base Output";

	public const string RootH5Group = "/Results";

	public const string RootOutputBlockGroup = "/Results/Output Blocks";

	public const string RootBaseOutputBlockGroup = "/Results/Output Blocks/Base Output";

	public const string RootMeshOutputGroup = "/Results/Output Blocks/Base Output/2D Flow Areas/Mesh";

	public const string RootInitialConditions = "/Results/Initial Conditions";

	public const string Root2DInitialConditions = "/Results/Initial Conditions/2D Flow Areas";

	public const string RootMeshInitialConditions = "/Results/Initial Conditions/2D Flow Areas/Mesh";

	public const string CellDepthInitialConditionsDataset = "Cell Depth";

	public const string FaceVelocityInitialConditionsDataset = "Face Velocity";

	private static readonly ILogger _logger = LogHelper.GetLogger("Result");

	private Project _project;

	private bool _isDisposed;

	private static readonly bool TrustResultIsImmutable = false;

	private DateTime _fileLastWriteTime;

	private string _fileSha256;

	public const string ComputeSummaryGroupName = "Compute Summary";

	public const string ComputeLogDatasetName = "Compute Log";

	private string _name;

	private readonly EnsureSingleton _compute = new EnsureSingleton();

	private NativeResourceWrapper<H5Reader> _h5Wrapper;

	private readonly object _ppLock = new object();

	private PostProcessor _postProcessor;

	public Project Project
	{
		get
		{
			return _project;
		}
		set
		{
			_project = value;
			Plan.Project = value;
			Geometry.Project = value;
		}
	}

	public Plan Plan { get; private set; }

	public Geometry Geometry { get; private set; }

	public BoundaryCondition BoundaryConditions { get; private set; }

	public Terrain AssociatedTerrain => Project?.GetBestTerrainFor(this);

	public NValueLayer AssociatedNValueLayer => Project?.DefaultNValueLayer;

	public bool IsInMemory => string.IsNullOrEmpty(Filename);

	private string ComputeLogDatasetPrefix => H5Reader.PathCombine("/Results", "Compute Summary", "Compute Log");

	[Metadata(PathRelativeTarget = "Compute Summary")]
	public ComputeSummary ComputeSummary { get; set; }

	public MeshWaterState InitialConditions { get; set; }

	public bool HasComputed
	{
		get
		{
			if (BaseOutputBlock != null)
			{
				return BaseOutputBlock.ProfileCount > 0;
			}
			return false;
		}
	}

	public List<IHorizontalOutputBlock> OutputBlocks { get; private set; }

	public IHorizontalOutputBlock BaseOutputBlock { get; private set; }

	public int ProfileCount => BaseOutputBlock.ProfileCount;

	public string ShortID => Plan.ShortID;

	public List<Result> ChildResults { get; private set; }

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
				throw new Exception("Can't set the result name via the property unless it's in memory.");
			}
			_name = value;
		}
	}

	string IReadOnlyDisplayName.DisplayName => Name;

	public string Filename { get; private set; }

	[Metadata("Units", Optional = true)]
	public UnitSystem EmbeddedUnitSystem { get; set; }

	public string DefaultExportDirectory => Path.Combine(Path.GetDirectoryName(Filename), Path.GetFileNameWithoutExtension(Filename)).Trim();

	public string DefaultArchiveDirectory => Path.Combine(DefaultExportDirectory, "Archive");

	internal NativeResourceWrapper<H5Reader> H5Wrapper => _h5Wrapper;

	public bool NeedsMigration => NeedsConvertToNewFormat();

	public IReadOnlyList<Message> LayerErrors
	{
		get
		{
			List<Message> messages = new List<Message>();
			if (NeedsMigration)
			{
				messages.Add(MessageCodes.GetNeedsMigrationWarning(Name));
			}
			Geometry geometry = Geometry;
			if (geometry != null && geometry.LayerErrors?.Count > 0)
			{
				messages.AddRange(Geometry.LayerErrors);
			}
			if (messages.Count <= 0)
			{
				return null;
			}
			return messages;
		}
	}

	public bool HasLoadErrors
	{
		get
		{
			IReadOnlyList<Message> layerErrors = LayerErrors;
			if (layerErrors == null)
			{
				return false;
			}
			return layerErrors.Count > 0;
		}
	}

	public event TimeStepCompletedEvent TimeStepCompleted;

	private UnitSystem GetUnits()
	{
		return Project?.Units ?? EmbeddedUnitSystem;
	}

	private Result()
	{
	}

	public Result(string filename)
	{
		Filename = filename;
		_name = Path.GetFileNameWithoutExtension(filename);
		OpenAndInit(filename);
	}

	private void OpenAndInit(string filename)
	{
		_h5Wrapper = H5ResourceWrapper.KeepOpen(filename);
		InitInternal();
		H5ResourceWrapper.ChangeToOpenOnDemand(_h5Wrapper, filename);
	}

	private void InitInternal()
	{
		Geometry = new Geometry(this);
		BoundaryConditions = new BoundaryCondition(this);
		Plan = new Plan(this);
		OutputBlocks = new List<IHorizontalOutputBlock>();
		_h5Wrapper.UseResource((H5Reader hr) =>
		{
			GeospatialH5Metadata.Read(this, hr, "/Results");
			if (ComputeSummary != null && hr.TryReadMessageLog(ComputeLogDatasetPrefix, out var entries))
			{
				ComputeSummary.LogEntries = entries;
			}
			LoadInitialConditions(hr);
			List<LegacyOutputBlock> blocks2;
			if (OutputBlock.TryLoadOutputBlocks(this, hr, out var blocks))
			{
				OutputBlocks.AddRange(blocks);
			}
			else if (LegacyOutputBlock.TryLoadOutputBlocks(this, hr, out blocks2))
			{
				OutputBlocks.AddRange(blocks2);
			}
			IHorizontalOutputBlock horizontalOutputBlock = OutputBlocks.FirstOrDefault((IHorizontalOutputBlock obb) => obb.Name == "Base Output");
			if (horizontalOutputBlock == null)
			{
				BaseOutputBlock = OutputBlock.CreateNew(this, "Base Output");
				OutputBlocks.Add(BaseOutputBlock);
			}
			else
			{
				BaseOutputBlock = horizontalOutputBlock;
			}
		});
	}

	public static OperationResult TryLoad(string filename, out Result res)
	{
		try
		{
			res = new Result();
			res.Filename = filename;
			res._name = Path.GetFileNameWithoutExtension(filename);
			res._h5Wrapper = H5ResourceWrapper.KeepOpen(filename);
			H5Reader resourceIfExists = res._h5Wrapper.ResourceIfExists;
			if (resourceIfExists == null || !resourceIfExists.SuccessfullyOpened)
			{
				res._h5Wrapper.Dispose();
				res = null;
				return OperationResult.Fail(H5Reader.MessageCodes.GetFileCorrupt(filename));
			}
			res.InitInternal();
			H5ResourceWrapper.ChangeToOpenOnDemand(res._h5Wrapper, filename);
			return true;
		}
		catch (Exception ex)
		{
			res = null;
			return OperationResult.Fail("Opening Result", ex);
		}
	}

	public static Result CreateInMemory(Geometry geom, IHorizontalOutputBlock baseOutput, string name = "")
	{
		return new Result
		{
			_name = name,
			Geometry = geom,
			BoundaryConditions = BoundaryCondition.CreateInMemory(),
			Plan = Plan.CreateInMemory(),
			OutputBlocks = new List<IHorizontalOutputBlock> { baseOutput }
		};
	}

	public void SaveAsReadyToRun(H5Writer hw)
	{
		Geometry.SaveAs(hw);
		BoundaryConditions.SaveAs(hw);
		Plan.SaveAs(hw);
		SaveInitialConditions(hw);
		UpdateSaveMetadata(hw);
	}

	public void SaveAsReadyToRun(string filename)
	{
		if (File.Exists(filename))
		{
			File.Delete(filename);
		}
		using H5Writer hw = new H5Writer(filename);
		SaveAsReadyToRun(hw);
	}

	public void RenameEmbeddedPlan(string name)
	{
		Plan.OverrideName(name);
		if (IsInMemory)
		{
			return;
		}
		using H5Writer h5Writer = new H5Writer(Filename);
		h5Writer.WriteAttribute("/Plan Data", "Name", name);
	}

	private static void InitResultFile(string filename, Plan p, Geometry g, BoundaryCondition bc, MeshWaterState mws, ProgressReporter reporter)
	{
		Result result = new Result();
		result.Plan = p;
		result.Geometry = g;
		result.BoundaryConditions = bc;
		result.InitialConditions = mws;
		result.Filename = filename;
		result.Project = p.Project;
		result.SaveAsReadyToRun(filename);
		reporter.ReportProgress(100);
	}

	private void UpdateSaveMetadata(H5Writer hw)
	{
		if (Project != null)
		{
			EmbeddedUnitSystem = Project.Units;
		}
		GeospatialH5Metadata.Write(this, hw, "/Results");
		Project?.SaveProjectMetadataToH5Header(hw);
	}

	private void SaveInitialConditions(H5Writer hw)
	{
		if (InitialConditions != null)
		{
			hw.CreateGroup("/Results/Initial Conditions/2D Flow Areas");
			string name = Geometry.FlowAreaLayer[0].Name;
			string textValue = "/Results/Initial Conditions/2D Flow Areas/" + name + "/";
			hw.CreateGroup(textValue);
			double[] cellElevation = InitialConditions.CellElevation;
			float[] floatValues = new float[cellElevation.Length];
			Buffers.ConvertAll(cellElevation, floatValues);
			Geometry.HydraulicPropertyTables.ConvertStageToDepth(floatValues);
			string datasetName = textValue + "Cell Depth";
			hw.WriteDataset(datasetName, floatValues);
			double[] faceVelocity = InitialConditions.FaceVelocity;
			if (faceVelocity != null)
			{
				float[] floatValuesArray = new float[faceVelocity.Length];
				Buffers.ConvertAll(faceVelocity, floatValuesArray);
				hw.WriteDataset(textValue + "Face Velocity", floatValuesArray);
			}
		}
	}

	public bool NeedsConvertToNewFormat()
	{
		OutputBlock ob = BaseOutputBlock as OutputBlock;
		if (ob == null)
		{
			return false;
		}
		return H5Wrapper.UseResource((H5Reader hr) => hr.ExistsAsDataset(ob.UnsteadyFlowAreaPath + "/Mesh/Water Surface"));
	}

	private static OperationResult ConvertHelperGetWselDepthDatasets(IHorizontalOutputBlock baseob, H5Writer hw, out string srcDset, out string dstDset)
	{
		int count = baseob.Geometry.FlowAreaLayer.Count;
		if (count != 1)
		{
			srcDset = "";
			dstDset = "";
			return OperationResult.Fail("Only one mesh supported at this time. Actual: " + count);
		}
		if (baseob is OutputBlock outputBlock)
		{
			if (!hw.ExistsAsGroup(outputBlock.RootPath))
			{
				srcDset = "";
				dstDset = "";
				return OperationResult.Fail("Output block " + outputBlock.Name + " does not exist in the expected location: " + outputBlock.RootPath);
			}
			string name = baseob.Geometry.FlowAreaLayer[0].Name;
			srcDset = outputBlock.UnsteadyFlowAreaPath + "/" + name + "/Water Surface";
			dstDset = outputBlock.UnsteadyFlowAreaPath + "/" + name + "/Cell Depth";
			return true;
		}
		if (baseob is LegacyOutputBlock)
		{
			throw new NotSupportedException();
		}
		srcDset = "";
		dstDset = "";
		return OperationResult.Fail("Base output block not recognized.");
	}

	public OperationResult ConvertToNewFormat(string targetFilename, ProgressReporter pr = null)
	{
		if (File.Exists(targetFilename))
		{
			throw new Exception("Target file exists.");
		}
		if (IsInMemory)
		{
			throw new Exception("Operation not supported for in-memory datasets.");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		_ = (OperationResult)true;
		File.Copy(Filename, targetFilename);
		using (H5Writer h5Writer = new H5Writer(targetFilename))
		{
			ConvertHelperGetWselDepthDatasets(BaseOutputBlock, h5Writer, out var srcDset, out var dstDset);
			if (!h5Writer.ExistsAsDataset(srcDset))
			{
				return OperationResult.Fail(H5Reader.MessageCodes.GetDatasetNotFound(targetFilename, srcDset));
			}
			using H5DatasetID dataset = h5Writer.OpenDataset(srcDset);
			long[] datasetDimensions = h5Writer.GetDatasetDimensions(dataset);
			long[] chunkSize = h5Writer.GetChunkSize(dataset);
			using H5DatasetID dataset2 = h5Writer.CreateAndOpen2dExtendibleDataset<float>(dstDset, (int)chunkSize[0], (int)chunkSize[1]);
			int datasetDimension = (int)datasetDimensions[0];
			Memory<float> floatSpan = new float[(int)datasetDimensions[1]];
			MeshPropertyTables hydraulicPropertyTables = Geometry.HydraulicPropertyTables;
			for (int i = 0; i < datasetDimension; i++)
			{
				h5Writer.ReadRow(dataset, i, floatSpan);
				hydraulicPropertyTables.ConvertStageToDepth(floatSpan);
				h5Writer.AddRow(dataset2, floatSpan);
			}
			h5Writer.Delete(srcDset);
		}
		H5Writer.Squeeze(targetFilename);
		return true;
	}

	private OperationResult LoadInitialConditions(H5Reader hr)
	{
		if (!hr.ExistsAsGroup("/Results/Initial Conditions/2D Flow Areas"))
		{
			return OperationResult.Fail(H5Reader.MessageCodes.GetGroupNotFound(hr.Filename, "/Results/Initial Conditions/2D Flow Areas"));
		}
		FlowArea flowArea = Geometry.FlowAreaLayer[0];
		Mesh mesh = flowArea.Mesh;
		string name = flowArea.Name;
		MeshPropertyTables propertyTables = flowArea.PropertyTables;
		if (propertyTables == null)
		{
			Message message = new Message("Property tables are null, cannot load initial conditions for {Result}", Name);
			_logger.LogWarning(message);
			return OperationResult.Fail(message);
		}
		string textValue = "/Results/Initial Conditions/2D Flow Areas/" + name + "/";
		if (!hr.ExistsAsGroup(textValue))
		{
			return OperationResult.Fail(H5Reader.MessageCodes.GetGroupNotFound(hr.Filename, textValue));
		}
		string textValue2 = textValue + "Cell Depth";
		if (!hr.ExistsAsDataset(textValue2))
		{
			return OperationResult.Fail(H5Reader.MessageCodes.GetGroupNotFound(hr.Filename, textValue2));
		}
		hr.ReadDataset(textValue2, out float[] values);
		InitialConditions = new MeshWaterState(mesh, propertyTables);
		InitialConditions.SetInitialFromCellDepth(values);
		string textValue3 = textValue + "Face Velocity";
		if (hr.ExistsAsDataset(textValue3))
		{
			hr.ReadDataset(textValue3, out float[] values2);
			if (values2.Length == mesh.FaceCount)
			{
				InitialConditions.SetInitialFaceVelocity(values2);
				if (_logger.IsEnabled(LogLevel.Debug))
				{
					int ordinal = 0;
					for (int i = 0; i < values2.Length; i++)
					{
						if (values2[i] != 0f)
						{
							ordinal++;
						}
					}
					_logger.LogDebug("Restored {Count} initial face velocities ({NonZero} non-zero) for {Result}.", values2.Length, ordinal, Name);
				}
			}
			else
			{
				_logger.LogWarning("Initial face velocity dataset has {Count} entries, expected {Expected}. Ignoring.", values2.Length, mesh.FaceCount);
			}
		}
		return true;
	}

	public static bool IsValid(string filename)
	{
		if (!Path.GetExtension(filename).RoughlyEquals(".h5"))
		{
			return false;
		}
		try
		{
			using H5Reader h5r = new H5Reader(filename);
			return IsValid(h5r);
		}
		catch
		{
			return false;
		}
	}

	public static bool IsValid(H5Reader h5r)
	{
		try
		{
			if (h5r.ExistsAsGroup("/Results"))
			{
				return true;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	private bool IsShaUpToDate(out string s)
	{
		if (string.IsNullOrEmpty(_fileSha256))
		{
			s = "";
			return false;
		}
		if (!TrustResultIsImmutable && !(new FileInfo(Filename).LastWriteTime == _fileLastWriteTime))
		{
			_logger.LogWarning("Result SHA256 has been computed once this process, but appears to be different now. Someone else could have written to thie file, or the result could have been computed after querying the SHA. Or it's a bug.");
			s = "";
			return false;
		}
		s = _fileSha256;
		return true;
	}

	private string InternalSetSha256(string sha)
	{
		_fileSha256 = sha;
		_fileLastWriteTime = new FileInfo(Filename).LastWriteTime;
		return sha;
	}

	public string GetFileSHA256()
	{
		if (IsInMemory)
		{
			throw new ArgumentException("Cannot compute SHA256 for in-memory files.");
		}
		return InternalSetSha256(FileHelpers.ComputeFileSHA256(Filename));
	}

	public async Task<string> GetFileSHA256Async(ProgressReporter pr = null)
	{
		if (IsInMemory)
		{
			throw new ArgumentException("Cannot compute SHA256 for in-memory files.");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		string sha = ((!Debugger.IsAttached) ? (await FileHelpers.ComputeFileSHA256Async(Filename, pr)) : (await Task.Run(() => FileHelpers.ComputeFileSHA256(Filename))));
		return InternalSetSha256(sha);
	}

	public void DebugUnsetSha()
	{
		_fileSha256 = "";
	}

	public OperationResult ArchiveResult(out string fileCreated)
	{
		string defaultArchiveDirectory = DefaultArchiveDirectory;
		if (!Directory.Exists(defaultArchiveDirectory))
		{
			Directory.CreateDirectory(defaultArchiveDirectory);
		}
		string baseFilename = Path.Combine(defaultArchiveDirectory, "Backup " + Time.CurrentDateFilenameSafe() + ".h5");
		baseFilename = FileHelpers.UniqueFilename(baseFilename);
		File.Copy(Filename, baseFilename);
		fileCreated = baseFilename;
		return true;
	}

	public static Result PrepareResult(string filename, Plan p, Geometry g, BoundaryCondition bc, MeshWaterState initialConditions, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (p.IsInMemory)
		{
			return PrepareResultInMemory(p, g, bc, initialConditions, Path.GetFileNameWithoutExtension(filename));
		}
		InitResultFile(filename, p, g, bc, initialConditions, reporter);
		return new Result(filename);
	}

	private static Result PrepareResultInMemory(Plan p, Geometry g, BoundaryCondition bc, MeshWaterState initialConditions, string name = "")
	{
		Result result = new Result();
		result._name = name;
		if (!p.IsInMemory || !g.IsInMemory || !bc.IsInMemory)
		{
			throw new Exception("Cannot compute result in-memory unless all components are also in-memory.");
		}
		g = g.CloneInMemory();
		result.Plan = p;
		result.Geometry = g;
		result.BoundaryConditions = bc;
		g.DebugSetResult(result);
		bc.DebugSetResult(result);
		MemoryOutputBlock memoryOutputBlock = MemoryOutputBlock.MockBaseOutputBlock(result);
		result.OutputBlocks = new List<IHorizontalOutputBlock> { memoryOutputBlock };
		result.BaseOutputBlock = memoryOutputBlock;
		result.InitialConditions = initialConditions;
		return result;
	}

	private SolverControl BuildSolverControl()
	{
		ComputeControl computeControl = new ComputeControl(GetUnits());
		SolverControl solverControl = new SolverControl(computeControl);
		solverControl.faceElevationMethod = Plan.FaceElevationMethod;
		solverControl.linFrictCoeff = Plan.LinearFrictionCoefficient;
		bool extraOutput = Plan.ExtraOutput;
		if (Plan.CheckMaxElevationChange > 0.0)
		{
			solverControl.checkMaxElevationChange = Plan.CheckMaxElevationChange;
		}
		solverControl.equationSet = Plan.EquationSet;
		solverControl.timeStepping = Plan.TimeStepping;
		if (solverControl.equationSet == SolverControl.EquationSet.DWE)
		{
			solverControl.timeStepping = SolverControl.TimeStepping.Implicit;
			solverControl.faceElevationMethod = FaceElevationMethod.BenAverage;
		}
		if (solverControl.timeStepping == SolverControl.TimeStepping.Implicit)
		{
			computeControl.variableDt = Plan.ImplicitAdaptiveTimestepping;
			solverControl.MaxCourant = Plan.MaxCourant;
			computeControl.dtMaxIncrease = 100.0;
			computeControl.nLookahead = 2;
			solverControl.checkSteps = false;
			solverControl.ImplicitFactor = Plan.ImplicitFactor;
			solverControl.UsePartialGrid = Plan.UsePartialGrid;
			solverControl.ActiveThresholdPartGrid = Plan.ActiveThresholdPartGrid;
			solverControl.BufferCountPartGrid = Plan.BufferCountPartGrid;
			solverControl.ElevationThresholdPartGrid = Plan.ElevationThresholdPartGrid;
			solverControl.OuterControl.IterMax = Plan.MaxOuterIterations + 1;
			if (Plan.MaxOuterIterations > 0)
			{
				if (Plan.WaterSurfaceTolerance > 0.0)
				{
					solverControl.OuterControl.tolConv = Plan.WaterSurfaceTolerance;
				}
				if (Plan.WaterSurfaceMaxTolerance > 0.0)
				{
					solverControl.OuterControl.tolMax = Plan.WaterSurfaceMaxTolerance;
				}
				if (Plan.WaterSurfaceRMSTolerance > 0.0)
				{
					solverControl.OuterControl.tolRMS = Plan.WaterSurfaceRMSTolerance;
				}
			}
			solverControl.InnerControl.IterMax = Plan.MaxInnerIterations + 1;
			if (Plan.MaxInnerIterations > 0)
			{
				if (Plan.VolumeTolerance > 0.0)
				{
					solverControl.InnerControl.tolConv = Plan.VolumeTolerance;
				}
				if (Plan.VolumeMaxTolerance > 0.0)
				{
					solverControl.InnerControl.tolMax = Plan.VolumeMaxTolerance;
				}
				if (Plan.VolumeRMSTolerance > 0.0)
				{
					solverControl.InnerControl.tolRMS = Plan.VolumeRMSTolerance;
				}
			}
		}
		else
		{
			solverControl.TimestepSafetyFactor = Plan.TimestepSafetyFactor;
			if (Plan.UseMinAdvectiveTimeStep)
			{
				solverControl.MinAdvDt = (double.IsFinite(Plan.MinAdvectiveTimeStep) ? Plan.MinAdvectiveTimeStep : (-1.0));
				if (double.IsFinite(Plan.MaxPercentWetAdvTimeStep))
				{
					solverControl.MaxRatioWetAdvDt = Plan.MaxPercentWetAdvTimeStep / 100.0;
				}
			}
			solverControl.LtsLevels = Plan.LtsLevels;
			if (Plan.LtsLevels != SolverControl.LtsLevelOptions.GlobalStepping)
			{
				if (Plan.UseFBF)
				{
					solverControl.stepType = SolverControl.StepType.FWDBACKFWD_LTS;
				}
				else
				{
					solverControl.stepType = SolverControl.StepType.FWDBACK_LTS;
				}
			}
			else if (Plan.UseFBF)
			{
				solverControl.stepType = SolverControl.StepType.FWDBACKFWD;
			}
			else
			{
				solverControl.stepType = SolverControl.StepType.FWDBACK;
			}
		}
		if (solverControl.equationSet == SolverControl.EquationSet.SWE)
		{
			if (!Plan.UseAdvection)
			{
				solverControl.advectionMethod = SolverControl.AdvectionMethod.None;
			}
			else
			{
				if (solverControl.timeStepping == SolverControl.TimeStepping.Implicit)
				{
					solverControl.advectionMethod = SolverControl.AdvectionMethod.SemiImplicitUpwind;
					solverControl.AdvectionImplicitFactor = 1.0;
					solverControl.faceTangentialVelocityMethod = SolverControl.FaceTangentialVelocityMethod.WLS;
					solverControl.faceElevationMethod = FaceElevationMethod.BenAverage;
				}
				else if (Plan.AdvectionImplicitFactor > 0.001)
				{
					solverControl.advectionMethod = SolverControl.AdvectionMethod.SemiImplicitUpwind;
					solverControl.AdvectionImplicitFactor = Plan.AdvectionImplicitFactor;
				}
				else
				{
					solverControl.advectionMethod = SolverControl.AdvectionMethod.ExplicitUpwind;
					solverControl.AdvectionImplicitFactor = 0.0;
				}
				solverControl.OutputCellDtMaxAdvection = extraOutput;
			}
			solverControl.Turbulence.Enabled = Plan.UseDiffusion;
			solverControl.Turbulence.Model = Plan.TurbulenceModel;
			solverControl.Turbulence.BaseEddyViscosity = Plan.BaseEddyViscosity;
			solverControl.Turbulence.LongMixCoeff = Plan.LongMixCoeff;
			solverControl.Turbulence.TransMixCoeff = Plan.TransMixCoeff;
			solverControl.Turbulence.HorizMixCoeff = Plan.HorizMixCoeff;
			if (Plan.UseDiffusion)
			{
				solverControl.OutputCellDtMaxDiffusion = extraOutput;
			}
			solverControl.OutputCellDtMaxCelerity = extraOutput;
			solverControl.SetCoriolisWidthLatitude(Plan.Latitude);
		}
		solverControl.parallelMethod = Plan.ParallelMethod;
		solverControl.numCores = Plan.CoreCount;
		computeControl.dtMinMicros = 1000L;
		if (SolverControl.UseBlockSolverOverride.HasValue)
		{
			solverControl.UseBlockSolver = SolverControl.UseBlockSolverOverride.Value;
		}
		return solverControl;
	}

	private OperationResult InitializeSolver(Solver solver)
	{
		OperationResult operationResult = true;
		OperationResult operationResult2 = Geometry.InitializeSolver(solver);
		if (!operationResult2)
		{
			operationResult.AddFailure(operationResult2);
		}
		OperationResult operationResult3 = BoundaryConditions.InitializeSolver(solver);
		if (!operationResult3)
		{
			operationResult.AddFailure("Failed to initialize BoundaryConditions in the solver.");
			operationResult.AddFailure(operationResult3);
		}
		return operationResult;
	}

	private OperationResult InitializeComputeDriver(ComputeDriver computeDriver)
	{
		OperationResult operationResult = true;
		OperationResult operationResult2 = Geometry.InitializeComputeDriver(computeDriver);
		if (!operationResult2)
		{
			operationResult.AddFailure(operationResult2);
		}
		OperationResult operationResult3 = BoundaryConditions.InitializeComputeDriver(computeDriver);
		if (!operationResult3)
		{
			operationResult.AddFailure("Failed to initialize BoundaryConditions in the solver.");
			operationResult.AddFailure(operationResult3);
		}
		return operationResult;
	}

	public OperationResult Compute(Action<Action> synchronizationContext = null, ProgressReporter pr = null)
	{
		ComputeSummary = new ComputeSummary();
		ComputeSummary.StartTime = DateTime.Now;
		ComputeSummary.SoftwareVersion = Project.SoftwareVersion;
		if (pr == null)
		{
			pr = new ProgressReporter();
		}
		using MessageLogCollector messageLogCollector = new MessageLogCollector(pr);
		if (BaseOutputBlock == null)
		{
			throw new Exception("Result not initialized correctly.");
		}
		if (synchronizationContext == null && !IsInMemory)
		{
			synchronizationContext = (Action a) =>
			{
				a();
			};
		}
		OutputBlock outputBlock = BaseOutputBlock as OutputBlock;
		if (outputBlock == null && !IsInMemory)
		{
			throw new Exception("Result not initialized correctly.");
		}
		IWriteableOutputBlock ob = outputBlock;
		if (IsInMemory)
		{
			ob = BaseOutputBlock as MemoryOutputBlock;
		}
		using (_compute.Start())
		{
			Geometry geometry = Geometry;
			BoundaryCondition boundaryConditions = BoundaryConditions;
			Plan plan = Plan;
			FlowArea flowArea = geometry.FlowAreaLayer[0];
			Mesh mesh = flowArea.Mesh;
			MeshPropertyTables propertyTables = flowArea.PropertyTables;
			if (propertyTables == null)
			{
				throw new Exception("No property tables, result has not been prepared for compute successfully.");
			}
			if (InitialConditions == null)
			{
				throw new Exception("No initial conditions, result has not been prepared for compute successfully.");
			}
			SolverControl solverControl = BuildSolverControl();
			DevOptionsReader devOptionsReader = DevOptionsParser.Parse(plan.DevOptions);
			if (devOptionsReader.TryGetValue("FaceElevationMethod", out var localValue))
			{
				if (Enum.TryParse<FaceElevationMethod>(localValue, ignoreCase: true, out var result))
				{
					solverControl.faceElevationMethod = result;
				}
				else
				{
					_logger.LogWarning("DevOptions: unknown FaceElevationMethod value '{Value}'", localValue);
				}
			}
			if (devOptionsReader.TryGetValue("MaxRatioWetAdvDt", out var localValue2))
			{
				if (double.TryParse(localValue2, out var result2))
				{
					solverControl.MaxRatioWetAdvDt = Math.Clamp(result2, 0.0, 1.0);
				}
				else
				{
					_logger.LogWarning("DevOptions: could not parse MaxRatioWetAdvDt value '{Value}'", localValue2);
				}
			}
			if (devOptionsReader.TryGetValue("SaveLinearSystem", out var localValue3))
			{
				if (Enum.TryParse<LinearSystemDumpMode>(localValue3, ignoreCase: true, out var result3))
				{
					if (string.IsNullOrEmpty(Filename))
					{
						_logger.LogWarning("DevOptions: SaveLinearSystem ignored for in-memory result (no output path).");
					}
					else
					{
						string path = Path.Combine(Path.GetDirectoryName(Filename), "LinearSystems");
						Directory.CreateDirectory(path);
						solverControl.SaveLinearSystem.Mode = result3;
						solverControl.SaveLinearSystem.Directory = path;
						solverControl.SaveLinearSystem.StartTime = plan.StartTime;
						solverControl.SaveLinearSystem.OutputIntervalMicros = (long)Math.Round(plan.BaseOutputInterval.TotalSeconds * 1000000.0);
					}
				}
				else
				{
					_logger.LogWarning("DevOptions: unknown SaveLinearSystem value '{Value}'", localValue3);
				}
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			Stopwatch stopwatch2 = new Stopwatch();
			Stopwatch stopwatch3 = new Stopwatch();
			if (outputBlock != null)
			{
				outputBlock.EnableWriting();
				outputBlock.TrackAllExposedDebugVariables();
			}
			bool hasKey = devOptionsReader.HasKey("UseGPU") || plan.UseGPU;
			devOptionsReader.WarnUnrecognised(_logger);
			OperationResult operationResult = null;
			if (!Plan.TryGetOutputFrequency(out var freq))
			{
				throw new Exception("Error preparing plan (or it's an old ready-to-run plan). Output interval does not evenly divide into the compute interval.");
			}
			if (hasKey)
			{
				if (solverControl.equationSet == SolverControl.EquationSet.DWE)
				{
					pr.ReportMessage("GPU Solver selected with Diffusion Wave equations. Only the Shallow Water equation set is supported on the GPU.");
					return OperationResult.Fail("GPU Solver selected with unsupported equation set.");
				}
				if (boundaryConditions.WarmupTime > TimeSpan.Zero)
				{
					pr.ReportMessage("Warm Start is not supported on the GPU solver; running without it.");
				}
				if (solverControl.timeStepping == SolverControl.TimeStepping.Implicit)
				{
					pr.ReportMessage("The GPU solver only runs the explicit scheme; the plan's implicit time-stepping settings are ignored.");
				}
				Solver solver = (Solver)solverControl.SolverFactory(null, mesh, propertyTables, InitialConditions, hasKey);
				solver.Reporter = pr;
				stopwatch2.Start();
				OperationResult operationResult2 = InitializeSolver(solver);
				if (!operationResult2)
				{
					return operationResult2;
				}
				stopwatch2.Stop();
				GPUSolver.DevMode = true;
				if (!GPUSolver.IsSupported())
				{
					throw new Exception("GPU Solver requested, but could not be initialized.");
				}
				ComputeSummary.Solver = SolverNames.GPUExplicit;
				stopwatch2.Start();
				using IExplicitSolver explicitSolver = GPUSolver.GetSolver();
				explicitSolver.Reporter = pr;
				if (synchronizationContext != null)
				{
					Track(this, explicitSolver, mesh, propertyTables, ob, synchronizationContext, freq, stopwatch3);
				}
				pr.ReportMessage("Initializing Solver (GPU)");
				solver.Prepare(pr);
				ComputeOptions opts = new ComputeOptions
				{
					UseAdapativeTimestep = true
				};
				explicitSolver.Init(mesh, solver.MeshConnectivity, propertyTables, solver.WaterState, solver.HydraulicMeshComputer, ((SolverExpSWE)solver).LocalCoordinates, solver.FaceFlags, solver.FlowBoundaryConditions, solver.StageBoundaryConditions, solver.NormalDepthBoundaryConditions, solver.BoundaryConditions.Precipitation, solver.Control, opts);
				stopwatch2.Stop();
				Stopwatch stopwatch4 = Stopwatch.StartNew();
				double computeBaseTimestepSecondsPlanTerm = plan.ComputeBaseTimestepSeconds * 1000.0 * 1000.0;
				pr.ReportMessage("Solving (GPU)");
				ExplicitSolverResult explicitSolverResult = null;
				try
				{
					explicitSolverResult = explicitSolver.Solve((long)plan.ComputeDurationSeconds, (long)computeBaseTimestepSecondsPlanTerm);
					stopwatch4.Stop();
					if (explicitSolverResult.Status == Status.OK)
					{
						pr.ReportMessage("GPU Solve took: " + stopwatch4.Elapsed.ToPrettyString());
						pr.ReportMessage(explicitSolverResult.GetStatusString());
						return true;
					}
					if (explicitSolverResult.Status == Status.NEG_VOLUME)
					{
						pr.ReportMessage("Negative volume - reduce timestep.");
						return false;
					}
					if (explicitSolverResult.Status == Status.USER_CANCELLED)
					{
						pr.ReportMessage("User cancelled operation.");
						return false;
					}
					pr.ReportMessage("Unrecognized model return code.");
					return false;
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error running compute.");
					return OperationResult.Fail("Solver", ex);
				}
				finally
				{
					if (explicitSolverResult == null)
					{
						ComputeSummary.Success = false;
						ComputeSummary.FailureReason = "Data Error";
					}
					else
					{
						ComputeSummary.Success = explicitSolverResult.Status == Status.OK;
						if (explicitSolverResult.Status != Status.OK)
						{
							ComputeSummary computeSummary = ComputeSummary;
							computeSummary.FailureReason = explicitSolverResult.Status switch
							{
								Status.USER_CANCELLED => "User Cancelled", 
								Status.NEG_VOLUME => "Model Unstable", 
								_ => "Model Error", 
							};
						}
					}
					ComputeSummary.TotalDuration = stopwatch.Elapsed;
					ComputeSummary.SolverInitDuration = stopwatch2.Elapsed;
					ComputeSummary.OutputDuration = stopwatch3.Elapsed;
					ComputeSummary.ComputeLog = messageLogCollector.Text;
					ComputeSummary.LogEntries = messageLogCollector.Entries;
					FinalizeRun(outputBlock, synchronizationContext);
				}
			}
			CheckSolverControl(solverControl, pr);
			ComputeDriver computeDriver = new ComputeDriver(mesh, propertyTables, InitialConditions, solverControl.computeControl, solverControl);
			computeDriver.Reporter = pr;
			stopwatch2.Start();
			OperationResult operationResult3 = InitializeComputeDriver(computeDriver);
			if (!operationResult3)
			{
				return operationResult3;
			}
			stopwatch2.Stop();
			if (boundaryConditions.WarmupTime > TimeSpan.Zero)
			{
				try
				{
					pr.ReportMessage("Starting Warm-Up ({WarmupTimestep})...", boundaryConditions.WarmupTime.ToPrettyString());
					WarmStart.ApplyWarmStart(computeDriver, boundaryConditions.WarmupTime.TotalSeconds, 0.5, pr);
					pr.ReportMessage("Warm-Up complete, starting main compute...");
				}
				catch (Exception ex2)
				{
					_logger.LogError(ex2, "Error running warm-up.");
					return OperationResult.Fail("WarmStart", ex2);
				}
			}
			ComputeSummary.Solver = SolverNames.ForCPU(solverControl.equationSet, solverControl.timeStepping);
			if (synchronizationContext != null)
			{
				Track(this, computeDriver, ob, synchronizationContext, freq, stopwatch3);
			}
			computeDriver.computeControl.Timestep = plan.ComputeBaseTimestepSeconds;
			computeDriver.computeControl.EndTime = plan.ComputeDurationSeconds;
			try
			{
				operationResult = computeDriver.Solve();
				if (!operationResult.Result)
				{
					pr.ReportMessage(operationResult.GetConcatenatedMessages());
				}
			}
			catch (Exception ex3)
			{
				_logger.LogError(ex3, "Error running compute.");
				return OperationResult.Fail("Solver", ex3);
			}
			finally
			{
				ComputeSummary.Success = operationResult?.Result ?? false;
				if (!ComputeSummary.Success)
				{
					if (operationResult.ContainsMessage(EngineMessageCodes.UserCancelled))
					{
						ComputeSummary.FailureReason = "User Cancelled";
					}
					else if (operationResult.ContainsMessage(EngineMessageCodes.ExceededMaxRetries) || operationResult.ContainsMessage(EngineMessageCodes.SolverCourantViolation))
					{
						ComputeSummary.FailureReason = "Model Unstable";
					}
					else
					{
						ComputeSummary.FailureReason = "Model Error";
					}
				}
				ComputeSummary.TotalDuration = stopwatch.Elapsed;
				ComputeSummary.SolverInitDuration = stopwatch2.Elapsed;
				ComputeSummary.OutputDuration = stopwatch3.Elapsed;
				ComputeSummary.GlobalRetryCount = computeDriver.globalRetryCount;
				ComputeSummary.ComputeLog = messageLogCollector.Text;
				ComputeSummary.LogEntries = messageLogCollector.Entries;
				FinalizeRun(outputBlock, synchronizationContext);
			}
			if (pr.CancelRequested)
			{
				pr.ReportOperationCancelled();
				return false;
			}
			return operationResult;
		}
	}

	private static void CheckSolverControl(SolverControl solverControl, ProgressReporter pr)
	{
		if (solverControl.equationSet == SolverControl.EquationSet.SWE)
		{
			pr.ReportMessage("Equation Set: Shallow Water Equations");
			if (solverControl.TimestepSafetyFactor < 0.001)
			{
				pr.ReportWarning("Time step safety factor < 0.001. Limiting to 0.001.");
			}
			else if (solverControl.TimestepSafetyFactor > 0.95)
			{
				pr.ReportWarning("Time step safety factor > 0.95.");
			}
			if (solverControl.advectionMethod == SolverControl.AdvectionMethod.None)
			{
				pr.ReportWarning("Advection is disabled.");
			}
			else if (solverControl.AdvectionImplicitFactor < 0.001)
			{
				solverControl.advectionMethod = SolverControl.AdvectionMethod.ExplicitUpwind;
			}
			else
			{
				solverControl.advectionMethod = SolverControl.AdvectionMethod.SemiImplicitUpwind;
			}
			if (solverControl.Turbulence.IsActive)
			{
				pr.ReportMessage("Turbulent mixing is enabled.");
			}
		}
		else if (solverControl.equationSet == SolverControl.EquationSet.DWE)
		{
			pr.ReportMessage("Equation Set: Diffusion Wave Equation");
			if (solverControl.MaxCourant < 0.1)
			{
				solverControl.MaxCourant = 0.1;
				pr.ReportWarning(" Max Courant < 0.01. Limiting to 0.01.");
			}
		}
	}

	private static void Track(Result res, ComputeDriver computeDriver, IWriteableOutputBlock ob, Action<Action> syncContext, int reportEveryNTimesteps, Stopwatch sw)
	{
		int tsCount = 0;
		computeDriver.TimestepDone = (MeshTimestep mt) =>
		{
			if (tsCount % reportEveryNTimesteps == 0)
			{
				sw.Start();
				MeshTimestep mtClone = mt.Clone(fullClone: true);
				syncContext(() =>
				{
					ob.AddTimestep(mtClone);
				});
				sw.Stop();
			}
			MeshTimestepStats stats = mt.Stats;
			syncContext(() =>
			{
				res.TimeStepCompleted?.Invoke(res, stats);
			});
			tsCount++;
		};
	}

	private static void Track(Result res, IExplicitSolver solver, Mesh m, MeshPropertyTables props, IWriteableOutputBlock ob, Action<Action> syncContext, int reportEveryNTimesteps, Stopwatch sw)
	{
		int tsCount = 0;
		long lastTMicros = 0L;
		int wetCellCount = 0;
		solver.TimestepDone = (IExplicitSolver mt) =>
		{
			long tMicro = mt.TMicro;
			if (tsCount % reportEveryNTimesteps == 0)
			{
				sw.Start();
				MeshWaterState meshWaterState = MeshWaterState.CreateEmpty(m, props);
				meshWaterState.CellElevation = new double[m.CellCount];
				meshWaterState.FaceVelocity = new double[m.FaceCount];
				mt.CopyCellWaterSurface(meshWaterState.CellElevation);
				float[] floatValues = new float[m.FaceCount];
				mt.CopyFaceVelocity(floatValues);
				for (int i = 0; i < floatValues.Length; i++)
				{
					meshWaterState.FaceVelocity[i] = floatValues[i];
				}
				meshWaterState.tMicros = tMicro;
				MeshTimestep mts = new MeshTimestep(meshWaterState, tsCount);
				wetCellCount = mts.Stats.WetCellCount;
				syncContext(() =>
				{
					ob.AddTimestep(mts);
				});
				sw.Stop();
			}
			MeshTimestepStats stats = new MeshTimestepStats
			{
				Step = tsCount,
				Time = ComputeDriver.MicrosToSeconds(tMicro),
				TimeStep = ComputeDriver.MicrosToSeconds(tMicro - lastTMicros),
				WetCellCount = wetCellCount,
				LimitingCell = -1,
				LimitingFace = -1
			};
			lastTMicros = tMicro;
			syncContext(() =>
			{
				res.TimeStepCompleted?.Invoke(res, stats);
			});
			tsCount++;
		};
	}

	private void FinalizeRun(OutputBlock eob, Action<Action> syncContext)
	{
		if (syncContext == null)
		{
			syncContext = (Action a) =>
			{
				a();
			};
		}
		syncContext(() =>
		{
			if (eob != null)
			{
				eob.TryUseWriter((H5Writer hw) =>
				{
					GeospatialH5Metadata.Write(this, hw, "/Results");
					hw.WriteMessageLog(ComputeLogDatasetPrefix, ComputeSummary?.LogEntries);
				});
				eob.FinishWriting();
			}
		});
	}

	public string ExpectedPostProcessFilename()
	{
		return DefaultExpectedPostProcessFilename(this);
	}

	public static string DefaultExpectedPostProcessFilename(Result res)
	{
		return Path.Combine(res.DefaultExportDirectory, "PostProcess.h5");
	}

	public PostProcessor GetPostProcessor()
	{
		if (IsComputing())
		{
			throw new Exception("Cannot create a PostProcess file while the solution is running.");
		}
		return LazyThreadsafe<PostProcessor>.AllocateOnce(ref _postProcessor, _ppLock, this, (Result rr) => PostProcessor.Ensure(rr, rr.ExpectedPostProcessFilename()));
	}

	public PostProcessor PeekGetPostProcessor()
	{
		return _postProcessor;
	}

	public async Task<PostProcessor> GetPostProcessorAsync(ProgressReporter pr = null)
	{
		if (IsComputing())
		{
			throw new Exception("Cannot create a PostProcess file while the solution is running.");
		}
		if (_postProcessor != null)
		{
			return _postProcessor;
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		await GetFileSHA256Async(pr);
		return GetPostProcessor();
	}

	public void DisposePostProcessor()
	{
		_postProcessor?.Dispose();
		_postProcessor = null;
	}

	public bool IsComputing()
	{
		return _compute.IsRunning;
	}

	public OperationResult SaveMap(string filename, MapTypes mapType, int profileIndex, double cellSize, Projection outProjection = null)
	{
		Terrain associatedTerrain = AssociatedTerrain;
		_ = Project.Projection;
		new ResultMapResampler(mapType, BaseOutputBlock, associatedTerrain, Geometry.FlowAreaLayer.AllPropertyTables()).AsSingleProfile(profileIndex);
		Extent hydraulicExtent = Geometry.HydraulicExtent;
		new RasterDefinition(hydraulicExtent, cellSize);
		throw new NotImplementedException();
	}

	public async Task<(OperationResult, double[])> ComputeFlowTimeSeriesAsync(Polyline pline, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		await GetPostProcessor().EnsureDefaultDatasetsAsync(pr);
		double[] flowTimeSeries;
		return (ComputeFlowTimeSeries(pline, out flowTimeSeries, pr), flowTimeSeries);
	}

	public OperationResult ComputeFlowTimeSeries(Polyline pline, out double[] flowTimeSeries, ProgressReporter pr = null)
	{
		PostProcessor postProcessor = GetPostProcessor();
		postProcessor.EnsureDefaultDatasets(pr);
		Mesh mesh = Geometry.Mesh;
		MeshPropertyTables hydraulicPropertyTables = Geometry.HydraulicPropertyTables;
		List<(int, bool)> facesAlongPolyline = mesh.FacesAlongPolyline(pline);
		int count = facesAlongPolyline.Count * 2;
		Dictionary<int, int> intByInt = new Dictionary<int, int>(count);
		int[] intValues = new int[count];
		int count2 = 0;
		foreach (var facesAlongPolyline2 in facesAlongPolyline)
		{
			Face face = mesh.Faces[facesAlongPolyline2.Item1];
			int cellA = face.cellA;
			if (cellA != -1 && intByInt.TryAdd(cellA, count2))
			{
				intValues[count2] = cellA;
				count2++;
			}
			int cellB = face.cellB;
			if (cellB != -1 && intByInt.TryAdd(cellB, count2))
			{
				intValues[count2] = cellB;
				count2++;
			}
		}
		Memory<float>[] floatSpanArray = new Memory<float>[count2];
		for (int i = 0; i < count2; i++)
		{
			floatSpanArray[i] = new float[postProcessor.ProfileCount];
		}
		postProcessor.GetCellWaterSurfaceTimeSeries(intValues.AsMemory().Slice(0, count2), floatSpanArray);
		Memory<FaceValues>[] faceValuesSpanArray = new Memory<FaceValues>[facesAlongPolyline.Count];
		Memory<float>[] floatSpanArraySpan = new Memory<float>[facesAlongPolyline.Count];
		HydraulicMeshComputer metadataCache = mesh.MetadataCache;
		for (int j = 0; j < facesAlongPolyline.Count; j++)
		{
			int facesAlongPolylineTerm = facesAlongPolyline[j].Item1;
			Face face2 = mesh.Faces[facesAlongPolylineTerm];
			int cellA2 = face2.cellA;
			int cellB2 = face2.cellB;
			Memory<float> cellAWSEL = null;
			Memory<float> cellBWSEL = null;
			if (cellA2 != -1)
			{
				cellAWSEL = floatSpanArray[intByInt[cellA2]];
			}
			if (cellB2 != -1)
			{
				cellBWSEL = floatSpanArray[intByInt[cellB2]];
			}
			floatSpanArraySpan[j] = new float[postProcessor.ProfileCount];
			Memory<FaceValues> outFaceWSEL = (faceValuesSpanArray[j] = new FaceValues[postProcessor.ProfileCount]);
			metadataCache.FaceComputes.WSELTimeSeries(facesAlongPolylineTerm, cellAWSEL, cellBWSEL, hydraulicPropertyTables, outFaceWSEL);
		}
		postProcessor.ReadTransposedDataset(OutputVariables.Velocity, facesAlongPolyline.Select(((int, bool) ft) => ft.Item1).ToArray(), floatSpanArraySpan);
		Memory<float>[] floatSpanArrayArray = new Memory<float>[facesAlongPolyline.Count];
		for (int intervalIndex = 0; intervalIndex < facesAlongPolyline.Count; intervalIndex++)
		{
			(int, bool) tuple = facesAlongPolyline[intervalIndex];
			int tupleTerm = tuple.Item1;
			Face face3 = mesh.Faces[tupleTerm];
			Span<FaceValues> faceValuesSpan = faceValuesSpanArray[intervalIndex].Span;
			Span<float> floatSpan = floatSpanArraySpan[intervalIndex].Span;
			float[] floatValues = new float[postProcessor.ProfileCount];
			floatSpanArrayArray[intervalIndex] = floatValues;
			float sign = 1f;
			if (tuple.Item2)
			{
				sign = -1f;
			}
			bool cellBFace3Term = face3.cellB == -1;
			for (int intervalIndex2 = 0; intervalIndex2 < postProcessor.ProfileCount; intervalIndex2++)
			{
				FaceValues faceValues = faceValuesSpan[intervalIndex2];
				float valueA = (cellBFace3Term ? faceValues.ValueA : faceValues.WselValueForFlowTransfer());
				float computed = floatSpan[intervalIndex2];
				float interpolateFaceArea = hydraulicPropertyTables.InterpolateFaceArea(tupleTerm, valueA);
				if (valueA == -9999f || computed == -9999f)
				{
					floatValues[intervalIndex2] = 0f;
				}
				else
				{
					floatValues[intervalIndex2] = interpolateFaceArea * computed * sign;
				}
			}
		}
		double[] flowTimeSeriesArray = new double[postProcessor.ProfileCount];
		Memory<float>[] floatSpanArray2 = floatSpanArrayArray;
		for (int intervalIndex3 = 0; intervalIndex3 < floatSpanArray2.Length; intervalIndex3++)
		{
			Memory<float> floatSpanArray3 = floatSpanArray2[intervalIndex3];
			for (int intervalIndex4 = 0; intervalIndex4 < postProcessor.ProfileCount; intervalIndex4++)
			{
				flowTimeSeriesArray[intervalIndex4] += floatSpanArray3.Span[intervalIndex4];
			}
		}
		flowTimeSeries = flowTimeSeriesArray;
		return true;
	}

	public List<ProfileFaceValue> ComputeFaceFlowsAtProfile(Polyline pline, int profileIndex, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		PostProcessor postProcessor = GetPostProcessor();
		postProcessor.EnsureDefaultDatasets(pr);
		Mesh mesh = Geometry.Mesh;
		MeshPropertyTables hydraulicPropertyTables = Geometry.HydraulicPropertyTables;
		List<ProfileFaceRange> facesAlongPolylineWithStations = mesh.FacesAlongPolylineWithStations(pline);
		if (facesAlongPolylineWithStations.Count == 0)
		{
			return new List<ProfileFaceValue>();
		}
		int count = facesAlongPolylineWithStations.Count * 2;
		Dictionary<int, int> intByInt = new Dictionary<int, int>(count);
		int[] intValues = new int[count];
		int count2 = 0;
		foreach (ProfileFaceRange facesAlongPolylineWithStation in facesAlongPolylineWithStations)
		{
			Face face = mesh.Faces[facesAlongPolylineWithStation.Face];
			int cellA = face.cellA;
			if (cellA != -1 && intByInt.TryAdd(cellA, count2))
			{
				intValues[count2] = cellA;
				count2++;
			}
			int cellB = face.cellB;
			if (cellB != -1 && intByInt.TryAdd(cellB, count2))
			{
				intValues[count2] = cellB;
				count2++;
			}
		}
		Memory<float>[] floatSpanArray = new Memory<float>[count2];
		for (int i = 0; i < count2; i++)
		{
			floatSpanArray[i] = new float[postProcessor.ProfileCount];
		}
		postProcessor.GetCellWaterSurfaceTimeSeries(intValues.AsMemory().Slice(0, count2), floatSpanArray);
		HydraulicMeshComputer metadataCache = mesh.MetadataCache;
		Memory<FaceValues>[] faceValuesSpanArray = new Memory<FaceValues>[facesAlongPolylineWithStations.Count];
		for (int j = 0; j < facesAlongPolylineWithStations.Count; j++)
		{
			int face2 = facesAlongPolylineWithStations[j].Face;
			Face face3 = mesh.Faces[face2];
			int cellA2 = face3.cellA;
			int cellB2 = face3.cellB;
			Memory<float> cellAWSEL = ((cellA2 != -1) ? floatSpanArray[intByInt[cellA2]] : ((Memory<float>)null));
			Memory<float> cellBWSEL = ((cellB2 != -1) ? floatSpanArray[intByInt[cellB2]] : ((Memory<float>)null));
			Memory<FaceValues> outFaceWSEL = (faceValuesSpanArray[j] = new FaceValues[postProcessor.ProfileCount]);
			metadataCache.FaceComputes.WSELTimeSeries(face2, cellAWSEL, cellBWSEL, hydraulicPropertyTables, outFaceWSEL);
		}
		int[] intValuesArray = facesAlongPolylineWithStations.Select((ProfileFaceRange fr) => fr.Face).ToArray();
		Memory<float>[] floatSpanArraySpan = new Memory<float>[facesAlongPolylineWithStations.Count];
		for (int intervalIndex = 0; intervalIndex < facesAlongPolylineWithStations.Count; intervalIndex++)
		{
			floatSpanArraySpan[intervalIndex] = new float[postProcessor.ProfileCount];
		}
		postProcessor.ReadTransposedDataset(OutputVariables.Velocity, intValuesArray, floatSpanArraySpan);
		List<ProfileFaceValue> profileFaceValues = new List<ProfileFaceValue>(facesAlongPolylineWithStations.Count);
		for (int intervalIndex2 = 0; intervalIndex2 < facesAlongPolylineWithStations.Count; intervalIndex2++)
		{
			ProfileFaceRange faceLocation = facesAlongPolylineWithStations[intervalIndex2];
			int face4 = faceLocation.Face;
			Face face5 = mesh.Faces[face4];
			bool cellBFace5Term = face5.cellB == -1;
			FaceValues faceValues = faceValuesSpanArray[intervalIndex2].Span[profileIndex];
			float valueA = (cellBFace5Term ? faceValues.ValueA : faceValues.WselValueForFlowTransfer());
			float profileIndexTerm = floatSpanArraySpan[intervalIndex2].Span[profileIndex];
			double interpolateFaceAreaTerm = 0.0;
			bool isForward = faceLocation.FPStartIdx == face5.fpA;
			if (valueA != -9999f && profileIndexTerm != -9999f)
			{
				float interpolateFaceArea = hydraulicPropertyTables.InterpolateFaceArea(face4, valueA);
				float isForwardTerm = ((!isForward) ? 1 : (-1));
				interpolateFaceAreaTerm = interpolateFaceArea * profileIndexTerm * isForwardTerm;
			}
			profileFaceValues.Add(new ProfileFaceValue
			{
				FaceLocation = faceLocation,
				Value = interpolateFaceAreaTerm,
				IsForward = isForward
			});
		}
		return profileFaceValues;
	}

	public List<Result> TryIdentifyRelatedResults()
	{
		if (IsInMemory)
		{
			return null;
		}
		List<Result> result = new List<Result>();
		if (Filename.EndsWith(".low.h5"))
		{
			_ = Filename.Substring(Filename.Length - "low.h5".Length) + "h5";
			_ = Filename.Substring(Filename.Length - "low.h5".Length) + "high.h5";
			return result;
		}
		if (!Filename.EndsWith(".high.h5"))
		{
			Path.ChangeExtension(Filename, ".low.h5");
			Path.ChangeExtension(Filename, ".high.h5");
		}
		return result;
	}

	public string EnsureExportDirectory()
	{
		string defaultExportDirectory = DefaultExportDirectory;
		if (!Directory.Exists(defaultExportDirectory))
		{
			Directory.CreateDirectory(defaultExportDirectory);
		}
		return defaultExportDirectory;
	}

	public void LoadProjectMetadataFromH5Header(out string projectName, out Projection projection, out UnitSystem units)
	{
		Geometry.LoadProjectMetadataFromH5Header(out projectName, out projection, out units);
	}

	public void DebugSaveBiggerResult(string newFilename, int nRepeats)
	{
		if (File.Exists(newFilename))
		{
			File.Delete(newFilename);
		}
		File.Copy(Filename, newFilename);
		H5Writer hw = new H5Writer(newFilename);
		int baseTimestepCount;
		int newTimestepCount;
		try
		{
			OutputBlock outputBlock = BaseOutputBlock as OutputBlock;
			string datasetName = OutputBlock.TimeDataset(outputBlock.RootPath);
			hw.ReadDataset(datasetName, out double[] values);
			baseTimestepCount = values.Length;
			newTimestepCount = baseTimestepCount * nRepeats;
			double computed = values[^1] - values[^2];
			double computed2 = values[^1];
			double[] doubleValues = new double[newTimestepCount];
			values.CopyTo(doubleValues, 0);
			for (int i = values.Length; i < doubleValues.Length; i++)
			{
				computed2 = (doubleValues[i] = computed2 + computed);
			}
			hw.WriteDataset(datasetName, doubleValues);
			PlusUp(outputBlock.GetTimeSeriesDatasetName(OutputVariables.Depth));
			PlusUp(outputBlock.GetTimeSeriesDatasetName(OutputVariables.Velocity));
		}
		finally
		{
			if (hw != null)
			{
				((IDisposable)hw).Dispose();
			}
		}
		void PlusUp(string ds)
		{
			hw.ReadDataset(ds, out float[,] values2);
			int length = values2.GetLength(1);
			float[,] floatValues = new float[newTimestepCount, length];
			for (int j = 0; j < newTimestepCount; j++)
			{
				for (int k = 0; k < length; k++)
				{
					floatValues[j, k] = values2[j % baseTimestepCount, k];
				}
			}
			hw.WriteDataset(ds, floatValues);
		}
	}

	public void DisposeAndDeleteAllBackingFiles()
	{
		Dispose();
		if (!IsInMemory && File.Exists(Filename))
		{
			FileHelpers.TryDelete(Filename);
		}
	}

	public void ReplaceFileContentsWith(string newFile)
	{
		if (IsInMemory)
		{
			throw new ArgumentException("Cannot invoke method for in-memory result.");
		}
		CleanupInternal();
		File.Copy(newFile, Filename, overwrite: true);
		OpenAndInit(Filename);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			CleanupInternal();
			_isDisposed = true;
		}
	}

	private void CleanupInternal()
	{
		_h5Wrapper?.Dispose();
		_h5Wrapper = null;
		if (OutputBlocks != null)
		{
			foreach (IHorizontalOutputBlock outputBlock2 in OutputBlocks)
			{
				if (outputBlock2 is OutputBlock outputBlock)
				{
					outputBlock.Dispose();
				}
			}
			OutputBlocks = null;
		}
		_fileLastWriteTime = DateTime.MinValue;
		_fileSha256 = "";
	}

	~Result()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
