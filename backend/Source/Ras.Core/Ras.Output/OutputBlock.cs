using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Geospatial;
using Geospatial.Vectors;
using H5Assist;
using H5Assist.Caching;
using Microsoft.Extensions.Logging;
using Ras.Engine;
using Ras.Engine.Structures.Culverts;
using Ras.Hydraulics;
using Ras.Layers;
using Utility;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;

namespace Ras.Output;

public class OutputBlock : IHorizontalOutputBlock, IOutputBlock, IDiscreteTimestepsWithEvent, IDiscreteTimesteps, IWriteableOutputBlock, IDisposable
{
	public const string H5OutputBlockRootDirectory = "/Results/Output Blocks/";

	public const string H5BaseOutputBlock = "/Results/Output Blocks/Base Output/";

	public const string H5OutputDebugSubdirectory = "DEBUG/";

	public const string H5CulvertsSubdirectory = "Culverts/";

	public const string H5CulvertOutsideDepth = "Outside Depth";

	public const string H5CulvertFaceFlow = "Face Flow";

	public const string H5CulvertFaceVelocity = "Face Velocity";

	public const int RowsPerChunk = 10;

	private static readonly ILogger _logger = LogHelper.GetLogger("OutputBlock");

	private readonly List<string> _profileNames;

	private readonly List<DateTime> _timesteps;

	private bool _isDisposed;

	private readonly H5RowCache<float> _rowCache;

	private H5Writer _writer;

	private H5AppendCache<float> _depthCache;

	private H5AppendCache<float> _velCache;

	private H5AppendCache<float> _culvertFlowCache;

	private H5AppendCache<float> _culvertOutsideDepthCache;

	private H5AppendCache<float> _culvertOutletVelocityCache;

	private int _culvertBarrelCount;

	private SummaryStatistics _summary = new SummaryStatistics();

	private double[] _maxCellDepth;

	private bool? _hasCulvertOutput;

	private int _culvertBarrelCountRead;

	private Dictionary<string, Tuple<Func<MeshTimestep, Memory<float>>, H5AppendCache<float>>> _debugVariables;

	public string Name { get; }

	public Result Result { get; init; }

	public Geometry Geometry => Result.Geometry;

	public IList<string> ProfileNames => _profileNames;

	public int ProfileCount => ProfileNames.Count;

	public IReadOnlyList<DateTime> Times => _timesteps;

	public SummaryStatistics Summary => _summary;

	public string RootPath { get; private set; }

	public string UnsteadyFlowAreaPath { get; private set; }

	public string CulvertsGroupPath => RootPath + "/Culverts/";

	public bool HasCulvertOutput
	{
		get
		{
			if (_hasCulvertOutput.HasValue)
			{
				return _hasCulvertOutput.Value;
			}
			if (IsWriting())
			{
				_hasCulvertOutput = _culvertFlowCache != null;
				return _hasCulvertOutput.Value;
			}
			try
			{
				using H5Reader h5Reader = new H5Reader(Result.Filename);
				string culvertsGroupPathTerm = CulvertsGroupPath + "Face Flow";
				if (!h5Reader.ExistsAsDataset(culvertsGroupPathTerm))
				{
					_hasCulvertOutput = false;
					return false;
				}
				long[] datasetDimensions = h5Reader.GetDatasetDimensions(culvertsGroupPathTerm);
				if (datasetDimensions.Length >= 2)
				{
					_culvertBarrelCountRead = (int)datasetDimensions[1];
				}
				_hasCulvertOutput = true;
				return true;
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Error checking for culvert output in {Path}", CulvertsGroupPath);
				_hasCulvertOutput = false;
				return false;
			}
		}
	}

	public int CulvertBarrelCount
	{
		get
		{
			if (IsWriting())
			{
				return _culvertBarrelCount;
			}
			if (!HasCulvertOutput)
			{
				return 0;
			}
			return _culvertBarrelCountRead;
		}
	}

	public event TimestepAddedEvent TimestepAdded;

	private OutputBlock(Result res, string name)
	{
		Result = res;
		Name = name;
		_timesteps = new List<DateTime>();
		_profileNames = new List<string>();
		_rowCache = new H5RowCache<float>(res.Filename);
		InitPaths();
	}

	public static OutputBlock CreateNew(Result res, string name)
	{
		return new OutputBlock(res, name);
	}

