using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using CommandLine;
using CommandLine.Text;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Ras.Process.Commands;
using Ras.Process.Helpers;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.RasterCommands;

[Verb("render", false, null, HelpText = "Renders a raster on disk into a georeferenced color image (RGB or RGBA GeoTIFF) by applying an interpolated or discrete color ramp. Takes a single raster file - it does not read a RAS result or project. Reads floating-point rasters only; integer grids are not yet supported. Specify the ramp with --ramp, --palette, --colors, or --surface-fill. Add --hillshade for shaded relief, on its own or blended over a ramp.")]
public class RasterRenderArgs
{
	private enum OutputImageFormat
	{
		GeoTiff,
		Png
	}

	public const string Verb = "render";

	private const string SampleRamp = "0=255,0,0|2.5=40,255,40|15=40,80,255";

	private const string SampleColors = "Red|0,128,0|0,0,255";

	[Value(0, MetaName = "source", Required = false, HelpText = "Input raster file (e.g. a GeoTIFF). Any format our GDAL build reads. Not required when only listing palettes/fills.")]
	public string SourceFile { get; set; }

	[Value(1, MetaName = "output", Required = false, HelpText = "Output image filename. The format is inferred from its extension: '.tif' (tiled GeoTIFF, the default) or '.png' (Windows-friendly, georeferenced via a sidecar world file). If not provided, defaults to a '.image.tif' file next to the source.")]
	public string OutputFilename { get; set; }

	[Option("if-exists", Required = false, HelpText = "What to do when the output file already exists.\n\t'force': delete the existing file and overwrite it.\n\t'append': write to a numbered copy instead (e.g. 'output (3).tif').\n\t'error': report a message and refuse to overwrite.\n\t'default' (default): error when you name the output explicitly, append when it's auto-derived.")]
	public string IfExists { get; set; }

	[Option('f', "force", Required = false, HelpText = "Alias for '--if-exists force': overwrite the output file if it already exists.")]
	public bool Force { get; set; }

	[Option("ramp", Required = false, HelpText = "Explicit color ramp as 'V=R,G,B[,A]|V=R,G,B[,A]|...', e.g. '0=255,0,0|2.5=40,255,40|15=40,80,255' where 'V' is the value and R/G/B(/A) are 0-255 color channel components. Requires at least two entries.")]
	public string Ramp { get; set; }

	[Option("palette", Required = false, HelpText = "Named color palette (e.g. 'Terrain', 'WaterSurface', 'Rainbow'). Combine with --values, with --min/--max, or with neither (auto-ranges from the raster histogram). See --list-palettes.")]
	public string Palette { get; set; }

	[Option("colors", Required = false, HelpText = "Inline color list, '|'-separated, used like a --palette (combine with --values, --min/--max, or neither). Each color is a System.Drawing name (e.g. 'SteelBlue'), an 'R,G,B' or 'R,G,B,A' triple (0-255), or a '#RRGGBB' hex. Sample: 'Red|0,128,0|0,0,255'. Mutually exclusive with --palette.")]
	public string Colors { get; set; }

	[Option("surface-fill", Required = false, HelpText = "Named, ready-made surface fill that carries both colors and values (e.g. 'Courant', 'Hazard', 'Precip Inches'). See --list-fills.")]
	public string SurfaceFillName { get; set; }

	[Option("values", Required = false, HelpText = "Comma-separated stop values to pair with --palette, e.g. '0,1,2,5,10'. Provide exactly one value per palette color (see --list-palettes); use --min/--max to spread the palette linearly instead.")]
	public string Values { get; set; }

	[Option("min", Required = false, HelpText = "Low value of a linear range spread across --palette. Use together with --max.")]
	public double? Min { get; set; }

	[Option("max", Required = false, HelpText = "High value of a linear range spread across --palette. Use together with --min.")]
	public double? Max { get; set; }

