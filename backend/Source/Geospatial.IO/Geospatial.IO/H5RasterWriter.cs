using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.IO.Rasters;
using Geospatial.PairedData;
using Geospatial.Rasters;
using H5Assist;
using H5Assist.Identifiers;
using HDF.PInvoke;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;
using Utility.Units;

namespace Geospatial.IO;

public static class H5RasterWriter
{
	public static class Attributes
	{
		public const string NoData = "NoData";

		public const string TimeSeriesDataType = "Time Series Data Type";

		public const string RateTimeDenominator = "Rate Time Units";

		public const string Times = "Times";

		public const string TimesDesc = "Times (DESC)";

		public const string StorageConfiguration = "Storage Configuration";

		public const string StorageConfig_Sequential = "Sequential";

		public const string StorageConfig_Tiled = "Tiled";

		public const string GUID = "GUID";

		public const string Version = "Version";
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("H5RasterWriter");

	public static void WriteRaster<T>(string filename, IRaster<T> raster, string dataset, ProgressReporter prog = null) where T : unmanaged, IEquatable<T>
	{
		using H5Writer hw = new H5Writer(filename);
		WriteRaster(hw, raster, dataset, prog);
	}

	public static void WriteRaster<T>(H5Writer hw, IRaster<T> raster, string dataset, ProgressReporter prog = null) where T : unmanaged, IEquatable<T>
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		T[] values = new T[raster.RasterDefinition.CellCount];
		raster.GetRange(raster.RasterDefinition.FullRange(), values);
		hw.WriteDataset(dataset, values);
		SignRaster(hw, dataset, raster.RasterDefinition, isTiledStorage: false);
	}

	public static IRaster<T> TryReadRaster<T>(H5Reader hr, string dataset, ProgressReporter errMessages = null) where T : unmanaged, IEquatable<T>
	{
		if (errMessages == null)
		{
			errMessages = ProgressReporter.None();
		}
		if (!IsValidRaster(hr, dataset, out var rdef, errMessages))
		{
			return null;
		}
		hr.ReadDataset(dataset, out T[] values);
		return new RasterData<T>(rdef, values);
	}

	public static void WriteBandedRaster<T>(string filename, IMultiBandRaster<T> raster, string dataset, TypedBandedRasterWriterConfig<T> configs, ProgressReporter prog = null) where T : unmanaged, IEquatable<T>
	{
		using H5Writer hw = new H5Writer(filename);
		WriteBandedRaster(hw, raster, dataset, configs, prog);
	}

	public static void WriteBandedRaster<T>(H5Writer hw, IMultiBandRaster<T> raster, string dataset, TypedBandedRasterWriterConfig<T> configs, ProgressReporter prog = null) where T : unmanaged, IEquatable<T>
	{
		if (configs.StorageConfiguration == MultiBandRasterStorageConfiguration.Contiguous)
		{
			WriteBandedRaster_Contiguous(hw, raster, dataset, configs, prog);
			return;
		}
		if (configs.StorageConfiguration == MultiBandRasterStorageConfiguration.Tiled)
		{
			WriteBandedRaster_Tiled(hw, raster, dataset, configs, prog);
			return;
		}
		throw new NotSupportedException();
	}

