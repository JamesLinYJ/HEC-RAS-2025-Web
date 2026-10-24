using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Rasters.Convolution;
using Geospatial.Transformers;
using Microsoft.Extensions.Logging;
using TiffBinaryReader;
using Utility;
using Utility.Extensions;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.IO;

public static class TiffExportEngine
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TiffExportEngine");

	public static void Export<T>(string destFilename, IResample<T> resampler, RasterDefinition exportRdef, Projection projectionMetadata = null, ProgressReporter pr = null, ParallelizationIntent intent = ParallelizationIntent.Parallel) where T : unmanaged, IEquatable<T>
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		T valueOrDefault = resampler.NoDataValue.GetValueOrDefault();
		List<TagMetadata> tagMetadatas = new List<TagMetadata>();
		tagMetadatas.Add(TagMetadata.ForWriting<T>(exportRdef.Cols, exportRdef.Rows, isOverlay: false, valueOrDefault));
		using (TiffFileWriter writer = new TiffFileWriter(destFilename, tagMetadatas))
		{
			TiffTileWriter<T> writer2 = new TiffTileWriter<T>(writer, 0, exportRdef, valueOrDefault);
			pr.ReportMessage("Exporting " + Path.GetFileName(destFilename) + "...");
			ExportEngine.Write(resampler, writer2, ExportEngine.GetBaseLevelProgress(pr), intent);
		}
		if (pr.CancelRequested)
		{
			pr.ReportMessage("Operation Cancelled - Cleaning up.");
			if (FileHelpers.TryDelete(destFilename))
			{
				pr.ReportMessage("Cleanup successful: File deleted.");
			}
			else
			{
				pr.ReportMessage("Could not delete " + destFilename.InDoubleQuotes());
			}
			return;
		}
		GDALRaster.SetLocation(destFilename, exportRdef);
		if (projectionMetadata != null)
		{
			GDALRaster.SetProjection(destFilename, projectionMetadata);
		}
		stopwatch.Stop();
		pr.ReportMessage("Finished in " + stopwatch.Elapsed);
	}

	public static void ExportWithOverviews<T>(string destFilename, IResample<T> resampler, RasterDefinition exportRdef, Projection projectionMetadata = null, ProgressReporter pr = null, ParallelizationIntent intent = ParallelizationIntent.Parallel) where T : unmanaged, IEquatable<T>
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		T valueOrDefault = resampler.NoDataValue.GetValueOrDefault();
		List<TagMetadata> tagMetadatas = new List<TagMetadata>();
		tagMetadatas.Add(TagMetadata.ForWriting<T>(exportRdef.Cols, exportRdef.Rows, isOverlay: false, valueOrDefault));
		List<RasterDefinition> defaultOverviews = exportRdef.DefaultOverviews();
		for (int i = 0; i < defaultOverviews.Count; i++)
		{
			RasterDefinition rasterDefinition = defaultOverviews[i];
			tagMetadatas.Add(TagMetadata.ForWriting<T>(rasterDefinition.Cols, rasterDefinition.Rows, isOverlay: true, valueOrDefault));
		}
		using (TiffFileWriter writer = new TiffFileWriter(destFilename, tagMetadatas))
		{
			TiffTileWriter<T> tiffTileWriter = new TiffTileWriter<T>(writer, 0, exportRdef, valueOrDefault);
			List<TiffTileWriter<T>> tiffTileWriters = new List<TiffTileWriter<T>>();
			for (int j = 0; j < defaultOverviews.Count; j++)
			{
				tiffTileWriters.Add(new TiffTileWriter<T>(writer, j + 1, defaultOverviews[j], valueOrDefault));
			}
			pr.ReportMessage("Exporting " + Path.GetFileName(destFilename) + "...");
			ExportEngine.Write(resampler, tiffTileWriter, ExportEngine.GetBaseLevelProgress(pr), intent);
			if (!pr.CancelRequested)
			{
				pr.ReportMessage("Creating Overviews...");
				if (typeof(T) == typeof(float))
				{
					ITileProvider<float> baseData = tiffTileWriter as ITileProvider<float>;
					List<TiffTileWriter<float>> tiffTileWritersList = new List<TiffTileWriter<float>>();
					foreach (TiffTileWriter<T> tiffTileWriter2 in tiffTileWriters)
					{
						tiffTileWritersList.Add(tiffTileWriter2 as TiffTileWriter<float>);
					}
					ExportEngine.CreateOverviews(baseData, tiffTileWritersList, ExportEngine.GetAllOverviewProgress(pr), intent);
				}
				else if (typeof(T) == typeof(double))
				{
					ITileProvider<double> baseData2 = tiffTileWriter as ITileProvider<double>;
					List<TiffTileWriter<double>> tiffTileWriters2 = new List<TiffTileWriter<double>>();
					foreach (TiffTileWriter<T> tiffTileWriter3 in tiffTileWriters)
					{
						tiffTileWriters2.Add(tiffTileWriter3 as TiffTileWriter<double>);
					}
					ExportEngine.CreateOverviews(baseData2, tiffTileWriters2, ExportEngine.GetAllOverviewProgress(pr), intent);
				}
				else if (typeof(T) == typeof(byte))
				{
					ITileProvider<byte> baseData3 = tiffTileWriter as ITileProvider<byte>;
					List<TiffTileWriter<byte>> tiffTileWriters3 = new List<TiffTileWriter<byte>>();
					foreach (TiffTileWriter<T> tiffTileWriter4 in tiffTileWriters)
					{
						tiffTileWriters3.Add(tiffTileWriter4 as TiffTileWriter<byte>);
					}
					ExportEngine.CreateDiscreteOverviews(baseData3, tiffTileWriters3, ExportEngine.GetAllOverviewProgress(pr));
				}
				else if (typeof(T) == typeof(short))
				{
					ITileProvider<short> baseData4 = tiffTileWriter as ITileProvider<short>;
					List<TiffTileWriter<short>> tiffTileWriters4 = new List<TiffTileWriter<short>>();
					foreach (TiffTileWriter<T> tiffTileWriter5 in tiffTileWriters)
					{
						tiffTileWriters4.Add(tiffTileWriter5 as TiffTileWriter<short>);
					}
					ExportEngine.CreateDiscreteOverviews(baseData4, tiffTileWriters4, ExportEngine.GetAllOverviewProgress(pr));
				}
				else if (typeof(T) == typeof(int))
				{
					ITileProvider<int> baseData5 = tiffTileWriter as ITileProvider<int>;
					List<TiffTileWriter<int>> tiffTileWriters5 = new List<TiffTileWriter<int>>();
					foreach (TiffTileWriter<T> tiffTileWriter6 in tiffTileWriters)
					{
						tiffTileWriters5.Add(tiffTileWriter6 as TiffTileWriter<int>);
					}
					ExportEngine.CreateDiscreteOverviews(baseData5, tiffTileWriters5, ExportEngine.GetAllOverviewProgress(pr));
				}
				else
				{
					if (!(typeof(T) == typeof(long)))
					{
						throw new NotSupportedException("Cannot export overviews for Type " + typeof(T).Name + " yet.");
					}
					ITileProvider<long> baseData6 = tiffTileWriter as ITileProvider<long>;
					List<TiffTileWriter<long>> tiffTileWriters6 = new List<TiffTileWriter<long>>();
					foreach (TiffTileWriter<T> tiffTileWriter7 in tiffTileWriters)
					{
						tiffTileWriters6.Add(tiffTileWriter7 as TiffTileWriter<long>);
					}
					ExportEngine.CreateDiscreteOverviews(baseData6, tiffTileWriters6, ExportEngine.GetAllOverviewProgress(pr));
				}
				pr.ReportProgress(100);
			}
		}
		if (pr.CancelRequested)
		{
			pr.ReportMessage("Operation Cancelled - Cleaning up.");
			if (FileHelpers.TryDelete(destFilename))
			{
				pr.ReportMessage("Cleanup successful: File deleted.");
			}
			else
			{
				pr.ReportMessage("Could not delete " + destFilename.InDoubleQuotes());
			}
			return;
		}
		GDALRaster.SetLocation(destFilename, exportRdef);
		if (projectionMetadata != null)
		{
			GDALRaster.SetProjection(destFilename, projectionMetadata);
		}
		stopwatch.Stop();
		pr.ReportMessage("Finished in " + stopwatch.Elapsed);
	}

	public static OperationResult ConvertFile<T>(string sourceFilename, string destinationFilename, InterpolatedRasterFileConverterConfig<T> config, ProgressReporter pr = null) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		if (!File.Exists(sourceFilename))
		{
			throw new FileNotFoundException(sourceFilename);
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (config == null)
		{
			config = InterpolatedRasterFileConverterConfig<T>.Default();
		}
		CheckDestinationFile(destinationFilename, config, pr);
		IDisposable cleanup = null;
		try
		{
			IResample<T> resampler = BuildResamplerFromConfig(RasterReader.TryGetBestRasterizer<T>(sourceFilename, out var supportsMT, out cleanup, config.SuggestedDriver) ?? throw new Exception("Could not find a suitable driver for source file " + sourceFilename), config, GDALRaster.GetProjection(sourceFilename), pr, out var exportRdef, out var outPrj);
			ParallelizationIntent intent = (supportsMT ? ParallelizationIntent.Parallel : ParallelizationIntent.Serial);
			Stopwatch stopwatch = Stopwatch.StartNew();
			if (config.GenerateOverviews)
			{
				ExportWithOverviews(destinationFilename, resampler, exportRdef, outPrj, pr, intent);
			}
			else
			{
				Export(destinationFilename, resampler, exportRdef, outPrj, pr, intent);
			}
			stopwatch.Stop();
			pr.ReportMessage("Wrote " + destinationFilename + " in " + stopwatch.Elapsed);
			return true;
		}
		catch (Exception ex)
		{
			return OperationResult.Fail("Converting Raster file", ex);
		}
		finally
		{
			cleanup?.Dispose();
		}
	}

	private static IResample<T> BuildResamplerFromConfig<T>(IRaster<T> rasterizer, InterpolatedRasterFileConverterConfig<T> config, Projection srcPrj, ProgressReporter pr, out RasterDefinition exportRdef, out Projection outPrj) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		IResample<T> resample = BuilResamplerFromConfig_SpatialManip(rasterizer, config, srcPrj, pr, out exportRdef, out outPrj);
		if (config.DestinationNoData.HasValue)
		{
			T destinationNoDataConfigTerm = config.DestinationNoData.Value;
			T? noDataValue = resample.NoDataValue;
			if (noDataValue.HasValue && noDataValue.Value.Equals(destinationNoDataConfigTerm))
			{
				pr.ReportMessage("Source NoData matches destination NoData ({NDV})", destinationNoDataConfigTerm);
			}
			else
			{
				resample = resample.ReplaceNodata(destinationNoDataConfigTerm);
				pr.ReportMessage("Source NoData will be converted to destination NoData ({NDV})", destinationNoDataConfigTerm);
			}
		}
		if (config.RoundingBase.HasValue)
		{
			resample = resample.WithTransformer(new RoundValueTransformer<T>(config.RoundingBase.Value, config.RoundingDigits));
			pr.ReportMessage("Rounding Output: (1/" + Math.Pow(RoundingConverters.RoundBaseToInt(config.RoundingBase.Value), config.RoundingDigits) + ")");
		}
		if (config.ConvertScaleFactor.HasValue && config.ConvertScaleFactor.Value != T.One)
		{
			resample = resample.WithTransformer(new ScaleValueTransformer<T>(config.ConvertScaleFactor.Value));
			pr.ReportMessage("Vertical Conversion: " + config.ConvertScaleFactor.Value);
		}
		return resample;
	}

	private static IResample<T> BuilResamplerFromConfig_SpatialManip<T>(IRaster<T> rasterizer, RasterFileConverterConfig config, Projection srcPrj, ProgressReporter pr, out RasterDefinition exportRdef, out Projection outPrj) where T : unmanaged, IEquatable<T>
	{
		exportRdef = rasterizer.RasterDefinition;
		IResample<T> resample = rasterizer;
		if (config.SourceClip.HasValue)
		{
			resample = resample.ClipTo(config.SourceClip.Value);
		}
		if (config.OverrideSourceProjection != null)
		{
			srcPrj = config.OverrideSourceProjection;
		}
		outPrj = config.DestinationProjection ?? srcPrj;
		if (config.DestinationProjection != null)
		{
			if (srcPrj == null)
			{
				pr.ReportMessage("Source data does not have a projection (file will not be reprojected).");
			}
			else if (srcPrj == outPrj || srcPrj.IsEqual(outPrj))
			{
				pr.ReportMessage("Projection matches destination (file will not be reprojected).");
			}
			else
			{
				pr.ReportMessage("Projection does not match destination (file will be reprojected).");
				ReprojectionTransformer orMake = ReprojectionTransformer.GetOrMake(srcPrj, outPrj);
				resample = resample.WithTransformer(orMake);
				if (!exportRdef.TryTransform(orMake, out var res))
				{
					throw new Exception("Reprojection error.");
				}
				exportRdef = res;
			}
		}
		else
		{
			outPrj = srcPrj;
		}
		if (config.OverrideExportRaster.HasValue)
		{
			exportRdef = config.OverrideExportRaster.Value;
		}
		return resample;
	}

	public static OperationResult ConvertFile<T>(string sourceFilename, string destinationFilename, RasterFileConverterConfig config, ProgressReporter pr = null) where T : unmanaged, IEquatable<T>
	{
		if (!File.Exists(sourceFilename))
		{
			throw new FileNotFoundException(sourceFilename);
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (config == null)
		{
			config = RasterFileConverterConfig.Default();
		}
		CheckDestinationFile(destinationFilename, config, pr);
		IDisposable cleanup = null;
		try
		{
			IResample<T> resampler = BuilResamplerFromConfig_SpatialManip(RasterReader.TryGetBestRasterizer<T>(sourceFilename, out var supportsMT, out cleanup, config.SuggestedDriver) ?? throw new Exception("Could not find a suitable driver for source file " + sourceFilename), config, GDALRaster.GetProjection(sourceFilename), pr, out var exportRdef, out var outPrj);
			ParallelizationIntent intent = (supportsMT ? ParallelizationIntent.Parallel : ParallelizationIntent.Serial);
			Stopwatch stopwatch = Stopwatch.StartNew();
			if (config.GenerateOverviews)
			{
				ExportWithOverviews(destinationFilename, resampler, exportRdef, outPrj, pr, intent);
			}
			else
			{
				Export(destinationFilename, resampler, exportRdef, outPrj, pr, intent);
			}
			stopwatch.Stop();
			pr.ReportMessage("Wrote " + destinationFilename + " in " + stopwatch.Elapsed);
			return true;
		}
		catch (Exception ex)
		{
			return OperationResult.Fail("Converting Raster file", ex);
		}
		finally
		{
			cleanup?.Dispose();
		}
	}

	public static void RegisterRastersNEW<T>(IList<string> filenames, string destinationFilename, InterpolatedRasterFileConverterConfig<T> config, ProgressReporter pr = null) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (config == null)
		{
			config = InterpolatedRasterFileConverterConfig<T>.Default();
		}
		if (filenames == null || filenames.Count == 0)
		{
			throw new ArgumentException("No files provided.");
		}
		CheckDestinationFile(destinationFilename, config, pr);
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tif");
		List<(IRaster<T>, IDisposable, bool)> valueTuples = new List<(IRaster<T>, IDisposable, bool)>(filenames.Count);
		try
		{
			Projection projection = GDALRaster.GetProjection(filenames[0]);
			for (int i = 0; i < filenames.Count; i++)
			{
				string filename = filenames[i];
				if (!File.Exists(filename))
				{
					throw new FileNotFoundException(filename);
				}
				IRaster<T> raster = RasterReader.TryGetBestRasterizer<T>(filename, out var supportsMT, out var cleanup, config.SuggestedDriver);
				if (raster == null)
				{
					throw new Exception("Could not find a suitable driver for source file " + filename);
				}
				valueTuples.Add((raster, cleanup, supportsMT));
			}
			IResample<T> resampler = BuildResamplerFromConfig(new RegisteredRasterCollection<T>(valueTuples.Select(((IRaster<T>, IDisposable, bool) d) => d.Item1), config.DestinationNoData ?? valueTuples[0].Item1.NoDataValue ?? InterpolatedRasterFileConverterConfig<T>.DefaultNoDataValue()), config, projection, pr, out var exportRdef, out var outPrj);
			ParallelizationIntent intent = (valueTuples.Any(((IRaster<T>, IDisposable, bool) d) => d.Item3) ? ParallelizationIntent.Parallel : ParallelizationIntent.Serial);
			ProgressReporter pr2 = pr.SubTask("Merging rasters to single TIFF...", 0f, 0.6f);
			Stopwatch stopwatch = Stopwatch.StartNew();
			Export(path, resampler, exportRdef, outPrj, pr2, intent);
			stopwatch.Stop();
			pr.ReportMessage("Wrote " + destinationFilename + " in " + stopwatch.Elapsed);
			using TiffDataSource<float> tiffDataSource = new TiffDataSource<float>(path);
			float valueOrDefault = tiffDataSource.NoDataValue.GetValueOrDefault();
			Projection projection2 = GDALRaster.GetProjection(path);
			using GeoTiffWriter<float> geoTiffWriter = GeoTiffWriter<float>.WithDefaultOverviews(destinationFilename, tiffDataSource.BasePyramidLevel.RasterDefinition, valueOrDefault, projection2);
			WindowKernel<float, float> kernel = Kernels.FillNDV_SimpleAverage(valueOrDefault);
			ProgressReporter prog = pr.SubTask("Processing Registered TIFF...", 60f, 0.25f);
			RasterOperations.ProcessKernel(tiffDataSource.BasePyramidLevel, kernel, geoTiffWriter.BaseWriter, int.MaxValue, prog);
			ProgressReporter pr3 = pr.SubTask("Generating Overviews...", 85f, 0.15f);
			geoTiffWriter.ComputeOverviewsFromBaseData(pr3);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Error in RegisterRasters");
			throw;
		}
		finally
		{
			if (valueTuples != null)
			{
				foreach (var localValue in valueTuples)
				{
					IDisposable iDisposable = localValue.Item2;
					try
					{
						iDisposable?.Dispose();
					}
					catch (Exception exception2)
					{
						_logger.LogError(exception2, "Could not dispose resource.");
					}
				}
			}
			FileHelpers.TryDelete(path);
		}
	}

	public static void RegisterRasters(IList<string> filenames, string outputFilename, ProgressReporter pr = null)
	{
		if (File.Exists(outputFilename))
		{
			throw new Exception(outputFilename + " already exists.");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		new List<RasterDefinition>(filenames.Count);
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".vrt");
		string path2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tif");
		try
		{
			GDALRaster.BuildVRT(filenames, path);
			ProgressReporter pr2 = pr.SubTask("Converting VRT to Tiff...", 0f, 0.6f);
			InterpolatedRasterFileConverterConfig<float> interpolatedRasterFileConverterConfig = InterpolatedRasterFileConverterConfig<float>.Default();
			interpolatedRasterFileConverterConfig.GenerateOverviews = false;
			ConvertFile(path, path2, interpolatedRasterFileConverterConfig, pr2);
			using TiffDataSource<float> tiffDataSource = new TiffDataSource<float>(path2);
			float valueOrDefault = tiffDataSource.NoDataValue.GetValueOrDefault();
			Projection projection = GDALRaster.GetProjection(path2);
			WindowKernel<float, float> kernel = Kernels.FillNDV_SimpleAverage(valueOrDefault);
			using GeoTiffWriter<float> geoTiffWriter = GeoTiffWriter<float>.WithDefaultOverviews(outputFilename, tiffDataSource.BasePyramidLevel.RasterDefinition, valueOrDefault, projection);
			ProgressReporter prog = pr.SubTask("Processing Registered Tiff...", 60f, 0.25f);
			RasterOperations.ProcessKernel(tiffDataSource.BasePyramidLevel, kernel, geoTiffWriter.BaseWriter, int.MaxValue, prog);
			ProgressReporter pr3 = pr.SubTask("Generating Overviews...", 85f, 0.15f);
			geoTiffWriter.ComputeOverviewsFromBaseData(pr3);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Error in RegisterRasters");
			throw;
		}
		finally
		{
			FileHelpers.TryDelete(path);
			FileHelpers.TryDelete(path2);
		}
	}

	public static List<(string, List<int>)> SmartMerge(IList<string> filenames, string outDirectory = "", ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (string.IsNullOrEmpty(outDirectory))
		{
			outDirectory = Path.GetTempPath();
		}
		List<(string, List<int>)> valueTuples = new List<(string, List<int>)>();
		List<List<int>> registeredFiles = GDALRaster.GetRegisteredFiles(filenames);
		int count = filenames.Count;
		int registeredFileCountSum = 0;
		foreach (List<int> registeredFile in registeredFiles)
		{
			ProgressReporter progressReporter = pr.SubTask("Importing...", (float)registeredFileCountSum / (float)count, (float)registeredFile.Count / (float)count);
			registeredFileCountSum += registeredFile.Count;
			string combinePathTerm = "";
			try
			{
				if (registeredFile.Count == 1)
				{
					valueTuples.Add((filenames[registeredFile[0]], registeredFile));
				}
				else
				{
					List<string> filenames2 = registeredFile.Select((int ii) => filenames[ii]).ToList();
					combinePathTerm = Path.Combine(outDirectory, Guid.NewGuid().ToString() + ".tif");
					RegisterRastersNEW(filenames2, combinePathTerm, InterpolatedRasterFileConverterConfig<float>.Default(), progressReporter);
					progressReporter.ReportProgress(100);
					valueTuples.Add((combinePathTerm, registeredFile));
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Error importing/merging rasters.");
				FileHelpers.TryDelete(combinePathTerm);
				throw;
			}
			progressReporter.ReportProgress(100);
		}
		return valueTuples;
	}

	private static void CheckDestinationFile(string filename, RasterFileConverterConfig config, ProgressReporter pr)
	{
		if (File.Exists(filename))
		{
			if (!config.OverwriteDestinationFile)
			{
				throw new Exception("File " + filename.InDoubleQuotes() + " already exists.");
			}
			pr.ReportMessage("File " + filename.InDoubleQuotes() + " already exists, deleting...");
			File.Delete(filename);
		}
	}
}