	[Option("discrete", Required = false, HelpText = "Render a stepped/classified ramp (flat color per band) instead of interpolating between stops.")]
	public bool Discrete { get; set; }

	[Option("hillshade", Required = false, HelpText = "Overlay shaded relief computed from the elevation surface. Works on its own (grayscale relief) or blended over a color ramp. Requires a floating-point (elevation) raster.")]
	public bool Hillshade { get; set; }

	[Option("azimuth", Required = false, HelpText = "Hillshade light direction in degrees (0=N, 90=E, clockwise), default 315. Only with --hillshade.")]
	public double? Azimuth { get; set; }

	[Option("altitude", Required = false, HelpText = "Hillshade light angle above the horizon in degrees (0-90), default 45. Only with --hillshade.")]
	public double? Altitude { get; set; }

	[Option("z-factor", Required = false, HelpText = "Hillshade vertical exaggeration, default 3. Higher values deepen the relief. Only with --hillshade.")]
	public double? ZFactor { get; set; }

	[Option("alpha", Required = false, Default = 255, HelpText = "Global opacity 0-255 applied to the ramp. Values below 255 imply --rgba.")]
	public int? Alpha { get; set; }

	[Option("rgba", Required = false, HelpText = "Write a 4-band RGBA image so NoData pixels stay transparent. Default is 3-band RGB (NoData collapses to black).")]
	public bool Rgba { get; set; }

	[Option("interp", Required = false, Default = InterpolationMode.Nearest, HelpText = "Resampling interpolation: Nearest (default), Linear, etc. Only matters when --cell-size differs from the source resolution.")]
	public InterpolationMode Interp { get; set; }

	[Option("cell-size", Required = false, HelpText = "Output cell size (native units). Defaults to the source raster's resolution.")]
	public double CellSize { get; set; }

	[Option("use-gdal", Required = false, HelpText = "Force the GDAL driver stack for reading the source raster.")]
	public bool UseGdalDrivers { get; set; }

	[Option("list-palettes", Required = false, HelpText = "Print the available named palettes and exit.")]
	public bool ListPalettes { get; set; }

	[Option("list-fills", Required = false, HelpText = "Print the available named surface fills and exit.")]
	public bool ListSurfaceFills { get; set; }

	[Option("dry-run", Required = false, HelpText = "Print the resolved settings (ramp, range, projection, output) without rendering.")]
	public bool DryRun { get; set; }

