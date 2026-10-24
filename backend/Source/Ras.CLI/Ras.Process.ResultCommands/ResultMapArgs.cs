using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommandLine;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.IO.Rasters;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Layers;
using Ras.Layers.Terrains;
using Ras.Mapping;
using Ras.Output;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.ResultCommands;

[Verb("map", false, null, HelpText = "Generates a map from a RAS 2025+ result.")]
public class ResultMapArgs
{
	private sealed class ResolvedTerrain
	{
		public IResample<float> Resampler { get; init; }

		public Projection SourceProjection { get; init; }

		public double BaseCellSize { get; init; }

		public string[] InputFiles { get; init; }
	}

	public const string Verb = "map";

	private bool CalculatedMapRequested => !string.IsNullOrWhiteSpace(Calculation);

	[Value(0, MetaName = "result", Required = true, HelpText = "RAS 2025 Result file (*.h5).")]
	public string ResultFilename { get; set; }

	[Value(1, MetaName = "output", Required = false, HelpText = "Destination filename. Should not exist. E.G. \"./out.tif\". If not provided, defaults to a name derived from the map type beneath the result's export folder.")]
	public string OutputFilename { get; set; }

	[Option("type", Required = true, HelpText = "Type of map to create, e.g. 'depth', 'velocity', 'watersurface'")]
	public string MapType { get; set; }

	[Option('p', "profile", Required = false, SetName = "singleProfileMap", HelpText = "Map profile to export. Required for single-profile map types (e.g. depth, velocity). Can be an integer index (0 to #Profiles-1, counted from the start) or a string name ('01JAN1900'). Also accepts 'max'/'min' (envelope), 'last' or '^N' (N-th from the end, so last := '^1' := 'last').")]
	public string Profile { get; set; }

	[Option('f', "force", Required = false, HelpText = "If set, automatically overwrite the OutputFilename. Defaults to false.")]
	public bool Force { get; set; }

	[Option("no-infer", Required = false, HelpText = "Do not open the result's parent project for projection or associations. When set, the terrain and output file must be provided explicitly (cell-size is still derived from the terrain).")]
	public bool NoInfer { get; set; }

	[Option("dry-run", Required = false, HelpText = "Print the resolved inputs (terrain, output file, cell-size, projection, etc.) without generating the map.")]
	public bool DryRun { get; set; }

	[Option("terrain", Required = false, HelpText = "Terrain file to provide elevations. May be a HEC-RAS terrain (.h5) or a single .tif file. If not provided, inferred from the result's parent project. Required when there is no parent project or '--no-infer' is set.")]
	public string TerrainFilename { get; set; }

	[Option("cell-size", Required = false, HelpText = "Output file resolution (cell size). Value is in native (pre-projected) units. If not provided, defaults to the finest resolution found across the terrain's input files.")]
	public double CellSize { get; set; }

	[Option("projection", Required = false, HelpText = "Output projection. Can be a filename (interpreted as an ESRI .prj), an EPSG code provided as a string, e.g. 'EPSG:900913', or a 'PROJ:' proj4 string.")]
	public string OutputProjection { get; set; }

	[Option("calculation", Required = false, SetName = "calculatedMap", HelpText = "If set, will calculate a map over time for the given parameter at the mapping output timestepvalid calculations are \"max,\" \"duration-over,\" and \"arrival-time.\"")]
	public string Calculation { get; set; }

