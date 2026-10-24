using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Geospatial.GDALAssist;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Recolor;
using Geospatial.Vectors;
using OSGeo.GDAL;
using Ras.Backend;
using Ras.Runtime;

namespace Ras.Backend.ServerChecks;

public sealed record RasterFormatNumericResult(
    string DataType,
    string SourcePath,
    int Width,
    int Height,
    double? NoDataValue,
    long[] RawValues,
    double[] DoubleReadValues,
    long[] RendererValues,
    string[] RenderedRgba,
    long[]? PaletteValues = null,
    string[]? PaletteColors = null,
    string? HighCategoryHoverValue = null);

public sealed record RasterFormatRenderResult(
    string SourcePath,
    int Width,
    int Height,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    string[] RenderedRgba);

public sealed record RasterFormatRejectionResult(
    string Format,
    string ExpectedException,
    string ActualException,
    string Message);

public sealed record RasterFormatNoDataResult(
    string SourcePath,
    bool SourceNoDataMissing,
    int RendererNoDataValue,
    string ZeroPixelRgba,
    string NonZeroPixelRgba);

public sealed record RasterFormatUInt32ExactResult(
    string SourcePath,
    uint[] SourceValues,
    uint SourceNoDataValue,
    uint[] PaletteValues,
    string[] PaletteColors,
    string[] RenderedRgba,
    string HoverValueAtUInt32Max,
    RasterFormatUInt32MaximumNoDataResult MaximumNoData);

public sealed record RasterFormatUInt32MaximumNoDataResult(
    string SourcePath,
    uint[] UInt32MaxNoDataSourceValues,
    uint UInt32MaxNoDataValue,
    uint[] PaletteValues,
    string[] PaletteColors,
    string[] RenderedRgba,
    bool UInt32MaxNoDataTransparent,
    bool ZeroRemainsVisible,
    string Int32MaxPixelRgba,
    string AboveInt32MaxPixelRgba,
    bool HighUInt32CategoriesHaveDistinctColors);

public sealed record RasterFormatRgbaRenderResult(
    string SourcePath,
    int Width,
    int Height,
    byte[] RedValues,
    byte[] GreenValues,
    byte[] BlueValues,
    byte[] AlphaValues,
    string[] RenderedRgba,
    string[] Opacity128Rgba,
    string[] RecoloredPremultipliedRgba,
    string RgbNoDataVrtPath,
    string[] RgbNoDataRenderedRgba,
    string AlphaNoDataVrtPath,
    string[] AlphaNoDataRenderedRgba);

public sealed record RasterFormatRgbNoDataResult(
    double[] CommonNoDataValues,
    bool CommonNoDataAccepted,
    RasterFormatRejectionResult DifferentNoDataRejected,
    RasterFormatRejectionResult InvalidNoDataRejected);

public sealed record RasterFormatAcceptanceReport(
    string Schema,
    string OutputDirectory,
    RasterFormatNumericResult[] NumericFormats,
    RasterFormatRenderResult PaletteRender,
    RasterFormatRenderResult RgbRender,
    RasterFormatRgbaRenderResult RgbaRender,
    RasterFormatNoDataResult MissingNoData,
    RasterFormatUInt32ExactResult UInt32Exact,
    RasterFormatRgbNoDataResult RgbNoDataContract,
    RasterFormatRejectionResult[] UnsupportedFormats,
    RasterFormatRejectionResult[] InvalidIntegerNoData);

/// <summary>
/// Creates small real GDAL datasets, reads their actual typed cells, and runs
/// the Host's retained source renderers against exact full-source viewports.
/// The requested output directory must be new and outside the repository.
/// </summary>
public static class RasterFormatAcceptance
{
    private const int Width = 4;
    private const int Height = 2;

    private static readonly RasterMapViewExtent NumericViewport = new(0, 0, Width, Height, Width, Height);