	[Usage(ApplicationAlias = "ras raster")]
	public static IEnumerable<Example> Examples => new Example[14]
	{
		new Example("Palette auto-ranged across the raster's own value distribution", new RasterRenderArgs
		{
			SourceFile = "depth.tif",
			Palette = "Rainbow"
		}),
		new Example("Palette spread linearly over a fixed range", new RasterRenderArgs
		{
			SourceFile = "depth.tif",
			Palette = "Rainbow",
			Min = 0.0,
			Max = 30.0
		}),
		new Example("Palette with one explicit stop value per color (must match the palette's color count)", new RasterRenderArgs
		{
			SourceFile = "hazard.tif",
			Palette = "Hazard",
			Values = "0,1,2,3,4,5,6,7,8,9,10"
		}),
		new Example("Discrete (classified) bands instead of a smooth gradient", new RasterRenderArgs
		{
			SourceFile = "landcover.tif",
			Palette = "NLCD",
			Discrete = true
		}),
		new Example("Inline colors by name, spread over a range", new RasterRenderArgs
		{
			SourceFile = "depth.tif",
			Colors = "blue|cyan|yellow|red",
			Min = 0.0,
			Max = 30.0
		}),
		new Example("Inline colors as RGB and hex, placed at explicit values ('|' separates colors, ',' the channels)", new RasterRenderArgs
		{
			SourceFile = "depth.tif",
			Colors = "0,0,255|#FFFFFF|255,0,0",
			Values = "0,15,30"
		}),
		new Example("Fully explicit value=color ramp", new RasterRenderArgs
		{
			SourceFile = "velocity.tif",
			Ramp = "0=0,0,255|2.5=0,255,0|10=255,0,0"
		}),
		new Example("Ready-made named surface fill (carries its own colors and values)", new RasterRenderArgs
		{
			SourceFile = "courant.tif",
			SurfaceFillName = "Courant"
		}),
		new Example("Standalone hillshade (grayscale shaded relief from an elevation raster)", new RasterRenderArgs
		{
			SourceFile = "terrain.tif",
			Hillshade = true
		}),
		new Example("Hypsometric tint: a color ramp with hillshade blended on top, custom light angle", new RasterRenderArgs
		{
			SourceFile = "terrain.tif",
			Palette = "Terrain",
			Min = 0.0,
			Max = 1000.0,
			Hillshade = true,
			Azimuth = 315.0,
			Altitude = 30.0
		}),
		new Example("Transparent NoData with partial opacity (4-band RGBA)", new RasterRenderArgs
		{
			SourceFile = "depth.tif",
			OutputFilename = "depth.rgba.tif",
			Palette = "Rainbow",
			Min = 0.0,
			Max = 10.0,
			Rgba = true,
			Alpha = 180
		}),
		new Example("Resample to a coarser cell size, overwriting any existing output", new RasterRenderArgs
		{
			SourceFile = "depth.tif",
			OutputFilename = "preview.tif",
			Palette = "Rainbow",
			CellSize = 50.0,
			Force = true
		}),
		new Example("Preview the resolved ramp, range and projection without rendering", new RasterRenderArgs
		{
			SourceFile = "depth.tif",
			Palette = "Terrain",
			Min = 0.0,
			Max = 50.0,
			DryRun = true
		}),
		new Example("Browse the built-in palette names (no raster needed)", new RasterRenderArgs
		{
			ListPalettes = true
		})
	};

	private int AlphaValue => Alpha ?? 255;

	private bool IncludeAlpha
	{
		get
		{
			if (!Rgba)
			{
				return AlphaValue < 255;
			}
			return true;
		}
	}

	private float AzimuthValue => (float)(Azimuth ?? 315.0);

	private float AltitudeValue => (float)(Altitude ?? 45.0);

	private float ZFactorValue => (float)(ZFactor ?? 3.0);

	private bool HasColorSource
	{
		get
		{
			if (string.IsNullOrWhiteSpace(Ramp) && string.IsNullOrWhiteSpace(Palette) && string.IsNullOrWhiteSpace(Colors))
			{
				return !string.IsNullOrWhiteSpace(SurfaceFillName);
			}
			return true;
		}
	}

