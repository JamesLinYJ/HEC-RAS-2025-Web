using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CommandLine;
using Geospatial.GDALAssist;
using Geospatial.Vectors.Topology;
using Ras.Layers;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Logging;

namespace Ras.Process.GeometryCommands;

[Verb("info", false, null, HelpText = "Prints information about an existing RAS geometry file (*.h5).")]
public class GeometryInfoArgs
{
	internal sealed class GeometryInfoModel
	{
		public string Filename { get; set; }

		public string FileType { get; set; }

		public string FileVersion { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public string Projection { get; set; }

		public List<LayerEntry> Layers { get; set; } = new List<LayerEntry>();

		public TopologyModel Topology { get; set; }

		public List<PropertyTableEntry> PropertyTables { get; set; } = new List<PropertyTableEntry>();
	}

	internal sealed class TopologyModel
	{
		public int Regions { get; set; }

		public int Arcs { get; set; }

		public int Nodes { get; set; }

		public int RegionControlPoints { get; set; }
	}

	internal sealed class LayerEntry
	{
		public string Label { get; set; }

		public int Count { get; set; }
	}

	internal sealed class PropertyTableEntry
	{
		public string FlowArea { get; set; }

		public bool Processed { get; set; }

		public PropertyTableSignatureModel Signature { get; set; }
	}

	internal sealed class PropertyTableSignatureModel
	{
		public string SoftwareVersion { get; set; }

		public string SignatureVersion { get; set; }

		public int MeshCellCount { get; set; }

		public int MeshFaceCount { get; set; }

		public int MeshHash { get; set; }

		public DateTime DateLastModifiedUtc { get; set; }

		public double ComputeDurationSeconds { get; set; }

		public TerrainSignatureModel Terrain { get; set; }
	}

	public const string Verb = "info";

	[Value(0, MetaName = "geometry", Required = true, HelpText = "Geometry file (*.h5).")]
	public string GeometryFilename { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, Markdown, or Properties (grep-friendly key=value).")]
	public OutputFormat OutputFormat { get; set; }

	[Option("get", Required = false, HelpText = "Print only the value(s) at a flattened key path (e.g. 'name' or 'layers[0].count'). Ignores --format.")]
	public string GetPath { get; set; }

	[Option("verify-table-signatures", Required = false, HelpText = "Locate the owning project and verify each mesh's stored property-table terrain signature against the associated terrain. Prints SUCCESS or FAILURE instead of the info dump. Also works on a geometry embedded in a result file.")]
	public bool VerifyTableSignatures { get; set; }

	public bool Execute()
	{
		GDALSetup.InitializeMultiplatform();
		if (VerifyTableSignatures)
		{
			return RunVerifyTableSignatures();
		}
		if (!TryResolveGeometry(out var geometry))
		{
			return false;
		}
		GeometryInfoModel geometryInfoModel = BuildModel(geometry);
		if (!string.IsNullOrEmpty(GetPath))
		{
			return OutputFormats.WriteGet(geometryInfoModel, GetPath);
		}
		Console.WriteLine(OutputFormat switch
		{
			OutputFormat.Json => JsonSerializer.Serialize(geometryInfoModel, OutputFormats.Json), 
			OutputFormat.Markdown => RenderMarkdown(geometryInfoModel), 
			OutputFormat.Properties => OutputFormats.Properties(geometryInfoModel), 
			_ => RenderText(geometryInfoModel), 
		});
		return true;
	}

	private bool TryResolveGeometry(out Geometry geometry)
	{
		return CliHelpers.TryResolveCoreFile(GeometryFilename, CoreFileType.Geometry, (string fn) => new Geometry(fn), out geometry);
	}

	private bool RunVerifyTableSignatures()
	{
		if (!TryResolveGeometryWithProject(out var geometry, out var note))
		{
			return false;
		}
		Terrain associatedTerrain = geometry.AssociatedTerrain;
		if (associatedTerrain == null)
		{
			Console.WriteLine("FAILURE");
			Console.WriteLine("  Could not resolve an associated terrain" + note + ".");
			return false;
		}
		TerrainSignature signature = associatedTerrain.GetSignature();
		List<MeshTableSignatureInfo> meshTableSignatureInfos = (from i in geometry.LoadTableSignatures()
			where i.HasTables && i.Signature?.TerrainSignature != null
			select i).ToList();
		if (meshTableSignatureInfos.Count == 0)
		{
			Console.WriteLine("FAILURE");
			Console.WriteLine("  No property-table terrain signatures found to verify.");
			return false;
		}
		StringBuilder stringBuilder = new StringBuilder();
		bool condition = true;
		foreach (MeshTableSignatureInfo meshTableSignatureInfo in meshTableSignatureInfos)
		{
			OperationResult operationResult = meshTableSignatureInfo.Signature.TerrainSignature.Compare(signature);
			StringBuilder stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler;
			if ((bool)operationResult)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder3 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(22, 2, stringBuilder2);
				handler.AppendLiteral("  ");
				handler.AppendFormatted(meshTableSignatureInfo.FlowAreaName);
				handler.AppendLiteral(": MATCH (terrain \"");
				handler.AppendFormatted(associatedTerrain.Name);
				handler.AppendLiteral("\")");
				stringBuilder3.AppendLine(ref handler);
				continue;
			}
			condition = false;
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(25, 2, stringBuilder2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(meshTableSignatureInfo.FlowAreaName);
			handler.AppendLiteral(": MISMATCH (terrain \"");
			handler.AppendFormatted(associatedTerrain.Name);
			handler.AppendLiteral("\")");
			stringBuilder4.AppendLine(ref handler);
			foreach (Message message in operationResult.Messages)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder5 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder2);
				handler.AppendLiteral("      ");
				handler.AppendFormatted(message.EvaluatedMessage);
				stringBuilder5.AppendLine(ref handler);
			}
		}
		Console.WriteLine(condition ? "SUCCESS" : "FAILURE");
		Console.Write(stringBuilder.ToString());
		return condition;
	}