	[Option("context", Required = false, SetName = "calculatedMap", HelpText = "Context value for the calculation. For example, the threshold depth a duration calculation, or the threshold of depth at which arrival time should be calculated")]
	public string CalculationContext { get; set; }

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		CliHelpers.TimeStep("Initializing GDAL...", () =>
		{
			GDALSetup.InitializeMultiplatform();
		});
		if (!File.Exists(ResultFilename))
		{
			progressReporter.ReportMessage(ResultFilename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		if (!GetMapType(out var mt))
		{
			return false;
		}
		if (!ValidateProfileRequirement(mt, progressReporter))
		{
			return false;
		}
		Result result = CliHelpers.TimeStep("Loading Result...", () => new Result(ResultFilename));
		bool terrainInferred = false;
		if (string.IsNullOrWhiteSpace(TerrainFilename) && !NoInfer)
		{
			terrainInferred = TryInferTerrainFromProject(result, progressReporter);
		}
		if (string.IsNullOrWhiteSpace(TerrainFilename))
		{
			progressReporter.ReportMessage("No terrain available. Provide '--terrain', or run inside a project without '--no-infer'.");
			return false;
		}
		if (!File.Exists(TerrainFilename))
		{
			progressReporter.ReportMessage(TerrainFilename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		if (!CliHelpers.TryResolveProjection(OutputProjection, out var prj))
		{
			return false;
		}
		if (!TryLoadTerrain(out var terrain))
		{
			return false;
		}
		bool cellSizeInferred = false;
		if (CellSize <= 0.0)
		{
			if (terrain.BaseCellSize <= 0.0)
			{
				progressReporter.ReportMessage("Could not determine a cell-size from the terrain. Provide '--cell-size' (must be > 0).");
				return false;
			}
			CellSize = terrain.BaseCellSize;
			cellSizeInferred = true;
		}
		bool isNullOrWhiteSpace = string.IsNullOrWhiteSpace(OutputFilename);
		if (isNullOrWhiteSpace && NoInfer)
		{
			progressReporter.ReportMessage("An output file is required with '--no-infer'.");
			return false;
		}
		if (isNullOrWhiteSpace)
		{
			OutputFilename = DeriveDefaultOutputFilename(result);
		}
		if (DryRun)
		{
			PrintDryRun(mt, terrain, prj, terrainInferred, cellSizeInferred, isNullOrWhiteSpace);
			return true;
		}
		if (isNullOrWhiteSpace)
		{
			result.EnsureExportDirectory();
		}
		if (!ValidateOutputFile())
		{
			return false;
		}
		if (CalculatedMapRequested)
		{
			return WriteCalculatedMap(mt, result, terrain.Resampler, terrain.SourceProjection, progressReporter);
		}
		if (!GetProfileIdx(result, out var pfIdx))
		{
			return false;
		}
		ResultMapResampler resultMapResampler = new ResultMapResampler(mt, result.BaseOutputBlock, terrain.Resampler, result.Geometry.FlowAreaLayer.AllPropertyTables());
		resultMapResampler.EnableStoredMapSettings();
		IResample<float> band = resultMapResampler.AsSingleProfile(pfIdx);
		Extent targetExtent = CliHelpers.TimeStep("Loading Geometry...", () => result.Geometry.HydraulicExtent);
		RasterDefinition rdef = new RasterDefinition(targetExtent, CellSize);
		Projection sourceProjection = terrain.SourceProjection;
		Reproject(ref prj, sourceProjection, ref band, ref rdef);
		return WriteMap(prj, band, rdef, progressReporter);
	}

	private bool ValidateProfileRequirement(MapTypes mt, ProgressReporter pr)
	{
		if (CalculatedMapRequested)
		{
			return true;
		}
		if (string.IsNullOrWhiteSpace(Profile))
		{
			pr.ReportMessage("A '--profile' is required for a " + mt.ToString() + " map (or use '--calculation' for a time-aggregated map).");
			return false;
		}
		return true;
	}

	private bool TryInferTerrainFromProject(Result result, ProgressReporter pr)
	{
		Project project = TryOpenParentProject(pr);
		if (project == null)
		{
			return false;
		}
		result.Project = project;
		Terrain associatedTerrain = result.AssociatedTerrain;
		if (associatedTerrain == null || string.IsNullOrEmpty(associatedTerrain.Filename))
		{
			pr.ReportMessage("Parent project has no terrain to infer from.");
			return false;
		}
		TerrainFilename = associatedTerrain.Filename;
		pr.ReportMessage("Inferred terrain: " + TerrainFilename.InDoubleQuotes());
		return true;
	}

	private Project TryOpenParentProject(ProgressReporter pr)
	{
		string directoryName = Path.GetDirectoryName(Path.GetFullPath(ResultFilename));
		string projDir = Directory.GetParent(directoryName)?.FullName;
		if (string.IsNullOrEmpty(projDir) || !Directory.Exists(projDir))
		{
			return null;
		}
		if (Directory.GetFiles(projDir, "*.ras").Length == 0)
		{
			return null;
		}
		return CliHelpers.TimeStep("Opening parent project...", () =>
		{
			OperationResult operationResult = Project.TryLoad(projDir, out var prj);
			if (!operationResult)
			{
				pr.ReportMessage(operationResult.GetConcatenatedMessages());
				return (Project)null;
			}
			return prj;
		});
	}

	private string DeriveDefaultOutputFilename(Result result)
	{
		string calculation = (CalculatedMapRequested ? Calculation : Profile);
		string path = SanitizeForFilename(MapType + "." + calculation) + ".tif";
		return Path.Combine(result.DefaultExportDirectory, path);
	}

	private static string SanitizeForFilename(string raw)
	{
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			raw = raw.Replace(oldChar, '_');
		}
		return raw;
	}

	private void PrintDryRun(MapTypes mt, ResolvedTerrain terrain, Projection outPrj, bool terrainInferred, bool cellSizeInferred, bool outputInferred)
	{
		string path = (string.IsNullOrWhiteSpace(OutputProjection) ? "(source / terrain projection)" : OutputProjection);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Dry run - no map will be generated.");
		stringBuilder.AppendLine("  Result:      " + ResultFilename.InDoubleQuotes());
		stringBuilder.AppendLine("  Map type:    " + mt);
		if (CalculatedMapRequested)
		{
			stringBuilder.AppendLine("  Calculation: " + Calculation + (string.IsNullOrWhiteSpace(CalculationContext) ? "" : (" (context: " + CalculationContext + ")")));
		}
		else
		{
			stringBuilder.AppendLine("  Profile:     " + Profile);
		}
		stringBuilder.AppendLine("  Terrain:     " + TerrainFilename.InDoubleQuotes() + Tag(terrainInferred));
		string[] inputFiles = terrain.InputFiles;
		foreach (string x in inputFiles)
		{
			stringBuilder.AppendLine("    Input:     " + x.InDoubleQuotes());
		}
		stringBuilder.AppendLine("  Cell-size:   " + CellSize + Tag(cellSizeInferred));
		stringBuilder.AppendLine("  Projection:  " + path);
		stringBuilder.AppendLine("  Output:      " + OutputFilename.InDoubleQuotes() + Tag(outputInferred));
		Console.WriteLine(stringBuilder.ToString().TrimEnd());
		static string Tag(bool inferred)
		{
			if (!inferred)
			{
				return "";
			}
			return " (inferred)";
		}
	}

	private bool GetMapType(out MapTypes mt)
	{
		if (!MapTypeParser.TryParse(MapType, out mt))
		{
			Console.WriteLine("Could not parse " + MapType.InDoubleQuotes() + " as map type.");
			return false;
		}
		return true;
	}

	private bool WriteMap(Projection outPrj, IResample<float> band, RasterDefinition rdef, ProgressReporter pr)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Console.WriteLine("Writing " + OutputFilename.InDoubleQuotes() + "...");
		TiffExportEngine.ExportWithOverviews(OutputFilename, band, rdef, outPrj, pr);
		stopwatch.Stop();
		Console.WriteLine();
		Console.WriteLine("File written " + stopwatch.Elapsed);
		return true;
	}

	private static void Reproject(ref Projection outPrj, Projection srcPrj, ref IResample<float> band, ref RasterDefinition rdef)
	{
		if (outPrj != null)
		{
			ReprojectionTransformer transformer = new ReprojectionTransformer(srcPrj, outPrj);
			IResample<float> resample = band.WithTransformer(transformer);
			if (!rdef.TryTransform(transformer, out var res))
			{
				throw new Exception("Reprojection error.");
			}
			band = resample;
			rdef = res;
			Console.WriteLine("Reprojecting from " + srcPrj.GetProj4() + " to " + outPrj.GetProj4());
		}
		else
		{
			outPrj = srcPrj;
		}
	}

	private bool WriteCalculatedMap(MapTypes mt, Result result, IResample<float> terr, Projection outPrj, ProgressReporter pr)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Console.WriteLine("Writing " + OutputFilename.InDoubleQuotes() + "...");
		CalculatedMapTuningParams options = new CalculatedMapTuningParams
		{
			TimestepsPerChunk = 100
		};
		IHorizontalOutputBlock baseOutputBlock = result.BaseOutputBlock;
		Geometry geometry = result.Geometry;
		MeshPropertyTables[] tables = geometry.FlowAreaLayer.AllPropertyTables();
		Extent hydraulicExtent = geometry.HydraulicExtent;
		MapperCache mapperCache = new MapperCache(baseOutputBlock, tables);
		mapperCache.SetTimestepCacheCount(options.TimestepsPerChunk);
		Mapper mapper = Mapper.CreateMapper(mt, baseOutputBlock, tables, mapperCache);
		Func<float, float, float> calculation = GetCalculation(Calculation, CalculationContext);
		StaticMapComputer staticMapComputer = new StaticMapComputer(geometry, terr, mapper, calculation);
		RasterDefinition rasterDefinition = new RasterDefinition(hydraulicExtent, CellSize);
		DiskCacheWriter<float> cacheReaderWriter = new DiskCacheWriter<float>(rasterDefinition, -9999f);
		GeoTiffWriter<float> geoTiffWriter = GeoTiffWriter<float>.JustBaseLevel(OutputFilename, rasterDefinition, -9999f, outPrj);
		CalculatedMapEngine.ComputeAcrossTimeAndSpace(geoTiffWriter.BaseWriter, staticMapComputer, cacheReaderWriter, options, pr);
		geoTiffWriter.Dispose();
		stopwatch.Stop();
		Console.WriteLine();
		Console.WriteLine("File written " + stopwatch.Elapsed);
		return true;
	}