	public bool Execute()
	{
		ProgressReporter pr = ProgressReporter.ConsoleWrite();
		GDALSetup.InitializeMultiplatform();
		if (ListPalettes)
		{
			PrintNames("Palettes", ColorPalettes.Names);
			return true;
		}
		if (ListSurfaceFills)
		{
			PrintNames("Surface fills", SurfaceFills.Names);
			return true;
		}
		if (string.IsNullOrWhiteSpace(SourceFile))
		{
			Console.WriteLine("A source raster is required. (Use --list-palettes or --list-fills to browse ramps.)");
			return false;
		}
		if (!File.Exists(SourceFile))
		{
			Console.WriteLine(SourceFile.InDoubleQuotes() + " does not exist.");
			return false;
		}
		if (AlphaValue < 0 || AlphaValue > 255)
		{
			Console.WriteLine("--alpha must be between 0 and 255.");
			return false;
		}
		if (!TryValidateRampOptions(out var error))
		{
			Console.WriteLine(error);
			return false;
		}
		if (!TryValidateHillshadeOptions(out var error2))
		{
			Console.WriteLine(error2);
			return false;
		}
		if (!TryValidateSourceDataType(out var error3))
		{
			Console.WriteLine(error3);
			return false;
		}
		if (!TryResolveIfExists(pr, out var strategy))
		{
			return false;
		}
		bool notIsNullOrWhiteSpace = !string.IsNullOrWhiteSpace(OutputFilename);
		string resolved = (notIsNullOrWhiteSpace ? OutputFilename : DeriveDefaultOutputFilename(SourceFile));
		if (!TryResolveOutputFormat(resolved, out var format, out var error4))
		{
			Console.WriteLine(error4);
			return false;
		}
		string suggestedDriver = (UseGdalDrivers ? "GDAL" : "");
		IDisposable cleanup = null;
		try
		{
			IRaster<float> raster = CliHelpers.TimeStep("Loading raster...", () => RasterReader.TryGetBestRasterizer<float>(SourceFile, out var _, out cleanup, suggestedDriver));
			if (raster == null)
			{
				Console.WriteLine("Could not find a suitable driver to read " + SourceFile.InDoubleQuotes() + ".");
				return false;
			}
			if (!TryBuildSurfaceFill(raster, out var fill, out var error5))
			{
				Console.WriteLine(error5);
				return false;
			}
			Projection projection = GDALRaster.GetProjection(SourceFile);
			RasterDefinition rasterDefinition = raster.RasterDefinition;
			RasterDefinition outRdef = ((CellSize > 0.0) ? new RasterDefinition(rasterDefinition.Extent, CellSize) : rasterDefinition);
			if (DryRun)
			{
				PrintDryRun(fill, projection, outRdef, resolved, !notIsNullOrWhiteSpace, format);
				return true;
			}
			if (!SolveHelpers.TryResolveOutputFile(resolved, strategy, notIsNullOrWhiteSpace, pr, out resolved))
			{
				return false;
			}
			RasterSymbology rasterSymbology = new RasterSymbology(fill)
			{
				Alpha = (byte)AlphaValue,
				InterpolationMode = Interp,
				IsSurfaceFillEnabled = HasColorSource
			};
			if (Hillshade)
			{
				rasterSymbology.HillshadeProperties.IsEnabled = true;
				rasterSymbology.HillshadeProperties.Azimuth = AzimuthValue;
				rasterSymbology.HillshadeProperties.Zenith = AltitudeValue;
				rasterSymbology.HillshadeProperties.ZFactor = ZFactorValue;
			}
			RasterRenderLayer renderLayer = new RasterRenderLayer(raster, rasterSymbology);
			switch (format)
			{
			case OutputImageFormat.GeoTiff:
				WriteGeoTiff(resolved, renderLayer, outRdef, projection, pr);
				break;
			case OutputImageFormat.Png:
				if (!TryWritePng(resolved, renderLayer, outRdef, projection, pr))
				{
					return false;
				}
				break;
			}
			return true;
		}
		finally
		{
			cleanup?.Dispose();
		}
	}

	private void WriteGeoTiff(string dest, RasterRenderLayer renderLayer, RasterDefinition outRdef, Projection projection, ProgressReporter pr)
	{
		if (IncludeAlpha)
		{
			RgbTiffExporter.ExportRgba(dest, renderLayer, outRdef, projection, pr);
		}
		else
		{
			RgbTiffExporter.Export(dest, renderLayer, outRdef, projection, pr);
		}
	}

	private bool TryWritePng(string destPng, RasterRenderLayer renderLayer, RasterDefinition outRdef, Projection projection, ProgressReporter pr)
	{
		string destPngTerm = destPng + ".tmp.tif";
		TryDeleteQuietly(destPngTerm);
		try
		{
			WriteGeoTiff(destPngTerm, renderLayer, outRdef, projection, pr);
			pr.ReportMessage("Converting to PNG...");
			if (!GDALRaster.TryCreateCopy(destPngTerm, destPng, "PNG", new string[1] { "WORLDFILE=YES" }))
			{
				Console.WriteLine("GDAL could not write the PNG " + destPng.InDoubleQuotes() + ".");
				return false;
			}
			return true;
		}
		finally
		{
			TryDeleteQuietly(destPngTerm);
		}
	}

