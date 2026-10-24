using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using CommandLine;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.Terrains;
using Ras.Process.Helpers;
using Utility.Logging;

namespace Ras.Process.TerrainCommands;

[Verb("info", false, null, HelpText = "Prints information about an existing RAS terrain.")]
public class TerrainInfoArgs
{
	private sealed class TerrainInfoModel
	{
		public string Filename { get; set; }

		public string FileType { get; set; }

		public string FileVersion { get; set; }

		public string Projection { get; set; }

		public ExtentModel Extent { get; set; }

		public List<TerrainLayerModel> Layers { get; set; } = new List<TerrainLayerModel>();

		public TerrainSignatureModel Signature { get; set; }
	}

	private sealed class TerrainLayerModel
	{
		public int Index { get; set; }

		public string Name { get; set; }

		public string Filename { get; set; }

		public ExtentModel Extent { get; set; }

		public double? CellSize { get; set; }
	}

	private sealed class ExtentModel
	{
		public double MinX { get; set; }

		public double MaxX { get; set; }

		public double MinY { get; set; }

		public double MaxY { get; set; }
	}

	public const string Verb = "info";

	[Value(0, MetaName = "terrain", Required = true, HelpText = "Terrain file (*.h5) to inspect.")]
	public string TerrainFilename { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, Markdown, or Properties (grep-friendly key=value).")]
	public OutputFormat OutputFormat { get; set; }

	[Option("get", Required = false, HelpText = "Print only the value(s) at a flattened key path (e.g. 'projection' or 'layers[0].cellSize'). Ignores --format.")]
	public string GetPath { get; set; }

	[Option("verify-signature", Required = false, HelpText = "Recompute the terrain signature (input file sizes and hashes) and verify it against the stored signature. Prints SUCCESS or FAILURE instead of the info dump.")]
	public bool VerifySignature { get; set; }

	public bool Execute()
	{
		GDALSetup.InitializeMultiplatform();
		if (!TryResolveTerrain(out var terrain))
		{
			return false;
		}
		if (VerifySignature)
		{
			return VerifySignatures(terrain);
		}
		TerrainInfoModel terrainInfoModel = BuildModel(terrain);
		if (!string.IsNullOrEmpty(GetPath))
		{
			return OutputFormats.WriteGet(terrainInfoModel, GetPath);
		}
		Console.WriteLine(OutputFormat switch
		{
			OutputFormat.Json => JsonSerializer.Serialize(terrainInfoModel, OutputFormats.Json), 
			OutputFormat.Markdown => RenderMarkdown(terrainInfoModel), 
			OutputFormat.Properties => OutputFormats.Properties(terrainInfoModel), 
			_ => RenderText(terrainInfoModel), 
		});
		return true;
	}

	private bool TryResolveTerrain(out Terrain terrain)
	{
		return CliHelpers.TryResolveCoreFile(TerrainFilename, CoreFileType.Terrain, (string fn) => new Terrain(fn), out terrain);
	}

	private static bool VerifySignatures(Terrain terrain)
	{
		TerrainSignature signature = terrain.GetSignature();
		TerrainSignature recomputedSignature = terrain.GetRecomputedSignature();
		OperationResult operationResult = signature.Compare(recomputedSignature);
		if ((bool)operationResult)
		{
			Console.WriteLine("SUCCESS");
			return true;
		}
		Console.WriteLine("FAILURE");
		foreach (Message message in operationResult.Messages)
		{
			Console.WriteLine("  " + message.EvaluatedMessage);
		}
		return false;
	}

	private static TerrainInfoModel BuildModel(Terrain terrain)
	{
		Projection projection = terrain.TryGetProjection();
		Extent validExtent = terrain.ValidExtent;
		TerrainInfoModel terrainInfoModel = new TerrainInfoModel
		{
			Filename = terrain.Filename,
			FileType = "RAS 2025 Terrain",
			FileVersion = terrain.GetCurrentFileFormatVersion(),
			Projection = projection?.GetName()
		};
		if (!validExtent.IsNull())
		{
			terrainInfoModel.Extent = new ExtentModel
			{
				MinX = validExtent.MinX,
				MaxX = validExtent.MaxX,
				MinY = validExtent.MinY,
				MaxY = validExtent.MaxY
			};
		}
		terrainInfoModel.Signature = TerrainSignatureModel.From(terrain.GetSignature());
		List<RasterTerrainInput> allRasterInputs = terrain.AllRasterInputs();
		for (int i = 0; i < allRasterInputs.Count; i++)
		{
			RasterTerrainInput rasterTerrainInput = allRasterInputs[i];
			TerrainLayerModel terrainLayerModel = new TerrainLayerModel
			{
				Index = i,
				Name = rasterTerrainInput.Name,
				Filename = rasterTerrainInput.Filename
			};
			IRasterPyramid<float> resampler = rasterTerrainInput.GetResampler();
			if (resampler != null)
			{
				Extent validExtent2 = resampler.ValidExtent;
				if (!validExtent2.IsNull())
				{
					terrainLayerModel.Extent = new ExtentModel
					{
						MinX = validExtent2.MinX,
						MaxX = validExtent2.MaxX,
						MinY = validExtent2.MinY,
						MaxY = validExtent2.MaxY
					};
				}
				terrainLayerModel.CellSize = resampler.RasterDefinition.CellSize;
			}
			terrainInfoModel.Layers.Add(terrainLayerModel);
		}
		return terrainInfoModel;
	}

	private static string RenderText(TerrainInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Filename:   " + m.Filename);
		stringBuilder.AppendLine("File Type:  " + m.FileType);
		stringBuilder.AppendLine("File Version: " + m.FileVersion);
		stringBuilder.AppendLine("Projection: " + (m.Projection ?? "(unknown)"));
		StringBuilder stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler;
		if (m.Extent == null)
		{
			stringBuilder.AppendLine("Extent:     (none)");
		}
		else
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(27, 4, stringBuilder2);
			handler.AppendLiteral("Extent:     X[");
			handler.AppendFormatted(m.Extent.MinX);
			handler.AppendLiteral(" .. ");
			handler.AppendFormatted(m.Extent.MaxX);
			handler.AppendLiteral("] Y[");
			handler.AppendFormatted(m.Extent.MinY);
			handler.AppendLiteral(" .. ");
			handler.AppendFormatted(m.Extent.MaxY);
			handler.AppendLiteral("]");
			stringBuilder3.AppendLine(ref handler);
		}
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("Layers:     ");
		handler.AppendFormatted(m.Layers.Count);
		stringBuilder4.AppendLine(ref handler);
		foreach (TerrainLayerModel layer in m.Layers)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(5, 2, stringBuilder2);
			handler.AppendLiteral("  [");
			handler.AppendFormatted(layer.Index);
			handler.AppendLiteral("] ");
			handler.AppendFormatted(layer.Name);
			stringBuilder5.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
			handler.AppendLiteral("      File: ");
			handler.AppendFormatted(layer.Filename);
			stringBuilder6.AppendLine(ref handler);
			if (layer.Extent != null)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder7 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(29, 4, stringBuilder2);
				handler.AppendLiteral("      Extent: X[");
				handler.AppendFormatted(layer.Extent.MinX);
				handler.AppendLiteral(" .. ");
				handler.AppendFormatted(layer.Extent.MaxX);
				handler.AppendLiteral("] Y[");
				handler.AppendFormatted(layer.Extent.MinY);
				handler.AppendLiteral(" .. ");
				handler.AppendFormatted(layer.Extent.MaxY);
				handler.AppendLiteral("]");
				stringBuilder7.AppendLine(ref handler);
			}
			if (layer.CellSize.HasValue)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder8 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
				handler.AppendLiteral("      CellSize: ");
				handler.AppendFormatted(layer.CellSize.Value);
				stringBuilder8.AppendLine(ref handler);
			}
		}
		TerrainSignatureModel signature = m.Signature;
		if (signature != null)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder9 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(22, 2, stringBuilder2);
			handler.AppendLiteral("Signature:  ");
			handler.AppendFormatted(signature.Name);
			handler.AppendLiteral(" (");
			handler.AppendFormatted(signature.Inputs.Count);
			handler.AppendLiteral(" inputs)");
			stringBuilder9.AppendLine(ref handler);
			foreach (TerrainInputSignatureModel input in signature.Inputs)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder10 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(8, 3, stringBuilder2);
				handler.AppendLiteral("  [");
				handler.AppendFormatted(input.Index);
				handler.AppendLiteral("] ");
				handler.AppendFormatted(input.Name);
				handler.AppendLiteral(" (");
				handler.AppendFormatted(input.TypeName);
				handler.AppendLiteral(")");
				stringBuilder10.AppendLine(ref handler);
				if (input.FileSize.HasValue)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder11 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
					handler.AppendLiteral("      File: ");
					handler.AppendFormatted(input.File);
					stringBuilder11.AppendLine(ref handler);
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder12 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder2);
					handler.AppendLiteral("      Size: ");
					handler.AppendFormatted(input.FileSize.Value);
					handler.AppendLiteral(" bytes");
					stringBuilder12.AppendLine(ref handler);
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder13 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder2);
					handler.AppendLiteral("      Modified (UTC): ");
					handler.AppendFormatted(input.LastModifiedUtc.Value, "O");
					stringBuilder13.AppendLine(ref handler);
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder14 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
					handler.AppendLiteral("      Hash: ");
					handler.AppendFormatted(input.Hash);
					stringBuilder14.AppendLine(ref handler);
				}
				else
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder15 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
					handler.AppendLiteral("      Data Hash: ");
					handler.AppendFormatted(input.DataHash);
					stringBuilder15.AppendLine(ref handler);
				}
			}
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string RenderMarkdown(TerrainInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("# Terrain");
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
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("| Projection | ");
		handler.AppendFormatted(m.Projection ?? "(unknown)");
		handler.AppendLiteral(" |");
		stringBuilder6.AppendLine(ref handler);
		if (m.Extent != null)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(28, 4, stringBuilder2);
			handler.AppendLiteral("| Extent | X[");
			handler.AppendFormatted(m.Extent.MinX);
			handler.AppendLiteral(" .. ");
			handler.AppendFormatted(m.Extent.MaxX);
			handler.AppendLiteral("] Y[");
			handler.AppendFormatted(m.Extent.MinY);
			handler.AppendLiteral(" .. ");
			handler.AppendFormatted(m.Extent.MaxY);
			handler.AppendLiteral("] |");
			stringBuilder7.AppendLine(ref handler);
		}
		else
		{
			stringBuilder.AppendLine("| Extent | (none) |");
		}
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("## Layers (");
		handler.AppendFormatted(m.Layers.Count);
		handler.AppendLiteral(")");
		stringBuilder8.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (m.Layers.Count == 0)
		{
			stringBuilder.AppendLine("_(none)_");
		}
		else
		{
			foreach (TerrainLayerModel layer in m.Layers)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder9 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(9, 2, stringBuilder2);
				handler.AppendLiteral("- **[");
				handler.AppendFormatted(layer.Index);
				handler.AppendLiteral("] ");
				handler.AppendFormatted(layer.Name);
				handler.AppendLiteral("**");
				stringBuilder9.AppendLine(ref handler);
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder10 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
				handler.AppendLiteral("  - File: `");
				handler.AppendFormatted(layer.Filename);
				handler.AppendLiteral("`");
				stringBuilder10.AppendLine(ref handler);
				if (layer.Extent != null)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder11 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(27, 4, stringBuilder2);
					handler.AppendLiteral("  - Extent: X[");
					handler.AppendFormatted(layer.Extent.MinX);
					handler.AppendLiteral(" .. ");
					handler.AppendFormatted(layer.Extent.MaxX);
					handler.AppendLiteral("] Y[");
					handler.AppendFormatted(layer.Extent.MinY);
					handler.AppendLiteral(" .. ");
					handler.AppendFormatted(layer.Extent.MaxY);
					handler.AppendLiteral("]");
					stringBuilder11.AppendLine(ref handler);
				}
				if (layer.CellSize.HasValue)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder12 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
					handler.AppendLiteral("  - CellSize: ");
					handler.AppendFormatted(layer.CellSize.Value);
					stringBuilder12.AppendLine(ref handler);
				}
			}
		}
		TerrainSignatureModel signature = m.Signature;
		if (signature != null)
		{
			stringBuilder.AppendLine();
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder13 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(24, 2, stringBuilder2);
			handler.AppendLiteral("## Signature: ");
			handler.AppendFormatted(signature.Name);
			handler.AppendLiteral(" (");
			handler.AppendFormatted(signature.Inputs.Count);
			handler.AppendLiteral(" inputs)");
			stringBuilder13.AppendLine(ref handler);
			stringBuilder.AppendLine();
			if (signature.Inputs.Count == 0)
			{
				stringBuilder.AppendLine("_(none)_");
			}
			else
			{
				foreach (TerrainInputSignatureModel input in signature.Inputs)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder14 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(12, 3, stringBuilder2);
					handler.AppendLiteral("- **[");
					handler.AppendFormatted(input.Index);
					handler.AppendLiteral("] ");
					handler.AppendFormatted(input.Name);
					handler.AppendLiteral("** (");
					handler.AppendFormatted(input.TypeName);
					handler.AppendLiteral(")");
					stringBuilder14.AppendLine(ref handler);
					if (input.FileSize.HasValue)
					{
						stringBuilder2 = stringBuilder;
						StringBuilder stringBuilder15 = stringBuilder2;
						handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
						handler.AppendLiteral("  - File: `");
						handler.AppendFormatted(input.File);
						handler.AppendLiteral("`");
						stringBuilder15.AppendLine(ref handler);
						stringBuilder2 = stringBuilder;
						StringBuilder stringBuilder16 = stringBuilder2;
						handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
						handler.AppendLiteral("  - Size: ");
						handler.AppendFormatted(input.FileSize.Value);
						handler.AppendLiteral(" bytes");
						stringBuilder16.AppendLine(ref handler);
						stringBuilder2 = stringBuilder;
						StringBuilder stringBuilder17 = stringBuilder2;
						handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
						handler.AppendLiteral("  - Modified (UTC): ");
						handler.AppendFormatted(input.LastModifiedUtc.Value, "O");
						stringBuilder17.AppendLine(ref handler);
						stringBuilder2 = stringBuilder;
						StringBuilder stringBuilder18 = stringBuilder2;
						handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
						handler.AppendLiteral("  - Hash: `");
						handler.AppendFormatted(input.Hash);
						handler.AppendLiteral("`");
						stringBuilder18.AppendLine(ref handler);
					}
					else
					{
						stringBuilder2 = stringBuilder;
						StringBuilder stringBuilder19 = stringBuilder2;
						handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
						handler.AppendLiteral("  - Data Hash: ");
						handler.AppendFormatted(input.DataHash);
						stringBuilder19.AppendLine(ref handler);
					}
				}
			}
		}
		return stringBuilder.ToString().TrimEnd();
	}
}
