using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using Geospatial;
using Geospatial.Vectors;
using H5Assist;
using H5Assist.Caching;
using Ras.Engine;
using Ras.Hydraulics;
using Ras.Layers;
using Utility;
using Utility.Memory;

namespace Ras.Output;

public class MemoryOutputBlock : IHorizontalOutputBlock, IOutputBlock, IDiscreteTimestepsWithEvent, IDiscreteTimesteps, IVerticalOutputBlock, IWriteableOutputBlock, IDisposable
{
	public const int RowsPerChunk = 10;

	private readonly List<string> _profileNames;

	private readonly DateTime _startTime;

	private readonly List<DateTime> _timesteps;

	private readonly H5Writer _writer;

	private readonly H5AppendCache<float> _depthCache;

	private readonly H5AppendCache<float> _velCache;

	private Dictionary<string, Tuple<Func<MeshTimestep, Memory<float>>, H5AppendCache<float>>> _debugVariables;

	private bool _isDisposed;

	private SummaryStatistics _summary = new SummaryStatistics();

	private double[] _maxCellDepth;

	public IList<string> ProfileNames => _profileNames;

	public int ProfileCount => ProfileNames.Count;

	public string Name { get; set; }

	public Result Result { get; init; }

	public Geometry Geometry { get; }

	public IReadOnlyList<DateTime> Times => _timesteps;

	public SummaryStatistics Summary => _summary;

	public event TimestepAddedEvent TimestepAdded;

	public MemoryOutputBlock(Result res, H5Writer writer, string blockName)
	{
		Result = res;
		Geometry = res.Geometry;
		Name = blockName;
		_writer = writer;
		_startTime = res.Plan.StartTime;
		_timesteps = new List<DateTime>();
		_profileNames = new List<string>();
		if (Geometry.FlowAreaLayer.Count != 1)
		{
			throw new Exception("Only supporting one mesh currently.");
		}
		FlowArea flowArea = Geometry.FlowAreaLayer[0];
		string name = flowArea.Name;
		Mesh mesh = flowArea.Mesh;
		string rootMeshOutputPath = OutputBlock.RootMeshOutputPath(blockName, name);
		string dataset = rootMeshOutputPath + OutputVariables.Depth.Name;
		string dataset2 = rootMeshOutputPath + OutputVariables.Velocity.Name;
		_depthCache = new H5AppendCache<float>(writer, dataset, 10, mesh.CellCount);
		_velCache = new H5AppendCache<float>(writer, dataset2, 10, mesh.FaceCount);
		_maxCellDepth = new double[mesh.CellCount];
		Array.Fill(_maxCellDepth, -9999.0);
	}

	public static MemoryOutputBlock MockBaseOutputBlock(Result res)
	{
		H5Writer writer = H5Writer.CreateInMemory();
		return CreateBaseOutputBlock(res, writer);
	}

	public static MemoryOutputBlock CreateBaseOutputBlock(Result res, H5Writer writer)
	{
		return new MemoryOutputBlock(res, writer, "Base Output");
	}

	public void TrackAllExposedDebugVariables()
	{
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

	public void TrackDebugVariable(string name, MeshElementType element, Func<MeshTimestep, Memory<float>> getData)
	{
		if (_debugVariables == null)
		{
			_debugVariables = new Dictionary<string, Tuple<Func<MeshTimestep, Memory<float>>, H5AppendCache<float>>>();
		}
		FlowArea flowArea = Geometry.FlowAreaLayer[0];
		string dataset = H5Reader.PathCombine(OutputBlock.RootMeshOutputPath(Name, flowArea.Name), "DEBUG", name);
		int nColumns = element switch
		{
			MeshElementType.Cell => flowArea.Mesh.CellCount, 
			MeshElementType.Face => flowArea.Mesh.FaceCount, 
			_ => throw new NotSupportedException(), 
		};
		_debugVariables[name] = new Tuple<Func<MeshTimestep, Memory<float>>, H5AppendCache<float>>(getData, new H5AppendCache<float>(_writer, dataset, 10, nColumns));
	}

	public void FlushDisk()
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
		if (_maxCellDepth != null)
		{
			_summary.AddRow("Cell Statistics", _maxCellDepth.Length, "Cell Max Depth", _maxCellDepth);
		}
		if (_summary.HasData)
		{
			_summary.WriteTo(_writer, OutputBlock.RootBlockOutputPath(Name));
		}
		OutputBlock.WriteTimestepsHowYouExpectToReadThem(_writer, Name, _timesteps);
	}

	public void AddTimestep(MeshTimestep ts)
	{
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
		DateTime startTimeFromSecondsTerm = _startTime.Add(TimeSpan.FromSeconds(ts.Time));
		_timesteps.Add(startTimeFromSecondsTerm);
		_profileNames.Add(startTimeFromSecondsTerm.ToString());
		TimestepAdded?.Invoke();
	}

	public bool TryReadPerMesh(OutputVariable variable, int profile, Memory<float>[] values)
	{
		if (Geometry.FlowAreaLayer.Count > 1)
		{
			throw new NotImplementedException();
		}
		if (profile < 0 || profile >= ProfileCount)
		{
			throw new ArgumentOutOfRangeException();
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

	public bool TryReadPerStorageArea(OutputVariable variable, int profile, out float[,] values)
	{
		values = null;
		return false;
	}

	public bool TryReadPerCrossSection(OutputVariable variable, int profile, out float[] values)
	{
		values = null;
		return false;
	}

	public bool TryReadMeshValues(OutputVariable variable, int meshIndex, IList<int> elementIndexes, Memory<float>[] values)
	{
		if (Geometry.FlowAreaLayer.Count > 1)
		{
			throw new NotImplementedException();
		}
		if (Geometry.FlowAreaLayer.Count >= meshIndex)
		{
			throw new ArgumentOutOfRangeException("meshIndex");
		}
		_ = Geometry.FlowAreaLayer[meshIndex].Mesh;
		return true;
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
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
		_writer?.Dispose();
		_isDisposed = true;
	}

	~MemoryOutputBlock()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public bool TryReadMeshValues(OutputVariable variable, int meshIndex, Memory<int> elementIndexes, Memory<float>[] values)
	{
		throw new NotImplementedException();
	}
}