	private bool TryResolveGeometryWithProject(out Geometry geometry, out string note)
	{
		geometry = null;
		note = "";
		string fn = Path.GetFullPath(GeometryFilename);
		if (!File.Exists(fn))
		{
			Console.WriteLine(fn.InDoubleQuotes() + " does not exist.");
			return false;
		}
		if (Result.IsValid(fn))
		{
			Result result = new Result(fn);
			Project project = TryOpenOwningProject(fn);
			if (project != null)
			{
				result.Project = project;
			}
			else
			{
				note = " (no project found next to the result)";
			}
			geometry = result.Geometry;
			return true;
		}
		Project project2 = TryOpenOwningProject(fn);
		if (project2 == null)
		{
			note = " (no project found in the expected location)";
			geometry = new Geometry(fn);
			return true;
		}
		geometry = project2.Geometries.FirstOrDefault((Geometry g) => Path.GetFullPath(g.Filename).RoughlyEquals(fn));
		if (geometry == null)
		{
			note = " (geometry is not one of the project's geometries)";
			geometry = new Geometry(fn);
		}
		return true;
	}

	private static Project TryOpenOwningProject(string fileFullPath)
	{
		string getParentDirectoryTerm = Directory.GetParent(Path.GetDirectoryName(fileFullPath))?.FullName;
		if (string.IsNullOrEmpty(getParentDirectoryTerm) || !Directory.Exists(getParentDirectoryTerm))
		{
			return null;
		}
		if (Directory.GetFiles(getParentDirectoryTerm, "*.ras").Length == 0)
		{
			return null;
		}
		OperationResult operationResult = Project.TryLoad(getParentDirectoryTerm, out var prj);
		if (!operationResult)
		{
			Console.WriteLine("Could not load project from " + getParentDirectoryTerm.InDoubleQuotes() + ": " + operationResult.GetConcatenatedMessages());
			return null;
		}
		return prj;
	}

	internal static GeometryInfoModel BuildModel(Geometry geometry)
	{
		GeometryInfoModel geometryInfoModel = new GeometryInfoModel
		{
			Filename = geometry.Filename,
			FileType = "RAS 2025 Geometry",
			FileVersion = geometry.GetCurrentFileFormatVersion(),
			Name = geometry.Name,
			Description = geometry.Description,
			Projection = geometry.Project?.Projection?.GetName()
		};
		AddIfPresent(geometryInfoModel.Layers, "Flow Areas (2D)", geometry.FlowAreaLayer);
		AddIfPresent(geometryInfoModel.Layers, "Opening Properties", geometry.OpeningPropertiesLayer);
		AddIfPresent(geometryInfoModel.Layers, "Barrel Properties", geometry.BarrelPropertiesLayer);
		AddIfPresent(geometryInfoModel.Layers, "Culvert Barrels", geometry.CulvertBarrelLayer);
		geometryInfoModel.Topology = BuildTopologyModel(geometry);
		AddPropertyTables(geometryInfoModel.PropertyTables, geometry);
		return geometryInfoModel;
	}

