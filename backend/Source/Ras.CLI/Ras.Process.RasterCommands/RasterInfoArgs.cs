using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using CommandLine;
using Geospatial.GDALAssist;
using Ras.Process.Helpers;
using Ras.Process.RasterCommands.Explorer;

namespace Ras.Process.RasterCommands;

[Verb("info", false, null, HelpText = "Prints information about a raster file (size, bands, projection, georeferencing, sub-datasets, pyramid levels, metadata). Use --driver to choose the reading stack.")]
public class RasterInfoArgs
{
	private sealed class RasterInfoModel
	{
		public string Filename { get; set; }

		public string FileType { get; set; }

		public string Driver { get; set; }

		public int Width { get; set; }

		public int Height { get; set; }

		public int Bands { get; set; }

		public string DataType { get; set; }

		public ProjectionModel Projection { get; set; }

		public GeoreferenceModel Georeference { get; set; }

		public List<SubdatasetModel> Subdatasets { get; set; } = new List<SubdatasetModel>();

		public List<PyramidLevelModel> PyramidLevels { get; set; } = new List<PyramidLevelModel>();

		public List<MetadataItemModel> Metadata { get; set; } = new List<MetadataItemModel>();
	}

	private sealed class ProjectionModel
	{
		public string Name { get; set; }

		public int? Epsg { get; set; }

		public string Units { get; set; }
	}

	private sealed class GeoreferenceModel
	{
		public double OriginX { get; set; }

		public double OriginY { get; set; }

		public double CellSize { get; set; }

		public double MinX { get; set; }

		public double MinY { get; set; }

		public double MaxX { get; set; }

		public double MaxY { get; set; }
	}

	private sealed class SubdatasetModel
	{
		public int Index { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }
	}

	private sealed class PyramidLevelModel
	{
		public int Index { get; set; }

		public int Cols { get; set; }

		public int Rows { get; set; }

		public double CellSize { get; set; }
	}

	private sealed class MetadataItemModel
	{
		public string Key { get; set; }

		public string Value { get; set; }
	}

	public const string Verb = "info";

	[Value(0, Required = true, HelpText = "Raster file to inspect (e.g. a GeoTIFF).")]
	public string FilePath { get; set; }

	[Option('d', "driver", Default = RasterDriver.Default, HelpText = "Reading stack: 'native' (our TiffBinaryReader, GeoTIFF only), 'gdal' (any GDAL format), or 'default' (native for a plain TIFF we parse, otherwise gdal).")]
	public RasterDriver Driver { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default), Json, Markdown, or Properties (grep-friendly key=value).")]
	public OutputFormat OutputFormat { get; set; }

	[Option("get", Required = false, HelpText = "Print only the value(s) at a flattened key path (e.g. 'width' or 'pyramidLevels[0].cellSize'). Ignores --format.")]
	public string GetPath { get; set; }

	public bool Execute()
	{
		if (!File.Exists(FilePath))
		{
			Console.WriteLine("File not found: " + FilePath);
			return false;
		}
		try
		{
			GDALSetup.InitializeMultiplatform();
			if (!RasterSourceFactory.TryCreate(FilePath, Driver, out var source, out var error))
			{
				Console.WriteLine("Could not open raster: " + error);
				return false;
			}
			using (source)
			{
				RasterInfoModel rasterInfoModel = BuildModel(source);
				if (!string.IsNullOrEmpty(GetPath))
				{
					return OutputFormats.WriteGet(rasterInfoModel, GetPath);
				}
				Console.WriteLine(OutputFormat switch
				{
					OutputFormat.Json => JsonSerializer.Serialize(rasterInfoModel, OutputFormats.Json), 
					OutputFormat.Markdown => RenderMarkdown(rasterInfoModel), 
					OutputFormat.Properties => OutputFormats.Properties(rasterInfoModel), 
					_ => RenderText(rasterInfoModel), 
				});
			}
			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: " + ex.Message);
			return false;
		}
	}