	private static void TryDeleteQuietly(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private bool TryValidateRampOptions(out string error)
	{
		error = null;
		bool notIsNullOrWhiteSpace = !string.IsNullOrWhiteSpace(Ramp);
		bool notIsNullOrWhiteSpace2 = !string.IsNullOrWhiteSpace(Palette);
		bool notIsNullOrWhiteSpace3 = !string.IsNullOrWhiteSpace(Colors);
		bool notIsNullOrWhiteSpace4 = !string.IsNullOrWhiteSpace(SurfaceFillName);
		int notIsNullOrWhiteSpaceTerm = (notIsNullOrWhiteSpace ? 1 : 0) + (notIsNullOrWhiteSpace2 ? 1 : 0) + (notIsNullOrWhiteSpace3 ? 1 : 0) + (notIsNullOrWhiteSpace4 ? 1 : 0);
		if (notIsNullOrWhiteSpaceTerm == 0 && !Hillshade)
		{
			error = "Provide a ramp via --ramp, --palette, --colors, or --surface-fill (or use --hillshade for shaded relief).";
			return false;
		}
		if (notIsNullOrWhiteSpaceTerm > 1)
		{
			error = "--ramp, --palette, --colors, and --surface-fill are mutually exclusive - use only one.";
			return false;
		}
		bool notIsNullOrWhiteSpace5 = !string.IsNullOrWhiteSpace(Values);
		bool hasValue = Min.HasValue;
		bool hasValue2 = Max.HasValue;
		if (notIsNullOrWhiteSpace5 && (hasValue | hasValue2))
		{
			error = "--values cannot be combined with --min/--max - they are alternative ways to place the colors' stops.";
			return false;
		}
		if (hasValue != hasValue2)
		{
			error = "--min and --max must be provided together.";
			return false;
		}
		bool notIsNullOrWhiteSpace2WithNotIsNullOrWhiteSpace3 = notIsNullOrWhiteSpace2 | notIsNullOrWhiteSpace3;
		if (notIsNullOrWhiteSpace5 && !notIsNullOrWhiteSpace2WithNotIsNullOrWhiteSpace3)
		{
			error = ((notIsNullOrWhiteSpaceTerm == 0) ? "--values requires --palette or --colors." : ((notIsNullOrWhiteSpace ? "--ramp" : "--surface-fill") + " already defines its stop values; --values only applies to --palette or --colors."));
			return false;
		}
		if ((hasValue | hasValue2) && !notIsNullOrWhiteSpace2WithNotIsNullOrWhiteSpace3)
		{
			error = ((notIsNullOrWhiteSpaceTerm == 0) ? "--min/--max require --palette or --colors." : ((notIsNullOrWhiteSpace ? "--ramp" : "--surface-fill") + " already defines its range; --min/--max only apply to --palette or --colors."));
			return false;
		}
		return true;
	}

	private bool TryValidateHillshadeOptions(out string error)
	{
		error = null;
		if (!Hillshade)
		{
			if (Azimuth.HasValue || Altitude.HasValue || ZFactor.HasValue)
			{
				error = "--azimuth, --altitude and --z-factor only apply with --hillshade.";
				return false;
			}
			return true;
		}
		if (Azimuth.HasValue && (Azimuth < 0.0 || Azimuth > 360.0))
		{
			error = "--azimuth must be between 0 and 360 degrees.";
			return false;
		}
		if (Altitude.HasValue && (Altitude < 0.0 || Altitude > 90.0))
		{
			error = "--altitude must be between 0 and 90 degrees (above the horizon).";
			return false;
		}
		if (ZFactor.HasValue && ZFactor <= 0.0)
		{
			error = "--z-factor must be greater than 0.";
			return false;
		}
		return true;
	}

	private bool TryValidateSourceDataType(out string error)
	{
		error = null;
		using GDALRaster gDALRaster = GDALRaster.TryOpen(SourceFile);
		Type type = gDALRaster?.GetDataType();
		if (type == typeof(float) || type == typeof(double))
		{
			return true;
		}
		string[] strings = gDALRaster?.BandDataType_FriendlyName();
		string textValue = ((strings != null && strings.Length != 0) ? strings[0] : "an unsupported type");
		error = SourceFile.InDoubleQuotes() + " is " + textValue + ". 'ras raster render' currently supports floating-point rasters only - integer grids are not yet supported.";
		return false;
	}

	private bool TryBuildSurfaceFill(IResample<float> raster, out InterpolatedSurfaceFill fill, out string error)
	{
		fill = null;
		error = null;
		if (!HasColorSource)
		{
			fill = InterpolatedSurfaceFill.DefaultSurfaceFill();
			return true;
		}
		if (!string.IsNullOrWhiteSpace(Ramp))
		{
			if (!TryParseRamp(Ramp, out var colors, out var values, out error))
			{
				return false;
			}
			fill = new InterpolatedSurfaceFill(colors, values);
		}
		else if (!string.IsNullOrWhiteSpace(SurfaceFillName))
		{
			if (!SurfaceFills.TryGet(SurfaceFillName, out var fill2))
			{
				error = "Unknown surface fill " + SurfaceFillName.InDoubleQuotes() + ". Use --list-fills to see the available names.";
				return false;
			}
			fill = fill2.Clone();
		}
		else
		{
			List<Color> colors2;
			string paletteTerm;
			if (!string.IsNullOrWhiteSpace(Palette))
			{
				if (!ColorPalettes.TryGet(Palette, out var palette))
				{
					error = "Unknown palette " + Palette.InDoubleQuotes() + ". Use --list-palettes to see the available names.";
					return false;
				}
				colors2 = palette.Colors;
				paletteTerm = palette.Name;
			}
			else
			{
				if (!TryParseColors(Colors, out colors2, out error))
				{
					return false;
				}
				paletteTerm = "";
			}
			if (!string.IsNullOrWhiteSpace(Values))
			{
				if (!TryParseValues(Values, out var values2, out error))
				{
					return false;
				}
				if (values2.Count != colors2.Count)
				{
					string isNullOrWhiteSpaceInDoubleQuotesTerm = (string.IsNullOrWhiteSpace(paletteTerm) ? "the --colors list" : ("palette " + paletteTerm.InDoubleQuotes()));
					error = "--values has " + values2.Count + " entries but " + isNullOrWhiteSpaceInDoubleQuotesTerm + " has " + colors2.Count + " colors. Provide exactly one value per color, or use --min/--max to spread them linearly.";
					return false;
				}
				fill = new InterpolatedSurfaceFill(colors2, values2);
			}
			else if (Min.HasValue)
			{
				if (Max.Value <= Min.Value)
				{
					error = "--max must be greater than --min.";
					return false;
				}
				fill = new InterpolatedSurfaceFill(colors2, LinearValues(Min.Value, Max.Value, colors2.Count));
			}
			else
			{
				fill = InterpolatedSurfaceFill.EstimateFrom(raster, colors2, paletteTerm);
			}
		}
		fill.Discrete = Discrete;
		return true;
	}

	private static bool TryParseRamp(string textValue, out List<Color> colors, out List<double> values, out string error)
	{
		colors = new List<Color>();
		values = new List<double>();
		error = null;
		string[] parts = textValue.Split('|', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 2)
		{
			error = "A --ramp needs at least two stops. Sample: 0=255,0,0|2.5=40,255,40|15=40,80,255";
			return false;
		}
		string[] partsArray = parts;
		foreach (string part in partsArray)
		{
			string[] parts2 = part.Split('=');
			if (parts2.Length != 2 || !double.TryParse(parts2[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				error = "Invalid ramp stop: '" + part + "'. Sample: 0=255,0,0|2.5=40,255,40|15=40,80,255";
				return false;
			}
			string[] parts3 = parts2[1].Split(',');
			if (parts3.Length != 3 && parts3.Length != 4)
			{
				error = "Invalid ramp stop: '" + part + "'. Expected R,G,B or R,G,B,A. Sample: 0=255,0,0|2.5=40,255,40|15=40,80,255";
				return false;
			}
			if (!byte.TryParse(parts3[0], out var result2) || !byte.TryParse(parts3[1], out var result3) || !byte.TryParse(parts3[2], out var result4))
			{
				error = "Invalid ramp stop: '" + part + "'. Channels must be 0-255. Sample: 0=255,0,0|2.5=40,255,40|15=40,80,255";
				return false;
			}
			byte result5 = byte.MaxValue;
			if (parts3.Length == 4 && !byte.TryParse(parts3[3], out result5))
			{
				error = "Invalid ramp stop: '" + part + "'. Alpha must be 0-255. Sample: 0=255,0,0|2.5=40,255,40|15=40,80,255";
				return false;
			}
			values.Add(result);
			colors.Add(Color.FromArgb(result5, result2, result3, result4));
		}
		return true;
	}

	private static bool TryParseValues(string textValue, out List<double> values, out string error)
	{
		values = new List<double>();
		error = null;
		string[] parts = textValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string part in parts)
		{
			if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				error = "Invalid --values entry: '" + part + "'. Expected comma-separated numbers.";
				return false;
			}
			values.Add(result);
		}
		if (values.Count < 2)
		{
			error = "--values needs at least two entries.";
			return false;
		}
		return true;
	}

	private static bool TryParseColors(string textValue, out List<Color> colors, out string error)
	{
		colors = new List<Color>();
		error = null;
		string[] parts = textValue.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string part in parts)
		{
			if (!TryParseColor(part, out var color, out var detail))
			{
				error = "Invalid --colors entry '" + part + "': " + detail + ". Sample: Red|0,128,0|0,0,255";
				return false;
			}
			colors.Add(color);
		}
		if (colors.Count < 2)
		{
			error = "--colors needs at least two colors. Sample: Red|0,128,0|0,0,255";
			return false;
		}
		return true;
	}

