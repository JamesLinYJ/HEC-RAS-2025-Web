using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Ras.Layers;
using Utility.Extensions;
using Utility.Files;
using Utility.IO;
using Utility.Progress;
using Utility.Units;

namespace Ras.Migration.Native;

public class Project
{
	private static class Keys
	{
		public const string GeomFile = "Geom File";

		public const string ProjectTitle = "Proj Title";

		public const string UnsteadyFile = "Unsteady File";

		public const string EventCondFile = "EventConditions File";

		public const string FlowFile = "Flow File";

		public const string QuasiSteadyFile = "QuasiSteady File";

		public const string CurrentPlan = "Current Plan";

		public const string PlanFile = "Plan File";
	}

	public string ProjectFilename { get; private set; }

	public string RasmapFilename { get; private set; }

	public string ProjectDirectory => Path.GetDirectoryName(ProjectFilename ?? "");

	public string Name { get; private set; }

	public FileState ProjectFileStatus { get; set; }

	public FileState RasmapFileStatus { get; set; }

	public List<Geometry> Geometries { get; private set; } = new List<Geometry>();

	public List<Plan> Plans { get; private set; } = new List<Plan>();

	public List<Result> Results { get; private set; } = new List<Result>();

	public List<Terrain> Terrains { get; private set; } = new List<Terrain>();

	public List<BoundaryCondition> BoundaryConditions { get; private set; } = new List<BoundaryCondition>();

	public List<MapLayer> MapLayers { get; private set; } = new List<MapLayer>();

	public Plan CurrentPlan { get; set; }

	public UnitSystem Units { get; set; }

	public string Description { get; set; }

	public Project(string prjFile)
	{
		if (!System.IO.File.Exists(prjFile))
		{
			throw new FileNotFoundException("prjFile");
		}
		ProjectFilename = prjFile;
		ProgressReporter pr = ProgressReporter.ConsoleWrite();
		Path.GetDirectoryName(prjFile);
		Path.GetFileNameWithoutExtension(prjFile);
		string changeExtension = Path.ChangeExtension(prjFile, ".rasmap");
		string[] allLines = System.IO.File.ReadAllLines(prjFile);
		if (!IsValid(allLines))
		{
			ProjectFileStatus = FileState.Malformed;
			return;
		}
		Utility.IO.File.ParseKVPDelimitedFile(allLines, '=', out var prjData);
		if (prjData.ContainsKey("Proj Title"))
		{
			Name = prjData["Proj Title"].First().Trim();
		}
		else
		{
			Name = Path.GetFileNameWithoutExtension(ProjectFilename);
		}
		if (prjData.ContainsKey("Geom File"))
		{
			foreach (string prjData2 in prjData["Geom File"])
			{
				string gxxFilename = Path.ChangeExtension(prjFile, prjData2);
				Geometries.Add(new Geometry(this, gxxFilename));
			}
		}
		if (prjData.ContainsKey("Unsteady File"))
		{
			foreach (string prjData3 in prjData["Unsteady File"])
			{
				if (BoundaryCondition.TryParse(Path.ChangeExtension(prjFile, prjData3), out var bc, pr))
				{
					bc.Project = this;
					BoundaryConditions.Add(bc);
				}
			}
		}
		if (prjData.ContainsKey("EventConditions File"))
		{
			foreach (string prjData4 in prjData["EventConditions File"])
			{
				if (BoundaryCondition.TryParse(Path.ChangeExtension(prjFile, prjData4), out var bc2, pr))
				{
					bc2.Project = this;
					BoundaryConditions.Add(bc2);
				}
			}
		}
		if (prjData.ContainsKey("Flow File"))
		{
			foreach (string prjData5 in prjData["Flow File"])
			{
				if (BoundaryCondition.TryParse(Path.ChangeExtension(prjFile, prjData5), out var bc3, pr))
				{
					bc3.Project = this;
					BoundaryConditions.Add(bc3);
				}
			}
		}
		if (prjData.ContainsKey("QuasiSteady File"))
		{
			foreach (string prjData6 in prjData["QuasiSteady File"])
			{
				if (BoundaryCondition.TryParse(Path.ChangeExtension(prjFile, prjData6), out var bc4, pr))
				{
					bc4.Project = this;
					BoundaryConditions.Add(bc4);
				}
			}
		}
		if (prjData.ContainsKey("Plan File"))
		{
			foreach (string prjData7 in prjData["Plan File"])
			{
				string changeExtension2 = Path.ChangeExtension(prjFile, prjData7);
				if (System.IO.File.Exists(changeExtension2))
				{
					Plan plan = new Plan(this, changeExtension2);
					Plans.Add(plan);
				}
			}
		}
		if (prjData.ContainsKey("Current Plan"))
		{
			CurrentPlan = Plans.FirstOrDefault((Plan p) => p.Extension.RoughlyEquals(prjData["Current Plan"].FirstOrDefault()));
		}
		if (allLines[3] == "English Units")
		{
			Units = UnitSystem.USCustomary;
		}
		else if (allLines[3] == "SI Units")
		{
			Units = UnitSystem.SI;
		}
		else
		{
			Units = UnitSystem.USCustomary;
		}
		for (int intervalIndex = 0; intervalIndex < allLines.Length; intervalIndex++)
		{
			if (allLines[intervalIndex].RoughlyEquals("BEGIN DESCRIPTION:"))
			{
				int i = intervalIndex + 1;
				List<string> values = AccumulateUntilLineEquals(allLines, ref i, "END DESCRIPTION:");
				Description = string.Join(Environment.NewLine, values);
				intervalIndex = i + 1;
			}
		}
		ProjectFileStatus = FileState.Good;
		if (System.IO.File.Exists(changeExtension))
		{
			List<Terrain> loadTerrains = RasMapper.LoadTerrains(XElement.Load(changeExtension), changeExtension);
			if (loadTerrains != null)
			{
				Terrains.AddRange(loadTerrains);
			}
		}
		else
		{
			RasmapFileStatus = FileState.DoesNotExist;
		}
	}