	private RasterInfoModel BuildModel(IRasterMetadataSource source)
	{
		RasterSummary summary = source.GetSummary();
		GeoreferenceInfo georeference = source.GetGeoreference();
		ProjectionInfo projection = source.GetProjection();
		RasterInfoModel rasterInfoModel = new RasterInfoModel
		{
			Filename = FilePath,
			FileType = "Raster",
			Driver = source.DriverLabel,
			Width = summary.Width,
			Height = summary.Height,
			Bands = summary.Bands,
			DataType = summary.DataType
		};
		if (projection.HasProjection)
		{
			rasterInfoModel.Projection = new ProjectionModel
			{
				Name = (string.IsNullOrEmpty(projection.Name) ? null : projection.Name),
				Epsg = ((projection.Epsg > 0) ? new int?(projection.Epsg) : ((int?)null)),
				Units = (string.IsNullOrEmpty(projection.Units) ? null : projection.Units)
			};
		}
		if (georeference.HasGeoreference && !double.IsNaN(georeference.CellSize))
		{
			rasterInfoModel.Georeference = new GeoreferenceModel
			{
				OriginX = georeference.OriginX,
				OriginY = georeference.OriginY,
				CellSize = georeference.CellSize,
				MinX = georeference.OriginX,
				MaxY = georeference.OriginY,
				MaxX = georeference.OriginX + (double)summary.Width * georeference.CellSize,
				MinY = georeference.OriginY - (double)summary.Height * georeference.CellSize
			};
		}
		foreach (SubdatasetEntry subdataset in source.GetSubdatasets())
		{
			rasterInfoModel.Subdatasets.Add(new SubdatasetModel
			{
				Index = subdataset.Index,
				Name = subdataset.Name,
				Description = subdataset.Description
			});
		}
		foreach (PyramidLevelInfo pyramidLevel in source.GetPyramidLevels())
		{
			rasterInfoModel.PyramidLevels.Add(new PyramidLevelModel
			{
				Index = pyramidLevel.Index,
				Cols = pyramidLevel.Cols,
				Rows = pyramidLevel.Rows,
				CellSize = pyramidLevel.CellSize
			});
		}
		foreach (MetadataEntry metadata in source.GetMetadata())
		{
			rasterInfoModel.Metadata.Add(new MetadataItemModel
			{
				Key = metadata.Key,
				Value = metadata.Value
			});
		}
		return rasterInfoModel;
	}

