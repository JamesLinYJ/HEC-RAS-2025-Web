using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Ras.Layers;
using Ras.Layers.Terrains;
using Utility;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.TerrainCommands;

internal static class TerrainOps
{
	public static bool AllInputsExist(IEnumerable<string> inputFiles)
	{
		bool condition = false;
		foreach (string inputFile in inputFiles)
		{
			if (!File.Exists(inputFile))
			{
				Console.WriteLine(inputFile.InDoubleQuotes() + " does not exist.");
				condition = true;
			}
		}
		return !condition;
	}

	public static bool AddRastersToTerrain(Terrain terrain, IEnumerable<string> inputFiles, Projection outPrj, int rounding, ProgressReporter pr)
	{
		string directoryName = Path.GetDirectoryName(terrain.Filename);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(terrain.Filename);
		foreach (string inputFile in inputFiles)
		{
			string fileNameWithoutExtension2 = Path.GetFileNameWithoutExtension(inputFile);
			string path = Path.Combine(directoryName, fileNameWithoutExtension + "." + fileNameWithoutExtension2 + ".tif");
			if (File.Exists(path))
			{
				pr.ReportMessage("Error: Target file " + path + " already exists.");
				return false;
			}
			IRaster<float> raster = RasterReader.TryGetBestRasterizer<float>(inputFile, out var supportsMT, out var cleanup);
			if (!raster.NoDataValue.HasValue)
			{
				pr.ReportMessage($"Warning: {inputFile.InDoubleQuotes()} has no internal NoData value we could parse. The raster file will be written with NoData = {-9999f}. To control this behavior, set an explicit NoData value on the input raster before importing.");
				raster = raster.ReplaceNodataRaster(-9999f);
			}
			RasterDefinition exportRdef = raster.RasterDefinition;
			IResample<float> resampler = raster;
			Projection projection = GDALRaster.GetProjection(inputFile);
			Projection projection2 = outPrj ?? projection;
			if (projection.IsEqual(projection2))
			{
				pr.ReportMessage(inputFile + " Projection matches destination (will not reproject)");
			}
			else
			{
				pr.ReportMessage(inputFile + " Projection does not match destination (File will be reprojected)");
				ReprojectionTransformer transformer = new ReprojectionTransformer(projection, projection2);
				resampler = raster.WithTransformer(transformer);
				if (!exportRdef.TryTransform(transformer, out var res))
				{
					throw new Exception("Reprojection error.");
				}
				exportRdef = res;
			}
			if (rounding > 0)
			{
				resampler = resampler.WithTransformer(new RoundValueTransformer<float>(RoundBase.Base2, rounding));
				pr.ReportMessage("Rounding Output: (1/" + Math.Pow(2.0, rounding) + ")");
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			TiffExportEngine.ExportWithOverviews(path, resampler, exportRdef, projection2, pr, supportsMT ? ParallelizationIntent.Parallel : ParallelizationIntent.Serial);
			stopwatch.Stop();
			pr.ReportMessage("Wrote " + path + " in " + stopwatch.Elapsed);
			terrain.Add(RasterTerrainInput.CreateAndScan(terrain, path));
			cleanup.Dispose();
		}
		return true;
	}
}