	private bool TryLoadTerrain(out ResolvedTerrain terrain)
	{
		terrain = null;
		if (TerrainFilename.EndsWith(".h5") || TerrainFilename.EndsWith(".hdf"))
		{
			Terrain terrain2 = new Terrain(TerrainFilename);
			double maxValue = double.MaxValue;
			List<string> stringList = new List<string>();
			foreach (RasterTerrainInput allRasterInput in terrain2.AllRasterInputs())
			{
				stringList.Add(allRasterInput.Filename);
				IRasterPyramid<float> resampler = allRasterInput.GetResampler();
				if (resampler != null)
				{
					maxValue = Math.Min(maxValue, resampler.RasterDefinition.CellSize);
				}
			}
			terrain = new ResolvedTerrain
			{
				Resampler = terrain2,
				SourceProjection = terrain2.TryGetProjection(),
				BaseCellSize = ((maxValue == double.MaxValue) ? 0.0 : maxValue),
				InputFiles = stringList.ToArray()
			};
			return true;
		}
		if (TerrainFilename.EndsWith(".tif"))
		{
			IRasterPyramid<float> rasterPyramid = new TiffDataSource<float>(TerrainFilename).AsRasterizer();
			terrain = new ResolvedTerrain
			{
				Resampler = rasterPyramid,
				SourceProjection = GDALRaster.GetProjection(TerrainFilename),
				BaseCellSize = rasterPyramid.RasterDefinition.CellSize,
				InputFiles = new string[1] { TerrainFilename }
			};
			return true;
		}
		Console.WriteLine("Unknown File/Extension: " + TerrainFilename.InDoubleQuotes());
		return false;
	}