    public static RasterFormatAcceptanceReport Run(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        string output = Path.GetFullPath(outputDirectory);
        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException("Raster format acceptance output must be a new directory; existing files are never overwritten.");
        EnsureOutsideRepository(output);

        RasRuntimeBootstrap.Configure();
        Directory.CreateDirectory(output);

        string bytePath = Path.Combine(output, "byte-values.tif");
        string int16Path = Path.Combine(output, "int16-values.tif");
        string uint16Path = Path.Combine(output, "uint16-values.tif");
        string int32Path = Path.Combine(output, "int32-values.tif");
        string uint32Path = Path.Combine(output, "uint32-values.tif");

        byte[] byteValues = [0, 1, 127, 254, 255, 42, 128, 2];
        short[] int16Values = [short.MinValue, -1, 0, short.MaxValue, 1234, 255, 256, -1234];
        ushort[] uint16Values = [0, 1, 32768, ushort.MaxValue, 1234, 65534, 256, 42];
        int[] int32Values = [int.MinValue, -1, 0, int.MaxValue, 1234567, 42, -1234567, 123];
        uint[] uint32Values = [0, 1, int.MaxValue, (uint)int.MaxValue + 1, uint.MaxValue, 123456, 42, (uint)int.MaxValue - 1];

        WriteSingleBand(bytePath, DataType.GDT_Byte, byteValues, 254);
        WriteSingleBand(int16Path, DataType.GDT_Int16, int16Values, 1234);
        WriteSingleBand(uint16Path, DataType.GDT_UInt16, uint16Values, 1234);
        WriteSingleBand(int32Path, DataType.GDT_Int32, int32Values, 1234567);
        WriteSingleBand(uint32Path, DataType.GDT_UInt32, uint32Values, 123456);

        RasterFormatNumericResult[] numericFormats =
        [
            CheckNumeric<byte>(bytePath, byteValues.Select(value => (long)value).ToArray(), 254),
            CheckNumeric<short>(int16Path, int16Values.Select(value => (long)value).ToArray(), 1234),
            CheckNumeric<ushort>(uint16Path, uint16Values.Select(value => (long)value).ToArray(), 1234),
            CheckNumeric<int>(int32Path, int32Values.Select(value => (long)value).ToArray(), 1234567),
            CheckNumeric<uint>(uint32Path, uint32Values.Select(value => (long)value).ToArray(), 123456),
        ];

        string palettePath = Path.Combine(output, "byte-palette-nodata-255.vrt");
        WritePaletteRaster(palettePath);
        RasterFormatRenderResult paletteRender = CheckPalettePixels(palettePath);

        string rgbPath = Path.Combine(output, "rgb-byte.tif");
        WriteRgbRaster(rgbPath);
        RasterFormatRenderResult rgbRender = CheckRgbPixels(rgbPath);
        RasterFormatRgbNoDataResult rgbNoDataContract = CheckRgbNoDataConstraints(rgbPath, output);

        string rgbaPath = Path.Combine(output, "rgba-byte.tif");
        byte[] rgbaRed = [255, 255, 12, 100, 0, 0, 254, 64];
        byte[] rgbaGreen = [100, 128, 34, 150, 0, 2, 253, 128];
        byte[] rgbaBlue = [0, 64, 56, 200, 0, 3, 252, 192];
        byte[] rgbaAlpha = [0, 1, 128, 255, 255, 128, 1, 0];
        WriteRgbaRaster(rgbaPath, rgbaRed, rgbaGreen, rgbaBlue, rgbaAlpha);
        RasterFormatRgbaRenderResult rgbaRender = CheckRgbaPixels(rgbaPath, output,
            rgbaRed, rgbaGreen, rgbaBlue, rgbaAlpha);

        string missingNoDataPath = Path.Combine(output, "byte-missing-nodata.tif");
        WriteSingleBand(missingNoDataPath, DataType.GDT_Byte, new byte[] { 0, 1, 2, 3 }, null,
            width: 4, height: 1);
        RasterFormatNoDataResult missingNoData = CheckMissingNoData(missingNoDataPath);

        string twoBandPath = Path.Combine(output, "unsupported-two-band-byte.tif");
        WriteMultiBand(twoBandPath, DataType.GDT_Byte, 2);
        string ambiguousFourBandSourcePath = Path.Combine(output, "ambiguous-four-band-byte-source.tif");
        WriteMultiBand(ambiguousFourBandSourcePath, DataType.GDT_Byte, 4);
        string ambiguousFourBandPath = Path.Combine(output, "unsupported-ambiguous-four-band-byte.vrt");
        WriteUndefinedColorInterpretationVrt(ambiguousFourBandPath, ambiguousFourBandSourcePath, 4);
        string nonByteMultiPath = Path.Combine(output, "unsupported-three-band-int16.tif");
        WriteMultiBand(nonByteMultiPath, DataType.GDT_Int16, 3);
        string nonByteFourBandPath = Path.Combine(output, "unsupported-four-band-int16.tif");
        WriteMultiBand(nonByteFourBandPath, DataType.GDT_Int16, 4);
        using (var ambiguous = new GDALRaster(ambiguousFourBandPath))
        {
            Require(ambiguous.GetBandCount() == 4 &&
                    Enumerable.Range(1, 4).All(index => ambiguous.GetBandColorInterpretation(index) == ColorInterp.GCI_Undefined),
                "The undefined four-band VRT did not retain four undefined color interpretations.");
        }

        RasterFormatRejectionResult[] unsupportedFormats =
        [
            AssertRejected(twoBandPath, "2-band Byte", typeof(NotSupportedException)),
            AssertRejected(ambiguousFourBandPath, "4-band Byte without RGBA interpretation", typeof(NotSupportedException)),
            AssertRejected(nonByteMultiPath, "3-band Int16", typeof(NotSupportedException)),
            AssertRejected(nonByteFourBandPath, "4-band Int16", typeof(NotSupportedException)),
        ];

        string invalidNoDataSourcePath = Path.Combine(output, "invalid-nodata-source.tif");
        WriteSingleBand(invalidNoDataSourcePath, DataType.GDT_Int16,
            new short[] { -2, -1, 0, 1, 2, 3, 4, 5 }, null);
        string fractionalNoDataPath = Path.Combine(output, "int16-fractional-nodata.vrt");
        WriteNoDataOverrideVrt(fractionalNoDataPath, invalidNoDataSourcePath, 1.5);
        string outOfRangeNoDataPath = Path.Combine(output, "int16-out-of-range-nodata.vrt");
        WriteNoDataOverrideVrt(outOfRangeNoDataPath, invalidNoDataSourcePath, 32768);
        RasterFormatRejectionResult[] invalidNoData =
        [
            AssertRejected(fractionalNoDataPath, "Int16 小数 NoData=1.5", typeof(InvalidDataException),
                expectedNoData: 1.5),
            AssertRejected(outOfRangeNoDataPath, "Int16 越界 NoData=32768", typeof(InvalidDataException),
                expectedNoData: 32768),
        ];

        RasterFormatUInt32ExactResult uint32Exact = CheckUInt32NoDataMaximum(
            uint32Path, Path.Combine(output, "uint32-nodata-maximum.tif"), numericFormats);

        return new("hecras-raster-format-acceptance/v2", output, numericFormats,
            paletteRender, rgbRender, rgbaRender, missingNoData, uint32Exact,
            rgbNoDataContract, unsupportedFormats, invalidNoData);
    }

    private static RasterFormatNumericResult CheckNumeric<T>(string path, long[] expectedRaw, double expectedNoData)
        where T : unmanaged, IEquatable<T>
    {
        long[] actualRaw;
        double[] actualDoubleRaw;
        double actualNoData;
        Type actualType;
        int bands;
        int width;
        int height;
        using (var source = new GDALRaster(path))
        {
            actualType = source.GetDataType();
            bands = source.GetBandCount();
            width = source.PixelsWide;
            height = source.PixelsTall;
            ReadOnlySpan<double> noData = source.GetNoDataMemory().Span;
            actualNoData = noData[0];
            actualRaw = source.ReadBand<T>(1)
                .Select(value => Convert.ToInt64(value, CultureInfo.InvariantCulture)).ToArray();
            actualDoubleRaw = source.ReadBand<double>(1);
        }

        Require(actualType == typeof(T), $"{typeof(T).Name} fixture opened as {actualType.Name}.");
        Require(bands == 1 && width == Width && height == Height,
            $"{typeof(T).Name} fixture has unexpected dimensions or band count.");
        Require(actualNoData.Equals(expectedNoData),
            $"{typeof(T).Name} NoData was {actualNoData}, expected {expectedNoData}.");
        if (!actualRaw.SequenceEqual(expectedRaw))
            throw new InvalidDataException(
                $"{typeof(T).Name} typed GDAL RasterIO values differ from the fixture. " +
                $"expectedRaw=[{string.Join(",", expectedRaw)}], " +
                $"actualRaw=[{string.Join(",", actualRaw)}], " +
                $"doubleReadRaw=[{string.Join(",", actualDoubleRaw.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))}].");
        double[] expectedDoubleRaw = expectedRaw.Select(value => (double)value).ToArray();
        if (!actualDoubleRaw.SequenceEqual(expectedDoubleRaw))
            throw new InvalidDataException(
                $"{typeof(T).Name} GDAL double RasterIO values differ from the fixture. " +
                $"expectedRaw=[{string.Join(",", expectedRaw)}], " +
                $"actualRaw=[{string.Join(",", actualRaw)}], " +
                $"doubleReadRaw=[{string.Join(",", actualDoubleRaw.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))}].");

        using MapRasterLayerRendering.IDisposableRenderer opened = MapRasterLayerRendering.Open(
            path, MapRasterLayerRendering.DiscreteMapType, targetProjection: null);
        if (typeof(T) == typeof(uint))
        {
            if (opened.Layer is not UInt32DiscreteRasterRenderLayer unsigned)
                throw new InvalidDataException("UInt32 did not open as a UInt32 discrete raster renderer.");
            uint unsignedNoData = checked((uint)expectedNoData);
            Require(unsigned.NoDataValue == unsignedNoData,
                $"UInt32 renderer NoData was {unsigned.NoDataValue}, expected {unsignedNoData}.");
            uint[] values = expectedRaw.Select(value => checked((uint)value)).ToArray();
            var unsignedColorMap = new ColorMap<uint>(unsignedNoData);
            var unsignedColors = new Dictionary<uint, Color>();
            foreach (uint value in values.Distinct())
            {
                if (value == unsignedNoData) continue;
                Color color = CategoryColor(unsignedColors.Count + 1);
                unsignedColors.Add(value, color);
                unsignedColorMap.SetColor(value, color);
            }
            unsigned.ColorMap = unsignedColorMap;

            MapSourceRasterFrame unsignedFrame = MapSourceRasterRendering.Render(unsigned, NumericViewport);
            AssertFrameExtent(unsignedFrame, NumericViewport, nameof(UInt32));
            byte[] unsignedExpected = ExpectedUInt32CategoryRgba(values, unsignedNoData, unsignedColors);
            Require(unsignedFrame.Rgba.SequenceEqual(unsignedExpected),
                "UInt32 renderer pixels did not preserve exact category values above Int32.MaxValue.");
            string hover = ReadUInt32Hover(unsigned, new Geospatial.Vectors.Point(0.5, 0.5));
            Require(hover == uint.MaxValue.ToString(CultureInfo.InvariantCulture),
                $"UInt32 hover displayed {hover}, expected the complete value {uint.MaxValue}.");
            string[] pixelColors = ToRgbaHex(unsignedFrame.Rgba);
            Require(pixelColors[2] != pixelColors[3] && pixelColors[3] != pixelColors[4],
                "Distinct UInt32 categories around Int32.MaxValue received the same palette color.");

            return new(typeof(T).Name, path, width, height, actualNoData, actualRaw, actualDoubleRaw,
                expectedRaw, pixelColors,
                unsignedColors.Keys.Select(value => (long)value).ToArray(),
                unsignedColors.Values.Select(color => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}").ToArray(),
                hover);
        }

        int[] converted = expectedRaw.Select(SaturatingInt32).ToArray();
        int convertedNoData = SaturatingInt32(checked((long)expectedNoData));
        if (opened.Layer is not DiscreteRasterRenderLayer discrete)
            throw new InvalidDataException($"{typeof(T).Name} did not open as a discrete raster renderer.");
        Require(discrete.NoDataValue == convertedNoData,
            $"{typeof(T).Name} renderer NoData was {discrete.NoDataValue}, expected {convertedNoData}.");

        var colorMap = new ColorMap<int>(convertedNoData);
        var colors = new Dictionary<int, Color>();
        foreach (int value in converted.Distinct())
        {
            if (value == convertedNoData) continue;
            Color color = CategoryColor(colors.Count + 1);
            colors.Add(value, color);
            colorMap.SetColor(value, color);
        }
        discrete.ColorMap = colorMap;

        MapSourceRasterFrame frame = MapSourceRasterRendering.Render(discrete, NumericViewport);
        AssertFrameExtent(frame, NumericViewport, typeof(T).Name);
        byte[] expectedRgba = ExpectedCategoryRgba(converted, convertedNoData, colors);
        Require(frame.Rgba.SequenceEqual(expectedRgba),
            $"{typeof(T).Name} renderer pixels do not match the typed values after int conversion.");

        return new(typeof(T).Name, path, width, height, actualNoData, actualRaw, actualDoubleRaw,
            converted.Select(value => (long)value).ToArray(),
            ToRgbaHex(frame.Rgba));
    }