	private static bool TryParseColor(string token, out Color color, out string detail)
	{
		color = Color.Empty;
		detail = null;
		if (token.Contains(','))
		{
			string[] parts = token.Split(',', StringSplitOptions.TrimEntries);
			if (parts.Length != 3 && parts.Length != 4)
			{
				detail = "expected R,G,B or R,G,B,A";
				return false;
			}
			if (!byte.TryParse(parts[0], out var result) || !byte.TryParse(parts[1], out var result2) || !byte.TryParse(parts[2], out var result3))
			{
				detail = "channels must be 0-255";
				return false;
			}
			byte result4 = byte.MaxValue;
			if (parts.Length == 4 && !byte.TryParse(parts[3], out result4))
			{
				detail = "alpha must be 0-255";
				return false;
			}
			color = Color.FromArgb(result4, result, result2, result3);
			return true;
		}
		if (token.StartsWith('#'))
		{
			try
			{
				color = ColorTranslator.FromHtml(token);
				return true;
			}
			catch
			{
				detail = "invalid hex color";
				return false;
			}
		}
		Color color2 = Color.FromName(token);
		if (color2.IsKnownColor)
		{
			color = color2;
			return true;
		}
		detail = "not a known color name";
		return false;
	}

	private static List<double> LinearValues(double min, double max, int count)
	{
		List<double> doubleList = new List<double>(count);
		if (count <= 1)
		{
			doubleList.Add(min);
			return doubleList;
		}
		for (int i = 0; i < count; i++)
		{
			doubleList.Add(min + (max - min) * (double)i / (double)(count - 1));
		}
		return doubleList;
	}