	private static List<string> AccumulateUntilLineEquals(string[] lines, ref int i, string matches)
	{
		List<string> stringList = new List<string>();
		for (int j = i; j < lines.Length; j++)
		{
			string line = lines[j];
			if (line.RoughlyEquals(matches))
			{
				i = j;
				return stringList;
			}
			stringList.Add(line);
		}
		i = lines.Length;
		return stringList;
	}

	private static bool CanExportTo(string destDirectory, ProgressReporter reporter)
	{
		if (Directory.Exists(destDirectory))
		{
			string[] files = Directory.GetFiles(destDirectory);
			foreach (string path in files)
			{
				if (Path.GetExtension(path).RoughlyEquals(".ras"))
				{
					reporter.ReportError("Could not migrate to target directory " + destDirectory.InDoubleQuotes() + ", HEC-RAS project " + Path.GetFileName(path) + " already exists there.");
					return false;
				}
			}
		}
		return true;
	}

	public string Migrate(string destDirectory, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		try
		{
			if (!CanExportTo(destDirectory, reporter))
			{
				return "";
			}
			reporter.ReportMessage("Migrating " + Name);
			if (!Directory.Exists(destDirectory))
			{
				reporter.ReportMessage("Creating destination directory " + destDirectory.InDoubleQuotes() + " ...");
				Directory.CreateDirectory(destDirectory);
			}
			reporter.ReportMessage("Migrating Plans...");
			foreach (Plan plan in Plans)
			{
				plan.Export7thGeneration(destDirectory, reporter);
			}
			reporter.ReportProgress(25);
			reporter.ReportMessage("Migrating Geometries...");
			foreach (Geometry geometry in Geometries)
			{
				geometry.Export7thGeneration(destDirectory, reporter);
			}
			reporter.ReportProgress(50);
			reporter.ReportMessage("Migrating Boundary/Event Conditions...");
			foreach (BoundaryCondition boundaryCondition in BoundaryConditions)
			{
				try
				{
					boundaryCondition.Export7thGeneration(destDirectory, reporter);
				}
				catch (Exception ex)
				{
					reporter.ReportError("Error migrating Event Condition file " + boundaryCondition.Filename + Environment.NewLine + ex.Message);
				}
			}
			reporter.ReportProgress(60);
			reporter.ReportMessage("Migrating Terrains...");
			foreach (Terrain terrain in Terrains)
			{
				terrain.Export7thGeneration(destDirectory, reporter);
			}
			reporter.ReportProgress(98);
			string export7thGeneration = Export7thGeneration(destDirectory, reporter);
			reporter.ReportProgress(100);
			reporter.ReportMessage("Success! Migrated " + Path.GetFileName(ProjectFilename) + " " + Name.InParentheses() + " to " + export7thGeneration.InDoubleQuotes());
			return export7thGeneration;
		}
		catch (Exception ex2)
		{
			reporter.ReportError(ex2.Message);
			return "";
		}
	}

	private string Export7thGeneration(string destDirectory, ProgressReporter reporter)
	{
		try
		{
			string sanitizeFilename = FileHelpers.SanitizeFilename(Name);
			Ras.Layers.Project project = Ras.Layers.Project.CreateNew(Path.Combine(destDirectory, sanitizeFilename + ".ras"), Units);
			project.Description = Description;
			project.Save();
			return project.Filename;
		}
		catch (Exception ex)
		{
			reporter.ReportError("Error occurred migrating project to " + destDirectory.InDoubleQuotes() + ": " + ex.Message);
			return "";
		}
	}

	private static void ParseLineSplitter(string[] lines)
	{
	}

	private static string SplitTakeRHS(string line, string splitter = "=")
	{
		string[] parts = line.Split(new string[1] { splitter }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 1)
		{
			return "";
		}
		string part = parts[1];
		for (int i = 2; i < parts.Length; i++)
		{
			part = part + splitter + parts[i];
		}
		return part;
	}

	public static bool IsValid(string file)
	{
		if (!System.IO.File.Exists(file))
		{
			return false;
		}
		if (Path.GetExtension(file)?.ToLower() != ".prj")
		{
			return false;
		}
		return IsValid(System.IO.File.ReadAllLines(file));
	}

	private static bool IsValid(string[] lines)
	{
		if (lines == null || lines.Length == 0)
		{
			return false;
		}
		if (lines[0].Substring(0, 10) == "Proj Title")
		{
			return true;
		}
		return false;
	}

	public static bool DirectoryContainsProject(string dir, out string prjFile)
	{
		string[] files = Directory.GetFiles(dir, "*.prj");
		foreach (string prjFile2 in files)
		{
			if (IsValid(prjFile2))
			{
				prjFile = prjFile2;
				return true;
			}
		}
		prjFile = "";
		return false;
	}

	public override string ToString()
	{
		return Name;
	}
}