    private static RasterFormatUInt32ExactResult CheckUInt32NoDataMaximum(
        string numericSourcePath, string path, RasterFormatNumericResult[] numericFormats)
    {
        RasterFormatNumericResult numeric = numericFormats.Single(item => item.DataType == nameof(UInt32));
        Require(Path.GetFullPath(numeric.SourcePath) == Path.GetFullPath(numericSourcePath),
            "The UInt32 primary numeric check did not use the expected source fixture.");
        RasterFormatUInt32MaximumNoDataResult maximumNoData = CheckUInt32MaximumNoData(path);
        uint[] sourceValues = numeric.RawValues.Select(value => checked((uint)value)).ToArray();
        return new(numeric.SourcePath, sourceValues,
            checked((uint)numeric.NoDataValue!.Value),
            numeric.PaletteValues!.Select(value => checked((uint)value)).ToArray(),
            numeric.PaletteColors!, numeric.RenderedRgba, numeric.HighCategoryHoverValue!, maximumNoData);
    }

    private static RasterFormatUInt32MaximumNoDataResult CheckUInt32MaximumNoData(string path)
    {
        uint[] values = [0, int.MaxValue, (uint)int.MaxValue + 1, uint.MaxValue,
            4, 5, 6, 7];
        WriteSingleBand(path, DataType.GDT_UInt32, values, uint.MaxValue);
        using (var source = new GDALRaster(path))
        {
            Require(source.GetDataType() == typeof(uint) && source.GetBandCount() == 1 &&
                    source.PixelsWide == Width && source.PixelsTall == Height,
                "The UInt32 maximum-NoData fixture reopened with unexpected metadata.");
            Require(source.ReadBand<uint>(1).SequenceEqual(values),
                "The UInt32 maximum-NoData fixture did not preserve its exact UInt32 values.");
            Require(source.TryGetNoData<uint>(1, out uint actualNoData) && actualNoData == uint.MaxValue,
                $"The UInt32 maximum-NoData fixture reported {actualNoData}, expected {uint.MaxValue}.");
        }
        using MapRasterLayerRendering.IDisposableRenderer opened = MapRasterLayerRendering.Open(
            path, MapRasterLayerRendering.DiscreteMapType, targetProjection: null);
        if (opened.Layer is not UInt32DiscreteRasterRenderLayer layer)
            throw new InvalidDataException("UInt32 NoData fixture did not open as a UInt32 discrete renderer.");
        Require(layer.NoDataValue == uint.MaxValue,
            $"UInt32 maximum NoData was changed to {layer.NoDataValue}.");

        var colorMap = new ColorMap<uint>(uint.MaxValue);
        var colors = new Dictionary<uint, Color>();
        foreach (uint value in values.Distinct())
        {
            if (value == uint.MaxValue) continue;
            Color color = CategoryColor(colors.Count + 1);
            colors.Add(value, color);
            colorMap.SetColor(value, color);
        }
        layer.ColorMap = colorMap;
        MapSourceRasterFrame frame = MapSourceRasterRendering.Render(layer, NumericViewport);
        AssertFrameExtent(frame, NumericViewport, "UInt32 maximum NoData");
        byte[] expected = ExpectedUInt32CategoryRgba(values, uint.MaxValue, colors);
        Require(frame.Rgba.SequenceEqual(expected),
            "UInt32 maximum NoData obscured a valid UInt32 category or rendered itself as data.");
        string[] pixels = ToRgbaHex(frame.Rgba);
        Require(pixels[3] == "#00000000", "UInt32.MaxValue was not transparent when declared NoData.");
        Require(pixels[0] != "#00000000", "Valid UInt32 zero was incorrectly treated as NoData.");
        Require(pixels[1] != pixels[2],
            "UInt32 values at and above Int32.MaxValue collapsed to one rendered color.");

        return new(path, values, layer.NoDataValue,
            colors.Keys.ToArray(), colors.Values.Select(color => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}").ToArray(),
            pixels,
            pixels[3] == "#00000000", pixels[0] != "#00000000", pixels[1], pixels[2], pixels[1] != pixels[2]);
    }

    private static string ReadUInt32Hover(UInt32DiscreteRasterRenderLayer layer, Geospatial.Vectors.Point point)
    {
        object cache = new object();
        List<MapHover>? hits = layer.MapHover(HitTestArgs.Generate(point, 1d), ref cache);
        return hits?.FirstOrDefault()?.Label ?? string.Empty;
    }

    private static Color CategoryColor(int index) =>
        Color.FromArgb(255, (index * 41 + 17) % 256,
            (index * 73 + 29) % 256, (index * 107 + 43) % 256);

    private static byte[] ExpectedUInt32CategoryRgba(uint[] values, uint noData,
        IReadOnlyDictionary<uint, Color> colors)
    {
        byte[] expected = new byte[values.Length * 4];
        for (int index = 0; index < values.Length; index++)
        {
            if (values[index] == noData) continue;
            if (!colors.TryGetValue(values[index], out Color color))
                throw new InvalidDataException("The expected UInt32 category color is missing.");
            expected[index * 4] = color.R;
            expected[index * 4 + 1] = color.G;
            expected[index * 4 + 2] = color.B;
            expected[index * 4 + 3] = color.A;
        }
        return expected;
    }