	private static bool TryResolveOutputFormat(string path, out OutputImageFormat format, out string error)
	{
		format = OutputImageFormat.GeoTiff;
		error = null;
		string extension = Path.GetExtension(path);
		if (extension.RoughlyEquals(".tif") || extension.RoughlyEquals(".tiff"))
		{
			format = OutputImageFormat.GeoTiff;
			return true;
		}
		if (extension.RoughlyEquals(".png"))
		{
			format = OutputImageFormat.Png;
			return true;
		}
		error = "Unsupported output format " + extension.InDoubleQuotes() + ". Supported: .tif (tiled GeoTIFF), .png.";
		return false;
	}

	private string DeriveDefaultOutputFilename(string source)
	{
		string? directoryName = Path.GetDirectoryName(Path.GetFullPath(source));
		string path = Path.GetFileNameWithoutExtension(source) + ".image.tif";
		return Path.Combine(directoryName, path);
	}

	private bool TryResolveIfExists(ProgressReporter pr, out OutputExistsStrategy strategy)
	{
		if (!SolveHelpers.TryParseIfExists(IfExists, pr, out strategy))
		{
			return false;
		}
		if (Force)
		{
			if (!string.IsNullOrWhiteSpace(IfExists) && strategy != OutputExistsStrategy.Force)
			{
				pr.ReportMessage("--force is shorthand for '--if-exists force' and conflicts with '--if-exists " + IfExists + "'.");
				return false;
			}
			strategy = OutputExistsStrategy.Force;
		}
		return true;
	}