	public static bool TryLoadOutputBlocks(Result res, H5Reader hr, out List<OutputBlock> blocks, ProgressReporter err = null)
	{
		try
		{
			if (err == null)
			{
				err = ProgressReporter.None();
			}
			if (!hr.ExistsAsGroup("/Results/Output Blocks/"))
			{
				blocks = null;
				return false;
			}
			blocks = new List<OutputBlock>();
			foreach (string groupName in hr.GetGroupNames("/Results/Output Blocks/"))
			{
				if (TryLoadOutputBlock(hr, groupName, res, out var block, err))
				{
					blocks.Add(block);
				}
			}
			return true;
		}
		catch
		{
			blocks = null;
			return false;
		}
	}

	private static bool TryLoadOutputBlock(H5Reader h5r, string name, Result res, out OutputBlock block, ProgressReporter err)
	{
		_ = "/Results/Output Blocks/" + name;
		block = new OutputBlock(res, name);
		string rootPathBlockTerm = block.RootPath + "/Time";
		double[] values = null;
		if (h5r.ExistsAsDataset(rootPathBlockTerm))
		{
			h5r.ReadDataset(rootPathBlockTerm, out values);
			DateTime startTime = res.Plan.StartTime;
			block._timesteps.AddRange(values.Select((double d) => startTime.AddDays(d)));
			block._profileNames.AddRange(block.Times.Select((DateTime ts) => ts.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture)));
			block._summary = SummaryStatistics.TryReadFrom(h5r, block.RootPath + "/");
			return true;
		}
		return false;
	}

	public static void WriteTimestepsHowYouExpectToReadThem(H5Writer writer, string blockName, IList<DateTime> times)
	{
		if (!times.IsNullOrEmpty())
		{
			string blockRootPath = RootBlockOutputPath(blockName);
			DateTime start = times[0];
			writer.WriteDataset(TimeDataset(blockRootPath), times.Select((DateTime t) => (t - start).TotalDays).ToArray());
		}
	}

	public static string TimeDataset(string blockRootPath)
	{
		return blockRootPath + "/Time";
	}

	public void EnableWriting()
	{
		if (IsWriting())
		{
			throw new Exception("H5Writer double initialized - error?");
		}
		if (Geometry.FlowAreaLayer.Count != 1)
		{
			throw new Exception("Only supporting one mesh currently.");
		}
		FlowArea flowArea = Geometry.FlowAreaLayer[0];
		string name = flowArea.Name;
		Mesh mesh = flowArea.Mesh;
		string rootMeshOutputPath = RootMeshOutputPath(Name, name);
		string dataset = rootMeshOutputPath + OutputVariables.Depth.Name;
		string dataset2 = rootMeshOutputPath + OutputVariables.Velocity.Name;
		_writer = MakeH5Writer();
		_depthCache = new H5AppendCache<float>(_writer, dataset, 10, mesh.CellCount);
		_velCache = new H5AppendCache<float>(_writer, dataset2, 10, mesh.FaceCount);
		_maxCellDepth = new double[mesh.CellCount];
		Array.Fill(_maxCellDepth, -9999.0);
		CulvertBarrelLayer culvertBarrelLayer = Geometry?.CulvertBarrelLayer;
		if (culvertBarrelLayer != null && culvertBarrelLayer.Count > 0)
		{
			_culvertBarrelCount = culvertBarrelLayer.Count;
			string rootPathTerm = RootPath + "/Culverts/";
			_culvertOutsideDepthCache = new H5AppendCache<float>(_writer, rootPathTerm + "Outside Depth", 10, _culvertBarrelCount * 2);
			_culvertFlowCache = new H5AppendCache<float>(_writer, rootPathTerm + "Face Flow", 10, _culvertBarrelCount);
			_culvertOutletVelocityCache = new H5AppendCache<float>(_writer, rootPathTerm + "Face Velocity", 10, _culvertBarrelCount);
		}
	}

	public bool TryUseWriter(Action<H5Writer> writeAction)
	{
		H5Writer writer = _writer;
		if (writer == null)
		{
			_logger.LogWarning("Output block for {Result} is not open for writing, skipping write.", Result?.Name);
			return false;
		}
		writeAction(writer);
		return true;
	}

	public void TrackAllExposedDebugVariables()
	{
		if (!IsWriting())
		{
			throw new Exception("Need to enable writing before tracking debug vars.");
		}
		if (_debugVariables == null)
		{
			_debugVariables = new Dictionary<string, Tuple<Func<MeshTimestep, Memory<float>>, H5AppendCache<float>>>();
		}
		PropertyInfo[] properties = typeof(MeshTimestep).GetProperties();
		foreach (PropertyInfo propType in properties)
		{
			if (propType.GetCustomAttribute(typeof(ExposeAttribute)) is ExposeAttribute exposeAttribute)
			{
				Func<MeshTimestep, Memory<float>> getData = (MeshTimestep mts) => ToFloatArray(propType.GetValue(mts));
				TrackDebugVariable(propType.Name, exposeAttribute.ElementType, getData);
			}
		}
		FieldInfo[] fields = typeof(MeshTimestep).GetFields();
		foreach (FieldInfo propType2 in fields)
		{
			if (propType2.GetCustomAttribute(typeof(ExposeAttribute)) is ExposeAttribute exposeAttribute2)
			{
				Func<MeshTimestep, Memory<float>> getData2 = (MeshTimestep mts) => ToFloatArray(propType2.GetValue(mts));
				TrackDebugVariable(propType2.Name, exposeAttribute2.ElementType, getData2);
			}
		}
		properties = typeof(MeshWaterState).GetProperties();
		foreach (PropertyInfo propType3 in properties)
		{
			if (propType3.GetCustomAttribute(typeof(ExposeAttribute)) is ExposeAttribute exposeAttribute3)
			{
				Func<MeshTimestep, Memory<float>> getData3 = (MeshTimestep mts) => ToFloatArray(propType3.GetValue(mts.WaterState));
				TrackDebugVariable(propType3.Name, exposeAttribute3.ElementType, getData3);
			}
		}
		fields = typeof(MeshWaterState).GetFields();
		foreach (FieldInfo propType4 in fields)
		{
			if (propType4.GetCustomAttribute(typeof(ExposeAttribute)) is ExposeAttribute exposeAttribute4)
			{
				Func<MeshTimestep, Memory<float>> getData4 = (MeshTimestep mts) => ToFloatArray(propType4.GetValue(mts.WaterState));
				TrackDebugVariable(propType4.Name, exposeAttribute4.ElementType, getData4);
			}
		}
		static Memory<float> ToFloatArray(object o)
		{
			if (o == null)
			{
				return Memory<float>.Empty;
			}
			float[] floatValues = o as float[];
			if (floatValues == null)
			{
				if (o is double[] doubleValues)
				{
					floatValues = new float[doubleValues.Length];
					for (int j = 0; j < doubleValues.Length; j++)
					{
						floatValues[j] = (float)doubleValues[j];
					}
				}
				else if (o is int[] intValues)
				{
					floatValues = new float[intValues.Length];
					for (int k = 0; k < intValues.Length; k++)
					{
						floatValues[k] = intValues[k];
					}
				}
				else if (o is byte[] byteValues)
				{
					floatValues = new float[byteValues.Length];
					for (int l = 0; l < byteValues.Length; l++)
					{
						floatValues[l] = (int)byteValues[l];
					}
				}
				else
				{
					floatValues = null;
				}
			}
			return floatValues;
		}
	}

	private void TrackDebugVariable(string name, MeshElementType element, Func<MeshTimestep, Memory<float>> getData)
	{
		if (_debugVariables == null)
		{
			_debugVariables = new Dictionary<string, Tuple<Func<MeshTimestep, Memory<float>>, H5AppendCache<float>>>();
		}
		FlowArea flowArea = Geometry.FlowAreaLayer[0];
		string dataset = H5Reader.PathCombine(RootMeshOutputPath(Name, flowArea.Name), "DEBUG/", name);
		int nColumns = element switch
		{
			MeshElementType.Cell => flowArea.Mesh.CellCount, 
			MeshElementType.Face => flowArea.Mesh.FaceCount, 
			_ => throw new NotSupportedException(), 
		};
		_debugVariables[name] = new Tuple<Func<MeshTimestep, Memory<float>>, H5AppendCache<float>>(getData, new H5AppendCache<float>(_writer, dataset, 10, nColumns));
	}

	public void FinishWriting()
	{
		FlushDisk();
		DisposeAnyWriters();
	}

	public bool IsWriting()
	{
		return _writer != null;
	}

	private void FlushDisk()
	{
		_depthCache.Flush();
		_velCache.Flush();
		if (_debugVariables != null)
		{
			foreach (KeyValuePair<string, Tuple<Func<MeshTimestep, Memory<float>>, H5AppendCache<float>>> debugVariable in _debugVariables)
			{
				debugVariable.Value.Item2.Flush();
			}
		}
		_culvertFlowCache?.Flush();
		_culvertOutsideDepthCache?.Flush();
		_culvertOutletVelocityCache?.Flush();
		if (_maxCellDepth != null)
		{
			_summary.AddRow("Cell Statistics", _maxCellDepth.Length, "Cell Max Depth", _maxCellDepth);
		}
		if (_summary.HasData)
		{
			_summary.WriteTo(_writer, RootPath + "/");
		}
		WriteTimestepsHowYouExpectToReadThem(_writer, Name, _timesteps);
	}

	private H5Writer MakeH5Writer()
	{
		if (Result.IsInMemory)
		{
			return H5Writer.CreateInMemory();
		}
		return new H5Writer(Result.Filename);
	}

	public void AddTimestep(MeshTimestep ts)
	{
		if (!IsWriting())
		{
			_logger.LogWarning("Discarding a timestep for {Result}; the output block is no longer open for writing.", Result?.Name);
			return;
		}
		double[] cellElevation = ts.WaterState.CellElevation;
		double[] faceVelocity = ts.WaterState.FaceVelocity;
		Memory<float> mem;
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.RentNoFill(Math.Max(cellElevation.Length, faceVelocity.Length), out mem);
		Memory<float> slice = memoryOwner.Memory.Slice(0, cellElevation.Length);
		Buffers.ConvertAll(cellElevation, slice);
		Geometry.HydraulicPropertyTables.ConvertStageToDepth(slice);
		_depthCache.AddRow(slice);
		Span<float> sliceSpan = slice.Span;
		for (int i = 0; i < sliceSpan.Length; i++)
		{
			if ((double)sliceSpan[i] > _maxCellDepth[i])
			{
				_maxCellDepth[i] = sliceSpan[i];
			}
		}
		Memory<float> data = memoryOwner.Memory.Slice(0, faceVelocity.Length);
		Span<float> floatSpan = data.Span;
		for (int j = 0; j < faceVelocity.Length; j++)
		{
			floatSpan[j] = (float)faceVelocity[j];
		}
		_velCache.AddRow(data);
		if (_debugVariables != null)
		{
			foreach (KeyValuePair<string, Tuple<Func<MeshTimestep, Memory<float>>, H5AppendCache<float>>> debugVariable in _debugVariables)
			{
				Memory<float> data2 = debugVariable.Value.Item1(ts);
				debugVariable.Value.Item2.AddRow(data2);
			}
		}
		if (ts.CulvertData != null && _culvertFlowCache != null)
		{
			_culvertFlowCache.AddRow(ts.CulvertData.Flow);
			_culvertOutsideDepthCache.AddRow(ts.CulvertData.OutsideDepth);
			_culvertOutletVelocityCache.AddRow(ts.CulvertData.OutletVelocity);
		}
		TimeSpan timeSpan = TimeSpan.FromMicroseconds(Math.Round(TimeSpan.FromSeconds(ts.Time).TotalMicroseconds));
		DateTime startTimePlanTerm = Result.Plan.StartTime.Add(TimeSpan.FromSeconds(ts.Time));
		_timesteps.Add(startTimePlanTerm);
		_profileNames.Add(startTimePlanTerm.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
		TimestepAdded?.Invoke();
	}

	public bool TryReadPerMesh(OutputVariable variable, int profile, Memory<float>[] values)
	{
		if (IsWriting())
		{
			return TryReadPerMesh_WriteMode(variable, profile, values);
		}
		return TryReadPerMesh_ReadMode(variable, profile, values);
	}

	private bool TryReadPerMesh_ReadMode(OutputVariable variable, int profile, Memory<float>[] values)
	{
		FlowAreaLayer flowAreaLayer = Result.Geometry.FlowAreaLayer;
		if (profile == int.MaxValue)
		{
			return false;
		}
		for (int i = 0; i < flowAreaLayer.Count; i++)
		{
			if (values[i].IsEmpty)
			{
				continue;
			}
			string name = flowAreaLayer[i].Name;
			string timeSeriesDatasetName = GetTimeSeriesDatasetName(variable, name);
			try
			{
				if (!_rowCache.TryGetRow(timeSeriesDatasetName, profile, values[i]))
				{
					_logger.LogOnce(LogLevel.Warning, "Error reading output dataset {DatasetName}", timeSeriesDatasetName);
					return false;
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Error reading output dataset {DatasetName}", timeSeriesDatasetName);
				return false;
			}
		}
		return true;
	}

	public string GetTimeSeriesDatasetName(OutputVariable variable, string meshName)
	{
		if (variable.IsDebug)
		{
			return UnsteadyFlowAreaPath + "/" + meshName + "/DEBUG/" + variable.Name;
		}
		return UnsteadyFlowAreaPath + "/" + meshName + "/" + variable.Name;
	}

	public string GetTimeSeriesDatasetName(OutputVariable variable)
	{
		return GetTimeSeriesDatasetName(variable, FirstFlowAreaName());
	}

	public string GetTimeSeriesDebugGroupName()
	{
		return UnsteadyFlowAreaPath + "/" + FirstFlowAreaName() + "/DEBUG";
	}

	private string FirstFlowAreaName()
	{
		FlowArea area = Geometry.FlowAreaLayer.FirstOrDefault();
		if (area == null)
		{
			throw new InvalidOperationException("The output block has no embedded 2D flow area.");
		}
		return area.Name;
	}

	public string GetTimeSeriesDebugDatasetName(string dsname)
	{
		return GetTimeSeriesDebugGroupName() + "/" + dsname;
	}

	private bool TryReadPerMesh_WriteMode(OutputVariable variable, int profile, Memory<float>[] values)
	{
		if (Geometry.FlowAreaLayer.Count > 1)
		{
			throw new NotImplementedException();
		}
		if (profile < 0 || profile >= ProfileCount)
		{
			throw new ArgumentOutOfRangeException("profile");
		}
		for (int i = 0; i < Geometry.FlowAreaLayer.Count; i++)
		{
			if (values[i].IsEmpty)
			{
				continue;
			}
			if (variable == OutputVariables.Depth)
			{
				_depthCache.GetRow(profile, values[i]);
				continue;
			}
			if (variable == OutputVariables.Velocity)
			{
				_velCache.GetRow(profile, values[i]);
				continue;
			}
			if (_debugVariables != null && _debugVariables.TryGetValue(variable.Name, out var localValue))
			{
				localValue.Item2.GetRow(profile, values[i]);
				continue;
			}
			throw new NotSupportedException();
		}
		return true;
	}

	public bool TryReadPerCrossSection(OutputVariable variable, int profile, out float[] values)
	{
		throw new NotImplementedException();
	}

	public bool TryReadPerStorageArea(OutputVariable variable, int profile, out float[,] values)
	{
		throw new NotImplementedException();
	}

	public bool TryReadCulvertData(int profile, out CulvertTimestepData data)
	{
		data = null;
		if (!HasCulvertOutput)
		{
			return false;
		}
		if (profile < 0 || profile >= ProfileCount)
		{
			return false;
		}
		int culvertBarrelCount = CulvertBarrelCount;
		if (culvertBarrelCount <= 0)
		{
			return false;
		}
		try
		{
			data = new CulvertTimestepData
			{
				Flow = new float[culvertBarrelCount],
				OutsideDepth = new float[culvertBarrelCount * 2],
				OutletVelocity = new float[culvertBarrelCount]
			};
			if (IsWriting())
			{
				_culvertFlowCache?.GetRow(profile, data.Flow);
				_culvertOutsideDepthCache?.GetRow(profile, data.OutsideDepth);
				_culvertOutletVelocityCache?.GetRow(profile, data.OutletVelocity);
				return true;
			}
			string dataset = CulvertsGroupPath + "Face Flow";
			string dataset2 = CulvertsGroupPath + "Outside Depth";
			string dataset3 = CulvertsGroupPath + "Face Velocity";
			if ((1u & (((bool)_rowCache.TryGetRow(dataset, profile, data.Flow)) ? 1u : 0u) & (((bool)_rowCache.TryGetRow(dataset2, profile, data.OutsideDepth)) ? 1u : 0u) & (((bool)_rowCache.TryGetRow(dataset3, profile, data.OutletVelocity)) ? 1u : 0u)) == 0)
			{
				_logger.LogOnce(LogLevel.Warning, "Failed to read culvert data for profile {Profile}", profile);
				data = null;
				return false;
			}
			return true;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error reading culvert data for profile {Profile}", profile);
			data = null;
			return false;
		}
	}

	public bool TryReadAllCulvertData(out CulvertTimestepData[] data)
	{
		data = null;
		if (!HasCulvertOutput)
		{
			return false;
		}
		int culvertBarrelCount = CulvertBarrelCount;
		if (culvertBarrelCount <= 0)
		{
			return false;
		}
		int profileCount = ProfileCount;
		if (profileCount <= 0)
		{
			return false;
		}
		try
		{
			string culvertsGroupPathTerm = CulvertsGroupPath + "Face Flow";
			string datasetName = CulvertsGroupPath + "Outside Depth";
			string datasetName2 = CulvertsGroupPath + "Face Velocity";
			if (IsWriting())
			{
				data = new CulvertTimestepData[profileCount];
				for (int i = 0; i < profileCount; i++)
				{
					data[i] = new CulvertTimestepData
					{
						Flow = new float[culvertBarrelCount],
						OutsideDepth = new float[culvertBarrelCount * 2],
						OutletVelocity = new float[culvertBarrelCount]
					};
					_culvertFlowCache?.GetRow(i, data[i].Flow);
					_culvertOutsideDepthCache?.GetRow(i, data[i].OutsideDepth);
					_culvertOutletVelocityCache?.GetRow(i, data[i].OutletVelocity);
				}
				return true;
			}
			using H5Reader h5Reader = new H5Reader(Result.Filename);
			if (!h5Reader.ExistsAsDataset(culvertsGroupPathTerm))
			{
				_logger.LogOnce(LogLevel.Warning, "Culvert flow dataset not found: {Path}", culvertsGroupPathTerm);
				return false;
			}
			h5Reader.ReadDataset(culvertsGroupPathTerm, out float[,] values);
			h5Reader.ReadDataset(datasetName, out float[,] values2);
			h5Reader.ReadDataset(datasetName2, out float[,] values3);
			int length = values.GetLength(0);
			if (length != profileCount)
			{
				_logger.LogWarning("Culvert data row count {Rows} doesn't match profile count {ProfileCount}", length, profileCount);
			}
			data = new CulvertTimestepData[length];
			for (int j = 0; j < length; j++)
			{
				data[j] = new CulvertTimestepData
				{
					Flow = new float[culvertBarrelCount],
					OutsideDepth = new float[culvertBarrelCount * 2],
					OutletVelocity = new float[culvertBarrelCount]
				};
				for (int k = 0; k < culvertBarrelCount; k++)
				{
					data[j].Flow[k] = values[j, k];
					data[j].OutletVelocity[k] = values3[j, k];
				}
				for (int l = 0; l < culvertBarrelCount * 2; l++)
				{
					data[j].OutsideDepth[l] = values2[j, l];
				}
			}
			return true;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error reading all culvert data");
			data = null;
			return false;
		}
	}

	public static string RootBlockOutputPath(string blockName)
	{
		return "/Results/Output Blocks/" + blockName + "/";
	}

	public static string RootMeshOutputPath(string blockName, string meshName)
	{
		return RootBlockOutputPath(blockName) + "2D Flow Areas/" + meshName + "/";
	}

	public static bool HasValidOutputBlocks(H5Reader h5r)
	{
		if (h5r.ExistsAsGroup("/Results/Output Blocks/"))
		{
			return h5r.GetGroupCount("/Results/Output Blocks/") > 0;
		}
		return false;
	}

	private void InitPaths()
	{
		RootPath = "/Results/Output Blocks/" + Name;
		UnsteadyFlowAreaPath = RootPath + "/2D Flow Areas";
	}

	private void DisposeAnyWriters()
	{
		_depthCache?.Dispose();
		_velCache?.Dispose();
		if (_debugVariables != null)
		{
			foreach (KeyValuePair<string, Tuple<Func<MeshTimestep, Memory<float>>, H5AppendCache<float>>> debugVariable in _debugVariables)
			{
				debugVariable.Value.Item2.Dispose();
			}
			_debugVariables.Clear();
		}
		_culvertFlowCache?.Dispose();
		_culvertOutsideDepthCache?.Dispose();
		_culvertOutletVelocityCache?.Dispose();
		_writer?.Dispose();
		_depthCache = null;
		_velCache = null;
		_culvertFlowCache = null;
		_culvertOutsideDepthCache = null;
		_culvertOutletVelocityCache = null;
		_writer = null;
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (IsWriting())
			{
				FlushDisk();
			}
			DisposeAnyWriters();
			_rowCache?.Dispose();
			_isDisposed = true;
		}
	}

	~OutputBlock()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