    private static RasterFormatRenderResult CheckPalettePixels(string path)
    {
        using (var source = new GDALRaster(path))
        {
            Tuple<byte, byte, byte, byte>[] palette = source.GetColorMap(1)
                ?? throw new InvalidDataException("The palette VRT did not expose a GDAL color table.");
            Require(palette.Length == 256 &&
                    palette[0].Item1 == 200 && palette[0].Item2 == 0 && palette[0].Item3 == 0 && palette[0].Item4 == 255 &&
                    palette[1].Item1 == 0 && palette[1].Item2 == 180 && palette[1].Item3 == 20 && palette[1].Item4 == 255 &&
                    palette[2].Item1 == 20 && palette[2].Item2 == 40 && palette[2].Item3 == 240 && palette[2].Item4 == 128 &&
                    palette[255].Item1 == 255 && palette[255].Item2 == 255 && palette[255].Item3 == 0 && palette[255].Item4 == 255,
                "GDAL did not preserve the VRT palette entries and alpha values used by the pixel assertion.");
        }
        using MapRasterLayerRendering.IDisposableRenderer opened = MapRasterLayerRendering.Open(
            path, MapRasterLayerRendering.DiscreteMapType, targetProjection: null);
        if (opened.Layer is not DiscreteRasterRenderLayer discrete)
            throw new InvalidDataException("The paletted Byte raster did not open as a discrete renderer.");
        Require(discrete.NoDataValue == 255,
            $"The paletted raster renderer NoData was {discrete.NoDataValue}, expected 255.");

        var viewport = new RasterMapViewExtent(0, 0, 4, 1, 4, 1);
        MapSourceRasterFrame frame = MapSourceRasterRendering.Render(discrete, viewport);
        AssertFrameExtent(frame, viewport, "Byte palette");
        byte[] expected =
        [
            200, 0, 0, 255,
            0, 180, 20, 255,
            10, 20, 120, 128,
            0, 0, 0, 0,
        ];
        Require(frame.Rgba.SequenceEqual(expected),
            "The GDAL palette did not render as the expected per-pixel premultiplied RGBA, including transparent NoData.");
        return RenderResult(path, frame);
    }

    private static RasterFormatRenderResult CheckRgbPixels(string path)
    {
        using (var source = new GDALRaster(path))
        {
            Require(source.GetBandCount() == 3 && source.GetDataType() == typeof(byte),
                "The RGB fixture did not reopen as three Byte bands.");
            Require(MapRasterLayerRendering.ResolveMapType(source) == MapRasterLayerRendering.ImageMapType,
                "Three Byte bands were not classified as an image raster map layer.");
        }

        using MapRasterLayerRendering.IDisposableRenderer opened = MapRasterLayerRendering.Open(
            path, MapRasterLayerRendering.ImageMapType, targetProjection: null);
        var viewport = new RasterMapViewExtent(0, 0, Width, Height, Width, Height);
        MapSourceRasterFrame frame = MapSourceRasterRendering.Render(opened.Layer, viewport);
        AssertFrameExtent(frame, viewport, "3-band Byte RGB");
        byte[] expected = new byte[Width * Height * 4];
        for (int index = 0; index < Width * Height; index++)
        {
            expected[index * 4] = (byte)(10 + index * 10);
            expected[index * 4 + 1] = (byte)(90 + index * 10);
            expected[index * 4 + 2] = (byte)(170 + index * 10);
            expected[index * 4 + 3] = 255;
        }
        Require(frame.Rgba.SequenceEqual(expected),
            "The 3-band Byte render changed red/green/blue channel order or row-major pixel order.");
        return RenderResult(path, frame);
    }