	private static string RenderText(RasterInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Filename:   " + m.Filename);
		stringBuilder.AppendLine("File Type:  " + m.FileType);
		stringBuilder.AppendLine("Driver:     " + m.Driver);
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(26, 5, stringBuilder2);
		handler.AppendLiteral("Size:       ");
		handler.AppendFormatted(m.Width);
		handler.AppendLiteral(" x ");
		handler.AppendFormatted(m.Height);
		handler.AppendLiteral("  (");
		handler.AppendFormatted(m.Bands);
		handler.AppendLiteral(" band");
		handler.AppendFormatted((m.Bands == 1) ? "" : "s");
		handler.AppendLiteral(", ");
		handler.AppendFormatted(m.DataType);
		handler.AppendLiteral(")");
		stringBuilder3.AppendLine(ref handler);
		if (m.Projection == null)
		{
			stringBuilder.AppendLine("Projection: (none)");
		}
		else
		{
			string message = (m.Projection.Epsg.HasValue ? $"EPSG:{m.Projection.Epsg.Value}" : "(no EPSG)");
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 2, stringBuilder2);
			handler.AppendLiteral("Projection: ");
			handler.AppendFormatted(m.Projection.Name ?? "(unknown)");
			handler.AppendLiteral("  [");
			handler.AppendFormatted(message);
			handler.AppendLiteral("]");
			stringBuilder4.AppendLine(ref handler);
			if (m.Projection.Units != null)
			{
				stringBuilder.AppendLine("Units:      " + m.Projection.Units);
			}
		}
		if (m.Georeference == null)
		{
			stringBuilder.AppendLine("Georef:     (none)");
		}
		else
		{
			GeoreferenceModel georeference = m.Georeference;
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 2, stringBuilder2);
			handler.AppendLiteral("Origin:     (");
			handler.AppendFormatted(georeference.OriginX);
			handler.AppendLiteral(", ");
			handler.AppendFormatted(georeference.OriginY);
			handler.AppendLiteral(")");
			stringBuilder5.AppendLine(ref handler);
			stringBuilder.AppendLine("Cell Size:  " + georeference.CellSize);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(27, 4, stringBuilder2);
			handler.AppendLiteral("Extent:     X[");
			handler.AppendFormatted(georeference.MinX);
			handler.AppendLiteral(" .. ");
			handler.AppendFormatted(georeference.MaxX);
			handler.AppendLiteral("] Y[");
			handler.AppendFormatted(georeference.MinY);
			handler.AppendLiteral(" .. ");
			handler.AppendFormatted(georeference.MaxY);
			handler.AppendLiteral("]");
			stringBuilder6.AppendLine(ref handler);
		}
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("Subdatasets: ");
		handler.AppendFormatted(m.Subdatasets.Count);
		stringBuilder7.AppendLine(ref handler);
		foreach (SubdatasetModel subdataset in m.Subdatasets)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(5, 2, stringBuilder2);
			handler.AppendLiteral("  [");
			handler.AppendFormatted(subdataset.Index);
			handler.AppendLiteral("] ");
			handler.AppendFormatted(subdataset.Name);
			stringBuilder8.AppendLine(ref handler);
		}
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("Pyramid Levels: ");
		handler.AppendFormatted(m.PyramidLevels.Count);
		stringBuilder9.AppendLine(ref handler);
		foreach (PyramidLevelModel pyramidLevel in m.PyramidLevels)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder10 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(17, 4, stringBuilder2);
			handler.AppendLiteral("  [");
			handler.AppendFormatted(pyramidLevel.Index);
			handler.AppendLiteral("] ");
			handler.AppendFormatted(pyramidLevel.Cols);
			handler.AppendLiteral(" x ");
			handler.AppendFormatted(pyramidLevel.Rows);
			handler.AppendLiteral("  (cell ");
			handler.AppendFormatted(pyramidLevel.CellSize);
			handler.AppendLiteral(")");
			stringBuilder10.AppendLine(ref handler);
		}
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder11 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
		handler.AppendLiteral("Metadata: ");
		handler.AppendFormatted(m.Metadata.Count);
		stringBuilder11.AppendLine(ref handler);
		foreach (MetadataItemModel metadata in m.Metadata)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder12 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(5, 2, stringBuilder2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(metadata.Key);
			handler.AppendLiteral(" = ");
			handler.AppendFormatted(metadata.Value);
			stringBuilder12.AppendLine(ref handler);
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string RenderMarkdown(RasterInfoModel m)
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("# Raster: `");
		handler.AppendFormatted(Path.GetFileName(m.Filename));
		handler.AppendLiteral("`");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("| Field | Value |");
		stringBuilder.AppendLine("|-------|-------|");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("| Filename | `");
		handler.AppendFormatted(m.Filename);
		handler.AppendLiteral("` |");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("| File Type | ");
		handler.AppendFormatted(m.FileType);
		handler.AppendLiteral(" |");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("| Driver | ");
		handler.AppendFormatted(m.Driver);
		handler.AppendLiteral(" |");
		stringBuilder6.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 2, stringBuilder2);
		handler.AppendLiteral("| Size | ");
		handler.AppendFormatted(m.Width);
		handler.AppendLiteral(" x ");
		handler.AppendFormatted(m.Height);
		handler.AppendLiteral(" |");
		stringBuilder7.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("| Bands | ");
		handler.AppendFormatted(m.Bands);
		handler.AppendLiteral(" |");
		stringBuilder8.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("| Data Type | ");
		handler.AppendFormatted(m.DataType);
		handler.AppendLiteral(" |");
		stringBuilder9.AppendLine(ref handler);
		if (m.Projection != null)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder10 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
			handler.AppendLiteral("| Projection | ");
			handler.AppendFormatted(m.Projection.Name ?? "(unknown)");
			handler.AppendLiteral(" |");
			stringBuilder10.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder11 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
			handler.AppendLiteral("| EPSG | ");
			handler.AppendFormatted(m.Projection.Epsg.HasValue ? m.Projection.Epsg.Value.ToString() : "(none)");
			handler.AppendLiteral(" |");
			stringBuilder11.AppendLine(ref handler);
			if (m.Projection.Units != null)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder12 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
				handler.AppendLiteral("| Units | ");
				handler.AppendFormatted(m.Projection.Units);
				handler.AppendLiteral(" |");
				stringBuilder12.AppendLine(ref handler);
			}
		}
		if (m.Georeference != null)
		{
			GeoreferenceModel georeference = m.Georeference;
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder13 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(17, 2, stringBuilder2);
			handler.AppendLiteral("| Origin | (");
			handler.AppendFormatted(georeference.OriginX);
			handler.AppendLiteral(", ");
			handler.AppendFormatted(georeference.OriginY);
			handler.AppendLiteral(") |");
			stringBuilder13.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder14 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("| Cell Size | ");
			handler.AppendFormatted(georeference.CellSize);
			handler.AppendLiteral(" |");
			stringBuilder14.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder15 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(28, 4, stringBuilder2);
			handler.AppendLiteral("| Extent | X[");
			handler.AppendFormatted(georeference.MinX);
			handler.AppendLiteral(" .. ");
			handler.AppendFormatted(georeference.MaxX);
			handler.AppendLiteral("] Y[");
			handler.AppendFormatted(georeference.MinY);
			handler.AppendLiteral(" .. ");
			handler.AppendFormatted(georeference.MaxY);
			handler.AppendLiteral("] |");
			stringBuilder15.AppendLine(ref handler);
		}
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder16 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("## Subdatasets (");
		handler.AppendFormatted(m.Subdatasets.Count);
		handler.AppendLiteral(")");
		stringBuilder16.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (m.Subdatasets.Count == 0)
		{
			stringBuilder.AppendLine("_(none)_");
		}
		else
		{
			foreach (SubdatasetModel subdataset in m.Subdatasets)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder17 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(12, 3, stringBuilder2);
				handler.AppendLiteral("- **[");
				handler.AppendFormatted(subdataset.Index);
				handler.AppendLiteral("] ");
				handler.AppendFormatted(subdataset.Name);
				handler.AppendLiteral("** — ");
				handler.AppendFormatted(subdataset.Description);
				stringBuilder17.AppendLine(ref handler);
			}
		}
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder18 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
		handler.AppendLiteral("## Pyramid Levels (");
		handler.AppendFormatted(m.PyramidLevels.Count);
		handler.AppendLiteral(")");
		stringBuilder18.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (m.PyramidLevels.Count == 0)
		{
			stringBuilder.AppendLine("_(none)_");
		}
		else
		{
			stringBuilder.AppendLine("| Level | Cols | Rows | Cell Size |");
			stringBuilder.AppendLine("|-------|------|------|-----------|");
			foreach (PyramidLevelModel pyramidLevel in m.PyramidLevels)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder19 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(13, 4, stringBuilder2);
				handler.AppendLiteral("| ");
				handler.AppendFormatted(pyramidLevel.Index);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted(pyramidLevel.Cols);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted(pyramidLevel.Rows);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted(pyramidLevel.CellSize);
				handler.AppendLiteral(" |");
				stringBuilder19.AppendLine(ref handler);
			}
		}
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder20 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
		handler.AppendLiteral("## Metadata (");
		handler.AppendFormatted(m.Metadata.Count);
		handler.AppendLiteral(")");
		stringBuilder20.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (m.Metadata.Count == 0)
		{
			stringBuilder.AppendLine("_(none)_");
		}
		else
		{
			stringBuilder.AppendLine("| Key | Value |");
			stringBuilder.AppendLine("|-----|-------|");
			foreach (MetadataItemModel metadata in m.Metadata)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder21 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(7, 2, stringBuilder2);
				handler.AppendLiteral("| ");
				handler.AppendFormatted(metadata.Key);
				handler.AppendLiteral(" | ");
				handler.AppendFormatted(metadata.Value);
				handler.AppendLiteral(" |");
				stringBuilder21.AppendLine(ref handler);
			}
		}
		return stringBuilder.ToString().TrimEnd();
	}
}
