using System.Drawing;
using System.Numerics;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Transformers;
using Geospatial.Vectors;
using OSGeo.GDAL;
using Utility.Memory;

namespace Ras.Backend;

/// <summary>Constructs the same three source renderer families as the retained importer.</summary>
internal static class MapRasterLayerRendering
{
    internal const string SingleBandMapType = "SingleBandRasterMapLayer";
    internal const string ImageMapType = "ImageRasterMapLayer";
    internal const string DiscreteMapType = "DiscreteRasterMapLayer";

    internal static string[] SupportedTypes { get; } = [SingleBandMapType, ImageMapType, DiscreteMapType];

    internal static string ResolveMapType(GDALRaster raster)
    {
        Type? dataType = raster.GetDataType();
        int bands = raster.GetBandCount();
        if (bands == 1 && (Equivalent(dataType, typeof(float)) || Equivalent(dataType, typeof(double))))
            return SingleBandMapType;
        if (bands == 3 && AreAllBandsByte(raster)) return ImageMapType;
        if (bands == 4 && AreAllBandsByte(raster) && HasRgbaBandOrder(raster)) return ImageMapType;
        if (bands == 1 && (Equivalent(dataType, typeof(int)) || Equivalent(dataType, typeof(uint)) ||
                            Equivalent(dataType, typeof(short)) || Equivalent(dataType, typeof(ushort)) ||
                            Equivalent(dataType, typeof(byte)))) return DiscreteMapType;
        throw new NotSupportedException("The GDAL raster is readable, but its band count and cell type do not match a supported source map layer.");
    }