    private static RasterFormatRgbaRenderResult CheckRgbaPixels(string path, string output,
        byte[] red, byte[] green, byte[] blue, byte[] alpha)
    {
        using (var source = new GDALRaster(path))
        {
            Require(source.GetBandCount() == 4 && source.GetDataType() == typeof(byte),
                "The RGBA fixture did not reopen as four Byte bands.");
            Require(MapRasterLayerRendering.ResolveMapType(source) == MapRasterLayerRendering.ImageMapType,
                "Four explicit Red/Green/Blue/Alpha Byte bands were not classified as an image raster map layer.");
            Require(source.ReadBand<byte>(1).SequenceEqual(red) && source.ReadBand<byte>(2).SequenceEqual(green) &&
                    source.ReadBand<byte>(3).SequenceEqual(blue) && source.ReadBand<byte>(4).SequenceEqual(alpha),
                "The RGBA GeoTIFF did not preserve its exact 4×2 input channel samples.");
        }
        using (Dataset dataset = Gdal.Open(path, Access.GA_ReadOnly)
            ?? throw new InvalidOperationException("The four-band Byte GeoTIFF could not be reopened."))
        {
            ColorInterp[] expectedInterpretations =
                [ColorInterp.GCI_RedBand, ColorInterp.GCI_GreenBand, ColorInterp.GCI_BlueBand, ColorInterp.GCI_AlphaBand];
            for (int bandIndex = 1; bandIndex <= expectedInterpretations.Length; bandIndex++)
            {
                using Band band = dataset.GetRasterBand(bandIndex);
                Require(band.GetRasterColorInterpretation() == expectedInterpretations[bandIndex - 1],
                    $"RGBA fixture band {bandIndex} has no explicit expected color interpretation.");
            }
        }

        var viewport = new RasterMapViewExtent(0, 0, Width, Height, Width, Height);
        using MapRasterLayerRendering.IDisposableRenderer opened = MapRasterLayerRendering.Open(
            path, MapRasterLayerRendering.ImageMapType, targetProjection: null);
        if (opened.Layer is not ImageRenderLayer image)
            throw new InvalidDataException("Four explicit Byte RGBA bands did not open as an ImageRenderLayer.");
        Require(image.Opacity == byte.MaxValue,
            $"The RGBA renderer's default opacity was {image.Opacity}, expected 255.");
        MapSourceRasterFrame frame = MapSourceRasterRendering.Render(image, viewport);
        AssertFrameExtent(frame, viewport, "4-band Byte RGBA");
        byte[] expectedDefault = ExpectedRgbaFrame(red, green, blue, alpha, byte.MaxValue);
        Require(frame.Rgba.SequenceEqual(expectedDefault),
            "The RGBA render changed channel order, alpha coverage, premultiplication, or row-major pixel order.");
        Require(frame.Rgba.AsSpan(4 * 4, 4).SequenceEqual(new byte[] { 0, 0, 0, 255 }),
            "A valid black RGBA pixel without NoData metadata was hidden.");

        image.Opacity = 128;
        MapSourceRasterFrame opacityFrame = MapSourceRasterRendering.Render(image, viewport);
        AssertFrameExtent(opacityFrame, viewport, "4-band Byte RGBA with layer opacity 128");
        byte[] expectedOpacity = ExpectedRgbaFrame(red, green, blue, alpha, 128);
        Require(opacityFrame.Rgba.SequenceEqual(expectedOpacity),
            "RGBA layer opacity did not multiply source alpha before Host frame premultiplication.");

        using MapRasterLayerRendering.IDisposableRenderer recoloredOpened = MapRasterLayerRendering.Open(
            path, MapRasterLayerRendering.ImageMapType, targetProjection: null);
        if (recoloredOpened.Layer is not ImageRenderLayer recoloredImage)
            throw new InvalidDataException("The RGBA recolor fixture did not open as an ImageRenderLayer.");
        recoloredImage.Recolorer = new FixedPremultipliedRecolorer();
        MapSourceRasterFrame recoloredFrame = MapSourceRasterRendering.Render(recoloredImage, viewport);
        byte[] expectedRecolored = ExpectedPremultipliedRecolorFrame(alpha, byte.MaxValue);
        Require(recoloredFrame.Rgba.SequenceEqual(expectedRecolored),
            "The RGBA recolorer's premultiplied nonblack color was not restored before the Host frame premultiplied it once.");

        string rgbNoDataPath = Path.Combine(output, "rgba-rgb-zero-nodata.vrt");
        WriteRgbaNoDataVrt(rgbNoDataPath, path, [0, 0, 0, null]);
        using (var source = new GDALRaster(rgbNoDataPath))
        {
            double[] noData = source.GetNoDataMemory().ToArray();
            Require(noData.Length == 4 && noData[0] == 0d && noData[1] == 0d && noData[2] == 0d &&
                    double.IsNaN(noData[3]),
                "The RGBA RGB-band NoData VRT did not expose three zero sentinels and no alpha sentinel.");
        }
        using MapRasterLayerRendering.IDisposableRenderer rgbNoDataOpened = MapRasterLayerRendering.Open(
            rgbNoDataPath, MapRasterLayerRendering.ImageMapType, targetProjection: null);
        MapSourceRasterFrame rgbNoDataFrame = MapSourceRasterRendering.Render(rgbNoDataOpened.Layer, viewport);
        byte[] expectedRgbNoData = ExpectedRgbaFrame(red, green, blue, alpha, byte.MaxValue,
            index => red[index] == 0 && green[index] == 0 && blue[index] == 0);
        Require(rgbNoDataFrame.Rgba.SequenceEqual(expectedRgbNoData),
            "Three matching RGB NoData values did not hide their joint sentinel while preserving nonblack pixels with zero channels.");
        Require(rgbNoDataFrame.Rgba.AsSpan(4 * 4, 4).SequenceEqual(new byte[4]),
            "The declared RGB zero NoData pixel was not transparent.");
        Require(rgbNoDataFrame.Rgba.AsSpan(5 * 4, 4).SequenceEqual(new byte[] { 0, 1, 2, 128 }),
            "A valid nonblack RGBA pixel with only its red channel equal to zero was hidden as NoData.");

        string alphaNoDataPath = Path.Combine(output, "rgba-alpha-128-nodata.vrt");
        WriteRgbaNoDataVrt(alphaNoDataPath, path, [null, null, null, 128]);
        using (var source = new GDALRaster(alphaNoDataPath))
        {
            double[] noData = source.GetNoDataMemory().ToArray();
            Require(noData.Length == 4 && double.IsNaN(noData[0]) && double.IsNaN(noData[1]) &&
                    double.IsNaN(noData[2]) && noData[3] == 128d,
                "The RGBA alpha-NoData VRT did not expose NoData=128 only on the alpha band.");
        }
        using MapRasterLayerRendering.IDisposableRenderer alphaNoDataOpened = MapRasterLayerRendering.Open(
            alphaNoDataPath, MapRasterLayerRendering.ImageMapType, targetProjection: null);
        MapSourceRasterFrame alphaNoDataFrame = MapSourceRasterRendering.Render(alphaNoDataOpened.Layer, viewport);
        byte[] expectedAlphaNoData = ExpectedRgbaFrame(red, green, blue, alpha, byte.MaxValue,
            index => alpha[index] == 128);
        Require(alphaNoDataFrame.Rgba.SequenceEqual(expectedAlphaNoData),
            "An alpha-band NoData sample was visible or a valid alpha=255 sample was hidden.");
        Require(alphaNoDataFrame.Rgba.AsSpan(2 * 4, 4).SequenceEqual(new byte[4]) &&
                alphaNoDataFrame.Rgba.AsSpan(3 * 4, 4).SequenceEqual(new byte[] { 100, 150, 200, 255 }),
            "Alpha NoData=128 did not hide its sample while preserving a valid alpha=255 pixel.");

        return new(path, frame.Width, frame.Height, red, green, blue, alpha,
            ToRgbaHex(frame.Rgba), ToRgbaHex(opacityFrame.Rgba), ToRgbaHex(recoloredFrame.Rgba),
            rgbNoDataPath, ToRgbaHex(rgbNoDataFrame.Rgba), alphaNoDataPath, ToRgbaHex(alphaNoDataFrame.Rgba));
    }

    private static byte[] ExpectedRgbaFrame(byte[] red, byte[] green, byte[] blue, byte[] alpha,
        byte opacity, Func<int, bool>? noData = null)
    {
        if (red.Length != green.Length || red.Length != blue.Length || red.Length != alpha.Length)
            throw new ArgumentException("The RGBA fixture bands have different lengths.");
        byte[] expected = new byte[red.Length * 4];
        for (int index = 0; index < red.Length; index++)
        {
            if (noData?.Invoke(index) == true) continue;
            int effectiveAlpha = (alpha[index] * opacity + 127) / byte.MaxValue;
            expected[index * 4] = Premultiply(red[index], effectiveAlpha);
            expected[index * 4 + 1] = Premultiply(green[index], effectiveAlpha);
            expected[index * 4 + 2] = Premultiply(blue[index], effectiveAlpha);
            expected[index * 4 + 3] = (byte)effectiveAlpha;
        }
        return expected;
    }

    private static byte[] ExpectedPremultipliedRecolorFrame(byte[] sourceAlpha, byte opacity)
    {
        byte[] expected = new byte[sourceAlpha.Length * 4];
        for (int index = 0; index < sourceAlpha.Length; index++)
        {
            int coverage = (sourceAlpha[index] * opacity + 127) / byte.MaxValue;
            int effectiveAlpha = (128 * coverage + 127) / byte.MaxValue;
            expected[index * 4] = Premultiply(128, effectiveAlpha);
            expected[index * 4 + 1] = Premultiply(64, effectiveAlpha);
            expected[index * 4 + 2] = Premultiply(32, effectiveAlpha);
            expected[index * 4 + 3] = (byte)effectiveAlpha;
        }
        return expected;
    }

    private static byte Premultiply(byte component, int alpha) =>
        (byte)((component * alpha + 127) / byte.MaxValue);

    private static RasterFormatRgbNoDataResult CheckRgbNoDataConstraints(string rgbPath, string output)
    {
        string commonPath = Path.Combine(output, "rgb-common-nodata.vrt");
        WriteRgbNoDataVrt(commonPath, rgbPath, [255, 255, 255]);
        using (var source = new GDALRaster(commonPath))
        {
            double[] noData = source.GetNoDataMemory().ToArray();
            Require(noData.Length == 3 && noData.All(value => value.Equals(255d)),
                "The common-NoData RGB VRT did not expose the same sentinel on all bands.");
            using MapRasterLayerRendering.IDisposableRenderer opened = MapRasterLayerRendering.Open(
                commonPath, MapRasterLayerRendering.ImageMapType, targetProjection: null);
            Require(opened.Layer is ImageRenderLayer,
                "Three Byte bands with matching NoData did not open as an image renderer.");
            return new(noData, true,
                AssertRgbRejected(output, rgbPath, "rgb-different-band-nodata.vrt", [254, 255, 253],
                    "RGB 各波段 NoData 不一致", typeof(NotSupportedException), expectedNoData: [254, 255, 253]),
                AssertRgbRejected(output, rgbPath, "rgb-fractional-nodata.vrt", [1.5, 1.5, 1.5],
                    "RGB Byte 小数 NoData=1.5", typeof(InvalidDataException), expectedNoData: [1.5, 1.5, 1.5]));
        }
    }