	private static TopologyModel BuildTopologyModel(Geometry geometry)
	{
		Topology topology = geometry.ConceptualMeshLayer?.Topology;
		if (topology == null)
		{
			return null;
		}
		return new TopologyModel
		{
			Regions = topology.CompletedRegions.Count,
			Arcs = topology.Arcs.Count,
			Nodes = topology.Nodes.Count,
			RegionControlPoints = topology.RegionControlPoints.Count
		};
	}

	private static void AddPropertyTables(List<PropertyTableEntry> dest, Geometry geometry)
	{
		foreach (MeshTableSignatureInfo loadTableSignature in geometry.LoadTableSignatures())
		{
			PropertyTableEntry propertyTableEntry = new PropertyTableEntry
			{
				FlowArea = loadTableSignature.FlowAreaName,
				Processed = loadTableSignature.HasTables
			};
			MeshPropertyTableSignature signature = loadTableSignature.Signature;
			if (propertyTableEntry.Processed && signature != null)
			{
				propertyTableEntry.Signature = new PropertyTableSignatureModel
				{
					SoftwareVersion = signature.SoftwareVersion,
					SignatureVersion = signature.SignatureVersion,
					MeshCellCount = signature.MeshCellCount,
					MeshFaceCount = signature.MeshFaceCount,
					MeshHash = signature.MeshHash,
					DateLastModifiedUtc = signature.DateLastModified,
					ComputeDurationSeconds = signature.ComputeDurationSeconds,
					Terrain = ((signature.TerrainSignature == null) ? null : TerrainSignatureModel.From(signature.TerrainSignature))
				};
			}
			dest.Add(propertyTableEntry);
		}
	}

	private static void AddIfPresent(List<LayerEntry> dest, string label, object layer)
	{
		if (layer != null)
		{
			int count;
			try
			{
				count = ((dynamic)layer).PeekCount();
			}
			catch
			{
				count = -1;
			}
			dest.Add(new LayerEntry
			{
				Label = label,
				Count = count
			});
		}
	}

