using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Geospatial;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using H5Assist;
using H5Assist.Identifiers;
using Ras.Mapping.ArrivalTime;
using Ras.Output;
using Utility;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;
using Utility.Threading;

namespace Ras.Layers.Results;

public class PostProcessor : IVerticalOutputBlock, IOutputBlock, IDiscreteTimestepsWithEvent, IDiscreteTimesteps, IDisposable
{
	private const int BytesPerVerticalChunk = 1048576;

	private const string ResultShaKey = "Result SHA256";

	private bool _isDisposed;

	private NativeResourceWrapper<H5Writer> _writer;

	private NativeResourceWrapper<H5Reader> _reader;

	private readonly EnsureSingleton _ensureSingleton = new EnsureSingleton();

	public Result Result { get; init; }

	public Geometry Geometry => Result.Geometry;

	public NativeResourceWrapper<H5Writer> Writer => _writer;

	public NativeResourceWrapper<H5Reader> Reader => _reader;

	public OutputBlock MappedOutputBlock => Result.BaseOutputBlock as OutputBlock;

	public string Name => MappedOutputBlock.Name;

	public IList<string> ProfileNames => MappedOutputBlock.ProfileNames;

	public IReadOnlyList<DateTime> Times => MappedOutputBlock.Times;

	public int ProfileCount => MappedOutputBlock.ProfileCount;

	public event TimestepAddedEvent TimestepAdded
	{
		add
		{
		}
		remove
		{
		}
	}

	private PostProcessor()
	{
	}

	private static PostProcessor CreateNew(Result res, string filename, string sha256)
	{
		if (res.IsInMemory)
		{
			return CreateInMemory(res);
		}
		if (File.Exists(filename))
		{
			File.Delete(filename);
		}
		PostProcessor postProcessor = new PostProcessor
		{
			Result = res,
			_reader = new NativeResourceWrapper<H5Reader>(() => new H5Reader(filename)),
			_writer = new NativeResourceWrapper<H5Writer>(() => new H5Writer(filename))
		};
		postProcessor.Writer.UseResource((H5Writer w) =>
		{
			SignHeader(w, sha256);
		});
		return postProcessor;
	}

	private static PostProcessor OpenExisting(Result res, string filename, string sha256)
	{
		if (res.IsInMemory)
		{
			return CreateInMemory(res);
		}
		return new PostProcessor
		{
			Result = res,
			_reader = new NativeResourceWrapper<H5Reader>(() => new H5Reader(filename)),
			_writer = new NativeResourceWrapper<H5Writer>(() => new H5Writer(filename))
		};
	}

	private static PostProcessor CreateInMemory(Result res)
	{
		H5Writer resource = H5Writer.CreateInMemory();
		return new PostProcessor
		{
			Result = res,
			_reader = NativeResourceWrapper<H5Reader>.Give(resource),
			_writer = NativeResourceWrapper<H5Writer>.Give(resource)
		};
	}

	private static void SignHeader(H5Writer writer, string sha256)
	{
		writer.WriteAttribute("/", "Result SHA256", sha256);
	}

	private static bool HeaderMatches(H5Reader reader, string expectedSha256)
	{
		return reader.TryReadStringAttributeScalar("/", "Result SHA256") == expectedSha256;
	}

	internal static PostProcessor Ensure(Result res, string filename)
	{
		string fileSHA = res.GetFileSHA256();
		if (IsValidPostProcessFile(filename, fileSHA))
		{
			return OpenExisting(res, filename, fileSHA);
		}
		return CreateNew(res, filename, fileSHA);
	}

