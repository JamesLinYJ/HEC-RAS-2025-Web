using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Geospatial;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Hydraulics;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Classic;
using Ras.Layers.Meteorology;
using Ras.Layers.Payloads;
using Ras.Migration.Files;
using Ras.Migration.Native;
using Ras.Output;
using RasMapperLib;
using Utility.Extensions;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;
using Utility.Units;

namespace Ras.Migration;

public static class ModelConverter
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ModelConverter");

	public static readonly string OriginSoftware = Ras.Layers.Project.ImporterSoftwareName;

	public static readonly string OriginVersion = Ras.Layers.Project.AssemblySoftwareVersion;

	public static void MigrateRecursive(string rootDirectory, string outputDirectory, string excludeIfLike = "[Test]", MigrationParams migrate = null, Action<Ras.Layers.Project> onMigrationCompleted = null, ProgressReporter reporter = null)
	{
		if (!Directory.Exists(rootDirectory))
		{
			throw new DirectoryNotFoundException("rootDirectory");
		}
		if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
		{
			throw new ArgumentException("outputDirectory must not exist, or be empty.");
		}
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		List<string> stringList = (from fn in Directory.GetFiles(rootDirectory, "*.prj", SearchOption.AllDirectories)
			where Ras.Migration.Native.Project.IsValid(fn)
			select fn).ToList();
		if (!string.IsNullOrEmpty(excludeIfLike))
		{
			stringList = stringList.Where((string fn) => !fn.Contains(excludeIfLike)).ToList();
		}
		HashSet<string> stringSet = new HashSet<string>();
		List<string> stringListList = new List<string>();
		foreach (string textValue in stringList)
		{
			string directoryName = Path.GetDirectoryName(textValue);
			if (stringSet.Add(directoryName))
			{
				stringListList.Add(textValue);
			}
			else
			{
				reporter.ReportWarning(Path.GetRelativePath(rootDirectory, textValue) + " will be skipped - two projects cannot exist in the same directory.");
			}
		}
		stringList = stringListList;
		if (stringList.Count == 0)
		{
			reporter.ReportMessage("No projects found to migrate.");
			return;
		}
		reporter.ReportMessage(stringList.Count + " projects found to migrate...");
		if (!Directory.Exists(outputDirectory))
		{
			Directory.CreateDirectory(outputDirectory);
		}
		List<Tuple<string, string>> v6v7Map = new List<Tuple<string, string>>();
		foreach (string textValue2 in stringList)
		{
			string directoryName2 = Path.GetDirectoryName(textValue2);
			string relativePath = Path.GetRelativePath(rootDirectory, directoryName2);
			string path = Path.Combine(outputDirectory, relativePath);
			Directory.CreateDirectory(path);
			v6v7Map.Add(new Tuple<string, string>(textValue2, path));
		}
		object repLock = new object();
		ParallelOptions parallelOptions = new ParallelOptions();
		if (migrate.ForceSerialProcessing)
		{
			parallelOptions.MaxDegreeOfParallelism = 1;
		}
		int nDone = 0;
		Parallel.ForEach((IEnumerable<Tuple<string, string>>)v6v7Map, parallelOptions, (Action<Tuple<string, string>>)((Tuple<string, string> tup) =>
		{
			StringBuilder stringBuilder = new StringBuilder();
			ProgressReporter reporter2 = ProgressReporter.BuildString(stringBuilder);
			Ras.Layers.Project project = null;
			try
			{
				project = MigrateProject(tup.Item1, tup.Item2, migrate, reporter2);
			}
			catch (Exception innerException)
			{
				while (innerException != null)
				{
					stringBuilder.AppendLine(innerException.Message);
					stringBuilder.AppendLine("@" + innerException.StackTrace);
					innerException = innerException.InnerException;
				}
			}
			File.WriteAllText(Path.Combine(tup.Item2, "Migration.txt"), stringBuilder.ToString());
			onMigrationCompleted?.Invoke(project);
			lock (repLock)
			{
				nDone++;
				string nDoneV6v7MapTerm = "[" + nDone + "/" + v6v7Map.Count + "] " + Path.GetRelativePath(outputDirectory, tup.Item2);
				if (project != null)
				{
					reporter.ReportMessage(nDoneV6v7MapTerm + " Migrated Successfully");
				}
				else
				{
					reporter.ReportMessage(nDoneV6v7MapTerm + " Failed to Migrate");
				}
			}
		}));
	}

	public static Ras.Layers.Project MigrateProject(string prj6Filename, string outputDirectory, MigrationParams migration = null, ProgressReporter reporter = null)
	{
		Ras.Migration.Native.Project prj;
		return MigrateProject(prj6Filename, outputDirectory, out prj, migration, reporter);
	}

	public static Ras.Layers.Project MigrateProject(string prj6Filename, string outputDirectory, out Ras.Migration.Native.Project prj6, MigrationParams migration = null, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (migration == null)
		{
			migration = MigrationParams.Default();
		}
		if (!Directory.Exists(outputDirectory))
		{
			Directory.CreateDirectory(outputDirectory);
		}
		string directoryName = Path.GetDirectoryName(prj6Filename);
		DirectoryInfo directoryInfo = new DirectoryInfo(directoryName);
		if (StringExtensions.RoughlyEquals(other: new DirectoryInfo(outputDirectory).FullName, x: directoryInfo.FullName))
		{
			throw new NotSupportedException("Cannot output a project to the input version 6 directory.");
		}
		prj6 = new Ras.Migration.Native.Project(prj6Filename);
		string prj6Dir = prj6.ProjectDirectory;
		Ras.Layers.Project project = Ras.Layers.Project.CreateNew(Ras.Layers.Project.MakeProjectFilename(outputDirectory, prj6.Name), prj6.Units);
		project.Description = prj6.Description;
		Ras.Layers.Project project2 = project;
		project2.Description = project2.Description + Environment.NewLine + "Converted from " + prj6.ProjectFilename + " on " + DateTime.Now;
		project.EnsureProjectDirectoryStructureExists();
		List<Tuple<string, Action<string>>> externalDeps = new List<Tuple<string, Action<string>>>();
		foreach (Ras.Migration.Native.Terrain terrain2 in prj6.Terrains)
		{
			try
			{
				string sanitizeForOS = FileHelpers.SanitizeForOS(terrain2.Filename);
				if (!Path.IsPathFullyQualified(sanitizeForOS))
				{
					sanitizeForOS = Path.Combine(prj6.ProjectDirectory, sanitizeForOS);
				}
				if (!File.Exists(sanitizeForOS))
				{
					continue;
				}
				foreach (string allSourceFile in new TerrainLayer("", sanitizeForOS).GetAllSourceFiles())
				{
					string path = FileHelpers.SanitizeForOS(allSourceFile);
					if (!Path.IsPathFullyQualified(path))
					{
						path = Path.Combine(prj6.ProjectDirectory, path);
					}
					path = path.Replace("/./", "/").Replace("\\.\\", "\\");
					string fileName = Path.GetFileName(path);
					string path2 = Path.Combine(project.TerrainsDirectory, fileName);
					if (path2.RoughlyEndsWith(".hdf"))
					{
						path2 = Path.ChangeExtension(path2, ".h5");
					}
					if (File.Exists(path))
					{
						if (!File.Exists(path2))
						{
							if (migration.MigrateTerrainTiffsAsPathRelSymlinks && path2.RoughlyEndsWith(".tif"))
							{
								string relativePath = Path.GetRelativePath(Path.GetDirectoryName(path2), path);
								File.CreateSymbolicLink(path2, relativePath);
								reporter.ReportMessage("SymLink created: " + path.InDoubleQuotes() + " -> " + path2.InDoubleQuotes());
								continue;
							}
							Stopwatch stopwatch = Stopwatch.StartNew();
							File.Copy(path, path2);
							stopwatch.Stop();
							long length = new FileInfo(path).Length;
							reporter.ReportMessage("Copied terrain file " + path.InDoubleQuotes() + " to " + path2.InDoubleQuotes() + " (" + FileHelpers.PrettyByteCount(length) + " in " + stopwatch.Elapsed.ToString() + ")");
						}
						else
						{
							reporter.ReportMessage("Terrain file " + path.InDoubleQuotes() + " already exists, likely copied in an earlier pass.");
						}
					}
					else
					{
						reporter.ReportMessage("Expected terrain file " + path.InDoubleQuotes() + " did not exist.");
					}
				}
				Ras.Layers.Terrain terrain = new Ras.Layers.Terrain(Path.Combine(project.TerrainsDirectory, Path.GetFileNameWithoutExtension(sanitizeForOS) + ".h5"));
				terrain.OriginSoftware = OriginSoftware;
				terrain.OriginVersion = OriginVersion;
				project.AddTerrain(terrain);
			}
			catch (Exception innerException)
			{
				reporter.ReportMessage("Fatal error migrating " + terrain2.Filename.InDoubleQuotes() + ": " + innerException.Message);
				while (innerException.InnerException != null)
				{
					reporter.ReportMessage("INNER: " + innerException.Message);
					innerException = innerException.InnerException;
				}
				throw;
			}
		}
		foreach (Ras.Migration.Native.Geometry geometry3 in prj6.Geometries)
		{
			if (!File.Exists(geometry3.Filename))
			{
				continue;
			}
			Ras.Layers.Geometry geometry = project.AddNewGeometry(geometry3.Name);
			if (!File.Exists(geometry3.HDF5Filename))
			{
				reporter.ReportMessage(geometry3.HDF5Filename + " does not exist, cannot import geometry data.");
				continue;
			}
			RASGeometry rasGeom = new RASGeometry(geometry3.HDF5Filename);
			MigrateNewGeometryData(rasGeom, geometry, migration);
			geometry.OriginSoftware = OriginSoftware;
			geometry.OriginVersion = OriginVersion;
			geometry.Save();
			project.GetGeometryAssociation(geometry).Terrain = project.Terrains.FirstOrDefault((Ras.Layers.Terrain t) => t.Name.RoughlyEquals(rasGeom.TerrainLayername));
		}
		foreach (Ras.Migration.Native.BoundaryCondition boundaryCondition2 in prj6.BoundaryConditions)
		{
			Ras.Layers.BoundaryCondition boundaryCondition = project.AddNewBoundaryCondition(Ras.Layers.Project.Sanitize(boundaryCondition2.Name));
			boundaryCondition.OriginSoftware = OriginSoftware;
			boundaryCondition.OriginVersion = OriginVersion;
			boundaryCondition.Description = boundaryCondition2.Description;
			Ras.Migration.Native.Geometry geometry2 = null;
			foreach (Ras.Migration.Native.Plan plan2 in prj6.Plans)
			{
				if (plan2.BoundaryConditions == boundaryCondition2 && plan2.Geometry != null)
				{
					geometry2 = plan2.Geometry;
					break;
				}
			}
			RASGeometry rASGeometry = null;
			if (geometry2 != null && File.Exists(geometry2.HDF5Filename))
			{
				rASGeometry = new RASGeometry(geometry2.HDF5Filename);
			}
			foreach (Boundary boundary in boundaryCondition2.Boundaries)
			{
				BoundaryConditionLine bcl = new BoundaryConditionLine(boundaryCondition);
				bcl.Name = boundary.Name;
				if (string.IsNullOrEmpty(boundary.Name))
				{
					bcl.Name = boundary.Location.ToPrettyString();
				}
				bcl.Description = boundary.Description;
				string hydVar = "";
				if (boundary.HydrographType == HydrographType.Flow)
				{
					hydVar = "Flow";
				}
				else if (boundary.HydrographType == HydrographType.Stage)
				{
					hydVar = "Stage";
				}
				else if (boundary.HydrographType == HydrographType.NormalDepth)
				{
					hydVar = "Normal Depth";
				}
				else if (boundary.HydrographType == HydrographType.StageAndFlow)
				{
					reporter.ReportWarning("Stage+Flow boundary conditions not yet supported.");
				}
				else if (boundary.HydrographType == HydrographType.Precipitation)
				{
					reporter.ReportWarning("Simplified Precipitation boundary conditions no longer supported, please add as a constant Meteorologic BC instead.");
				}
				else if (boundary.HydrographType == HydrographType.GateOpening)
				{
					reporter.ReportWarning("Gate Openings not yet supported.");
				}
				else if (boundary.HydrographType == HydrographType.LateralInflow || boundary.HydrographType == HydrographType.UniformLateralInflow)
				{
					reporter.ReportWarning("Lateral Inflow hydrographs not yet supported.");
				}
				else
				{
					string textValue = "Migration alert - BC Hydrograph Type not recognized.";
					reporter.ReportWarning(textValue);
					_logger.LogWarning(textValue);
				}
				if (!string.IsNullOrEmpty(hydVar))
				{
					if (!boundary.HydroUseDss)
					{
						if (boundary.HydroUseAbsTime && !string.IsNullOrEmpty(boundary.HydroFixedStartDt) && !(boundary.HydroFixedStartDt == ",") && DateTime.TryParse(boundary.HydroFixedStartDt, out var _))
						{
							reporter.ReportWarning("Use Fixed Start conversion issue.");
						}
						throw new NotImplementedException();
					}
					RegisterConnection(boundary.HydroDssFile, (string conn) =>
					{
						bcl.Add(new ExternalPayload(hydVar, conn, boundary.HydroDssPath));
					});
				}
				if (rASGeometry != null)
				{
					if (!string.IsNullOrEmpty(boundary.Location.River))
					{
						string river = boundary.Location.River;
						string reach = boundary.Location.Reach;
						string rS = boundary.Location.RS;
						string lowerRS = boundary.Location.LowerRS;
						if (string.IsNullOrEmpty(lowerRS))
						{
							int findXS = rASGeometry.XS.FindXS(river, reach, rS);
							if (findXS >= 0)
							{
								bcl.Polyline = GeoConverter.Convert(rASGeometry.XS.Polyline(findXS));
							}
							bcl.Name = river + "-" + reach + "-" + rS;
						}
						else
						{
							int findXS2 = rASGeometry.XS.FindXS(river, reach, rS);
							int findXS3 = rASGeometry.XS.FindXS(river, reach, lowerRS);
							if (findXS2 >= 0)
							{
								_ = 0;
							}
							bcl.Name = river + "-" + reach + "-" + rS + "-" + lowerRS;
						}
					}
					else if (!string.IsNullOrEmpty(boundary.Location.BCLine))
					{
						int featureByName = rASGeometry.BoundaryConditionLines.GetFeatureByName(boundary.Location.BCLine);
						if (featureByName >= 0)
						{
							bcl.Polyline = GeoConverter.Convert(rASGeometry.BoundaryConditionLines.Polyline(featureByName));
						}
						bcl.Name = boundary.Location.BCLine;
					}
					else if (!string.IsNullOrEmpty(boundary.Location.Connection))
					{
						int featureByName2 = rASGeometry.Structures.GetFeatureByName(boundary.Location.Connection);
						if (featureByName2 >= 0)
						{
							bcl.Polyline = GeoConverter.Convert(rASGeometry.Structures.Polyline(featureByName2));
						}
						bcl.Name = boundary.Location.Connection;
					}
					else if (!string.IsNullOrEmpty(boundary.Location.StorageArea))
					{
						int featureByName3 = rASGeometry.StorageArea.GetFeatureByName(boundary.Location.StorageArea);
						if (featureByName3 >= 0)
						{
							Geospatial.Vectors.Polygon polygon = GeoConverter.Convert(rASGeometry.StorageArea.Polygon(featureByName3));
							double length2 = polygon.Length;
							Geospatial.Vectors.Point centroid = polygon.ComputeCentroid();
							double y = length2 / 100.0;
							bcl.Polyline = new Geospatial.Vectors.Polyline();
							bcl.Polyline.Add(centroid);
							bcl.Polyline.Add(centroid.Translate(0.0, y));
						}
						bcl.Name = boundary.Location.StorageArea;
					}
				}
				boundaryCondition.BoundaryConditionLines.Add(bcl);
			}
			foreach (MetStation metStation in boundaryCondition2.MetStations)
			{
				MeteorologicStation meteorologicStation = new MeteorologicStation();
				meteorologicStation.Name = metStation.Name;
				meteorologicStation.Location = new Geospatial.Vectors.Point(metStation.X, metStation.Y);
				if (float.IsNaN(metStation.GaugeHeight))
				{
					meteorologicStation.ElevationOffGround = 0f;
				}
				else
				{
					meteorologicStation.ElevationOffGround = metStation.GaugeHeight;
				}
				boundaryCondition.MetStations.Add(meteorologicStation);
			}
			MetBC metBC = boundaryCondition2.MetBCs.FirstOrDefault((MetBC bb) => bb.Name.RoughlyEquals("Wind Velocity X"));
			MetBC metBC2 = boundaryCondition2.MetBCs.FirstOrDefault((MetBC bb) => bb.Name.RoughlyEquals("Wind Velocity Y"));
			MetBC metBC3 = boundaryCondition2.MetBCs.FirstOrDefault((MetBC bb) => bb.Name.RoughlyEquals("Wind Speed"));
			MetBC metBC4 = boundaryCondition2.MetBCs.FirstOrDefault((MetBC bb) => bb.Name.RoughlyEquals("Wind Direction"));
			if (metBC != null && metBC2 != null && metBC.TryParseFromMode(out var dt) && metBC2.TryParseFromMode(out dt))
			{
				GenericSpatialBoundaryCondition genericSpatialBoundaryCondition = new GenericSpatialBoundaryCondition(boundaryCondition, "");
				GenericSpatialBoundaryCondition genericSpatialBoundaryCondition2 = new GenericSpatialBoundaryCondition(boundaryCondition, "");
				ConvertSpatialBC(metBC, genericSpatialBoundaryCondition);
				ConvertSpatialBC(metBC2, genericSpatialBoundaryCondition2);
				boundaryCondition.Wind.IsEnabled = true;
				if (genericSpatialBoundaryCondition.SpatialDataType == SpatialDataType.Constant && genericSpatialBoundaryCondition2.SpatialDataType == SpatialDataType.Constant)
				{
					boundaryCondition.Wind.SpatialDataType = SpatialDataType.Constant;
					boundaryCondition.Wind.ConstantVx = genericSpatialBoundaryCondition.ConstantValue;
					boundaryCondition.Wind.ConstantVy = genericSpatialBoundaryCondition2.ConstantValue;
					string checkNativeUnitsMessage = CheckNativeUnitsMessage(boundaryCondition, "Wind", metBC, "f/s", "m/s");
					if (string.IsNullOrEmpty(checkNativeUnitsMessage))
					{
						checkNativeUnitsMessage = CheckNativeUnitsMessage(boundaryCondition, "Wind", metBC2, "f/s", "m/s");
					}
					if (!string.IsNullOrEmpty(checkNativeUnitsMessage))
					{
						boundaryCondition.Wind.Description += checkNativeUnitsMessage;
						reporter.ReportMessage(checkNativeUnitsMessage);
					}
				}
				else if (genericSpatialBoundaryCondition.SpatialDataType == SpatialDataType.Point && genericSpatialBoundaryCondition2.SpatialDataType == SpatialDataType.Point)
				{
					boundaryCondition.Wind.SpatialDataType = SpatialDataType.Point;
					reporter.ReportMessage("TODO: Merge Vx/Vy wind gauge data.");
				}
				else if (genericSpatialBoundaryCondition.SpatialDataType != SpatialDataType.Gridded || genericSpatialBoundaryCondition2.SpatialDataType != SpatialDataType.Gridded)
				{
					reporter.ReportMessage("Error migrating wind data.");
				}
			}
			else if (metBC3 != null && metBC4 != null && metBC3.TryParseFromMode(out dt) && metBC4.TryParseFromMode(out dt))
			{
				GenericSpatialBoundaryCondition genericSpatialBoundaryCondition3 = new GenericSpatialBoundaryCondition(boundaryCondition, "");
				GenericSpatialBoundaryCondition genericSpatialBoundaryCondition4 = new GenericSpatialBoundaryCondition(boundaryCondition, "");
				ConvertSpatialBC(metBC3, genericSpatialBoundaryCondition3);
				ConvertSpatialBC(metBC4, genericSpatialBoundaryCondition4);
				boundaryCondition.Wind.IsEnabled = true;
				if (genericSpatialBoundaryCondition3.SpatialDataType == SpatialDataType.Constant && genericSpatialBoundaryCondition4.SpatialDataType == SpatialDataType.Constant)
				{
					boundaryCondition.Wind.SpatialDataType = SpatialDataType.Constant;
					VelocityMath.ConvertToVxVy((double)genericSpatialBoundaryCondition4.ConstantValue, (double)genericSpatialBoundaryCondition3.ConstantValue, DirectionConvention.ComingFrom, out double vx, out double vy);
					boundaryCondition.Wind.ConstantVx = (float)vx;
					boundaryCondition.Wind.ConstantVy = (float)vy;
					string checkNativeUnitsMessage2 = CheckNativeUnitsMessage(boundaryCondition, "Wind", metBC3, "f/s", "m/s");
					if (!string.IsNullOrEmpty(checkNativeUnitsMessage2))
					{
						boundaryCondition.Wind.Description += checkNativeUnitsMessage2;
						reporter.ReportMessage(checkNativeUnitsMessage2);
					}
				}
				else if (genericSpatialBoundaryCondition3.SpatialDataType == SpatialDataType.Point && genericSpatialBoundaryCondition4.SpatialDataType == SpatialDataType.Point)
				{
					boundaryCondition.Wind.SpatialDataType = SpatialDataType.Point;
					reporter.ReportMessage("TODO: Merge Mag/Dir wind gauge data.");
				}
				else if (genericSpatialBoundaryCondition3.SpatialDataType != SpatialDataType.Gridded || genericSpatialBoundaryCondition4.SpatialDataType != SpatialDataType.Gridded)
				{
					reporter.ReportMessage("Error migrating wind data.");
				}
			}
			foreach (MetBC metBC5 in boundaryCondition2.MetBCs)
			{
				if (metBC5.Name.RoughlyEquals("Wind Velocity X") || metBC5.Name.RoughlyEquals("Wind Velocity Y") || metBC5.Name.RoughlyEquals("Wind Speed") || metBC5.Name.RoughlyEquals("Wind Direction"))
				{
					continue;
				}
				if (metBC5.Name.RoughlyEquals("Precipitation"))
				{
					ConvertSpatialBC(metBC5, boundaryCondition.Precipitation);
					string checkNativeUnitsMessage3 = CheckNativeUnitsMessage(boundaryCondition, "Precipitation", metBC5, "in/hr", "mm/hr");
					if (!string.IsNullOrEmpty(checkNativeUnitsMessage3))
					{
						boundaryCondition.Precipitation.Description += checkNativeUnitsMessage3;
						reporter.ReportMessage(checkNativeUnitsMessage3);
					}
				}
				else if (metBC5.Name.RoughlyEquals("Evapotranspiration"))
				{
					ConvertSpatialBC(metBC5, boundaryCondition.Evapotranspiration);
				}
				else if (metBC5.Name.RoughlyEquals("Air Density"))
				{
					ConvertSpatialBC(metBC5, boundaryCondition.AirDensity);
				}
				else if (metBC5.Name.RoughlyEquals("Air Temperature"))
				{
					ConvertSpatialBC(metBC5, boundaryCondition.AirTemperature);
				}
				else if (metBC5.Name.RoughlyEquals("Humidity"))
				{
					ConvertSpatialBC(metBC5, boundaryCondition.Humidity);
				}
				else if (metBC5.Name.RoughlyEquals("Air Pressure"))
				{
					ConvertSpatialBC(metBC5, boundaryCondition.AirPressure);
				}
				else
				{
					reporter.ReportMessage(metBC5.Name.InDoubleQuotes() + " met type not supported.");
				}
			}
			boundaryCondition.Precipitation.IsEnabled &= boundaryCondition2.PrecipitationMode.RoughlyEquals("Enable");
		}
		foreach (Ras.Migration.Native.Plan pl in prj6.Plans)
		{
			Ras.Layers.Plan plan = project.AddNewPlan(pl.Name);
			plan.OriginSoftware = OriginSoftware;
			plan.OriginVersion = OriginVersion;
			PlanAssociation planAssociation = project.GetPlanAssociation(plan);
			planAssociation.Geometry = project.Geometries.FirstOrDefault((Ras.Layers.Geometry g) => g.Name == pl.Geometry?.Name);
			planAssociation.BoundaryCondition = project.BoundaryConditions.FirstOrDefault((Ras.Layers.BoundaryCondition bc) => bc.Name == pl.BoundaryConditions?.Name);
		}
		IEnumerable<string> enumerable = externalDeps.Select((Tuple<string, Action<string>> s) => s.Item1).Distinct(StringComparer.OrdinalIgnoreCase);
		if (migration.CopyExternalConnectionsLocal && enumerable.Count() > 0)
		{
			string path3 = Path.Combine(project.ProjectDirectory, "External");
			Directory.CreateDirectory(path3);
			foreach (string path4 in enumerable)
			{
				string path5 = Path.Combine(path3, Path.GetFileName(path4));
				while (File.Exists(path5))
				{
					path5 = Path.ChangeExtension(path5, Guid.NewGuid().ToString().Take(5)?.ToString() + "." + Path.GetExtension(path5));
				}
				File.Copy(path4, path5);
				string uniqueFileConnectionName = project.ConnectionSource.UniqueFileConnectionName(Path.GetFileNameWithoutExtension(path5));
				project.ConnectionSource.AddFileConnection(uniqueFileConnectionName, path5);
				foreach (Tuple<string, Action<string>> externalDep in externalDeps)
				{
					if (externalDep.Item1.RoughlyEquals(path4))
					{
						externalDep.Item2(uniqueFileConnectionName);
					}
				}
			}
		}
		else
		{
			foreach (string enumerable2 in enumerable)
			{
				string uniqueFileConnectionName2 = project.ConnectionSource.UniqueFileConnectionName(Path.GetFileNameWithoutExtension(enumerable2));
				project.ConnectionSource.AddFileConnection(uniqueFileConnectionName2, enumerable2);
				foreach (Tuple<string, Action<string>> externalDep2 in externalDeps)
				{
					if (externalDep2.Item1.RoughlyEquals(enumerable2))
					{
						externalDep2.Item2(uniqueFileConnectionName2);
					}
				}
			}
		}
		project.Save(force: true);
		reporter.ReportMessage("Saved " + project.Filename.InDoubleQuotes());
		MigrateAllResults(directoryName, project.ResultsDirectory, reporter);
		MigrateAllHydraulicDesign(directoryName, project.HydraulicDesignDirectory, reporter);
		return project;
		void ConvertSpatialBC(MetBC metBc, SpatialScalarBoundaryConditionBase bc7)
		{
			bc7.IsEnabled = metBc.TryParseFromMode(out var dt2);
			bc7.SpatialDataType = dt2;
			bc7.Ratio = metBc.Ratio;
			bc7.ConstantValue = metBc.ConstValue;
			metBc.TryParseInterpolationFromMode(out var type);
			bc7.InterpolationMethod = type;
			if (metBc.PointGauges != null)
			{
				foreach (MetPointGauge pg in metBc.PointGauges)
				{
					MeteorologicStation meteorologicStation2 = bc7.BoundaryCondition.MetStations.FirstOrDefault((MeteorologicStation ms) => ms.Name.RoughlyEquals(pg.Name));
					if (meteorologicStation2 == null)
					{
						reporter.ReportMessage("Could not locate Met Station " + pg.Name.InDoubleQuotes());
					}
					else
					{
						MetGaugedData mg = new MetGaugedData();
						mg.Station = meteorologicStation2;
						mg.IsEnabled = pg.Used;
						RegisterConnection(pg.DssFile, (string conn) =>
						{
							mg.ConnectionName = conn;
						});
						mg.ConnectionPath = pg.DssPath;
						mg.Table = pg.TableYData;
						mg.Table2 = pg.TableXData;
						if (Time.TryParseTimestep(pg.TableInterval, out var ts))
						{
							mg.TableTimeSeries.RTSTimeSpan = ts;
						}
						if (string.IsNullOrEmpty(pg.Source))
						{
							mg.IsEnabled = false;
						}
						else if (pg.Source.RoughlyEquals("Table"))
						{
							mg.DataLocation = DataLocation.Local;
							mg.LocalDataType = LocalDataType.Table;
						}
						else if (pg.Source.RoughlyEquals("Constant"))
						{
							mg.DataLocation = DataLocation.Local;
							mg.LocalDataType = LocalDataType.Constant;
						}
						else
						{
							if (!pg.Source.RoughlyEquals("DSS"))
							{
								throw new NotImplementedException();
							}
							mg.DataLocation = DataLocation.Remote;
						}
						if (pg.TableUseFixedStart)
						{
							string tableStartDate = pg.TableStartDate;
							if (!string.IsNullOrEmpty(tableStartDate))
							{
								if (DateTime.TryParse(tableStartDate, out var result2))
								{
									mg.TableTimeSeries.FixedStartTime = result2;
								}
								else
								{
									reporter.ReportMessage("Could not parse table start time: " + tableStartDate);
								}
							}
						}
						else
						{
							mg.TableTimeSeries.FixedStartTime = MetGaugedData.NullTimestamp;
						}
						bc7.Gauges.Add(mg);
					}
				}
			}
			if (bc7.SpatialDataType != SpatialDataType.Constant && bc7.SpatialDataType != SpatialDataType.Point && bc7.SpatialDataType == SpatialDataType.Gridded)
			{
				if (metBc.GriddedSource == "DSS")
				{
					RegisterConnection(metBc.GriddedDssFile, (string conn) =>
					{
						bc7.ExternalGriddedConnection = conn;
					});
					bc7.ExternalGriddedPath = metBc.GriddedDssPath;
				}
				else if (metBc.GriddedSource == "Single Raster File")
				{
					reporter.ReportMessage("Imported raster file not supported yet. " + metBc.Name.InDoubleQuotes());
				}
				else
				{
					reporter.ReportMessage(metBc.GriddedSource + " not supported. " + metBc.Name.InDoubleQuotes());
				}
			}
		}
		void RegisterConnection(string connectionFile, Action<string> callback)
		{
			if (!string.IsNullOrEmpty(connectionFile))
			{
				if (!Path.IsPathFullyQualified(connectionFile))
				{
					connectionFile = Path.Combine(prj6Dir, connectionFile);
				}
				externalDeps.Add(new Tuple<string, Action<string>>(connectionFile, callback));
			}
		}
	}

	private static string CheckNativeUnitsMessage(Ras.Layers.BoundaryCondition bc7, string lyrName, MetBC metBc, string expectedUscUnits, string expectedSiUnits)
	{
		if (bc7.Project.Units == UnitSystem.USCustomary)
		{
			if (metBc.ConstUnits.RoughlyEquals(expectedUscUnits))
			{
				return "";
			}
			return $"Potential issue converting {lyrName} layer on {bc7.Name}. Constant units disagree with native unit system. Examine your data closely.";
		}
		if (metBc.ConstUnits.RoughlyEquals(expectedSiUnits))
		{
			return "";
		}
		return $"Potential issue converting {lyrName} layer on {bc7.Name}. Constant units disagree with native unit system. Examine your data closely.";
	}

	public static bool MigrateGeometry(string geom6Filename, string outputFilename, MigrationParams migrate = null)
	{
		if (migrate == null)
		{
			migrate = MigrationParams.Default();
		}
		if (File.Exists(outputFilename))
		{
			File.Delete(outputFilename);
		}
		RASGeometry geom = new RASGeometry(geom6Filename);
		if (migrate.AsClassic)
		{
			using ClassicGeometry classicGeometry = ClassicGeometry.CreateInMemory();
			MigrateClassicGeometryData(geom, classicGeometry, migrate);
			classicGeometry.SaveAs(outputFilename);
			return true;
		}
		using Ras.Layers.Geometry geometry = Ras.Layers.Geometry.CreateInMemory();
		MigrateNewGeometryData(geom, geometry, migrate);
		geometry.SaveAs(outputFilename);
		return true;
	}

	private static void MigrateNewGeometryData(RASGeometry geom6, Ras.Layers.Geometry geom, MigrationParams migrate = null)
	{
		if (migrate == null)
		{
			migrate = MigrationParams.Default();
		}
		RASD2FlowArea d2FlowArea = geom6.D2FlowArea;
		if (d2FlowArea.FeatureCount() <= 0)
		{
			return;
		}
		using H5Reader hr = new H5Reader(geom6.SourceFilename);
		for (int i = 0; i < d2FlowArea.FeatureCount(); i++)
		{
			geom.FlowAreaLayer.Add(RasFeatureConverter.Convert(d2FlowArea, i, hr));
		}
	}

	private static void MigrateClassicGeometryData(RASGeometry geom6, ClassicGeometry geom, MigrationParams migrate = null)
	{
		if (migrate == null)
		{
			migrate = MigrationParams.Default();
		}
		Generate1DLines(geom6, migrate.Generate1DLines);
		RASRiver river = geom6.River;
		for (int i = 0; i < river.FeatureCount(); i++)
		{
			geom.RiverLayer.Add(RasFeatureConverter.Convert(river, i));
		}
		RASXS xS = geom6.XS;
		for (int j = 0; j < xS.FeatureCount(); j++)
		{
			geom.CrossSectionLayer.Add(RasFeatureConverter.Convert(xS, j));
		}
		RASBankLines bankLines = geom6.BankLines;
		for (int k = 0; k < bankLines.FeatureCount(); k++)
		{
			geom.BanklineLayer.Add(RasFeatureConverter.Convert(bankLines, k));
		}
		FlowPaths flowPathLines = geom6.FlowPathLines;
		for (int l = 0; l < flowPathLines.FeatureCount(); l++)
		{
			geom.FlowPathLayer.Add(RasFeatureConverter.Convert(flowPathLines, l));
		}
		RASEdgeLines edgeLines = geom6.EdgeLines;
		for (int m = 0; m < edgeLines.FeatureCount(); m++)
		{
			geom.RiverEdgeLineLayer.Add(RasFeatureConverter.Convert(edgeLines, m));
		}
		RASD2FlowArea d2FlowArea = geom6.D2FlowArea;
		if (d2FlowArea.FeatureCount() > 0)
		{
			using H5Reader hr = new H5Reader(geom6.SourceFilename);
			for (int n = 0; n < d2FlowArea.FeatureCount(); n++)
			{
				geom.FlowAreaLayer.Add(RasFeatureConverter.Convert(d2FlowArea, n, hr));
			}
		}
		RASStorageArea storageArea = geom6.StorageArea;
		for (int ordinal = 0; ordinal < storageArea.FeatureCount(); ordinal++)
		{
			geom.StorageAreaLayer.Add(RasFeatureConverter.Convert(storageArea, ordinal));
		}
		RASXSInterpolationSurface xSInterpolationSurface = geom6.XSInterpolationSurface;
		for (int ordinal2 = 0; ordinal2 < xSInterpolationSurface.FeatureCount(); ordinal2++)
		{
			geom.InterpolationSurfaceLayer.Add(RasFeatureConverter.Convert(xSInterpolationSurface, ordinal2));
		}
	}

	private static void Generate1DLines(RASGeometry geom6, Generate1DLinesMode mode)
	{
		if (mode != Generate1DLinesMode.No && geom6.River.FeatureCount() != 0 && geom6.XS.FeatureCount() != 0)
		{
			if (mode == Generate1DLinesMode.Yes)
			{
				geom6.BankLines.ComputeBankLines();
				geom6.EdgeLines.ComputeEdgeLines();
			}
			else
			{
				geom6.BankLines.EnsureBankLines();
				geom6.EdgeLines.EnsureEdgeLines();
			}
		}
	}

	public static Ras.Layers.Result MigrateResult(string res6Filename, string outputFilename, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (File.Exists(outputFilename))
		{
			File.Delete(outputFilename);
		}
		Ras.Layers.Geometry geometry = Ras.Layers.Geometry.CreateInMemory();
		RASGeometry rASGeometry = new RASGeometry(res6Filename);
		MigrateNewGeometryData(rASGeometry, geometry);
		if (geometry.FlowAreaLayer.Count > 0)
		{
			string name = geometry.FlowAreaLayer[0].Name;
			if (name != "Mesh")
			{
				geometry.FlowAreaLayer[0].Name = "Mesh";
				prog.ReportMessage("Renaming mesh from " + name.InDoubleQuotes() + " to Mesh for maximum compatibility.");
			}
			if (geometry.FlowAreaLayer.Count > 1)
			{
				prog.ReportMessage("Multiple flow-areas detected. This can cause compatibility issues with the 2025 geometry.");
			}
		}
		RASResults rASResults = new RASResults(res6Filename);
		Ras.Layers.Plan plan = Ras.Layers.Plan.CreateInMemory(rASResults.Name);
		plan.InternalStartTime = rASResults.PlanAttributes.StartTime;
		plan.TimeWindowMode = PlanTimeWindowMode.Plan;
		using (H5Writer hw = new H5Writer(outputFilename))
		{
			geometry.SaveAs(hw);
			plan.SaveAs(hw);
		}
		using (H5Reader h5Reader = new H5Reader(res6Filename))
		{
			using H5Writer h5Writer = new H5Writer(outputFilename);
			string absolutePath = "/Results/Output Blocks/Base Output/";
			h5Writer.CreateGroup(absolutePath);
			for (int i = 0; i < rASGeometry.D2FlowArea.MeshCount; i++)
			{
				FlowArea flowArea = geometry.FlowAreaLayer[i];
				string featureName = rASGeometry.D2FlowArea.GetFeatureName(i);
				string name2 = flowArea.Name;
				string sourceObject = LegacyOutputBlock.Get2DOutputDatasetName(OutputVariables.Legacy_WaterSurface, "Base Output", featureName, isSummary: false);
				string rootMeshOutputPathOutputBlockTerm = OutputBlock.RootMeshOutputPath("Base Output", name2) + OutputVariables.Legacy_WaterSurface.Name;
				h5Reader.CopyTo(h5Writer, sourceObject, rootMeshOutputPathOutputBlockTerm);
				h5Writer.Trim<float>(rootMeshOutputPathOutputBlockTerm, -1, flowArea.CellCount);
				string sourceObject2 = LegacyOutputBlock.Get2DOutputDatasetName(OutputVariables.Velocity, "Base Output", featureName, isSummary: false);
				string destinationObject = OutputBlock.RootMeshOutputPath("Base Output", name2) + OutputVariables.Velocity.Name;
				h5Reader.CopyTo(h5Writer, sourceObject2, destinationObject);
				string sourceObject3 = "/Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Time";
				string destinationObject2 = OutputBlock.RootBlockOutputPath("Base Output") + "Time";
				h5Reader.CopyTo(h5Writer, sourceObject3, destinationObject2);
			}
		}
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".h5");
		using (Ras.Layers.Result result = new Ras.Layers.Result(outputFilename))
		{
			if (!result.ConvertToNewFormat(path, prog))
			{
				return null;
			}
		}
		File.Delete(outputFilename);
		File.Move(path, outputFilename);
		stopwatch.Stop();
		prog.ReportMessage("Migrated " + res6Filename.InDoubleQuotes() + " to " + outputFilename.InDoubleQuotes() + " (" + stopwatch.Elapsed.ToString() + ")");
		return new Ras.Layers.Result(outputFilename);
	}

	public static void MigrateAllResults(string v6ProjectDirectory, string targetDirectory, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		foreach (string textValue in Directory.GetFiles(v6ProjectDirectory, "*.p*.hdf", SearchOption.TopDirectoryOnly).ToList())
		{
			try
			{
				if (!textValue.Contains(".prev"))
				{
					string name = new RASResults(textValue).Name;
					string outputFilename = Path.Combine(targetDirectory, name + ".h5");
					MigrateResult(textValue, outputFilename, prog);
				}
			}
			catch (Exception ex)
			{
				prog.ReportError("Error migrating " + textValue.InDoubleQuotes() + Environment.NewLine + ex.Message);
			}
		}
	}

	public static void MigrateAllHydraulicDesign(string v6ProjectDirectory, string targetDirectory, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		foreach (string textValue in (from a in Directory.GetFiles(v6ProjectDirectory, "*.h*", SearchOption.TopDirectoryOnly)
			where Regex.IsMatch(a.Split('.').Last(), "h[0-9][0-9]")
			select a).ToList())
		{
			try
			{
				if (!textValue.Contains(".prev"))
				{
					SimpleFileResults results = new SimpleFileResults(new HFileReader(textValue), null);
					string peekHDTitle = HydraulicDesignConverter.PeekHDTitle(results, textValue);
					StableChannelDesign stableChannelDesign = HydraulicDesignConverter.MigrateStableChannelDesign(results);
					string path = Path.Combine(targetDirectory, peekHDTitle + ".scd.h5");
					int ordinal = 2;
					while (File.Exists(path))
					{
						path = Path.Combine(targetDirectory, peekHDTitle + ordinal + ".scd.h5");
					}
					stableChannelDesign.SaveAs(path);
				}
			}
			catch (Exception ex)
			{
				prog.ReportError("Error migrating " + textValue.InDoubleQuotes() + Environment.NewLine + ex.Message);
			}
		}
	}
}