	private static void WriteBandedRaster_Contiguous<T>(H5Writer hw, IMultiBandRaster<T> raster, string dataset, TypedBandedRasterWriterConfig<T> configs, ProgressReporter prog = null) where T : unmanaged, IEquatable<T>
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		T? destinationNoData = configs.DestinationNoData;
		int cellCount = raster.RasterDefinition.CellCount;
		ulong[] optimalChunking = H5Writer.OptimalChunking<T>(raster.ProfileCount, cellCount);
		hw.Create2dExtendibleDataset<T>(dataset, (int)optimalChunking[0], (int)optimalChunking[1]);
		if (raster.NoDataValue.HasValue)
		{
			if (destinationNoData.HasValue)
			{
				WriteNoData(hw, dataset, destinationNoData.Value);
				prog.ReportMessage("Replacing NoData value: " + raster.NoDataValue.Value.ToString() + " to " + destinationNoData.Value);
			}
			else
			{
				WriteNoData(hw, dataset, raster.NoDataValue.Value);
			}
		}
		SignRaster(hw, dataset, raster.RasterDefinition, isTiledStorage: false);
		using H5DatasetID dataset2 = hw.OpenDataset(dataset);
		hw.SetExtent(dataset2, (ulong)raster.ProfileCount, (ulong)raster.RasterDefinition.CellCountLong);
		ThreadLocal<T[]> tlBuffer = new ThreadLocal<T[]>(() => new T[raster.RasterDefinition.CellCount]);
		hw.WriteParallelByRow(dataset2, (int pfIdx) =>
		{
			T[] tlBufferTerm = tlBuffer.Value;
			raster.GetBand(pfIdx, tlBufferTerm);
			return tlBufferTerm;
		}, prog);
	}

	private static void WriteBandedRaster_Tiled<T>(H5Writer hw, IMultiBandRaster<T> raster, string dataset, TypedBandedRasterWriterConfig<T> configs, ProgressReporter prog = null) where T : unmanaged, IEquatable<T>
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		_ = raster.RasterDefinition.CellCount;
		_ = raster.ProfileCount;
		T? srcNoData = configs.OverrideSourceNoData;
		if (!srcNoData.HasValue)
		{
			srcNoData = raster.NoDataValue;
		}
		T outputNoData;
		if (configs.DestinationNoData.HasValue)
		{
			outputNoData = configs.DestinationNoData.Value;
		}
		else
		{
			if (!srcNoData.HasValue)
			{
				_logger.LogWarning("WriteBandedRaster: No value provided for destination NoData - intentional?");
			}
			outputNoData = srcNoData.GetValueOrDefault();
		}
		if (hw.ExistsAsDataset(dataset))
		{
			hw.Delete(dataset);
		}
		hw.Create2dExtendibleDataset(dataset, 1, 65536, outputNoData);
		WriteNoData(hw, dataset, outputNoData);
		ProgressReporter progressReporter = prog;
		string? srcNoDataTerm = srcNoData?.ToString() ?? "(null)";
		T outputNoData2 = outputNoData;
		progressReporter.ReportMessage("Source NoData: " + srcNoDataTerm + ", Destination NoData: " + outputNoData2);
		SignRaster(hw, dataset, raster.RasterDefinition, isTiledStorage: true);
		H5DatasetID dset = hw.OpenDataset(dataset);
		try
		{
			RasterDefinition tileDef = raster.RasterDefinition.TiledRaster(256);
			ulong nRows = (ulong)raster.ProfileCount;
			ulong nCols = (ulong)(tileDef.CellCountLong * 256 * 256);
			hw.SetExtent(dset, nRows, nCols);
			using ThreadLocal<T[]> tlBuffer1 = new ThreadLocal<T[]>(() => new T[raster.RasterDefinition.CellCount]);
			object progLock = new object();
			int done = 0;
			using ((raster as ICacheEviction)?.TryPauseCache())
			{
				bool isThreadSafe = false;
				if (H5.is_library_threadsafe(ref isThreadSafe) < 0) throw new IOException("Cannot determine HDF library thread safety.");
				// Some packaged HDF runtimes are serial builds. Both source reads and
				// destination writes must stay serial for those runtimes.
				ParallelOptions parallelOptions = new ParallelOptions
				{
					MaxDegreeOfParallelism = isThreadSafe ? 32 : 1
				};
				System.Threading.Tasks.Parallel.For(0, raster.ProfileCount, parallelOptions, (int pfIdx) =>
				{
					T[] buf = tlBuffer1.Value;
					raster.GetBand(pfIdx, buf);
					System.Threading.Tasks.Parallel.For(0, tileDef.CellCount, parallelOptions, (int tIdx) =>
					{
						int[] intValues = new int[2] { pfIdx, 0 };
						Memory<T> mem;
						using (SharedMemoryPool<T>.RentNoFill(65536, out mem))
						{
							mem.Span.Fill(outputNoData);
							intValues[1] = tIdx;
							RasterDefinition cellRasterDefinition = tileDef.GetCellRasterDefinition(tIdx, 256);
							RasterOperations.CopyTo(raster.RasterDefinition, buf, cellRasterDefinition, mem);
							if (srcNoData.HasValue)
							{
								Buffers.ReplaceValue(mem.Span, srcNoData.Value, outputNoData);
							}
							if (!Buffers.EqualsScalar(mem.Span, outputNoData))
							{
								hw.WriteChunkDirect(dset, intValues, mem);
							}
						}
					});
					lock (progLock)
					{
						done++;
						prog.ReportProgressFraction((float)done / (float)raster.ProfileCount);
					}
				});
			}
		}
		finally
		{
			if (dset != null)
			{
				((IDisposable)dset).Dispose();
			}
		}
		prog.ReportProgress(100);
	}

	public static void WriteTimeSeriesRaster<T>(H5Writer hw, ITimeSeriesRaster<T> raster, string dataset, TypedBandedRasterWriterConfig<T> configs, ProgressReporter prog = null) where T : unmanaged, INumber<T>
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		ProgressReporter progressReporter = null;
		ProgressReporter pr = null;
		if (configs.CreateOverviews)
		{
			progressReporter = ExportEngine.GetBaseLevelProgress(prog);
			pr = ExportEngine.GetAllOverviewProgress(prog);
		}
		else
		{
			progressReporter = prog;
		}
		if (configs.StorageConfiguration == MultiBandRasterStorageConfiguration.Contiguous)
		{
			WriteBandedRaster_Contiguous(hw, raster, dataset, configs, progressReporter);
			WriteTimesAttribute(hw, dataset, raster.Times);
			if (configs.CreateOverviews)
			{
				_logger.LogWarning("Cannot create overviews for a non-tiled data source.");
			}
			return;
		}
		if (configs.StorageConfiguration == MultiBandRasterStorageConfiguration.Tiled)
		{
			WriteBandedRaster_Tiled(hw, raster, dataset, configs, progressReporter);
			WriteTimesAttribute(hw, dataset, raster.Times);
			if (configs.CreateOverviews)
			{
				BuildOverviews<T>(hw, dataset, pr);
			}
			return;
		}
		throw new NotSupportedException();
	}

	public static string GetDefaultOverviewGroupName(string baseDataset)
	{
		return baseDataset + " (Overviews)/";
	}

	public static string GetDefaultOverviewLevelName(string pyramidGroup, int level)
	{
		return H5Reader.PathCombine(pyramidGroup, "Level " + level);
	}

	private static void BuildOverviews<T>(H5Writer hw, string dataset, ProgressReporter pr) where T : unmanaged, INumber<T>
	{
		H5MultiBandTileProvider<T> result = null;
		try
		{
			if (!H5MultiBandTileProvider<T>.TryOpen(hw, dataset, out result).Result)
			{
				throw new Exception("Error writing H5 TimeSeries raster overviews.");
			}
			List<RasterDefinition> defaultOverviews = result.RasterDefinition.DefaultOverviews();
			List<H5MultiBandTileWriter<T>> h5MultiBandTileWriters = new List<H5MultiBandTileWriter<T>>();
			string defaultOverviewGroupName = GetDefaultOverviewGroupName(dataset);
			for (int i = 0; i < defaultOverviews.Count; i++)
			{
				int level = i + 1;
				string defaultOverviewLevelName = GetDefaultOverviewLevelName(defaultOverviewGroupName, level);
				h5MultiBandTileWriters.Add(new H5MultiBandTileWriter<T>(hw, defaultOverviewLevelName, defaultOverviews[i], result.ProfileCount, result.NoDataValue));
			}
			ExportEngine.CreateOverviews(result, h5MultiBandTileWriters, pr);
		}
		finally
		{
			result?.Dispose();
		}
	}

	public static OperationResult TryReadTimeSeriesRaster<T>(H5Reader hr, string dataset, out H5TimeSeriesRasterDataSource<T> dataSource) where T : unmanaged, IEquatable<T>
	{
		return H5TimeSeriesRasterDataSource<T>.TryOpen(hr, dataset, out dataSource);
	}

	public static void WriteRasterMetadata(H5Writer hw, string dataset, RasterMetadata metadata)
	{
		if (!hw.ExistsAsDataset(dataset))
		{
			throw new ArgumentException(dataset + " does not exist.");
		}
		if (!string.IsNullOrEmpty(metadata.Projection))
		{
			hw.WriteAttribute(dataset, "Projection", metadata.Projection);
		}
		if (!string.IsNullOrEmpty(metadata.Units))
		{
			hw.WriteAttribute(dataset, "Units", metadata.Units);
		}
		if (!string.IsNullOrEmpty(metadata.DataType))
		{
			hw.WriteAttribute(dataset, "Data Type", metadata.DataType);
		}
	}

	public static void WriteAccumulatedRaster<T>(string filename, string dataset, IAccumulatedRasterizer<T> rasterizer, TimeSeriesDataType desiredWriteType, T? replaceNoDataValue = null, ProgressReporter prog = null) where T : unmanaged, IEquatable<T>
	{
		using H5Writer hw = new H5Writer(filename);
		WriteAccumulatedRaster(hw, dataset, rasterizer, desiredWriteType, replaceNoDataValue, prog);
	}

	public static void WriteAccumulatedRaster<T>(H5Writer hw, string dataset, IAccumulatedRasterizer<T> rasterizer, TimeSeriesDataType desiredWriteType, T? replaceNoDataValue = null, ProgressReporter prog = null) where T : unmanaged, IEquatable<T>
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		int pfCount = rasterizer.Times.Count;
		int cellCount = rasterizer.RasterDefinition.CellCount;
		ulong[] optimalChunking = H5Writer.OptimalChunking<T>(pfCount, cellCount);
		hw.Create2dExtendibleDataset<T>(dataset, (int)optimalChunking[0], (int)optimalChunking[1]);
		bool desiredWriteTypeAmountTerm = false;
		if (rasterizer.NoDataValue.HasValue)
		{
			if (replaceNoDataValue.HasValue)
			{
				WriteNoData(hw, dataset, replaceNoDataValue.Value);
				prog.ReportMessage("Replacing NoData value: " + rasterizer.NoDataValue.Value.ToString() + " to " + replaceNoDataValue.Value);
				desiredWriteTypeAmountTerm = true;
			}
			else
			{
				WriteNoData(hw, dataset, rasterizer.NoDataValue.Value);
			}
		}
		desiredWriteTypeAmountTerm = desiredWriteTypeAmountTerm && desiredWriteType != TimeSeriesDataType.Amount;
		SignRaster(hw, dataset, rasterizer.RasterDefinition, isTiledStorage: false);
		H5DatasetID dset = hw.OpenDataset(dataset);
		try
		{
			rasterizer.SampleAsDataType(desiredWriteType, (int pfIdx, Memory<T> data) =>
			{
				hw.AddRow(dset, data);
				prog.ReportProgress((int)(100f * ((float)(pfIdx + 1) / (float)pfCount)));
			});
		}
		finally
		{
			if (dset != null)
			{
				((IDisposable)dset).Dispose();
			}
		}
		WriteTimesAttribute(hw, dataset, rasterizer.Times);
		hw.WriteEnum(dataset, "Time Series Data Type", desiredWriteType);
		if (rasterizer.TimeDenominator != null)
		{
			hw.WriteAttribute(dataset, "Rate Time Units", rasterizer.TimeDenominator.Name);
		}
	}

	public static RasterMetadata ReadRasterMetadata(H5Reader hr, string dataset)
	{
		if (!hr.ExistsAsDataset(dataset))
		{
			throw new ArgumentException(dataset + " does not exist.");
		}
		return new RasterMetadata
		{
			Projection = hr.TryReadStringAttributeScalar(dataset, "Projection"),
			Units = hr.TryReadStringAttributeScalar(dataset, "Units"),
			DataType = hr.TryReadStringAttributeScalar(dataset, "Data Type")
		};
	}

	public static RasterMetadata ReadRasterMetadata<T>(NativeResourceWrapper<T> hrWrapper, string dataset) where T : H5Reader
	{
		return hrWrapper.UseResource((T hr) => ReadRasterMetadata(hr, dataset));
	}

	public static void WriteNoData<T>(H5Writer hw, string dataset, T noData)
	{
		hw.WriteAttribute(dataset, "NoData", noData);
	}

	public static void WriteTimesAttribute(H5Writer hw, string dataset, IEnumerable<DateTime> times)
	{
		long[] values = times.Select((DateTime t) => t.ToBinary()).ToArray();
		string[] values2 = times.Select((DateTime dt) => dt.ToString("yyyy-MM-dd HH:mm:ss")).ToArray();
		hw.WriteAttribute(dataset, "Times", values);
		hw.WriteAttribute(dataset, "Times (DESC)", values2);
	}

	public static DateTime[] ReadTimesAttribute(H5Reader hr, string dataset)
	{
		hr.ReadAttribute(dataset, "Times", out long[] values);
		return values.Select((long v) => DateTime.FromBinary(v)).ToArray();
	}

	public static void SignRaster(H5Writer hw, string dataset, RasterDefinition rasterDef, bool isTiledStorage)
	{
		HDF5VectorReader.WriteAttribute(hw, dataset, rasterDef);
		if (isTiledStorage)
		{
			hw.WriteAttribute(dataset, "Storage Configuration", "Tiled");
		}
		else
		{
			hw.WriteAttribute(dataset, "Storage Configuration", "Sequential");
		}
		hw.WriteAttribute(dataset, "GUID", Guid.NewGuid().ToString());
		hw.WriteAttribute(dataset, "Version", "1.0");
	}

	public static bool IsValidRaster(H5Reader hr, string dataset, out RasterDefinition rdef, ProgressReporter errMessages = null)
	{
		if (errMessages == null)
		{
			errMessages = ProgressReporter.None();
		}
		try
		{
			if (!hr.ExistsAsDataset(dataset))
			{
				rdef = RasterDefinition.NullRaster;
				errMessages.ReportMessage(dataset + " does not exist.");
				return false;
			}
			long[] datasetDimensions = hr.GetDatasetDimensions(dataset);
			if (datasetDimensions.Length != 1)
			{
				rdef = RasterDefinition.NullRaster;
				errMessages.ReportMessage(dataset + " has incorrect dimensions.");
				return false;
			}
			if (!hr.ExistsAsAttribute(dataset, "Storage Configuration"))
			{
				rdef = RasterDefinition.NullRaster;
				errMessages.ReportMessage(dataset + " missing required attribute 'Storage Configuration'.");
				return false;
			}
			hr.ReadAttributeScalar(dataset, "Storage Configuration", out var localValue);
			if (localValue != "Sequential" && localValue != "Tiled")
			{
				rdef = RasterDefinition.NullRaster;
				errMessages.ReportMessage(dataset + " has invalid value for required attribute 'Storage Configuration': " + localValue);
				return false;
			}
			rdef = HDF5VectorReader.TryReadRasterDefinition(hr, dataset);
			if (rdef.IsNull())
			{
				errMessages.ReportMessage(dataset + " has invalid RasterDefinition attributes.");
				return false;
			}
			if (rdef.CellCount != datasetDimensions[0])
			{
				errMessages.ReportMessage(dataset + " specified Raster has incorrect number of cells.");
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			rdef = RasterDefinition.NullRaster;
			errMessages.ReportMessage("Dataset " + dataset + " encountered fatal error: " + ex.Message);
			return false;
		}
	}

	public static bool IsTiledConfig(H5Reader hr, string dataset)
	{
		if (!hr.ExistsAsAttribute(dataset, "Storage Configuration"))
		{
			return false;
		}
		return hr.TryReadStringAttributeScalar(dataset, "Storage Configuration") == "Tiled";
	}

	public static MultiBandRasterStorageConfiguration GetStorageConfig(H5Reader hr, string dataset)
	{
		if (!IsTiledConfig(hr, dataset))
		{
			return MultiBandRasterStorageConfiguration.Contiguous;
		}
		return MultiBandRasterStorageConfiguration.Tiled;
	}

	private static Time TryParseRateTimeUnits(H5Reader hr, string dataset, Time fallbackValue)
	{
		string tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar(dataset, "Rate Time Units");
		Time result = fallbackValue;
		if (!string.IsNullOrEmpty(tryReadStringAttributeScalar))
		{
			Time time = Time.TryParse(tryReadStringAttributeScalar);
			if (time != null)
			{
				result = time;
			}
		}
		return result;
	}

	public static OperationResult TryReadBandedRaster<T>(NativeResourceWrapper<H5Reader> hr, string dataset, out RasterMetadata md, out H5MultiBandRasterProvider<T> result) where T : unmanaged, IEquatable<T>
	{
		OperationResult operationResult = H5MultiBandRasterProvider<T>.TryOpen(hr, dataset, out result);
		if (operationResult == null)
		{
			md = RasterMetadata.Empty;
			return operationResult;
		}
		md = ReadRasterMetadata(hr, dataset);
		return operationResult;
	}

	public static OperationResult TryReadTimeSeriesRaster<T>(NativeResourceWrapper<H5Reader> hr, string dataset, out RasterMetadata md, out H5TimeSeriesRasterDataSource<T> result) where T : unmanaged, IEquatable<T>
	{
		OperationResult operationResult = H5TimeSeriesRasterDataSource<T>.TryOpen(hr, dataset, out result);
		if (operationResult == null)
		{
			md = RasterMetadata.Empty;
			return operationResult;
		}
		md = ReadRasterMetadata(hr, dataset);
		return operationResult;
	}

	public static H5TimeSeriesRasterDataSource<T> ReadTimeSeriesRaster<T>(NativeResourceWrapper<H5Reader> hr, string dataset, out RasterMetadata md) where T : unmanaged, IEquatable<T>
	{
		OperationResult operationResult = TryReadTimeSeriesRaster(hr, dataset, out md, out H5TimeSeriesRasterDataSource<T> result);
		if (!operationResult.Result)
		{
			throw new Exception(operationResult.GetConcatenatedMessages());
		}
		return result;
	}

	public static OperationResult TryReadTimeSeriesRaster<T>(string filename, string dataset, bool keepFileHandleOpen, out H5TimeSeriesRasterDataSource<T> result) where T : unmanaged, IEquatable<T>
	{
		if (!File.Exists(filename))
		{
			result = null;
			return OperationResult.Fail(FileMessageCodes.GetFileNotFound(filename));
		}
		NativeResourceWrapper<H5Reader> nativeResourceWrapper = (keepFileHandleOpen ? NativeResourceWrapper<H5Reader>.Give(new H5Reader(filename)) : new NativeResourceWrapper<H5Reader>(() => new H5Reader(filename)));
		OperationResult operationResult = H5TimeSeriesRasterDataSource<T>.TryOpen(nativeResourceWrapper, dataset, out result);
		if (!operationResult.Result)
		{
			nativeResourceWrapper.Dispose();
		}
		return operationResult;
	}

	public static IAccumulatedRasterizer<float> TryReadAccumulatedRaster(string filename, string dataset, out OperationResult or)
	{
		throw new NotImplementedException();
	}

	public static IAccumulatedRasterizer<float> ReadAccumulatedRaster(NativeResourceWrapper<H5Reader> hwrap, string dataset)
	{
		if (!TryReadAccumulatedRaster(hwrap, dataset, out var result).Result)
		{
			throw new Exception("Could not read raster");
		}
		return result;
	}

	public static OperationResult TryReadAccumulatedRaster(NativeResourceWrapper<H5Reader> hwrap, string dataset, out IAccumulatedRasterizer<float> result)
	{
		OperationResult operationResult = TryReadTimeSeriesRaster(hwrap, dataset, out RasterMetadata _, out H5TimeSeriesRasterDataSource<float> ts);
		if (!operationResult.Result)
		{
			result = null;
			return operationResult;
		}
		IAccumulatedRasterizer<float> resTmp = null;
		operationResult = hwrap.UseResource((H5Reader hr) =>
		{
			if (!hr.TryReadEnum<TimeSeriesDataType>(dataset, "Time Series Data Type", out var localValue))
			{
				return OperationResult.Fail(H5Reader.MessageCodes.GetAttributeNotFound(hr.Filename, dataset, "Time Series Data Type", "TimeSeriesDataType"));
			}
			Time timeUnits = TryParseRateTimeUnits(hr, dataset, Time.Hours);
			ITimeSeriesRaster<float> resampler = ts.AsTimeSeriesRasterizer();
			resTmp = resampler.AsAccumulatedRasterizer(localValue, timeUnits);
			return OperationResult.Success();
		});
		result = resTmp;
		return operationResult;
	}

	public static void OptimizeForVerticalReading<T>(string filename, string rasterDataset, ProgressReporter prog = null) where T : unmanaged, IEquatable<T>
	{
		using H5Writer hw = new H5Writer(filename);
		OptimizeForVerticalReading<T>(hw, rasterDataset, prog);
	}

	public static void OptimizeForVerticalReading<T>(H5Writer hw, string rasterDataset, ProgressReporter prog = null) where T : unmanaged, IEquatable<T>
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (!hw.ExistsAsDataset(rasterDataset))
		{
			throw new ArgumentException(rasterDataset + " not found in " + hw.Filename);
		}
		long[] datasetDimensions = hw.GetDatasetDimensions(rasterDataset);
		int dsetHeight = (int)datasetDimensions[0];
		int dsetWidth = (int)datasetDimensions[1];
		int destChunkWidth = H5Reader.ColumnChunkSize(dsetHeight, dsetWidth, 1048576, 4);
		string defaultVerticalDatasetName = GetDefaultVerticalDatasetName(rasterDataset);
		hw.TransposeToColumnChunking<T>(rasterDataset, hw, defaultVerticalDatasetName, destChunkWidth, prog);
		hw.CopyAttributesTo(hw, rasterDataset, defaultVerticalDatasetName);
	}

	public static string TryGetVerticalDataset(H5Reader hr, string rasterDataset)
	{
		if (!hr.ExistsAsDataset(rasterDataset))
		{
			return string.Empty;
		}
		string defaultVerticalDatasetName = GetDefaultVerticalDatasetName(rasterDataset);
		if (!hr.ExistsAsDataset(defaultVerticalDatasetName))
		{
			return string.Empty;
		}
		return defaultVerticalDatasetName;
	}

	public static string GetDefaultVerticalDatasetName(string dataset)
	{
		return dataset + " (Vertical)";
	}
}
