using System.Reflection;
using System.Security.Cryptography;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Systems;
using Geospatial.Vectors;
using Utility.Systems;

namespace Ras.Backend;

/// <summary>
/// Reconstructs the real source TIN from its exact stream record and invokes the
/// retained C# BandedTin renderer. No geometry or color is synthesized here.
/// </summary>
public static class StreamModeTinRenderingService
{
    private const int MaximumViewportDimension = 4096;
    private const int MaximumViewportPixels = 4_194_304;
    private const int MaximumLabels = 50_000;
    private const int MaximumProfilesPerStream = 512;

    private static readonly IReadOnlyDictionary<string, Type> SupportedTinTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [typeof(Tin).AssemblyQualifiedName
                ?? throw new InvalidOperationException("The retained Tin type has no assembly-qualified name.")] = typeof(Tin)
        };

    public static async Task<StreamModeTinOptionsResult> LoadOptionsAsync(
        StreamModeTinOptionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        BandedTinRenderLayer layer = await LoadLayerAsync(request.Assets,
            request.SelectedProfileIndex, [], cancellationToken)
            .ConfigureAwait(false);
        return new StreamModeTinOptionsResult(request.Assets, request.SelectedProfileIndex,
            DescribeOptions(layer));
    }

    public static async Task<StreamModeTinRenderResult> RenderAsync(
        StreamModeTinRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestID == Guid.Empty) throw new ArgumentException("A render request identity is required.");
        ValidateViewport(request.Viewport);
        BandedTinRenderLayer layer = await LoadLayerAsync(request.Assets, request.SelectedProfileIndex,
            request.Options, cancellationToken)
            .ConfigureAwait(false);

        var extent = new Extent(request.Viewport.MinX, request.Viewport.MaxX,
            request.Viewport.MinY, request.Viewport.MaxY);
        var definition = new RasterDefinition(extent, request.Viewport.PixelHeight, request.Viewport.PixelWidth);
        using PlotRequest plot = layer.Render(definition);
        RasterDefinition raster = plot.RasterQuery;
        int width = raster.Cols;
        int height = raster.Rows;
        if (width <= 0 || height <= 0 || (long)width * height > MaximumViewportPixels)
            throw new InvalidDataException("The source renderer returned an invalid viewport raster.");

        byte[] rgba = ToStraightAlphaRgba(plot.ColorBufferARGB, width, height);

        ILabelDefinition[] sourceLabels = plot.Labels?.ToArray() ?? [];
        if (sourceLabels.Length > MaximumLabels)
            throw new InvalidDataException("The source renderer returned too many labels for one viewport.");
        var labels = new StreamModeTinLabel[sourceLabels.Length];
        for (int index = 0; index < sourceLabels.Length; index++)
        {
            ILabelDefinition label = sourceLabels[index];
            if (!double.IsFinite(label.PointLocation.X) || !double.IsFinite(label.PointLocation.Y))
                throw new InvalidDataException("The source renderer returned a label with a non-finite location.");
            labels[index] = new StreamModeTinLabel(label.Text, label.PointLocation.X,
                label.PointLocation.Y, label is LabelDefinitionBase labelBase
                    ? labelBase.PointLocationRotation : 0);
        }

        return new StreamModeTinRenderResult(request.Assets, request.SelectedProfileIndex,
            request.RequestID, width, height,
            raster.MinX, raster.MinY, raster.MaxX, raster.MaxY,
            Convert.ToBase64String(rgba), labels);
    }

    public static StreamModeTinAssetReleaseResult Release(StreamModeTinAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new StreamModeTinAssetReleaseResult(StreamModeTinAssetStore.Release(request.Asset));
    }

    public static StreamModeTinSessionReleaseResult ReleaseSession(
        StreamModeTinSessionReleaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SessionID == Guid.Empty)
            throw new ArgumentException("A streamed TIN session identity is required.", nameof(request));
        StreamModeTinAssetStore.ReleaseSession(request.SessionID);
        return new StreamModeTinSessionReleaseResult(Released: true);
    }

    private static async Task<BandedTinRenderLayer> LoadLayerAsync(
        StreamModeTinAssetReference[] assets,
        int selectedProfileIndex,
        IReadOnlyList<StreamModeTinOptionAssignment> assignments,
        CancellationToken cancellationToken)
    {
        ValidateProfileGroup(assets, selectedProfileIndex);
        ArgumentNullException.ThrowIfNull(assignments);
        var layer = new BandedTinRenderLayer();
        foreach (StreamModeTinAssetReference asset in assets)
        {
            byte[] rawRecord = await StreamModeTinAssetStore.ReadAsync(asset, cancellationToken)
                .ConfigureAwait(false);
            using var stream = new MemoryStream(rawRecord, writable: false);
            IStreamableObject? source = await StreamableObjectHelper.ReadAsync(stream, SupportedTinTypes,
                cancellationToken).ConfigureAwait(false);
            if (source is not Tin tin || stream.Position != stream.Length)
                throw new InvalidDataException("The cached TIN asset is not one complete source Tin record.");
            layer.AddTin(tin);
        }
        if (layer.ProfileCount != assets.Length)
            throw new InvalidDataException("The streamed TIN profile group is incomplete.");
        layer.ProfileState.CurrentProfileIndex = selectedProfileIndex;
        ApplyAssignments(layer, assignments);
        return layer;
    }

    private static void ValidateProfileGroup(StreamModeTinAssetReference[] assets, int selectedProfileIndex)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (assets.Length == 0 || assets.Length > MaximumProfilesPerStream ||
            selectedProfileIndex < 0 || selectedProfileIndex >= assets.Length)
            throw new ArgumentException("The streamed TIN profile group or selected profile is invalid.");
        Guid sessionID = assets[0]?.SessionID ?? Guid.Empty;
        long priorSequence = -1;
        var assetIDs = new HashSet<Guid>();
        foreach (StreamModeTinAssetReference asset in assets)
        {
            StreamModeTinAssetStore.ValidateReference(asset);
            if (asset.SessionID != sessionID || asset.Sequence <= priorSequence ||
                !assetIDs.Add(asset.AssetID))
                throw new ArgumentException("TIN profiles must be unique source records in stream order.");
            priorSequence = asset.Sequence;
        }
    }

    private static StreamModeTinOptionDescriptor[] DescribeOptions(BandedTinRenderLayer layer)
    {
        var defaults = new BandedTinRenderLayer();
        return EnumerateOptions(layer).Select(option =>
        {
            object? value = option.Property.GetValue(layer);
            object? defaultValue = option.Property.GetValue(defaults);
            Type valueType = option.Property.PropertyType;
            string[] enumValues = valueType.IsEnum ? Enum.GetNames(valueType) : [];
            string sourceEffect = SourceEffect(option.Property.Name);
            return new StreamModeTinOptionDescriptor(option.Attribute.Name, option.Attribute.Label,
                option.Attribute.Description, valueType == typeof(bool) ? "boolean" : "enum",
                FormatOptionValue(value), FormatOptionValue(defaultValue), enumValues,
                option.Attribute.EnabledIf, EvaluateEnabledIf(layer, option.Attribute.EnabledIf) &&
                    sourceEffect == "renderedBySource",
                option.Attribute.IsDebug, sourceEffect);
        }).ToArray();
    }

    private static IEnumerable<(PlotOption Attribute, PropertyInfo Property)> EnumerateOptions(
        BandedTinRenderLayer layer)
    {
        return layer.GetPlotOptions()
            .Select(pair => (pair.Key, pair.Value))
            .OrderBy(pair => pair.Value.Name, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value));
    }

    private static void ApplyAssignments(BandedTinRenderLayer layer,
        IReadOnlyList<StreamModeTinOptionAssignment> assignments)
    {
        var options = EnumerateOptions(layer).ToDictionary(option => option.Attribute.Name,
            option => option, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (StreamModeTinOptionAssignment assignment in assignments)
        {
            if (assignment is null || string.IsNullOrEmpty(assignment.Name) || assignment.Value is null ||
                !seen.Add(assignment.Name) || !options.TryGetValue(assignment.Name, out var option))
                throw new ArgumentException("The TIN render options contain an unknown or duplicate source option.");
            if (!EvaluateEnabledIf(layer, option.Attribute.EnabledIf) ||
                SourceEffect(option.Property.Name) != "renderedBySource")
                throw new ArgumentException("The requested source TIN render option is disabled.");
            option.Property.SetValue(layer, ParseOptionValue(option.Property.PropertyType, assignment.Value));
        }
    }

    private static object ParseOptionValue(Type type, string value)
    {
        if (type == typeof(bool) && bool.TryParse(value, out bool boolean)) return boolean;
        if (type.IsEnum && Enum.GetNames(type).Contains(value, StringComparer.Ordinal))
            return Enum.Parse(type, value, ignoreCase: false);
        throw new ArgumentException("A TIN render option value does not match its retained source type.");
    }

    private static string FormatOptionValue(object? value) => value switch
    {
        bool boolean => boolean ? "true" : "false",
        Enum enumeration => enumeration.ToString(),
        _ => throw new InvalidOperationException("A retained PlotOption has an unsupported value type.")
    };

    private static bool EvaluateEnabledIf(BandedTinRenderLayer layer, string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;
        bool negate = expression.StartsWith('!');
        string propertyName = negate ? expression[1..] : expression;
        PropertyInfo? property = layer.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance);
        if (property?.PropertyType == typeof(bool) && property.GetValue(layer) is bool value)
            return negate ? !value : value;
        return false;
    }

    private static string SourceEffect(string propertyName) => propertyName switch
    {
        "PaintZ" or "PaintM" => "storedWithoutSurfacePaint",
        "LabelEdges" or "LabelPoints" => "sourceReportsNotImplemented",
        _ => "renderedBySource"
    };

    private static void ValidateViewport(StreamModeTinViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if (!double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY) ||
            !double.IsFinite(viewport.MaxX) || !double.IsFinite(viewport.MaxY) ||
            viewport.MinX >= viewport.MaxX || viewport.MinY >= viewport.MaxY ||
            viewport.PixelWidth <= 0 || viewport.PixelHeight <= 0 ||
            viewport.PixelWidth > MaximumViewportDimension || viewport.PixelHeight > MaximumViewportDimension ||
            (long)viewport.PixelWidth * viewport.PixelHeight > MaximumViewportPixels)
            throw new ArgumentException("The TIN render viewport is invalid or exceeds its pixel limit.");
    }

    private static byte[] ToStraightAlphaRgba(Memory<int> sourceArgb, int width, int height)
    {
        if (sourceArgb.Length != checked(width * height))
            throw new InvalidDataException("The source renderer returned an incomplete color buffer.");

        ReadOnlySpan<int> argb = sourceArgb.Span;
        byte[] rgba = new byte[checked(width * height * 4)];
        for (int index = 0, byteIndex = 0; index < argb.Length; index++, byteIndex += 4)
        {
            uint pixel = unchecked((uint)argb[index]);
            rgba[byteIndex] = (byte)(pixel >> 16);
            rgba[byteIndex + 1] = (byte)(pixel >> 8);
            rgba[byteIndex + 2] = (byte)pixel;
            rgba[byteIndex + 3] = (byte)(pixel >> 24);
        }
        return rgba;
    }
}