    private static RasterFormatRejectionResult AssertRgbRejected(string output, string rgbPath,
        string fileName, double[] noDataValues, string format, Type expectedException, double[] expectedNoData)
    {
        string path = Path.Combine(output, fileName);
        WriteRgbNoDataVrt(path, rgbPath, noDataValues);
        return AssertRejected(path, format, expectedException,
            mapLayerType: MapRasterLayerRendering.ImageMapType, expectedNoDataValues: expectedNoData);
    }

    private static RasterFormatNoDataResult CheckMissingNoData(string path)
    {
        using (var source = new GDALRaster(path))
        {
            Require(double.IsNaN(source.GetNoDataMemory().Span[0]),
                "The missing-NoData Byte fixture unexpectedly has NoData metadata.");
        }

        using MapRasterLayerRendering.IDisposableRenderer opened = MapRasterLayerRendering.Open(
            path, MapRasterLayerRendering.DiscreteMapType, targetProjection: null);
        if (opened.Layer is not DiscreteRasterRenderLayer discrete)
            throw new InvalidDataException("The missing-NoData Byte raster did not open as a discrete renderer.");
        Require(discrete.NoDataValue == 0,
            $"A missing integer NoData did not retain the original default-zero behavior; got {discrete.NoDataValue}.");

        MapSourceRasterFrame frame = MapSourceRasterRendering.Render(discrete,
            new RasterMapViewExtent(0, 0, 4, 1, 4, 1));
        byte[] zero = frame.Rgba.AsSpan(0, 4).ToArray();
        byte[] nonZero = frame.Rgba.AsSpan(4, 4).ToArray();
        Require(zero.SequenceEqual(new byte[] { 0, 0, 0, 0 }) && nonZero[3] > 0,
            "Missing integer NoData did not make category zero transparent while preserving a valid nonzero category.");
        return new(path, true, discrete.NoDataValue, ToRgbaHex(zero)[0], ToRgbaHex(nonZero)[0]);
    }

    private static RasterFormatRejectionResult AssertRejected(string path, string format,
        Type expectedException, double? expectedNoData = null,
        string? mapLayerType = null, double[]? expectedNoDataValues = null)
    {
        if (expectedNoData is { } expected)
        {
            using var source = new GDALRaster(path);
            double actual = source.GetNoDataMemory().Span[0];
            Require(actual.Equals(expected),
                $"Invalid NoData VRT exposed {actual}; expected the invalid value {expected}.");
        }
        if (expectedNoDataValues is not null)
        {
            using var source = new GDALRaster(path);
            double[] actual = source.GetNoDataMemory().ToArray();
            Require(actual.SequenceEqual(expectedNoDataValues),
                $"{format} exposed different band NoData values than its VRT fixture specified.");
        }

        try
        {
            using MapRasterLayerRendering.IDisposableRenderer _ = MapRasterLayerRendering.Open(
                path, mapLayerType ?? MapRasterLayerRendering.DiscreteMapType, targetProjection: null);
        }
        catch (Exception error) when (expectedException.IsInstanceOfType(error))
        {
            return new(format, expectedException.Name, error.GetType().Name, error.Message);
        }
        catch (Exception error)
        {
            throw new InvalidDataException(
                $"{format} was rejected with {error.GetType().Name}, expected {expectedException.Name}.", error);
        }
        throw new InvalidDataException($"{format} was silently accepted; expected {expectedException.Name}.");
    }

    private static void WriteSingleBand(string path, DataType dataType, Array pixels, double? noData,
        int width = Width, int height = Height)
    {
        EnsureNewFile(path);
        using Driver driver = Gdal.GetDriverByName("GTiff")
            ?? throw new InvalidOperationException("The GDAL GeoTIFF driver is unavailable.");
        using Dataset dataset = driver.Create(path, width, height, 1, dataType, null)
            ?? throw new InvalidOperationException("The GDAL GeoTIFF dataset could not be created.");
        dataset.SetGeoTransform([0, 1, 0, height, 0, -1]);
        using Band band = dataset.GetRasterBand(1);
        if (noData is { } value) band.SetNoDataValue(value);
        WriteBandValues(band, width, height, pixels);
        band.FlushCache();
        dataset.FlushCache();
    }

    private static void WriteMultiBand(string path, DataType dataType, int bandCount)
    {
        EnsureNewFile(path);
        using Driver driver = Gdal.GetDriverByName("GTiff")
            ?? throw new InvalidOperationException("The GDAL GeoTIFF driver is unavailable.");
        using Dataset dataset = driver.Create(path, Width, Height, bandCount, dataType, null)
            ?? throw new InvalidOperationException("The multiband GeoTIFF dataset could not be created.");
        dataset.SetGeoTransform([0, 1, 0, Height, 0, -1]);
        for (int index = 1; index <= bandCount; index++)
        {
            using Band band = dataset.GetRasterBand(index);
            RequireRasterWrite(band.SetRasterColorInterpretation(ColorInterp.GCI_Undefined),
                $"{dataType} undefined color interpretation");
            if (dataType == DataType.GDT_Byte)
                WriteBandValues(band, Width, Height, Enumerable.Range(0, Width * Height)
                    .Select(value => (byte)(value + index)).ToArray());
            else
                WriteBandValues(band, Width, Height, Enumerable.Range(0, Width * Height)
                    .Select(value => (short)(value + index)).ToArray());
        }
        dataset.FlushCache();
    }