	internal static string RenderText(GeometryInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Filename:   " + m.Filename);
		stringBuilder.AppendLine("File Type:  " + m.FileType);
		stringBuilder.AppendLine("File Version: " + m.FileVersion);
		stringBuilder.AppendLine("Name:       " + (m.Name ?? "(unknown)"));
		stringBuilder.AppendLine("Description: " + (string.IsNullOrEmpty(m.Description) ? "(none)" : m.Description));
		stringBuilder.AppendLine("Projection: " + (m.Projection ?? "(unknown)"));
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("Layers:     ");
		handler.AppendFormatted(m.Layers.Count);
		stringBuilder3.AppendLine(ref handler);
		foreach (LayerEntry layer in m.Layers)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(4, 2, stringBuilder2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted<string>(layer.Label, -22);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(FormatCount(layer.Count));
			stringBuilder4.AppendLine(ref handler);
		}
		if (m.Topology != null)
		{
			stringBuilder.AppendLine("Topology:");
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(4, 2, stringBuilder2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted<string>("Regions", -22);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(m.Topology.Regions);
			stringBuilder5.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(4, 2, stringBuilder2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted<string>("Arcs", -22);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(m.Topology.Arcs);
			stringBuilder6.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(4, 2, stringBuilder2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted<string>("Nodes", -22);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(m.Topology.Nodes);
			stringBuilder7.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(4, 2, stringBuilder2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted<string>("Region Control Points", -22);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(m.Topology.RegionControlPoints);
			stringBuilder8.AppendLine(ref handler);
		}
		if (m.PropertyTables.Count > 0)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder9 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(30, 1, stringBuilder2);
			handler.AppendLiteral("Property Tables: ");
			handler.AppendFormatted(m.PropertyTables.Count);
			handler.AppendLiteral(" flow area(s)");
			stringBuilder9.AppendLine(ref handler);
			foreach (PropertyTableEntry propertyTable in m.PropertyTables)
			{
				if (!propertyTable.Processed)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder10 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
					handler.AppendLiteral("  ");
					handler.AppendFormatted(propertyTable.FlowArea);
					handler.AppendLiteral(": not processed");
					stringBuilder10.AppendLine(ref handler);
					continue;
				}
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder11 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
				handler.AppendLiteral("  ");
				handler.AppendFormatted(propertyTable.FlowArea);
				handler.AppendLiteral(": processed");
				stringBuilder11.AppendLine(ref handler);
				PropertyTableSignatureModel signature = propertyTable.Signature;
				if (signature == null)
				{
					stringBuilder.AppendLine("      (no signature - tables predate signature support)");
					continue;
				}
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder12 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder2);
				handler.AppendLiteral("      Software Version: ");
				handler.AppendFormatted(OrUnknown(signature.SoftwareVersion));
				stringBuilder12.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder13 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(25, 1, stringBuilder2);
				handler.AppendLiteral("      Signature Version: ");
				handler.AppendFormatted(OrUnknown(signature.SignatureVersion));
				stringBuilder13.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder14 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(33, 3, stringBuilder2);
				handler.AppendLiteral("      Mesh: ");
				handler.AppendFormatted(signature.MeshCellCount);
				handler.AppendLiteral(" cells, ");
				handler.AppendFormatted(signature.MeshFaceCount);
				handler.AppendLiteral(" faces, hash ");
				handler.AppendFormatted(signature.MeshHash);
				stringBuilder14.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder15 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(22, 2, stringBuilder2);
				handler.AppendLiteral("      Computed (UTC): ");
				handler.AppendFormatted(FormatDate(signature.DateLastModifiedUtc));
				handler.AppendFormatted(FormatDuration(signature.ComputeDurationSeconds));
				stringBuilder15.AppendLine(ref handler);
				AppendTerrainSignatureText(stringBuilder, signature.Terrain, "      ");
			}
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static void AppendTerrainSignatureText(StringBuilder sb, TerrainSignatureModel sig, string indent)
	{
		StringBuilder stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler;
		if (sig == null)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder2 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(25, 1, stringBuilder);
			handler.AppendFormatted(indent);
			handler.AppendLiteral("Terrain Signature: (none)");
			stringBuilder2.AppendLine(ref handler);
			return;
		}
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(29, 3, stringBuilder);
		handler.AppendFormatted(indent);
		handler.AppendLiteral("Terrain Signature: ");
		handler.AppendFormatted(sig.Name);
		handler.AppendLiteral(" (");
		handler.AppendFormatted(sig.Inputs.Count);
		handler.AppendLiteral(" inputs)");
		stringBuilder3.AppendLine(ref handler);
		foreach (TerrainInputSignatureModel input in sig.Inputs)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder4 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(8, 4, stringBuilder);
			handler.AppendFormatted(indent);
			handler.AppendLiteral("  [");
			handler.AppendFormatted(input.Index);
			handler.AppendLiteral("] ");
			handler.AppendFormatted(input.Name);
			handler.AppendLiteral(" (");
			handler.AppendFormatted(input.TypeName);
			handler.AppendLiteral(")");
			stringBuilder4.AppendLine(ref handler);
			if (input.FileSize.HasValue)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder5 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(12, 2, stringBuilder);
				handler.AppendFormatted(indent);
				handler.AppendLiteral("      File: ");
				handler.AppendFormatted(input.File);
				stringBuilder5.AppendLine(ref handler);
				stringBuilder = sb;
				StringBuilder stringBuilder6 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(18, 2, stringBuilder);
				handler.AppendFormatted(indent);
				handler.AppendLiteral("      Size: ");
				handler.AppendFormatted(input.FileSize.Value);
				handler.AppendLiteral(" bytes");
				stringBuilder6.AppendLine(ref handler);
				stringBuilder = sb;
				StringBuilder stringBuilder7 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(22, 2, stringBuilder);
				handler.AppendFormatted(indent);
				handler.AppendLiteral("      Modified (UTC): ");
				handler.AppendFormatted(input.LastModifiedUtc.Value, "O");
				stringBuilder7.AppendLine(ref handler);
				stringBuilder = sb;
				StringBuilder stringBuilder8 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(12, 2, stringBuilder);
				handler.AppendFormatted(indent);
				handler.AppendLiteral("      Hash: ");
				handler.AppendFormatted(input.Hash);
				stringBuilder8.AppendLine(ref handler);
			}
			else
			{
				stringBuilder = sb;
				StringBuilder stringBuilder9 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(17, 2, stringBuilder);
				handler.AppendFormatted(indent);
				handler.AppendLiteral("      Data Hash: ");
				handler.AppendFormatted(input.DataHash);
				stringBuilder9.AppendLine(ref handler);
			}
		}
	}