	/// <summary>
	/// Creates a caller-owned derived cache without touching the Result's shared
	/// export directory. The caller owns a unique path and must dispose this
	/// processor before removing the cache.
	/// </summary>
	public static PostProcessor CreateReadCache(Result result, string filename)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (string.IsNullOrWhiteSpace(filename) || !Path.IsPathFullyQualified(filename))
			throw new ArgumentException("An absolute private cache path is required.", nameof(filename));
		if (File.Exists(filename))
			throw new IOException("The private postprocess cache path already exists.");
		return CreateNew(result, filename, result.GetFileSHA256());
	}

	internal static async Task<PostProcessor> EnsureAsync(Result res, string filename)
	{
		string fileSHA256Async = await res.GetFileSHA256Async();
		if (IsValidPostProcessFile(filename, fileSHA256Async))
		{
			return OpenExisting(res, filename, fileSHA256Async);
		}
		return CreateNew(res, filename, fileSHA256Async);
	}

	private static bool IsValidPostProcessFile(string filename, string expectedSha256)
	{
		try
		{
			if (!File.Exists(filename))
			{
				return false;
			}
			using H5Reader h5Reader = new H5Reader(filename);
			h5Reader.AssertLoadedFileID();
			return HeaderMatches(h5Reader, expectedSha256);
		}
		catch
		{
			return false;
		}
	}

	public static string DefaultTransposedDatasetName(string dataset)
	{
		return dataset + " (Transposed)";
	}

	public static string DefaultMIRInfoDatasetName(string dataset)
	{
		return dataset + " (Increasing Region Info)";
	}

	public static string DefaultMIRValueDatasetName(string dataset)
	{
		return dataset + " (Increasing Region Values)";
	}

	public bool HasDefaultDatasets()
	{
		return HasDefaultDatasets(AssertGetBaseOutputBlock());
	}

	public bool HasDefaultDatasets(OutputBlock outputBlock)
	{
		if (TransposedDatasetExists(outputBlock.GetTimeSeriesDatasetName(OutputVariables.Velocity)))
		{
			return TransposedDatasetExists(outputBlock.GetTimeSeriesDatasetName(OutputVariables.Depth));
		}
		return false;
	}

	public void EnsureDefaultDatasets(ProgressReporter pr = null)
	{
		if (DefaultDatasetsExist())
		{
			return;
		}
		using (_ensureSingleton.Start())
		{
			if (pr == null)
			{
				pr = ProgressReporter.None();
			}
			OutputBlock ob = AssertGetBaseOutputBlock();
			pr.ReportMessage("Writing Depth...");
			EnsureTransposedDataset<float>(ob, OutputVariables.Depth, pr.WithProgressModifier(0, 0.4f));
			pr.ReportProgress(40);
			pr.ReportMessage("Writing Velocity...");
			EnsureTransposedDataset<float>(ob, OutputVariables.Velocity, pr.WithProgressModifier(50, 0.6f));
			pr.ReportProgress(100);
		}
	}

	public bool DefaultDatasetsExist()
	{
		OutputBlock outputBlock = AssertGetBaseOutputBlock();
		string ds1 = DefaultTransposedDatasetName(outputBlock.GetTimeSeriesDatasetName(OutputVariables.Depth));
		string ds2 = DefaultTransposedDatasetName(outputBlock.GetTimeSeriesDatasetName(OutputVariables.Velocity));
		return Reader.UseResource((H5Reader hr) => hr.ExistsAsDataset(ds1) && hr.ExistsAsDataset(ds2));
	}

	public void EnsureDebugDatasets(ProgressReporter pr = null)
	{
		using (_ensureSingleton.Start())
		{
			if (pr == null)
			{
				pr = ProgressReporter.None();
			}
			OutputBlock outputBlock = AssertGetBaseOutputBlock();
			List<string> ds = new List<string>();
			string dbg = outputBlock.GetTimeSeriesDebugGroupName();
			Result.H5Wrapper.UseResource((H5Reader hr) =>
			{
				ds.AddRange(hr.GetDatasetNames(dbg));
			});
			float scaledCount = 1f / (float)ds.Count;
			float scaledCountSum = 0f;
			foreach (string ds2 in ds)
			{
				string baseDataset = H5Reader.PathCombine(dbg, ds2);
				pr.ReportMessage("Writing " + ds2);
				ProgressReporter prog = pr.WithProgressModifier((int)(scaledCountSum * 100f), scaledCount);
				EnsureTransposedDataset<float>(baseDataset, prog);
				scaledCountSum += scaledCount;
				pr.ReportProgressFraction(scaledCountSum);
			}
			pr.ReportProgress(100);
		}
	}

	public async Task EnsureDefaultDatasetsAsync(ProgressReporter pr = null)
	{
		await Task.Run(() =>
		{
			EnsureDefaultDatasets(pr);
		});
	}

	public bool TransposedDatasetExists(string baseDataset)
	{
		return _reader.UseResource((H5Reader hr) => ExistsAndIsValid(hr, DefaultTransposedDatasetName(baseDataset)));
	}

	public bool TransposedDatasetExists(OutputVariable ov)
	{
		return TransposedDatasetExists(AssertGetBaseOutputBlock().GetTimeSeriesDatasetName(ov));
	}

	private OutputBlock AssertGetBaseOutputBlock()
	{
		return (Result.BaseOutputBlock as OutputBlock) ?? throw new Exception("Result does not have a valid base output block, is it still computing?");
	}

	public void EnsureTransposedDataset<T>(string baseDataset, ProgressReporter prog = null) where T : unmanaged
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (!TransposedDatasetExists(baseDataset))
		{
			H5Reader.TransposeWithIntermediateFile<T>(Result.H5Wrapper, _writer, baseDataset, DefaultTransposedDatasetName(baseDataset), null, prog);
		}
	}

	public void EnsureTransposedDataset<T>(OutputVariable variable, ProgressReporter prog = null) where T : unmanaged
	{
		OutputBlock ob = AssertGetBaseOutputBlock();
		EnsureTransposedDataset<T>(ob, variable, prog);
	}

	public void EnsureTransposedDataset<T>(OutputBlock ob, OutputVariable variable, ProgressReporter prog = null) where T : unmanaged
	{
		string timeSeriesDatasetName = ob.GetTimeSeriesDatasetName(variable);
		EnsureTransposedDataset<T>(timeSeriesDatasetName, prog);
	}

	public void EnsureTransposedDataset<T>(LegacyOutputBlock ob, OutputVariable variable, ProgressReporter prog = null) where T : unmanaged
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		string baseDataset = ob.ExpectedDatasetName(variable);
		EnsureTransposedDataset<T>(baseDataset, prog);
	}

	public void ReadTransposedDataset<T>(string transposedDatasetName, int column, Memory<T> data) where T : unmanaged
	{
		_reader.UseResource((H5Reader hr) =>
		{
			hr.ReadColumn(transposedDatasetName, column, data);
		});
	}

	public OperationResult GetCellWaterSurfaceTimeSeries(Memory<int> cellIndexes, Memory<float>[] outData)
	{
		OperationResult operationResult = ReadTransposedDataset(OutputVariables.Depth, cellIndexes, outData);
		if (!operationResult)
		{
			return operationResult;
		}
		ConvertDepthToStage(cellIndexes, outData);
		return true;
	}

	public OperationResult GetCellWaterSurfaceTimeSeries(int cellIndex, Memory<float> outData)
	{
		ReadTransposedDataset(OutputVariables.Depth, cellIndex, outData);
		ConvertDepthToStage(cellIndex, outData);
		return true;
	}

	public OperationResult GetCellWaterSurfaceTimeSeries(OutputBlock ob, Memory<int> cellIndexes, Memory<float>[] data)
	{
		OperationResult operationResult = ReadTransposedDataset(ob, OutputVariables.Depth, cellIndexes, data);
		if (!operationResult)
		{
			return operationResult;
		}
		ConvertDepthToStage(cellIndexes, data);
		return true;
	}

	private void ConvertDepthToStage(Memory<int> cellIndexes, Memory<float>[] buffer)
	{
		Span<float> ensureCellMinElevationTable = Result.Geometry.HydraulicPropertyTables.EnsureCellMinElevationTable().Span;
		Span<int> cellIndexesSpan = cellIndexes.Span;
		for (int i = 0; i < cellIndexesSpan.Length; i++)
		{
			int index = cellIndexesSpan[i];
			float addValue = ensureCellMinElevationTable[index];
			Buffers.AddConstant(buffer[i].Span, addValue);
		}
	}

	private void ConvertDepthToStage(int cIdx, Memory<float> buffer)
	{
		float addValue = Result.Geometry.HydraulicPropertyTables.EnsureCellMinElevationTable().Span[cIdx];
		Buffers.AddConstant(buffer.Span, addValue);
	}

	public VelocityTimeSeries GetCellVelocityTimeSeries(int cellIndex)
	{
		Mesh mesh = Result.Geometry.FlowAreaLayer[0].Mesh;
		HydraulicMeshComputer metadataCache = mesh.MetadataCache;
		Cell cell = mesh.Cells[cellIndex];
		int[] faces = cell.Faces.Span.ToArray();
		int faceLength = faces.Length;
		int profileCount = ProfileCount;
		Memory<float>[] floatSpanArray = new Memory<float>[faceLength];
		for (int i = 0; i < faceLength; i++)
		{
			floatSpanArray[i] = new float[profileCount];
		}
		ReadTransposedDataset(OutputVariables.Velocity, faces.AsMemory(), floatSpanArray);
		Dictionary<int, Memory<float>> floatSpanByInt = new Dictionary<int, Memory<float>>(faceLength);
		for (int j = 0; j < faceLength; j++)
		{
			floatSpanByInt[faces[j]] = floatSpanArray[j];
		}
		Geospatial.Vectors.Vector[] velocityTimeSeries = metadataCache.CellComputes.VelocityTimeSeries(cellIndex, floatSpanByInt);
		double[] doubleValues = new double[profileCount];
		double[] doubleValuesArray = new double[profileCount];
		for (int k = 0; k < profileCount; k++)
		{
			doubleValues[k] = velocityTimeSeries[k].X;
			doubleValuesArray[k] = velocityTimeSeries[k].Y;
		}
		return new VelocityTimeSeries((IList<DateTime>)Times, doubleValues, doubleValuesArray);
	}

	public OperationResult GetCellVelocityTimeSeries(int cellIndex, Memory<double> outVx, Memory<double> outVy)
	{
		Mesh mesh = Result.Geometry.FlowAreaLayer[0].Mesh;
		HydraulicMeshComputer metadataCache = mesh.MetadataCache;
		Cell cell = mesh.Cells[cellIndex];
		int[] faces = cell.Faces.Span.ToArray();
		int faceLength = faces.Length;
		int profileCount = ProfileCount;
		Memory<float>[] floatSpanArray = new Memory<float>[faceLength];
		for (int i = 0; i < faceLength; i++)
		{
			floatSpanArray[i] = new float[profileCount];
		}
		ReadTransposedDataset(OutputVariables.Velocity, faces.AsMemory(), floatSpanArray);
		Dictionary<int, Memory<float>> floatSpanByInt = new Dictionary<int, Memory<float>>(faceLength);
		for (int j = 0; j < faceLength; j++)
		{
			floatSpanByInt[faces[j]] = floatSpanArray[j];
		}
		Geospatial.Vectors.Vector[] velocityTimeSeries = metadataCache.CellComputes.VelocityTimeSeries(cellIndex, floatSpanByInt);
		Span<double> outVxSpan = outVx.Span;
		Span<double> outVySpan = outVy.Span;
		for (int k = 0; k < profileCount; k++)
		{
			outVxSpan[k] = velocityTimeSeries[k].X;
			outVySpan[k] = velocityTimeSeries[k].Y;
		}
		return true;
	}

	public OperationResult GetCellVelocityMagnitudeTimeSeries(int cellIndex, Memory<double> outMagnitude)
	{
		Mesh mesh = Result.Geometry.FlowAreaLayer[0].Mesh;
		HydraulicMeshComputer metadataCache = mesh.MetadataCache;
		Cell cell = mesh.Cells[cellIndex];
		int[] faces = cell.Faces.Span.ToArray();
		int faceLength = faces.Length;
		int profileCount = ProfileCount;
		Memory<float>[] floatSpanArray = new Memory<float>[faceLength];
		for (int i = 0; i < faceLength; i++)
		{
			floatSpanArray[i] = new float[profileCount];
		}
		ReadTransposedDataset(OutputVariables.Velocity, faces.AsMemory(), floatSpanArray);
		Dictionary<int, Memory<float>> floatSpanByInt = new Dictionary<int, Memory<float>>(faceLength);
		for (int j = 0; j < faceLength; j++)
		{
			floatSpanByInt[faces[j]] = floatSpanArray[j].ToArray();
		}
		Geospatial.Vectors.Vector[] velocityTimeSeries = metadataCache.CellComputes.VelocityTimeSeries(cellIndex, floatSpanByInt);
		Span<double> outMagnitudeSpan = outMagnitude.Span;
		for (int k = 0; k < profileCount; k++)
		{
			Geospatial.Vectors.Vector vector = velocityTimeSeries[k];
			outMagnitudeSpan[k] = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
		}
		return true;
	}

	public OperationResult GetCellVelocityTimeSeries(Memory<int> cellIndexes, Memory<double>[] outVx, Memory<double>[] outVy)
	{
		Mesh mesh = Result.Geometry.FlowAreaLayer[0].Mesh;
		HydraulicMeshComputer metadataCache = mesh.MetadataCache;
		int nTimesteps = ProfileCount;
		HashSet<int> intSet = new HashSet<int>();
		Span<int> cellIndexesSpan = cellIndexes.Span;
		Span<int> cellIndexes2 = cellIndexesSpan;
		for (int i = 0; i < cellIndexes2.Length; i++)
		{
			int cellIndex = cellIndexes2[i];
			Span<int> faces = mesh.Cells[cellIndex].Faces.Span;
			for (int j = 0; j < faces.Length; j++)
			{
				int faceIndex = faces[j];
				intSet.Add(faceIndex);
			}
		}
		int[] intSetArray = intSet.ToArray();
		Dictionary<int, Memory<float>> floatSpanByInt = new Dictionary<int, Memory<float>>(intSetArray.Length);
		Memory<float>[] floatSpanArray = intSetArray.Select((int _) => new float[nTimesteps].AsMemory()).ToArray();
		OperationResult operationResult = ReadTransposedDataset(OutputVariables.Velocity, intSetArray.AsMemory(), floatSpanArray);
		if (!operationResult)
		{
			return operationResult;
		}
		for (int intervalIndex = 0; intervalIndex < intSetArray.Length; intervalIndex++)
		{
			floatSpanByInt[intSetArray[intervalIndex]] = floatSpanArray[intervalIndex];
		}
		for (int intervalIndex2 = 0; intervalIndex2 < cellIndexesSpan.Length; intervalIndex2++)
		{
			Geospatial.Vectors.Vector[] velocityTimeSeries = metadataCache.CellComputes.VelocityTimeSeries(cellIndexesSpan[intervalIndex2], floatSpanByInt);
			Span<double> outVxSpan = outVx[intervalIndex2].Span;
			Span<double> outVySpan = outVy[intervalIndex2].Span;
			for (int intervalIndex3 = 0; intervalIndex3 < nTimesteps; intervalIndex3++)
			{
				outVxSpan[intervalIndex3] = velocityTimeSeries[intervalIndex3].X;
				outVySpan[intervalIndex3] = velocityTimeSeries[intervalIndex3].Y;
			}
		}
		return true;
	}

	public void ReadTransposedDataset<T>(OutputVariable variable, int column, Memory<T> data) where T : unmanaged
	{
		OutputBlock outputBlock = AssertGetBaseOutputBlock();
		ReadTransposedDataset(DefaultTransposedDatasetName(outputBlock.GetTimeSeriesDatasetName(variable)), column, data);
	}

	public T[] ReadTransposedDataset<T>(OutputVariable variable, int column) where T : unmanaged
	{
		OutputBlock outputBlock = AssertGetBaseOutputBlock();
		T[] transposedDataset = new T[outputBlock.ProfileCount];
		ReadTransposedDataset<T>(DefaultTransposedDatasetName(outputBlock.GetTimeSeriesDatasetName(variable)), column, transposedDataset);
		return transposedDataset;
	}

	public T[] ReadTransposedDataset<T>(string dataset, int column) where T : unmanaged
	{
		T[] transposedDataset = new T[AssertGetBaseOutputBlock().ProfileCount];
		ReadTransposedDataset<T>(dataset, column, transposedDataset);
		return transposedDataset;
	}

	public void ReadTransposedDataset<T>(OutputBlock ob, OutputVariable variable, int column, Memory<T> data) where T : unmanaged
	{
		ReadTransposedDataset(DefaultTransposedDatasetName(ob.GetTimeSeriesDatasetName(variable)), column, data);
	}

	public T[] ReadTransposedDataset<T>(OutputBlock ob, OutputVariable variable, int column) where T : unmanaged
	{
		T[] transposedDataset = new T[ob.ProfileCount];
		ReadTransposedDataset<T>(DefaultTransposedDatasetName(ob.GetTimeSeriesDatasetName(variable)), column, transposedDataset);
		return transposedDataset;
	}

	public OperationResult ReadTransposedDataset<T>(OutputVariable variable, Memory<int> columnIndexes, Memory<T>[] data) where T : unmanaged
	{
		return ReadTransposedDataset(AssertGetBaseOutputBlock(), variable, columnIndexes, data);
	}

	public OperationResult ReadTransposedDataset<T>(OutputBlock ob, OutputVariable variable, Memory<int> columnIndexes, Memory<T>[] data) where T : unmanaged
	{
		return ReadTransposedDataset(ob.GetTimeSeriesDatasetName(variable), columnIndexes, data);
	}

	public OperationResult ReadAsTimeSeries(OutputVariable variable, int columnIndex, out TimeSeries series)
	{
		return ReadAsTimeSeries(AssertGetBaseOutputBlock(), variable, columnIndex, out series);
	}

	public OperationResult ReadAsTimeSeries(OutputBlock ob, OutputVariable variable, int columnIndex, out TimeSeries series)
	{
		string transposedDatasetName = DefaultTransposedDatasetName(ob.GetTimeSeriesDatasetName(variable));
		return ReadAsTimeSeries(ob, transposedDatasetName, columnIndex, out series);
	}

	public OperationResult ReadAsTimeSeries(OutputBlock ob, string transposedDatasetName, int columnIndex, out TimeSeries series)
	{
		TimeSeries lambdaSeries = null;
		OperationResult operationResult = _reader.UseResource<OperationResult>((H5Reader hr) =>
		{
			using H5DatasetID h5DatasetID = hr.TryOpenDataset(transposedDatasetName);
			if (h5DatasetID == null)
			{
				return OperationResult.Fail(H5Reader.MessageCodes.GetDatasetNotFound(hr.Filename, transposedDatasetName));
			}
			Type datasetType = hr.GetDatasetType(h5DatasetID);
			if (datasetType == typeof(float))
			{
				float[] floatValues = new float[ob.ProfileCount];
				hr.ReadColumn<float>(h5DatasetID, columnIndex, floatValues);
				lambdaSeries = new TimeSeries(ob.Times, floatValues);
			}
			else
			{
				if (!(datasetType == typeof(double)))
				{
					return OperationResult.Fail(H5Reader.MessageCodes.GetDatasetIncorrectType(hr.Filename, transposedDatasetName, "Float/Double", datasetType.Name.ToString()));
				}
				double[] doubleValues = new double[ob.ProfileCount];
				hr.ReadColumn<double>(h5DatasetID, columnIndex, doubleValues);
				lambdaSeries = new TimeSeries(ob.Times, doubleValues);
			}
			return true;
		});
		if ((bool)operationResult)
		{
			series = lambdaSeries;
			return operationResult;
		}
		series = null;
		return operationResult;
	}

	public OperationResult ReadTransposedDataset<T>(string dataset, Memory<int> columnIndexes, Memory<T>[] data) where T : unmanaged
	{
		if (columnIndexes.Length != data.Length)
		{
			throw new ArgumentException("Array length mismatch.");
		}
		_reader.UseResource((H5Reader hr) =>
		{
			string datasetName = DefaultTransposedDatasetName(dataset);
			using H5DatasetID dataset2 = hr.OpenDataset(datasetName);
			hr.ReadColumnsBatched(dataset2, columnIndexes.Span, data);
		});
		return true;
	}

	public OperationResult ReadTransposedDataset<T>(string dataset, Memory<int> columnIndexes, Memory<T> data) where T : unmanaged
	{
		_reader.UseResource((H5Reader hr) =>
		{
			string datasetName = DefaultTransposedDatasetName(dataset);
			using H5DatasetID dataset2 = hr.OpenDataset(datasetName);
			int datasetDimension = (int)hr.GetDatasetDimensions(dataset2)[0];
			Span<int> columnIndexesSpan = columnIndexes.Span;
			for (int i = 0; i < columnIndexesSpan.Length; i++)
			{
				Memory<T> buffer = data.Slice(i * datasetDimension, datasetDimension);
				hr.ReadColumn(dataset2, columnIndexesSpan[i], buffer);
			}
		});
		return true;
	}

	private static bool ExistsAndIsValid(H5Reader hr, string dataset)
	{
		if (hr.ExistsAsDataset(dataset))
		{
			return true;
		}
		return false;
	}

	public void ReadMIR(LegacyOutputBlock ob, OutputVariable variable, out int[,] info, out int[] values)
	{
		string baseDataset = ob.ExpectedDatasetName(variable);
		ReadMIR(baseDataset, out info, out values);
	}

	public void ReadMIR(string baseDataset, out int[,] info, out int[] values)
	{
		int[,] tmp1 = null;
		int[] tmp2 = null;
		_writer.UseResource((H5Writer hw) =>
		{
			string defaultMIRInfoDatasetName = DefaultMIRInfoDatasetName(baseDataset);
			string defaultMIRValueDatasetName = DefaultMIRValueDatasetName(baseDataset);
			if (!hw.ExistsAsDataset(defaultMIRInfoDatasetName) || !hw.ExistsAsDataset(defaultMIRValueDatasetName))
			{
				throw new Exception("Dataset does not exist. Expected EnsureMIRDataset to be called.");
			}
			hw.ReadDataset(defaultMIRInfoDatasetName, out tmp1);
			hw.ReadDataset(defaultMIRValueDatasetName, out tmp2);
		});
		info = tmp1;
		values = tmp2;
	}

	public void EnsureMIRDataset<T>(string baseDataset, ProgressReporter prog = null) where T : unmanaged, INumber<T>
	{
		EnsureMIRDataset<T>(Result.H5Wrapper, _writer, baseDataset, prog);
	}

	public void EnsureMIR<T>(LegacyOutputBlock ob, OutputVariable variable, ProgressReporter prog = null) where T : unmanaged, INumber<T>
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		string baseDataset = ob.ExpectedDatasetName(variable);
		EnsureMIRDataset<T>(baseDataset, prog);
	}

	public static void EnsureMIRDataset<T>(NativeResourceWrapper<H5Reader> hrWrap, NativeResourceWrapper<H5Writer> hwWrap, string baseDataset, ProgressReporter prog = null) where T : unmanaged, INumber<T>
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		string transposedDataset = DefaultTransposedDatasetName(baseDataset);
		string rgnInfoDset = DefaultMIRInfoDatasetName(baseDataset);
		string rgnValuesDset = DefaultMIRValueDatasetName(baseDataset);
		hwWrap.UseResource((H5Writer hw) =>
		{
			if (hw.ExistsAsDataset(rgnInfoDset) && hw.ExistsAsDataset(rgnValuesDset))
			{
				return;
			}
			if (!ExistsAndIsValid(hw, transposedDataset))
			{
				ProgressReporter pr = prog.WithProgressModifier(0, 0.5f);
				H5Reader.TransposeWithIntermediateFile<T>(hrWrap, hwWrap, baseDataset, transposedDataset, null, pr);
				prog = prog.WithProgressModifier(50, 0.5f);
			}
			using H5DatasetID dataset = hw.OpenDataset(transposedDataset);
			long[] datasetDimensions = hw.GetDatasetDimensions(dataset);
			if (datasetDimensions.Length != 2)
			{
				throw new Exception("Incorrect dataset dimensions, expected 2.");
			}
			long datasetDimension = datasetDimensions[0];
			long datasetDimension2 = datasetDimensions[1];
			int datasetDimension3 = (int)datasetDimension;
			if (datasetDimension == 1)
			{
				int[] intValues = new int[datasetDimension2];
				int[,] intValues2 = new int[(int)checked((nint)datasetDimension2), 3];
				intValues.Fill(0);
				for (int i = 0; i < datasetDimension2; i++)
				{
					intValues2[i, 0] = i;
					intValues2[i, 1] = 1;
					intValues2[i, 2] = 0;
				}
				hw.WriteDataset(rgnInfoDset, intValues2);
				hw.WriteDataset(rgnValuesDset, intValues);
				return;
			}
			long chunkSize = hw.GetChunkSize(dataset)[1];
			using H5DatasetID dataset2 = hw.CreateAndOpen2dExtendibleDataset<int>(rgnInfoDset, (int)Math.Min(50000L, datasetDimension2), 3);
			using H5DatasetID dsetId = hw.CreateAndOpen1dExtendibleDataset<int>(rgnValuesDset, (int)Math.Max(1000L, Math.Min(50000L, datasetDimension2 * 4)));
			int count = (int)Math.Ceiling((double)datasetDimension2 / (double)chunkSize);
			int ordinal = 0;
			int ordinal2 = 0;
			T[,] rows = new T[(int)checked((nint)datasetDimension), (int)checked((nint)chunkSize)];
			Span<T> values = MemoryMarshal.CreateSpan(ref rows[0, 0], rows.GetLength(0) * rows.GetLength(1));
			for (int j = 0; j < count; j++)
			{
				int chunkSizeTerm = (int)(j * chunkSize);
				int count2 = (int)Math.Min(chunkSize, datasetDimension2 - chunkSizeTerm);
				int[] chunkIndex = new int[2] { 0, j };
				hw.ReadChunkDirect(dataset, chunkIndex, values);
				List<LocalExtrema>[] localExtremaListArray = new List<LocalExtrema>[count2];
				for (int k = 0; k < count2; k++)
				{
					List<LocalExtrema> localExtremas = new List<LocalExtrema>();
					Slope slope;
					if (rows[1, k] >= rows[0, k])
					{
						slope = Slope.Increasing;
						localExtremas.Add(new LocalExtrema(ExtremaType.Minima, 0));
					}
					else
					{
						slope = Slope.Decreasing;
						localExtremas.Add(new LocalExtrema(ExtremaType.Maxima, 0));
					}
					for (int l = 1; l < datasetDimension3; l++)
					{
						T localValue = rows[l, k];
						T localValue2 = rows[l - 1, k];
						if (localValue >= localValue2)
						{
							if (slope != Slope.Increasing)
							{
								localExtremas.Add(new LocalExtrema(ExtremaType.Minima, l - 1));
								slope = Slope.Increasing;
							}
						}
						else if (slope == Slope.Increasing)
						{
							localExtremas.Add(new LocalExtrema(ExtremaType.Maxima, l - 1));
							slope = Slope.Decreasing;
						}
					}
					if (localExtremas.Last().Extrema == ExtremaType.Minima)
					{
						localExtremas.Add(new LocalExtrema(ExtremaType.Maxima, datasetDimension3 - 1));
					}
					else
					{
						localExtremas.Add(new LocalExtrema(ExtremaType.Minima, datasetDimension3 - 1));
					}
					localExtremaListArray[k] = localExtremas;
				}
				int[] intValuesArray = new int[localExtremaListArray.Sum((List<LocalExtrema> e) => e.Count)];
				int[,] intValues3 = new int[count2, 3];
				int intervalIndex = 0;
				for (int intervalIndex2 = 0; intervalIndex2 < count2; intervalIndex2++)
				{
					intValues3[intervalIndex2, 0] = ordinal2;
					intValues3[intervalIndex2, 1] = localExtremaListArray[intervalIndex2].Count;
					intValues3[intervalIndex2, 2] = (int)localExtremaListArray[intervalIndex2][0].Extrema;
					for (int intervalIndex3 = 0; intervalIndex3 < localExtremaListArray[intervalIndex2].Count; intervalIndex3++)
					{
						intValuesArray[intervalIndex + intervalIndex3] = localExtremaListArray[intervalIndex2][intervalIndex3].Time;
					}
					intervalIndex += localExtremaListArray[intervalIndex2].Count;
					ordinal2 += localExtremaListArray[intervalIndex2].Count;
				}
				hw.AddRows(dataset2, intValues3);
				hw.AddElements(dsetId, intValuesArray);
				ordinal++;
				if (ordinal % 10 == 0)
				{
					prog.ReportProgress((int)((double)(100 * ordinal) / (double)count));
				}
			}
			prog.ReportProgress(100);
		});
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				_reader.Dispose();
				_writer.Dispose();
			}
			_isDisposed = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public bool TryReadMeshValues(OutputVariable variable, int meshIndex, Memory<int> elementIndexes, Memory<float>[] values)
	{
		if (meshIndex != 0)
		{
			throw new ArgumentException("Only supporting a single mesh currently.");
		}
		return ReadTransposedDataset(MappedOutputBlock, variable, elementIndexes, values);
	}

	public string DebugPrint(OutputVariable variable, int faceOrCellIndex, int nRows = 40, int nCols = 180)
	{
		try
		{
			float[] floatValues = new float[AssertGetBaseOutputBlock().ProfileCount];
			ReadTransposedDataset<float>(variable, faceOrCellIndex, floatValues);
			return StationElevationProfile.FromConstantStationing(((IEnumerable<float>)floatValues).Select((Func<float, double>)((float f) => f)).ToArray()).DebugPrint(nRows, nCols);
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}
}
