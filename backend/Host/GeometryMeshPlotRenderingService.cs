using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Systems;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Layers;
using Visual.Mapping.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    private const int GeometryMeshPlotRenderPixelLimit = 2_097_152;

    /// <summary>
    /// Replays exact Geometry parent drafts when requested, or uses the saved
    /// Geometry model when no parent editor owns it. The retained source Mesh,
    /// ConceptualMesh, and diagnostic Tin renderers paint the viewport; options
    /// live only on the transient renderer instance.
    /// </summary>
    public static async Task<GeometryMeshPlotRenderResponse> RenderGeometryMeshPlotAsync(
        GeometryMeshPlotRenderRequest request)
    {
        ValidatePlotRenderRequest(request);
        VerifyGeometryMeshModel(request.Model);
        string revision = Revision(request.Model.AssetPath);
        if (!string.Equals(revision, request.SourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        VerifyDraftDigest(request.MeshDraftState, request.MeshDraftSHA256, "mesh");
        VerifyDraftDigest(request.TopologyDraftState ?? "", request.TopologyDraftSHA256, "topology");
        bool replayParentDraft = request.OwnerMode switch
        {
            "parentDraft" when !string.IsNullOrEmpty(request.MeshDraftState) => true,
            "savedSource" when request.MeshDraftState.Length == 0 && request.TopologyDraftState is null => false,
            "parentDraft" => throw new ArgumentException("Geometry parent-draft rendering requires an exact mesh draft."),
            "savedSource" => throw new ArgumentException("Saved Geometry rendering cannot include parent draft payloads."),
            _ => throw new ArgumentException("The Geometry plot-render owner mode is not supported.")
        };

        using var context = new ModelContext(ToModelRequest(request.Model));
        if (replayParentDraft)
        {
            ApplyGeometryTopologyDraft(context, request.Model.AssetPath, revision, request.TopologyDraftState);
            string topologyFingerprint = GeometryMeshTopologyFingerprint(context);
            GeometryMeshDraftState meshState = ReadGeometryMeshDraft(request.MeshDraftState,
                request.Model.AssetPath, revision);
            EnsureTopologyFingerprint(meshState, topologyFingerprint);
            _ = await ApplyGeometryMeshMutationsAsync(context, request.Model.AssetPath, revision,
                meshState, replayPropertyTableComputations: false);
        }

        IRenderLayer renderer;
        switch (request.RendererFamily)
        {
            case "Mesh":
            {
                var (geometry, _) = GeometryMeshTopologyFor(context);
                renderer = new MeshRenderLayer(geometry.FlowAreaLayer.Layer.AsMeshCollection());
                break;
            }
            case "ConceptualMesh":
            {
                var (_, observableTopology) = GeometryMeshTopologyFor(context);
                Topology topology = observableTopology?.Topology
                    ?? throw new InvalidOperationException("The source Conceptual Mesh topology is unavailable.");
                renderer = new ConceptualMeshRenderLayer(topology);
                break;
            }
            case "TopologyTIN":
            {
                var (_, observableTopology) = GeometryMeshTopologyFor(context);
                Topology topology = observableTopology?.Topology
                    ?? throw new InvalidOperationException("The source Geometry topology is unavailable.");
                var diagnostics = new TopologyBuildDiagnostics();
                _ = topology.Complete(TopologyBuilderParams.Default(), diagnostics, forceRecomplete: true);
                if (diagnostics.CompletedTin is null)
                    throw new InvalidOperationException("The source Geometry topology did not produce a completed TIN.");
                renderer = new TinRenderLayer(diagnostics.CompletedTin);
                break;
            }
            default:
                throw new ArgumentException("The requested Geometry plot renderer is not supported.");
        }

        Dictionary<string, (PlotOption Option, PropertyInfo Property)> sourceOptions = renderer.GetPlotOptions()
            .ToDictionary(pair => pair.Value.Name, pair => (pair.Key, pair.Value), StringComparer.Ordinal);
        ApplyPlotOptions(renderer, sourceOptions, request.Options);
        ApplyPlotSymbology(renderer, request.RendererFamily, request.Styles);
        GeometryMeshPlotOptionDescriptor[] optionDescriptors = DescribePlotOptions(renderer, sourceOptions);
        var extent = new Extent(request.Viewport.MinX, request.Viewport.MaxX,
            request.Viewport.MinY, request.Viewport.MaxY);
        var raster = new RasterDefinition(extent, request.PixelHeight, request.PixelWidth);
        if (!raster.IsValid() || raster.CellCount != request.PixelWidth * request.PixelHeight)
            throw new ArgumentException("The Geometry plot viewport could not be represented as a source raster.");

        byte[] rgba;
        GeometryMeshPlotLabel[] labels;
        bool paintedPixels;
        double sourceRenderScale = request.RenderScale * request.PixelSamplingScale;
        double previousRenderScale = VectorManagerOptions.RenderScaling;
        try
        {
            // RenderScale is the desired on-screen point multiplier. Pixel
            // dimensions determine how many raster pixels represent one point;
            // multiply them so source pen/marker dimensions remain correct
            // after the raster is mapped back onto the fixed viewport bounds.
            VectorManagerOptions.RenderScaling = sourceRenderScale;
            using PlotRequest plot = renderer.Render(raster);
            if (plot.ColorBufferARGB.Length != checked(request.PixelWidth * request.PixelHeight))
                throw new InvalidOperationException("The source renderer returned an incomplete pixel buffer.");
            rgba = PremultipliedRgba(plot.ColorBufferARGB.Span);
            paintedPixels = plot.SplatCount > 0;
            labels = (plot.Labels ?? []).Select(label =>
            {
                Point point = label.PointLocation;
                GeometryMeshPlotCoordinate[] polyline = label.PolylineLocation.ToArray()
                    .Select(location => new GeometryMeshPlotCoordinate(location.X, location.Y)).ToArray();
                return new GeometryMeshPlotLabel(label.Text ?? "",
                    new GeometryMeshPlotCoordinate(point.X, point.Y), polyline);
            }).ToArray();
        }
        finally
        {
            VectorManagerOptions.RenderScaling = previousRenderScale;
        }

        if (!string.Equals(Revision(request.Model.AssetPath), revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        return new GeometryMeshPlotRenderResponse(
            request.SourceIdentity, PlotOwnerIdentity(request), request.MapID, request.MapContextToken,
            request.GeometryNodeIdentity, request.ProjectRevision, request.VisualizationRevision,
            revision, request.MeshDraftSHA256, request.TopologyDraftSHA256, request.OwnerMode, request.Viewport,
            request.PixelWidth, request.PixelHeight, request.RenderScale, request.PixelSamplingScale,
            sourceRenderScale, request.StylePacketSHA256,
            request.StyleIdentity, request.Styles,
            new GeometryMeshPlotViewport(raster.MinX, raster.MinY, raster.MaxX, raster.MaxY),
            request.RendererFamily, "rendered", paintedPixels,
            Convert.ToBase64String(rgba), labels, optionDescriptors);
    }

    private static void ValidatePlotRenderRequest(GeometryMeshPlotRenderRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.Model is null || request.Viewport is null || request.Options is null)
            throw new ArgumentException("The Geometry plot-render request is incomplete.");
        if (request.MeshDraftState is null || string.IsNullOrWhiteSpace(request.MeshDraftSHA256)
            || string.IsNullOrWhiteSpace(request.TopologyDraftSHA256) || request.Styles is null
            || string.IsNullOrWhiteSpace(request.StylePacketSHA256)
            || string.IsNullOrWhiteSpace(request.StyleIdentity) || request.StyleRevision is null)
            throw new ArgumentException("The Geometry plot-render draft identity is incomplete.");
        if (request.Model.Kind != "Geometry")
            throw new ArgumentException("Geometry plot rendering requires a Geometry source.");
        if (request.OwnerMode is not ("parentDraft" or "savedSource"))
            throw new ArgumentException("The Geometry plot-render owner mode is not supported.");
        if (string.IsNullOrWhiteSpace(request.SourceIdentity) || string.IsNullOrWhiteSpace(request.MapID)
            || !Guid.TryParse(request.MapID, out _) || string.IsNullOrWhiteSpace(request.MapContextToken)
            || string.IsNullOrWhiteSpace(request.GeometryNodeIdentity)
            || string.IsNullOrWhiteSpace(request.ProjectRevision)
            || string.IsNullOrWhiteSpace(request.VisualizationRevision))
            throw new ArgumentException("The Geometry plot-render map owner identity is incomplete.");
        if (request.PixelWidth is < 1 or > 4096 || request.PixelHeight is < 1 or > 4096
            || (long)request.PixelWidth * request.PixelHeight > GeometryMeshPlotRenderPixelLimit)
            throw new ArgumentException("The Geometry plot-render pixel dimensions exceed the supported limit.");
        if (!double.IsFinite(request.RenderScale) || request.RenderScale <= 0)
            throw new ArgumentException("The Geometry source render scale must be finite and greater than zero.");
        if (!double.IsFinite(request.PixelSamplingScale) || request.PixelSamplingScale <= 0
            || !double.IsFinite(request.RenderScale * request.PixelSamplingScale)
            || request.RenderScale * request.PixelSamplingScale <= 0
            || request.RenderScale * request.PixelSamplingScale > 64)
            throw new ArgumentException("The Geometry pixel sampling scale must produce a finite source render scale.");
        GeometryMeshPlotViewport viewport = request.Viewport;
        if (!double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY)
            || !double.IsFinite(viewport.MaxX) || !double.IsFinite(viewport.MaxY)
            || viewport.MinX >= viewport.MaxX || viewport.MinY >= viewport.MaxY)
            throw new ArgumentException("The Geometry plot viewport must be finite and ordered.");
        if (request.RendererFamily is not ("Mesh" or "ConceptualMesh" or "TopologyTIN"))
            throw new ArgumentException("The requested Geometry plot renderer is not supported.");
        string packetSHA256 = GeometryPlotStylePacketSHA256(request.Styles);
        if (!string.Equals(packetSHA256, request.StylePacketSHA256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(GeometryPlotStyleIdentity(request, packetSHA256), request.StyleIdentity,
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Geometry plot symbology packet identity does not match its values.");

        string[] expectedStyleIDs = request.RendererFamily switch
        {
            "Mesh" => ["Mesh_Line_Symbology"],
            "ConceptualMesh" => ["Arc_Line_Symbology", "Node_Symbology", "Deletable_Node_Symbology",
                "Internal_Point_Symbology"],
            "TopologyTIN" => [],
            _ => throw new ArgumentException("The requested Geometry plot renderer is not supported.")
        };
        if (!request.Styles.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedStyleIDs))
            throw new ArgumentException("The Geometry plot symbology packet does not match its source renderer.");
        if (request.RendererFamily == "TopologyTIN"
            ? request.StyleScope != "sourceDefault" || request.StyleRevision.Length != 0
                || request.GlobalStyleRevision != 0
            : request.StyleScope is not ("localVis" or "global")
                || (request.StyleScope == "global" && request.StyleRevision.Length != 0))
            throw new ArgumentException("The Geometry plot symbology owner scope is invalid.");
        foreach (MapVectorPlotStyleValues values in request.Styles.Values)
            _ = ValidateGeometryPlotStyle(values);
    }

    private static void ApplyPlotSymbology(IRenderLayer renderer, string family,
        Dictionary<string, MapVectorPlotStyleValues> styles)
    {
        if (family == "TopologyTIN") return; // Temporary source TIN keeps its real source defaults.
        MapVectorPlotStyleValues Style(string key) => ValidateGeometryPlotStyle(styles[key]);
        if (renderer is MeshRenderLayer mesh && family == "Mesh")
        {
            mesh.VectorSymbology = MakeGeometryPlotSymbology(Style("Mesh_Line_Symbology"));
            return;
        }
        if (renderer is ConceptualMeshRenderLayer conceptual && family == "ConceptualMesh")
        {
            conceptual.VectorSymbology = MakeGeometryPlotSymbology(Style("Arc_Line_Symbology"));
            conceptual.NodeSymbology = MakeGeometryPlotSymbology(Style("Node_Symbology"));
            conceptual.DeletableNodeSymbology = MakeGeometryPlotSymbology(Style("Deletable_Node_Symbology"));
            conceptual.InternalPointSymbology = MakeGeometryPlotSymbology(Style("Internal_Point_Symbology"));
            return;
        }
        throw new InvalidOperationException("The source Geometry renderer did not accept its symbology packet.");
    }

    private static VectorSymbology MakeGeometryPlotSymbology(MapVectorPlotStyleValues values) =>
        new(ParseARGB(values.PenColor), values.PenWidth, ParseARGB(values.FillColor))
        {
            PointSize = values.PointSize,
            HaloColor = ParseARGB(values.HaloColor),
            HaloThickness = checked((float)values.HaloThickness),
            TextEmphasis = values.TextEmphasis
        };

    private static MapVectorPlotStyleValues ValidateGeometryPlotStyle(MapVectorPlotStyleValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _ = ParseARGB(values.PenColor);
        _ = ParseARGB(values.FillColor);
        _ = ParseARGB(values.HaloColor);
        if (!double.IsFinite(values.PenWidth) || !double.IsFinite(values.HaloThickness)
            || values.HaloThickness < -float.MaxValue || values.HaloThickness > float.MaxValue)
            throw new ArgumentException("The Geometry plot symbology contains an invalid measurement.");
        return values;
    }

    private static System.Drawing.Color ParseARGB(string value)
    {
        if (value is null || value.Length != 9 || value[0] != '#'
            || !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            throw new ArgumentException("Geometry plot colors must use #AARRGGBB source format.");
        return System.Drawing.Color.FromArgb(unchecked((int)argb));
    }

    private static string GeometryPlotStylePacketSHA256(Dictionary<string, MapVectorPlotStyleValues> styles)
    {
        string canonical = string.Join("\n", styles.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
        {
            MapVectorPlotStyleValues values = ValidateGeometryPlotStyle(pair.Value);
            return string.Join("|", pair.Key, values.PenColor.ToUpperInvariant(), DoubleBits(values.PenWidth),
                values.FillColor.ToUpperInvariant(), values.PointSize.ToString(CultureInfo.InvariantCulture),
                values.HaloColor.ToUpperInvariant(), DoubleBits(values.HaloThickness),
                values.TextEmphasis.ToString(CultureInfo.InvariantCulture));
        }));
        return Digest(canonical);
    }

    private static string GeometryPlotStyleIdentity(GeometryMeshPlotRenderRequest request, string packetSHA256) =>
        Digest(string.Join("\n", "geometry-mesh-plot-style-v1", request.SourceIdentity,
            PlotOwnerIdentity(request), request.MapID, request.MapContextToken, request.GeometryNodeIdentity,
            request.ProjectRevision, request.VisualizationRevision, request.SourceRevision,
            request.StyleScope, request.StyleRevision, request.GlobalStyleRevision.ToString(CultureInfo.InvariantCulture),
            packetSHA256));

    private static string DoubleBits(double value) =>
        unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("x", CultureInfo.InvariantCulture);

    private static void VerifyDraftDigest(string value, string expected, string kind)
    {
        string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"The Geometry {kind} draft identity does not match its payload.");
    }

    private static string PlotOwnerIdentity(GeometryMeshPlotRenderRequest request) => string.Join("|",
        SourceFilePaths.Canonical(request.Model.AssetPath), request.Model.Kind,
        request.Model.ProjectPath ?? "", Digest(request.Model.ProjectXml ?? ""), request.SourceRevision,
        request.MeshDraftSHA256, request.TopologyDraftSHA256);

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void ApplyPlotOptions(IRenderLayer renderer,
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> sourceOptions,
        Dictionary<string, string> values)
    {
        foreach ((string propertyName, string value) in values)
        {
            if (!sourceOptions.TryGetValue(propertyName, out var sourceOption))
                throw new ArgumentException($"The {renderer.GetType().Name} renderer has no PlotOption named '{propertyName}'.");
            if (!HasDrawingImplementation(renderer, sourceOption.Property))
                throw new ArgumentException($"PlotOption '{propertyName}' has no drawing implementation in the source renderer.");
            Type propertyType = sourceOption.Property.PropertyType;
            object parsed;
            if (propertyType == typeof(bool))
            {
                if (!bool.TryParse(value, out bool boolean))
                    throw new ArgumentException($"PlotOption '{propertyName}' requires a Boolean value.");
                parsed = boolean;
            }
            else if (propertyType.IsEnum && Enum.TryParse(propertyType, value, ignoreCase: false, out object? enumeration)
                     && enumeration is not null && Enum.IsDefined(propertyType, enumeration))
            {
                parsed = enumeration;
            }
            else
            {
                throw new ArgumentException($"PlotOption '{propertyName}' has an unsupported value.");
            }
            sourceOption.Property.SetValue(renderer, parsed);
        }
        foreach (string propertyName in values.Keys)
        {
            if (!IsPlotOptionEnabled(renderer, sourceOptions[propertyName].Option))
                throw new ArgumentException($"PlotOption '{propertyName}' is unavailable for the current renderer state.");
        }
    }

    private static GeometryMeshPlotOptionDescriptor[] DescribePlotOptions(IRenderLayer renderer,
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> sourceOptions) => sourceOptions
        .Select(pair =>
        {
            PropertyInfo property = pair.Value.Property;
            PlotOption option = pair.Value.Option;
            object? current = property.GetValue(renderer);
            string value = FormatPlotOptionValue(current);
            bool hasDrawingImplementation = HasDrawingImplementation(renderer, property);
            string? implementationNote = hasDrawingImplementation
                ? null
                : "The source renderer does not implement point labels.";
            if (property.PropertyType == typeof(bool))
                return new GeometryMeshPlotOptionDescriptor(property.Name, option.Name, DisplayPlotOptionLabel(option),
                    "bool", value, [], hasDrawingImplementation && IsPlotOptionEnabled(renderer, option),
                    option.EnabledIf, hasDrawingImplementation, implementationNote);
            GeometryMeshPlotEnumChoice[] enumValues = Enum.GetNames(property.PropertyType).Select(name =>
            {
                MemberInfo member = property.PropertyType.GetMember(name).First();
                string label = member.GetCustomAttribute<Utility.Attributes.EnumDisplayName>()?.DisplayName
                    ?? member.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
                return new GeometryMeshPlotEnumChoice(name, label);
            }).ToArray();
            return new GeometryMeshPlotOptionDescriptor(property.Name, option.Name, DisplayPlotOptionLabel(option),
                "enum", value, enumValues, hasDrawingImplementation && IsPlotOptionEnabled(renderer, option),
                option.EnabledIf, hasDrawingImplementation, implementationNote);
        })
        .OrderBy(option => option.PropertyName, StringComparer.Ordinal)
        .ToArray();

    private static bool HasDrawingImplementation(IRenderLayer renderer, PropertyInfo property) =>
        renderer is not TinRenderLayer || property.Name != nameof(TinRenderLayer.LabelPoints);

    private static string DisplayPlotOptionLabel(PlotOption option) =>
        option.Label.EndsWith(" ??", StringComparison.Ordinal) ? option.Label[..^3] : option.Label;

    private static bool IsPlotOptionEnabled(IRenderLayer renderer, PlotOption option)
    {
        if (string.IsNullOrWhiteSpace(option.EnabledIf)) return true;
        PropertyInfo? condition = renderer.GetType().GetProperty(option.EnabledIf,
            BindingFlags.Instance | BindingFlags.Public);
        if (condition?.PropertyType != typeof(bool))
            throw new InvalidOperationException($"PlotOption condition '{option.EnabledIf}' is not a Boolean source property.");
        return condition.GetValue(renderer) is true;
    }

    private static string FormatPlotOptionValue(object? value) => value switch
    {
        bool boolean => boolean ? "true" : "false",
        Enum enumeration => enumeration.ToString(),
        _ => throw new InvalidOperationException("The source renderer returned an unsupported PlotOption value.")
    };

    private static byte[] PremultipliedRgba(Span<int> argb)
    {
        byte[] bytes = new byte[checked(argb.Length * 4)];
        for (int i = 0; i < argb.Length; i++)
        {
            uint color = unchecked((uint)argb[i]);
            byte alpha = (byte)(color >> 24);
            bytes[i * 4] = (byte)((((color >> 16) & 0xff) * alpha + 127) / 255);
            bytes[i * 4 + 1] = (byte)((((color >> 8) & 0xff) * alpha + 127) / 255);
            bytes[i * 4 + 2] = (byte)(((color & 0xff) * alpha + 127) / 255);
            bytes[i * 4 + 3] = alpha;
        }
        return bytes;
    }
}