	private static string OrUnknown(string s)
	{
		if (!string.IsNullOrEmpty(s))
		{
			return s;
		}
		return "(unknown)";
	}

	private static string FormatDate(DateTime dt)
	{
		if (!(dt == DateTime.MinValue))
		{
			return dt.ToString("O");
		}
		return "(unknown)";
	}

	private static string FormatDuration(double seconds)
	{
		if (!double.IsNaN(seconds))
		{
			return $" in {seconds:0.###}s";
		}
		return "";
	}

	internal static string RenderMarkdown(GeometryInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("# Geometry: " + (m.Name ?? "(unknown)"));
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("| Field | Value |");
		stringBuilder.AppendLine("|-------|-------|");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("| Filename | `");
		handler.AppendFormatted(m.Filename);
		handler.AppendLiteral("` |");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("| File Type | ");
		handler.AppendFormatted(m.FileType);
		handler.AppendLiteral(" |");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
		handler.AppendLiteral("| File Version | ");
		handler.AppendFormatted(m.FileVersion);
		handler.AppendLiteral(" |");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("| Name | ");
		handler.AppendFormatted(m.Name ?? "(unknown)");
		handler.AppendLiteral(" |");
		stringBuilder6.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder2);
		handler.AppendLiteral("| Description | ");
		handler.AppendFormatted(string.IsNullOrEmpty(m.Description) ? "(none)" : m.Description);
		handler.AppendLiteral(" |");
		stringBuilder7.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("| Projection | ");
		handler.AppendFormatted(m.Projection ?? "(unknown)");
		handler.AppendLiteral(" |");
		stringBuilder8.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("## Layers (");
		handler.AppendFormatted(m.Layers.Count);
		handler.AppendLiteral(")");
		stringBuilder9.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (m.Layers.Count == 0)
		{
			stringBuilder.AppendLine("_(none)_");
		}
		else
		{
			stringBuilder.AppendLine("| Layer | Count |");
			stringBuilder.AppendLine("|-------|-------|");
			foreach (LayerEntry layer in m.Layers)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder10 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(7, 2, stringBuilder2);
				handler.AppendLiteral("| ");
				handler.AppendFormatted(layer.Label);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted(FormatCount(layer.Count));
				handler.AppendLiteral(" |");
				stringBuilder10.AppendLine(ref handler);
			}
		}
		if (m.Topology != null)
		{
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("## Topology");
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("| Element | Count |");
			stringBuilder.AppendLine("|---------|-------|");
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder11 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
			handler.AppendLiteral("| Regions | ");
			handler.AppendFormatted(m.Topology.Regions);
			handler.AppendLiteral(" |");
			stringBuilder11.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder12 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
			handler.AppendLiteral("| Arcs | ");
			handler.AppendFormatted(m.Topology.Arcs);
			handler.AppendLiteral(" |");
			stringBuilder12.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder13 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
			handler.AppendLiteral("| Nodes | ");
			handler.AppendFormatted(m.Topology.Nodes);
			handler.AppendLiteral(" |");
			stringBuilder13.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder14 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(28, 1, stringBuilder2);
			handler.AppendLiteral("| Region Control Points | ");
			handler.AppendFormatted(m.Topology.RegionControlPoints);
			handler.AppendLiteral(" |");
			stringBuilder14.AppendLine(ref handler);
		}
		AppendPropertyTablesMarkdown(stringBuilder, m);
		return stringBuilder.ToString().TrimEnd();
	}

	private static void AppendPropertyTablesMarkdown(StringBuilder sb, GeometryInfoModel m)
	{
		if (m.PropertyTables.Count == 0)
		{
			return;
		}
		sb.AppendLine();
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(34, 1, stringBuilder);
		handler.AppendLiteral("## Property Tables (");
		handler.AppendFormatted(m.PropertyTables.Count);
		handler.AppendLiteral(" flow area(s))");
		stringBuilder2.AppendLine(ref handler);
		foreach (PropertyTableEntry propertyTable in m.PropertyTables)
		{
			sb.AppendLine();
			if (!propertyTable.Processed)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder3 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder);
				handler.AppendLiteral("- **");
				handler.AppendFormatted(propertyTable.FlowArea);
				handler.AppendLiteral("** — not processed");
				stringBuilder3.AppendLine(ref handler);
				continue;
			}
			stringBuilder = sb;
			StringBuilder stringBuilder4 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder);
			handler.AppendLiteral("- **");
			handler.AppendFormatted(propertyTable.FlowArea);
			handler.AppendLiteral("** — processed");
			stringBuilder4.AppendLine(ref handler);
			PropertyTableSignatureModel signature = propertyTable.Signature;
			if (signature == null)
			{
				sb.AppendLine("  - _(no signature - tables predate signature support)_");
				continue;
			}
			stringBuilder = sb;
			StringBuilder stringBuilder5 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder);
			handler.AppendLiteral("  - Software Version: ");
			handler.AppendFormatted(OrUnknown(signature.SoftwareVersion));
			stringBuilder5.AppendLine(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder6 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(23, 1, stringBuilder);
			handler.AppendLiteral("  - Signature Version: ");
			handler.AppendFormatted(OrUnknown(signature.SignatureVersion));
			stringBuilder6.AppendLine(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder7 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(33, 3, stringBuilder);
			handler.AppendLiteral("  - Mesh: ");
			handler.AppendFormatted(signature.MeshCellCount);
			handler.AppendLiteral(" cells, ");
			handler.AppendFormatted(signature.MeshFaceCount);
			handler.AppendLiteral(" faces, hash `");
			handler.AppendFormatted(signature.MeshHash);
			handler.AppendLiteral("`");
			stringBuilder7.AppendLine(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder8 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(20, 2, stringBuilder);
			handler.AppendLiteral("  - Computed (UTC): ");
			handler.AppendFormatted(FormatDate(signature.DateLastModifiedUtc));
			handler.AppendFormatted(FormatDuration(signature.ComputeDurationSeconds));
			stringBuilder8.AppendLine(ref handler);
			if (signature.Terrain == null)
			{
				sb.AppendLine("  - Terrain Signature: (none)");
				continue;
			}
			stringBuilder = sb;
			StringBuilder stringBuilder9 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(33, 2, stringBuilder);
			handler.AppendLiteral("  - Terrain Signature: ");
			handler.AppendFormatted(signature.Terrain.Name);
			handler.AppendLiteral(" (");
			handler.AppendFormatted(signature.Terrain.Inputs.Count);
			handler.AppendLiteral(" inputs)");
			stringBuilder9.AppendLine(ref handler);
			foreach (TerrainInputSignatureModel input in signature.Terrain.Inputs)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder10 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(16, 3, stringBuilder);
				handler.AppendLiteral("    - **[");
				handler.AppendFormatted(input.Index);
				handler.AppendLiteral("] ");
				handler.AppendFormatted(input.Name);
				handler.AppendLiteral("** (");
				handler.AppendFormatted(input.TypeName);
				handler.AppendLiteral(")");
				stringBuilder10.AppendLine(ref handler);
				if (input.FileSize.HasValue)
				{
					stringBuilder = sb;
					StringBuilder stringBuilder11 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder);
					handler.AppendLiteral("      - File: `");
					handler.AppendFormatted(input.File);
					handler.AppendLiteral("`");
					stringBuilder11.AppendLine(ref handler);
					stringBuilder = sb;
					StringBuilder stringBuilder12 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder);
					handler.AppendLiteral("      - Size: ");
					handler.AppendFormatted(input.FileSize.Value);
					handler.AppendLiteral(" bytes");
					stringBuilder12.AppendLine(ref handler);
					stringBuilder = sb;
					StringBuilder stringBuilder13 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder);
					handler.AppendLiteral("      - Modified (UTC): ");
					handler.AppendFormatted(input.LastModifiedUtc.Value, "O");
					stringBuilder13.AppendLine(ref handler);
					stringBuilder = sb;
					StringBuilder stringBuilder14 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder);
					handler.AppendLiteral("      - Hash: `");
					handler.AppendFormatted(input.Hash);
					handler.AppendLiteral("`");
					stringBuilder14.AppendLine(ref handler);
				}
				else
				{
					stringBuilder = sb;
					StringBuilder stringBuilder15 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder);
					handler.AppendLiteral("      - Data Hash: ");
					handler.AppendFormatted(input.DataHash);
					stringBuilder15.AppendLine(ref handler);
				}
			}
		}
	}

	private static string FormatCount(int count)
	{
		if (count >= 0)
		{
			return count.ToString();
		}
		return "?";
	}
}