    private static bool AreAllBandsByte(GDALRaster raster)
    {
        string[] bandTypes = raster.BandDataType_Names();
        for (int band = 0; band < bandTypes.Length; band++)
        {
            if (!string.Equals(bandTypes[band], DataType.GDT_Byte.ToString(), StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static bool HasRgbaBandOrder(GDALRaster raster)
    {
        ColorInterp[] roles = Enumerable.Range(1, 4).Select(raster.GetBandColorInterpretation).ToArray();
        if (roles.SequenceEqual(new[] { ColorInterp.GCI_RedBand, ColorInterp.GCI_GreenBand,
                ColorInterp.GCI_BlueBand, ColorInterp.GCI_AlphaBand })) return true;
        // OpenFileGDB can omit color roles. For its four-Byte image contexts
        // this importer explicitly uses stored order R/G/B/A, extending the
        // existing three-Byte RGB convention. This is an import policy, not
        // evidence that arbitrary four-band scientific data is inherently RGBA.
        return raster.DriverShortName == "OpenFileGDB" &&
               roles.All(role => role == ColorInterp.GCI_Undefined);
    }

    private static byte?[] ReadImageNoData(GDALRaster raster) =>
        Enumerable.Range(1, raster.GetBandCount()).Select(band =>
            raster.TryGetNoData<byte>(band, out byte value) ? (byte?)value : null).ToArray();

    private static void ValidateImageNoData(GDALRaster raster)
    {
        bool firstBandHasNoData = raster.TryGetNoData<byte>(1, out byte firstBandNoData);
        for (int band = 2; band <= raster.GetBandCount(); band++)
        {
            bool bandHasNoData = raster.TryGetNoData<byte>(band, out byte bandNoData);
            if (bandHasNoData != firstBandHasNoData || (bandHasNoData && bandNoData != firstBandNoData))
                throw new NotSupportedException("The RGB raster has different NoData values across bands, which the current image renderer cannot represent.");
        }
    }

    private static void ValidateDiscreteNoData(GDALRaster raster, Type dataType)
    {
        if (Equivalent(dataType, typeof(int)))
            raster.TryGetNoData<int>(1, out _);
        else if (Equivalent(dataType, typeof(uint)))
            raster.TryGetNoData<uint>(1, out _);
        else if (Equivalent(dataType, typeof(short)))
            raster.TryGetNoData<short>(1, out _);
        else if (Equivalent(dataType, typeof(ushort)))
            raster.TryGetNoData<ushort>(1, out _);
        else if (Equivalent(dataType, typeof(byte)))
            raster.TryGetNoData<byte>(1, out _);
    }

    internal static IDisposableRenderer Open(string sourcePath, string mapLayerType, Projection? targetProjection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string actualType;
        using (var metadata = new GDALRaster(sourcePath)) actualType = ResolveMapType(metadata);
        if (!string.Equals(actualType, mapLayerType, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        return mapLayerType switch
        {
            SingleBandMapType => OpenSingleBand(sourcePath, targetProjection),
            ImageMapType => OpenImage(sourcePath, targetProjection),
            DiscreteMapType => OpenDiscrete(sourcePath, targetProjection),
            _ => throw new NotSupportedException("The selected raster map type is not supported.")
        };
    }

    internal sealed class IDisposableRenderer : IDisposable
    {
        public Geospatial.Rendering.Layers.IRenderLayer Layer { get; }
        private readonly IDisposable? cleanup;
        internal IDisposableRenderer(Geospatial.Rendering.Layers.IRenderLayer layer, IDisposable? cleanup = null)
        { Layer = layer; this.cleanup = cleanup; }
        public void Dispose() => cleanup?.Dispose();
    }

    private static IDisposableRenderer OpenSingleBand(string path, Projection? targetProjection)
    {
        using var metadata = new GDALRaster(path);
        Type dataType = metadata.GetDataType();
        IResample<float>? resampler;
        IDisposable? cleanup;
        if (Equivalent(dataType, typeof(float)))
        {
            resampler = RasterReader.TryGetBestRasterizer<float>(path, out _, out cleanup);
        }
        else
        {
            IResample<double>? doubleResampler = RasterReader.TryGetBestRasterizer<double>(path,
                out _, out cleanup);
            if (doubleResampler is null)
                throw new InvalidDataException("The retained GDAL double-precision raster reader could not open the selected source.");
            resampler = doubleResampler.ConvertType(ConvertTypeResamplers.DoubleToFloatConverter,
                (float?)doubleResampler.NoDataValue);
        }
        if (resampler is null)
        {
            cleanup?.Dispose();
            throw new InvalidDataException("The retained GDAL raster reader could not open the selected source.");
        }
        try
        {
            Projection? sourceProjection = metadata.GetProjection();
            if (targetProjection is not null && sourceProjection is not null && !targetProjection.IsEqual(sourceProjection))
                resampler = resampler.WithTransformer(new ReprojectionTransformer(sourceProjection, targetProjection));
            var layer = new RasterRenderLayer(resampler);
            bool canResampleEfficiently = metadata.GetPyramidDefinition().CanResampleEfficiently(
                RasterLayerSymbologyOptions.Default().MaxBytesToSample / 4);
            if (canResampleEfficiently) layer.SetDefaultTerrainHistoSymbology();
            else layer.SetGrayscaleLinearSymbology(0d, 100d);
            return new IDisposableRenderer(layer, cleanup);
        }
        catch
        {
            cleanup?.Dispose();
            throw;
        }
    }

    private static IDisposableRenderer OpenImage(string path, Projection? targetProjection)
    {
        using var metadata = new GDALRaster(path);
        int bands = metadata.GetBandCount();
        byte?[]? rgbaNoData = bands == 4 ? ReadImageNoData(metadata) : null;
        if (bands == 3) ValidateImageNoData(metadata);
        Projection? sourceProjection = metadata.GetProjection();
        GdalBandedRaster<byte> source = GdalBandedRaster<byte>.Pooled(path, out Pool<GDALRaster> pool);
        source.AllowParallelResampling = false;
        try
        {
            IMultiBandResampler<byte> resampler = source;
            if (targetProjection is not null && sourceProjection is not null && !targetProjection.IsEqual(sourceProjection))
                resampler = resampler.WithTransformer(new ReprojectionTransformer(sourceProjection, targetProjection));
            ImageRenderLayer layer = rgbaNoData is null
                ? new ImageRenderLayer(resampler) : new ImageRenderLayer(resampler, rgbaNoData);
            return new IDisposableRenderer(layer, new RasterPoolCleanup(pool));
        }
        catch
        {
            pool.ClearAll(raster => raster.Dispose());
            throw;
        }
    }

    private static IDisposableRenderer OpenDiscrete(string path, Projection? targetProjection)
    {
        using var metadata = new GDALRaster(path);
        Projection? sourceProjection = metadata.GetProjection();
        Type dataType = metadata.GetDataType();
        Tuple<byte, byte, byte, byte>[]? colors = metadata.GetColorMap(1);
        IDisposable? cleanup = null;
        try
        {
            ValidateDiscreteNoData(metadata, dataType);
            if (Equivalent(dataType, typeof(uint)))
            {
                IResample<uint> typed = OpenDiscreteSource<uint>(path, sourceProjection, targetProjection, out cleanup);
                var unsigned = new UInt32DiscreteRasterRenderLayer(typed);
                if (colors is not null)
                {
                    for (int value = 0; value < colors.Length; value++)
                    {
                        uint category = checked((uint)value);
                        if (category == unsigned.NoDataValue) continue;
                        Tuple<byte, byte, byte, byte> color = colors[value];
                        unsigned.ColorMap.SetColor(category,
                            Color.FromArgb(color.Item4, color.Item1, color.Item2, color.Item3));
                    }
                }
                return new IDisposableRenderer(unsigned, cleanup);
            }
            DiscreteRasterRenderLayer layer;
            if (Equivalent(dataType, typeof(int)))
            {
                IResample<int> resampler = OpenDiscreteSource<int>(path, sourceProjection, targetProjection, out cleanup);
                layer = new DiscreteRasterRenderLayer(resampler);
            }
            else if (Equivalent(dataType, typeof(short)))
            {
                IResample<short> typed = OpenDiscreteSource<short>(path, sourceProjection, targetProjection, out cleanup);
                IResample<int> resampler = ConvertDiscrete(typed);
                layer = new DiscreteRasterRenderLayer(resampler);
            }
            else if (Equivalent(dataType, typeof(ushort)))
            {
                IResample<ushort> typed = OpenDiscreteSource<ushort>(path, sourceProjection, targetProjection, out cleanup);
                IResample<int> resampler = ConvertDiscrete(typed);
                layer = new DiscreteRasterRenderLayer(resampler);
            }
            else if (Equivalent(dataType, typeof(byte)))
            {
                IResample<byte> typed = OpenDiscreteSource<byte>(path, sourceProjection, targetProjection, out cleanup);
                IResample<int> resampler = ConvertDiscrete(typed);
                layer = new DiscreteRasterRenderLayer(resampler);
            }
            else throw new NotSupportedException("The discrete raster renderer supports one byte, Int16, UInt16, Int32, or UInt32 band.");

            int noDataValue = layer.NoDataValue;

            if (colors is not null)
            {
                for (int value = 0; value < colors.Length; value++)
                {
                    if (value == noDataValue) continue;
                    Tuple<byte, byte, byte, byte> color = colors[value];
                    layer.ColorMap.SetColor(value, Color.FromArgb(color.Item4, color.Item1, color.Item2, color.Item3));
                }
            }
            return new IDisposableRenderer(layer, cleanup);
        }
        catch
        {
            cleanup?.Dispose();
            throw;
        }
    }

    private static IResample<T> OpenDiscreteSource<T>(string path, Projection? sourceProjection,
        Projection? targetProjection, out IDisposable? cleanup) where T : unmanaged, IEquatable<T>
    {
        // Preserve the source context's fast TIFF reader where it can open the
        // dataset, and keep a GDAL resampler fallback for every other driver.
        TiffDataSource<T>? tiff = TiffDataSource<T>.TryLoad(path);
        cleanup = tiff;
        IResample<T> resampler = tiff is not null
            ? tiff.AsRasterizer()
            : new GdalBandedRaster<T>(path) { AllowParallelResampling = false }.AsSingleProfile(0);
        if (targetProjection is not null && sourceProjection is not null && !targetProjection.IsEqual(sourceProjection))
            resampler = resampler.WithTransformer(new ReprojectionTransformer(sourceProjection, targetProjection));
        return resampler;
    }

    private static IResample<int> ConvertDiscrete<T>(IResample<T> source) where T : unmanaged, INumber<T>
    {
        // Byte/Int16/UInt16 widen exactly into Int32. Missing source NoData
        // preserves the original default-zero classification sentinel. UInt32
        // uses its own typed renderer and never passes through this conversion.
        int noData = int.CreateSaturating(source.NoDataValue.GetValueOrDefault());
        return new ConvertTypeResampler<T, int>(source, (Memory<T> input, Memory<int> output) =>
        {
            Span<T> inputSpan = input.Span;
            Span<int> outputSpan = output.Span;
            for (int index = 0; index < outputSpan.Length; index++)
                outputSpan[index] = int.CreateSaturating(inputSpan[index]);
        }, noData);
    }

    private static bool Equivalent(Type? candidate, Type expected) =>
        candidate is not null && candidate.IsEquivalentTo(expected);

    private sealed class RasterPoolCleanup : IDisposable
    {
        private readonly Pool<GDALRaster> pool;
        internal RasterPoolCleanup(Pool<GDALRaster> pool) => this.pool = pool;
        public void Dispose() => pool.ClearAll(raster => raster.Dispose());
    }
}