	private bool GetProfileIdx(Result result, out int pfIdx)
	{
		IHorizontalOutputBlock baseOutputBlock = result.BaseOutputBlock;
		if (int.TryParse(Profile, out pfIdx))
		{
			if (pfIdx < 0 || pfIdx >= baseOutputBlock.ProfileCount)
			{
				Console.WriteLine("Bad profile index (out of bounds): " + Profile.InDoubleQuotes());
				return false;
			}
		}
		else
		{
			if (Profile.StartsWith('^'))
			{
				if (int.TryParse(Profile.Substring(1), out pfIdx))
				{
					pfIdx = baseOutputBlock.ProfileCount - pfIdx;
					return true;
				}
				Console.WriteLine("Bad profile index: " + Profile.InDoubleQuotes());
				return false;
			}
			if (Profile.RoughlyEquals("last"))
			{
				pfIdx = baseOutputBlock.ProfileCount - 1;
			}
			else if (Profile.RoughlyEquals("max") || Profile.RoughlyEquals("maximum"))
			{
				pfIdx = int.MaxValue;
			}
			else
			{
				if (!Profile.RoughlyEquals("min") && !Profile.RoughlyEquals("minimum"))
				{
					pfIdx = baseOutputBlock.ProfileNames.FirstIndexOf((string textValue) => textValue.RoughlyEquals(Profile));
					if (pfIdx == -1)
					{
						Console.WriteLine("No best-match profile found for " + Profile.InDoubleQuotes());
						return false;
					}
					return true;
				}
				pfIdx = int.MinValue;
			}
		}
		return true;
	}

	private bool ValidateOutputFile()
	{
		if (File.Exists(OutputFilename) && !Force)
		{
			Console.WriteLine(OutputFilename.InDoubleQuotes() + " already exists. Use --force to replace.");
			return false;
		}
		if (Force && File.Exists(OutputFilename))
		{
			File.Delete(OutputFilename);
		}
		return true;
	}

	public Func<float, float, float> GetCalculation(string calculation, string calculationContext)
	{
		if (calculation.RoughlyEquals("max"))
		{
			if (!string.IsNullOrWhiteSpace(calculationContext))
			{
				Console.WriteLine("max is calculated over the time window of the available data. Context argument was ignored");
			}
			return StaticMapAccumulators.Max;
		}
		if (calculation.RoughlyEquals("duration-over"))
		{
			if (!float.TryParse(calculationContext, out var result))
			{
				Console.WriteLine("Could not parse context argument as a float. Defaulting to threshold 0.");
				result = 0f;
			}
			Result result2 = new Result(ResultFilename);
			return StaticMapAccumulators.DurationOver((float)(result2.BaseOutputBlock.Times[1] - result2.BaseOutputBlock.Times[0]).TotalMinutes, result);
		}
		throw new Exception("No calculator implemented for: " + calculation);
	}
}