	private void PrintDryRun(InterpolatedSurfaceFill fill, Projection projection, RasterDefinition outRdef, string outputFn, bool outputInferred, OutputImageFormat format)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Dry run - no image will be rendered.");
		stringBuilder.AppendLine("  Source:     " + SourceFile.InDoubleQuotes());
		stringBuilder.AppendLine("  Output:     " + outputFn.InDoubleQuotes() + Tag(outputInferred));
		stringBuilder.AppendLine("  Format:     " + format);
		stringBuilder.AppendLine("  Bands:      " + (IncludeAlpha ? "RGBA (4)" : "RGB (3)"));
		StringBuilder stringBuilder2 = stringBuilder;
		string discreteTerm;
		if (HasColorSource)
		{
			discreteTerm = (Discrete ? "discrete" : "interpolated");
		}
		else
		{
			discreteTerm = "(none - hillshade only)";
		}
		stringBuilder2.AppendLine("  Ramp:       " + discreteTerm);
		stringBuilder.AppendLine("  Hillshade:  " + (Hillshade ? $"az {AzimuthValue}, alt {AltitudeValue}, z-factor {ZFactorValue}" : "off"));
		stringBuilder.AppendLine("  Alpha:      " + AlphaValue);
		stringBuilder.AppendLine("  Interp:     " + Interp);
		stringBuilder.AppendLine("  Size:       " + outRdef.Cols + " x " + outRdef.Rows + " px  (cell " + outRdef.CellSize + ")");
		stringBuilder.AppendLine("  Projection: " + ((projection == null) ? "(none)" : projection.GetProj4()));
		if (HasColorSource)
		{
			stringBuilder.AppendLine("  Stops:");
			for (int i = 0; i < fill.Count; i++)
			{
				Color color = fill.Colors[i];
				StringBuilder stringBuilder3 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(10, 5, stringBuilder3);
				handler.AppendLiteral("    ");
				handler.AppendFormatted(fill.ColorValues[i]);
				handler.AppendLiteral(" = ");
				handler.AppendFormatted(color.R);
				handler.AppendLiteral(",");
				handler.AppendFormatted(color.G);
				handler.AppendLiteral(",");
				handler.AppendFormatted(color.B);
				handler.AppendLiteral(",");
				handler.AppendFormatted(color.A);
				stringBuilder3.AppendLine(ref handler);
			}
		}
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

	private static void PrintNames(string header, IReadOnlyList<string> names)
	{
		Console.WriteLine(header + " (" + names.Count + "):");
		foreach (string name in names)
		{
			Console.WriteLine("  " + name);
		}
	}
}