    private static void WriteUndefinedColorInterpretationVrt(string vrtPath, string sourcePath, int bandCount)
    {
        EnsureNewFile(vrtPath);
        using (var source = new GDALRaster(sourcePath))
        {
            if (source.GetDataType() != typeof(byte) || source.GetBandCount() != bandCount ||
                source.PixelsWide != Width || source.PixelsTall != Height)
                throw new InvalidDataException("The undefined-color VRT requires a matching Byte multiband source.");
        }

        var vrt = new XElement("VRTDataset",
            new XAttribute("rasterXSize", Width),
            new XAttribute("rasterYSize", Height),
            new XElement("GeoTransform", $"0, 1, 0, {Height}, 0, -1"));
        for (int bandIndex = 1; bandIndex <= bandCount; bandIndex++)
        {
            vrt.Add(new XElement("VRTRasterBand",
                new XAttribute("dataType", "Byte"),
                new XAttribute("band", bandIndex),
                new XElement("ColorInterp", "Undefined"),
                new XElement("SimpleSource",
                    new XElement("SourceFilename", new XAttribute("relativeToVRT", 1), Path.GetFileName(sourcePath)),
                    new XElement("SourceBand", bandIndex),
                    new XElement("SrcRect", new XAttribute("xOff", 0), new XAttribute("yOff", 0),
                        new XAttribute("xSize", Width), new XAttribute("ySize", Height)),
                    new XElement("DstRect", new XAttribute("xOff", 0), new XAttribute("yOff", 0),
                        new XAttribute("xSize", Width), new XAttribute("ySize", Height)))));
        }
        new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), vrt)
            .Save(vrtPath, SaveOptions.DisableFormatting);
    }

    private static void WritePaletteRaster(string path)
    {
        string dataPath = Path.Combine(Path.GetDirectoryName(path)!, "byte-palette-data.tif");
        WriteSingleBand(dataPath, DataType.GDT_Byte, new byte[] { 0, 1, 2, 255 }, 255,
            width: 4, height: 1);
        WritePaletteVrt(path, dataPath);
    }

    private static void WriteRgbRaster(string path)
    {
        EnsureNewFile(path);
        using Driver driver = Gdal.GetDriverByName("GTiff")
            ?? throw new InvalidOperationException("The GDAL GeoTIFF driver is unavailable.");
        using Dataset dataset = driver.Create(path, Width, Height, 3, DataType.GDT_Byte, null)
            ?? throw new InvalidOperationException("The 3-band Byte GeoTIFF dataset could not be created.");
        dataset.SetGeoTransform([0, 1, 0, Height, 0, -1]);
        for (int bandIndex = 0; bandIndex < 3; bandIndex++)
        {
            using Band band = dataset.GetRasterBand(bandIndex + 1);
            byte[] values = Enumerable.Range(0, Width * Height)
                .Select(pixel => checked((byte)((10 + bandIndex * 80) + pixel * 10))).ToArray();
            WriteBandValues(band, Width, Height, values);
        }
        dataset.FlushCache();
    }

    private static void WriteRgbaRaster(string path, byte[] red, byte[] green, byte[] blue, byte[] alpha)
    {
        EnsureNewFile(path);
        if (new[] { red.Length, green.Length, blue.Length, alpha.Length }.Any(length => length != Width * Height))
            throw new ArgumentException("The RGBA fixture must contain exactly four 4×2 bands.");
        using Driver driver = Gdal.GetDriverByName("GTiff")
            ?? throw new InvalidOperationException("The GDAL GeoTIFF driver is unavailable.");
        using Dataset dataset = driver.Create(path, Width, Height, 4, DataType.GDT_Byte,
            ["PHOTOMETRIC=RGB", "ALPHA=YES"])
            ?? throw new InvalidOperationException("The 4-band RGBA GeoTIFF dataset could not be created.");
        dataset.SetGeoTransform([0, 1, 0, Height, 0, -1]);
        ColorInterp[] interpretations =
            [ColorInterp.GCI_RedBand, ColorInterp.GCI_GreenBand, ColorInterp.GCI_BlueBand, ColorInterp.GCI_AlphaBand];
        byte[][] values = [red, green, blue, alpha];
        for (int index = 0; index < interpretations.Length; index++)
        {
            using Band band = dataset.GetRasterBand(index + 1);
            RequireRasterWrite(band.SetRasterColorInterpretation(interpretations[index]),
                $"RGBA band {index + 1} color interpretation");
            WriteBandValues(band, Width, Height, values[index]);
            band.FlushCache();
        }
        dataset.FlushCache();
    }

    private static void WriteNoDataOverrideVrt(string vrtPath, string sourcePath, double noData)
    {
        EnsureNewFile(vrtPath);
        string typeName;
        using (var source = new GDALRaster(sourcePath))
        {
            if (source.GetDataType() != typeof(short) || source.GetBandCount() != 1)
                throw new InvalidDataException("Invalid-NoData VRT source must be a single-band Int16 GeoTIFF.");
            typeName = "Int16";
        }

        var vrt = new XElement("VRTDataset",
            new XAttribute("rasterXSize", Width),
            new XAttribute("rasterYSize", Height),
            new XElement("GeoTransform", "0, 1, 0, 2, 0, -1"),
            new XElement("VRTRasterBand",
                new XAttribute("dataType", typeName),
                new XAttribute("band", 1),
                new XElement("NoDataValue", noData.ToString("R", CultureInfo.InvariantCulture)),
                new XElement("SimpleSource",
                    new XElement("SourceFilename", new XAttribute("relativeToVRT", 1), Path.GetFileName(sourcePath)),
                    new XElement("SourceBand", 1),
                    new XElement("SrcRect", new XAttribute("xOff", 0), new XAttribute("yOff", 0),
                        new XAttribute("xSize", Width), new XAttribute("ySize", Height)),
                    new XElement("DstRect", new XAttribute("xOff", 0), new XAttribute("yOff", 0),
                        new XAttribute("xSize", Width), new XAttribute("ySize", Height)))));
        new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), vrt)
            .Save(vrtPath, SaveOptions.DisableFormatting);
    }

    private static void WritePaletteVrt(string vrtPath, string sourcePath)
    {
        EnsureNewFile(vrtPath);
        var entries = new XElement("ColorTable");
        for (int index = 0; index < 256; index++)
        {
            (int red, int green, int blue, int alpha) = index switch
            {
                0 => (200, 0, 0, 255),
                1 => (0, 180, 20, 255),
                2 => (20, 40, 240, 128),
                255 => (255, 255, 0, 255),
                _ => (0, 0, 0, 255),
            };
            entries.Add(new XElement("Entry", new XAttribute("c1", red), new XAttribute("c2", green),
                new XAttribute("c3", blue), new XAttribute("c4", alpha)));
        }
        var vrt = new XElement("VRTDataset",
            new XAttribute("rasterXSize", 4), new XAttribute("rasterYSize", 1),
            new XElement("GeoTransform", "0, 1, 0, 1, 0, -1"),
            new XElement("VRTRasterBand",
                new XAttribute("dataType", "Byte"), new XAttribute("band", 1),
                new XElement("NoDataValue", 255),
                new XElement("ColorInterp", "Palette"),
                entries,
                new XElement("SimpleSource",
                    new XElement("SourceFilename", new XAttribute("relativeToVRT", 1), Path.GetFileName(sourcePath)),
                    new XElement("SourceBand", 1),
                    new XElement("SrcRect", new XAttribute("xOff", 0), new XAttribute("yOff", 0),
                        new XAttribute("xSize", 4), new XAttribute("ySize", 1)),
                    new XElement("DstRect", new XAttribute("xOff", 0), new XAttribute("yOff", 0),
                        new XAttribute("xSize", 4), new XAttribute("ySize", 1)))));
        new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), vrt)
            .Save(vrtPath, SaveOptions.DisableFormatting);
    }

    private static void WriteRgbNoDataVrt(string vrtPath, string sourcePath, double[] bandNoData)
    {
        EnsureNewFile(vrtPath);
        if (bandNoData.Length != 3)
            throw new ArgumentException("RGB NoData VRT requires exactly three band values.", nameof(bandNoData));
        var vrt = new XElement("VRTDataset",
            new XAttribute("rasterXSize", Width), new XAttribute("rasterYSize", Height),
            new XElement("GeoTransform", "0, 1, 0, 2, 0, -1"));
        for (int bandIndex = 1; bandIndex <= 3; bandIndex++)
        {
            var band = new XElement("VRTRasterBand",
                new XAttribute("dataType", "Byte"), new XAttribute("band", bandIndex),
                new XElement("NoDataValue", bandNoData[bandIndex - 1].ToString("R", CultureInfo.InvariantCulture)),
                new XElement("ColorInterp", bandIndex switch { 1 => "Red", 2 => "Green", _ => "Blue" }),
                new XElement("SimpleSource",
                    new XElement("SourceFilename", new XAttribute("relativeToVRT", 1), Path.GetFileName(sourcePath)),
                    new XElement("SourceBand", bandIndex),
                    new XElement("SrcRect", new XAttribute("xOff", 0), new XAttribute("yOff", 0),
                        new XAttribute("xSize", Width), new XAttribute("ySize", Height)),
                    new XElement("DstRect", new XAttribute("xOff", 0), new XAttribute("yOff", 0),
                        new XAttribute("xSize", Width), new XAttribute("ySize", Height))));
            vrt.Add(band);
        }
        new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), vrt)
            .Save(vrtPath, SaveOptions.DisableFormatting);
    }

    private static void WriteRgbaNoDataVrt(string vrtPath, string sourcePath, double?[] bandNoData)
    {
        EnsureNewFile(vrtPath);
        if (bandNoData.Length != 4)
            throw new ArgumentException("RGBA NoData VRT requires exactly four band values.", nameof(bandNoData));
        string[] colorNames = ["Red", "Green", "Blue", "Alpha"];
        var vrt = new XElement("VRTDataset",
            new XAttribute("rasterXSize", Width),
            new XAttribute("rasterYSize", Height),
            new XElement("GeoTransform", "0, 1, 0, 2, 0, -1"));
        for (int index = 0; index < bandNoData.Length; index++)
        {
            var band = new XElement("VRTRasterBand",
                new XAttribute("dataType", "Byte"),
                new XAttribute("band", index + 1));
            if (bandNoData[index] is { } noData)
                band.Add(new XElement("NoDataValue", noData.ToString("R", CultureInfo.InvariantCulture)));
            band.Add(new XElement("ColorInterp", colorNames[index]));
            band.Add(new XElement("SimpleSource",
                new XElement("SourceFilename", new XAttribute("relativeToVRT", 1), Path.GetFileName(sourcePath)),
                new XElement("SourceBand", index + 1),
                new XElement("SrcRect", new XAttribute("xOff", 0), new XAttribute("yOff", 0),
                    new XAttribute("xSize", Width), new XAttribute("ySize", Height)),
                new XElement("DstRect", new XAttribute("xOff", 0), new XAttribute("yOff", 0),
                    new XAttribute("xSize", Width), new XAttribute("ySize", Height))));
            vrt.Add(band);
        }
        new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), vrt)
            .Save(vrtPath, SaveOptions.DisableFormatting);
    }

    private static void WriteBandValues(Band band, int width, int height, Array pixels)
    {
        // CLR primitive arrays with signed/unsigned element pairs can satisfy
        // each other's type patterns. Require exact runtime types so UInt16/32
        // high bits are never reinterpreted as negative signed samples.
        switch (pixels)
        {
            case byte[] values when pixels.GetType() == typeof(byte[]):
                RequireRasterWrite(band.WriteRaster(0, 0, width, height, values, width, height, 0, 0), "Byte");
                break;
            case short[] values when pixels.GetType() == typeof(short[]):
                RequireRasterWrite(band.WriteRaster(0, 0, width, height, values, width, height, 0, 0), "Int16");
                break;
            case ushort[] values when pixels.GetType() == typeof(ushort[]):
                WriteUnsignedBand(band, width, height, values, DataType.GDT_UInt16);
                break;
            case int[] values when pixels.GetType() == typeof(int[]):
                RequireRasterWrite(band.WriteRaster(0, 0, width, height, values, width, height, 0, 0), "Int32");
                break;
            case uint[] values when pixels.GetType() == typeof(uint[]):
                WriteUnsignedBand(band, width, height, values, DataType.GDT_UInt32);
                break;
            default:
                throw new ArgumentException($"Unsupported fixture sample array {pixels.GetType().Name}.", nameof(pixels));
        }
    }

    private static void WriteUnsignedBand(Band band, int width, int height, Array pixels, DataType dataType)
    {
        // The C# binding has no unsigned array overload. Pin the actual unsigned
        // buffer with its exact GDAL type, avoiding an unrelated numeric conversion
        // while creating the known-value reader fixture.
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            RequireRasterWrite(band.WriteRaster(0, 0, width, height, handle.AddrOfPinnedObject(),
                width, height, dataType, 0, 0), dataType.ToString());
        }
        finally { handle.Free(); }
    }

    private static void RequireRasterWrite(CPLErr result, string dataType)
    {
        if (result != CPLErr.CE_None)
            throw new IOException($"GDAL RasterIO failed while writing the {dataType} fixture (code {(int)result}).");
    }

    private static byte[] ExpectedCategoryRgba(int[] values, int noData, IReadOnlyDictionary<int, Color> colors)
    {
        byte[] expected = new byte[values.Length * 4];
        for (int index = 0; index < values.Length; index++)
        {
            if (values[index] == noData) continue;
            if (!colors.TryGetValue(values[index], out Color color))
                throw new InvalidDataException("The expected category color is missing.");
            expected[index * 4] = color.R;
            expected[index * 4 + 1] = color.G;
            expected[index * 4 + 2] = color.B;
            expected[index * 4 + 3] = color.A;
        }
        return expected;
    }

    private static void AssertFrameExtent(MapSourceRasterFrame frame, RasterMapViewExtent expected, string label)
    {
        Require(frame.Width == expected.PixelWidth && frame.Height == expected.PixelHeight &&
                frame.Rgba.Length == expected.PixelWidth * expected.PixelHeight * 4,
            $"{label} render returned an unexpected frame size or RGBA buffer length.");
        Require(frame.MinX.Equals(expected.MinX) && frame.MinY.Equals(expected.MinY) &&
                frame.MaxX.Equals(expected.MaxX) && frame.MaxY.Equals(expected.MaxY),
            $"{label} render returned a different map extent.");
    }

    private static RasterFormatRenderResult RenderResult(string path, MapSourceRasterFrame frame) =>
        new(path, frame.Width, frame.Height, frame.MinX, frame.MinY, frame.MaxX, frame.MaxY,
            ToRgbaHex(frame.Rgba));

    private static string[] ToRgbaHex(ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length % 4 != 0)
            throw new InvalidDataException("RGBA output length is not divisible by four.");
        string[] pixels = new string[rgba.Length / 4];
        for (int index = 0; index < pixels.Length; index++)
            pixels[index] = $"#{rgba[index * 4]:X2}{rgba[index * 4 + 1]:X2}{rgba[index * 4 + 2]:X2}{rgba[index * 4 + 3]:X2}";
        return pixels;
    }

    private sealed class FixedPremultipliedRecolorer : IRecolorer
    {
        public void RecolorARGB(Memory<int> input, Memory<int> output)
        {
            if (input.Length != output.Length)
                throw new ArgumentException("The RGBA test recolorer requires equal input and output lengths.");
            output.Span.Fill(unchecked((int)0x80402010u));
        }

        public void RecolorBrightness(Memory<byte> input, Memory<int> output) =>
            throw new NotSupportedException("The RGBA fixture does not exercise brightness recoloring.");
    }

    private static int SaturatingInt32(long value) =>
        value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

    private static void EnsureNewFile(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException("Raster format acceptance never overwrites an input or existing output: " + path);
    }

    private static void EnsureOutsideRepository(string output)
    {
        string? repository = FindRepositoryRoot();
        if (repository is not null && IsSameOrDescendant(output, repository))
            throw new ArgumentException("Raster format acceptance output must be outside the product repository.", nameof(output));
    }

    private static string? FindRepositoryRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? current = new(Path.GetFullPath(start));
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "backend", "Host", "Ras.Backend.Host.csproj")))
                    return current.FullName;
                current = current.Parent;
            }
        }
        return null;
    }

    private static bool IsSameOrDescendant(string path, string root)
    {
        string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return normalizedPath.Equals(normalizedRoot, comparison) ||
               normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
