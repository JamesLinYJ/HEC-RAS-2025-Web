using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Geospatial.IO.Vectors;
using Geospatial.Rendering;

namespace Ras.Backend;

public static partial class MapTreeService
{
    private const string VectorSymbologyElement = "Symbology";
    private static readonly string[] VectorConditionGroups =
    [
        "PenColorConditions", "PenWidthConditions", "FillColorConditions", "PointSizeConditions"
    ];
    private static readonly HashSet<string> VectorStyleAttributes = new(StringComparer.Ordinal)
    {
        "ID", "PenColor", "PenWidth", "FillColor", "PointSize",
        "HaloColor", "HaloThickness", "TextEmphasis"
    };
    private static readonly HashSet<string> VectorConditionAttributes = new(StringComparer.Ordinal)
    {
        "Column", "Operator", "Threshold", "Result"
    };

    /// <summary>Inspects real global defaults and sparse project overrides for one exact saved vector layer.</summary>
    public static MapVectorSymbologyInspection InspectVectorSymbology(MapVectorSymbologyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateVectorSymbologyOwner(request.Map, request.Node, request.LayerID,
            request.ExpectedProjectRevision, request.SourceRevision);

        using OperationContext context = Open(request.Map.ProjectPath, request.Map.ProjectXml,
            request.Map.ProjectXmlSHA, request.Map.ExpectedVisualizationRevision, allowMissing: false,
            projectSessionID: request.Map.ProjectSessionID,
            externalCoreLayerReferences: request.Map.ExternalCoreLayerReferences,
            coreLayerContext: request.Map.CoreLayerContext);
        VerifyExpectedProjectRevision(context, request.ExpectedProjectRevision);
        XElement map = ResolveMap(context.Visualization, request.Map.MapIndex,
            request.Map.MapName, request.Map.MapType);
        XElement node = ResolveNode(map, request.Node, required: true)!;

        MapVectorPlotOptionsResponse options = InspectVectorPlotOptions(request);
        if (!string.Equals(options.VisualizationRevision, context.PreviousVisualizationRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        GlobalSymbologyInspectResponse global = GlobalSymbologyService.Inspect();
        StyleState state = ReadVectorStyleState(node, request.Node, options.SourcePath,
            options.SourceRevision, global);
        return ToVectorSymbologyInspection(request, context, options, global, state);
    }

    /// <summary>
    /// Saves base style attributes sparsely against current global defaults. Existing conditions, unknown
    /// attributes, and unknown child XML remain attached to their original Symbology elements.
    /// </summary>
    public static MapVectorSymbologyInspection SaveVectorSymbology(MapVectorSymbologySaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateVectorSymbologyOwner(request.Map, request.Node, request.LayerID,
            request.ExpectedProjectRevision, request.SourceRevision);
        ValidateSHA256(request.ExpectedStyleSHA256, "The expected project style revision is invalid.");
        ValidateSHA256(request.ExpectedGlobalSymbologyRevision,
            "The expected global symbology revision is invalid.");

        using (OperationContext context = Open(request.Map.ProjectPath, request.Map.ProjectXml,
                   request.Map.ProjectXmlSHA, request.Map.ExpectedVisualizationRevision, allowMissing: false,
                   projectSessionID: request.Map.ProjectSessionID,
                   externalCoreLayerReferences: request.Map.ExternalCoreLayerReferences,
                   coreLayerContext: request.Map.CoreLayerContext))
        {
            VerifyExpectedProjectRevision(context, request.ExpectedProjectRevision);
            XElement map = ResolveMap(context.Visualization, request.Map.MapIndex,
                request.Map.MapName, request.Map.MapType);
            XElement node = ResolveNode(map, request.Node, required: true)!;
            MapVectorPlotOptionsResponse options = InspectVectorPlotOptions(new MapVectorSymbologyRequest(
                request.Map, request.Node, request.LayerID, request.ExpectedProjectRevision,
                request.ExpectedFamily, request.SourceRevision));
            if (!string.Equals(options.VisualizationRevision, context.PreviousVisualizationRevision,
                    StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            GlobalSymbologyInspectResponse global = GlobalSymbologyService.Inspect();
            if (!global.Revision.Equals(request.ExpectedGlobalSymbologyRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            StyleState current = ReadVectorStyleState(node, request.Node, options.SourcePath,
                options.SourceRevision, global);
            if (!current.StyleSHA256.Equals(request.ExpectedStyleSHA256, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            MapVectorPlotStyle[] submitted = ValidateSubmittedStyles(request.Styles, current);
            bool changed = ApplyVectorBaseStyles(node, current, submitted);
            if (!changed)
                return InspectVectorSymbology(new MapVectorSymbologyRequest(request.Map,
                    request.Node, request.LayerID, request.ExpectedProjectRevision,
                    request.ExpectedFamily, request.SourceRevision));

            GlobalSymbologyInspectResponse currentGlobal = GlobalSymbologyService.Inspect();
            if (!currentGlobal.Revision.Equals(request.ExpectedGlobalSymbologyRevision,
                    StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            string visualizationRevision = Commit(context);
            MapVectorSymbologyRequest refreshedRequest = new(request.Map with
            {
                ExpectedVisualizationRevision = visualizationRevision
            }, request.Node, request.LayerID, request.ExpectedProjectRevision,
                request.ExpectedFamily, request.SourceRevision);
            return InspectVectorSymbology(refreshedRequest);
        }
    }

    private static void ValidateVectorSymbologyOwner(MapTreeRequest? map,
        CatalogTreeNodeLocator? node, string? layerID, string? expectedProjectRevision,
        string? sourceRevision)
    {
        if (map is null || node is null || string.IsNullOrWhiteSpace(layerID) ||
            !IsSHA256Revision(expectedProjectRevision) || !IsSHA256Revision(sourceRevision))
            throw new ArgumentException("The vector style request has an incomplete owner or revision.");
        bool fileVector = node.NodeType == "MapLayer" &&
            node.MapType is "PointsVector" or "PolylinesVector" or "PolygonsVector";
        bool gdbVector = node.NodeType == "GdbVectorLayer" && node.MapType == "GdbVectorLayer";
        if ((!fileVector && !gdbVector) || node.NodePath is not { Length: > 0 } ||
            string.IsNullOrWhiteSpace(node.RootName) || string.IsNullOrWhiteSpace(node.NodeName) ||
            string.IsNullOrWhiteSpace(node.Filename) || string.IsNullOrWhiteSpace(node.FilenameRelative))
            throw new NotSupportedException("Only exact persisted point, polyline, polygon, and GDB vector map layers have editable styles.");
        if (gdbVector && map.MapType is not ("Project" or "Buildable" or "Geometry" or "Plan"))
            throw new NotSupportedException("The selected GDB vector layer is not owned by a supported saved map.");
        _ = MapSourceIdentity.CaptureMap(map);
    }

    private static void VerifyExpectedProjectRevision(OperationContext context, string expectedProjectRevision)
    {
        if (!context.ProjectRevision.Equals(expectedProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static MapVectorPlotOptionsResponse InspectVectorPlotOptions(MapVectorSymbologyRequest request)
    {
        if (request.Node.MapType == "GdbVectorLayer" && request.ExpectedFamily is not null)
            throw new ArgumentException("A GDB vector uses its directory manifest revision, not a file-family capture.");
        return VectorMapPlotRenderingService.InspectOptions(new MapVectorPlotOptionsRequest(
            request.Map, request.Node, request.LayerID, request.ExpectedFamily, request.SourceRevision));
    }

    private static StyleState ReadVectorStyleState(XElement node,
        CatalogTreeNodeLocator locator, string sourcePath, string sourceRevision,
        GlobalSymbologyInspectResponse global)
    {
        string[] definitionIDs = ResolveVectorDefinitionIDs(node, locator);
        Dictionary<string, GlobalSymbologyDefinitionSnapshot> globalByID = global.Groups
            .SelectMany(group => group.Options)
            .ToDictionary(definition => definition.DefinitionID, StringComparer.Ordinal);
        if (definitionIDs.Any(id => !globalByID.ContainsKey(id)))
            throw new NotSupportedException("The retained renderer's vector style definition is not registered.");

        var styles = new List<MapVectorPlotStyle>(definitionIDs.Length);
        var definitions = new List<MapVectorSymbologyDefinition>(definitionIDs.Length);
        var conditions = new List<MapVectorSymbologyConditionGroup>();
        bool unsupportedConditions = false;
        bool unknownContent = false;
        if (node.Elements().Any(element => element.Name.LocalName == VectorSymbologyElement &&
                                           element.Name.NamespaceName.Length > 0))
            unknownContent = true;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string definitionID in definitionIDs)
        {
            GlobalSymbologyDefinitionSnapshot definition = globalByID[definitionID];
            XElement[] matches = node.Elements(VectorSymbologyElement)
                .Where(element => string.Equals((string?)element.Attribute("ID"), definitionID,
                    StringComparison.Ordinal)).ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException($"The saved map contains duplicate '{definitionID}' style entries.");
            XElement? serialized = matches.SingleOrDefault();
            AppendStyleRevision(hash, definitionID, serialized);

            MapVectorPlotStyleValues baseValues = serialized is null
                ? ToPlotValues(definition.Values)
                : ApplySerializedBaseValues(definition.Values, serialized);
            styles.Add(new MapVectorPlotStyle(definitionID, baseValues, [], serialized is not null));
            definitions.Add(new MapVectorSymbologyDefinition(definition.DefinitionID,
                definition.Name, definition.LayerName, definition.VectorType,
                definition.EditableProperties, definition.DefaultValues, definition.Values));
            if (serialized is not null)
                ReadProjectConditions(definitionID, serialized, definition.EditableProperties,
                    conditions, ref unsupportedConditions, ref unknownContent);
        }

        // Unknown serialized style data can affect drawing semantics. Keep it in XML, but do not
        // let the UI preview a flat approximation of a style the editor cannot interpret.
        if (unknownContent) unsupportedConditions = true;
        if (conditions.Count > 0)
        {
            styles = EvaluateProjectConditions(styles, conditions, locator.MapType ?? "",
                sourcePath, sourceRevision, ref unsupportedConditions);
        }

        return new StyleState(Convert.ToHexString(hash.GetHashAndReset()), styles.ToArray(),
            definitions.ToArray(), conditions.ToArray(), unsupportedConditions, unknownContent,
            global.Revision);
    }

    private static string[] ResolveVectorDefinitionIDs(XElement node, CatalogTreeNodeLocator locator)
    {
        if (locator.MapType == "GdbVectorLayer")
        {
            // InspectVectorPlotOptions has already verified this exact saved node and checked that
            // the persisted GDB type matches the current source layer before this value is consumed.
            string gdbVectorType = (string?)node.Attribute("GdbVectorType")
                ?? throw new SourceFileChangedException();
            return ResolveGdbDefinitionIDs(gdbVectorType);
        }
        else
        {
            return locator.MapType switch
            {
                "PointsVector" => ["Point_Map_Layer_Symbology"],
                "PolylinesVector" => ["Polyline_Line_Symbology", "Polyline_Vertex_Symbology"],
                "PolygonsVector" => ["Polygon_Map_Layer_Shape_Symbology", "Polygon_Map_Layer_Vertex_Symbology"],
                _ => throw new NotSupportedException("The selected vector map layer has no retained style definition.")
            };
        }
    }

    private static string[] ResolveGdbDefinitionIDs(string vectorType) => vectorType switch
    {
        "Point" or "PointCollection" => ["Gdb_Point_Symbology"],
        "Line" or "LineCollection" => ["Gdb_Line_Symbology", "Gdb_Line_Vertex_Symbology"],
        "Polygon" or "PolygonCollection" => ["Gdb_Polygon_Symbology"],
        _ => throw new NotSupportedException("The selected GDB vector geometry has no retained style definition.")
    };

    private static void AppendStyleRevision(IncrementalHash hash, string definitionID, XElement? serialized)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(definitionID));
        hash.AppendData(new byte[] { 0 });
        hash.AppendData(Encoding.UTF8.GetBytes(serialized?.ToString(SaveOptions.DisableFormatting) ?? "<absent/>"));
        hash.AppendData(new byte[] { 0 });
    }

    private static MapVectorPlotStyleValues ToPlotValues(GlobalSymbologyValues values) => new(
        values.PenColor, values.PenWidth, values.FillColor, values.PointSize,
        values.HaloColor, values.HaloThickness, values.TextEmphasis);

    private static MapVectorPlotStyleValues ApplySerializedBaseValues(
        GlobalSymbologyValues defaults, XElement serialized)
    {
        string penColor = ReadColorAttribute(serialized, "PenColor", defaults.PenColor);
        double penWidth = ReadDoubleAttribute(serialized, "PenWidth", defaults.PenWidth);
        string fillColor = ReadColorAttribute(serialized, "FillColor", defaults.FillColor);
        int pointSize = ReadIntAttribute(serialized, "PointSize", defaults.PointSize);
        string haloColor = ReadColorAttribute(serialized, "HaloColor", defaults.HaloColor);
        double haloThickness = ReadDoubleAttribute(serialized, "HaloThickness", defaults.HaloThickness);
        int textEmphasis = ReadIntAttribute(serialized, "TextEmphasis", defaults.TextEmphasis);
        return new(penColor, penWidth, fillColor, pointSize, haloColor, haloThickness, textEmphasis);
    }

    private static string ReadColorAttribute(XElement element, string name, string fallback)
    {
        string? value = (string?)element.Attribute(name);
        if (value is null) return fallback;
        if (value.Length != 9 || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            throw new InvalidDataException($"The saved vector style attribute '{name}' is invalid.");
        return "#" + argb.ToString("X8", CultureInfo.InvariantCulture);
    }

    private static double ReadDoubleAttribute(XElement element, string name, double fallback)
    {
        string? value = (string?)element.Attribute(name);
        if (value is null) return fallback;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
            !double.IsFinite(parsed))
            throw new InvalidDataException($"The saved vector style attribute '{name}' is invalid.");
        return parsed;
    }

    private static int ReadIntAttribute(XElement element, string name, int fallback)
    {
        string? value = (string?)element.Attribute(name);
        if (value is null) return fallback;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            throw new InvalidDataException($"The saved vector style attribute '{name}' is invalid.");
        return parsed;
    }

    private static void ReadProjectConditions(string definitionID, XElement serialized,
        string[] editableProperties, List<MapVectorSymbologyConditionGroup> output,
        ref bool hasUnsupportedConditions, ref bool hasUnknownContent)
    {
        foreach (XAttribute attribute in serialized.Attributes())
            if (attribute.Name.NamespaceName.Length > 0 ||
                !VectorStyleAttributes.Contains(attribute.Name.LocalName)) hasUnknownContent = true;

        foreach (XElement child in serialized.Elements())
        {
            if (child.Name.NamespaceName.Length > 0 ||
                !VectorConditionGroups.Contains(child.Name.LocalName, StringComparer.Ordinal))
            {
                hasUnknownContent = true;
                continue;
            }
            string property = child.Name.LocalName[..^"Conditions".Length];
            if (!editableProperties.Contains(property, StringComparer.Ordinal))
                hasUnsupportedConditions = true;
            if (child.Attributes().Any())
            {
                hasUnknownContent = true;
                hasUnsupportedConditions = true;
            }
            XElement[] groups = serialized.Elements(child.Name).ToArray();
            if (groups.Length > 1)
            {
                hasUnsupportedConditions = true;
                hasUnknownContent = true;
            }
            if (child.Nodes().Any(node => node is not XElement &&
                                          (node is not XText text || !string.IsNullOrWhiteSpace(text.Value))))
            {
                hasUnsupportedConditions = true;
                hasUnknownContent = true;
            }

            var parsed = new List<MapVectorSymbologyCondition>();
            foreach (XElement condition in child.Elements())
            {
                if (condition.Name.NamespaceName.Length > 0 || condition.Name.LocalName != "Condition")
                {
                    hasUnsupportedConditions = true;
                    hasUnknownContent = true;
                    continue;
                }
                foreach (XAttribute attribute in condition.Attributes())
                    if (attribute.Name.NamespaceName.Length > 0 ||
                        !VectorConditionAttributes.Contains(attribute.Name.LocalName))
                    {
                        hasUnsupportedConditions = true;
                        hasUnknownContent = true;
                    }
                if (condition.HasElements || condition.Nodes().Any(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value)))
                {
                    hasUnsupportedConditions = true;
                    hasUnknownContent = true;
                }
                string column = (string?)condition.Attribute("Column") ?? string.Empty;
                string op = (string?)condition.Attribute("Operator") ?? string.Empty;
                string threshold = (string?)condition.Attribute("Threshold") ?? string.Empty;
                string result = (string?)condition.Attribute("Result") ?? string.Empty;
                if (op.Length > 0 && !ConditionOperatorMap.Map.TryGetValue(op, out _))
                    hasUnsupportedConditions = true;
                parsed.Add(new MapVectorSymbologyCondition(column, op, threshold, result));
            }
            output.Add(new MapVectorSymbologyConditionGroup(definitionID, property, parsed.ToArray()));
        }
    }

    private static MapVectorSymbologyInspection ToVectorSymbologyInspection(
        MapVectorSymbologyRequest request, OperationContext context,
        MapVectorPlotOptionsResponse options, GlobalSymbologyInspectResponse global, StyleState state) =>
        new(request.Map.MapID, request.LayerID, request.Node, context.ProjectPath,
            request.Map.ProjectXmlSHA, context.ProjectRevision, context.ProjectSessionID,
            options.SourcePath, options.SourceRevision, options.VisualizationRevision,
            state.StyleSHA256, global.Revision, state.Definitions, state.Styles,
            options.PlotOptions, state.ProjectConditions, state.HasUnsupportedProjectConditions,
            state.HasUnknownProjectSymbologyContent);

    private static List<MapVectorPlotStyle> EvaluateProjectConditions(
        List<MapVectorPlotStyle> styles, List<MapVectorSymbologyConditionGroup> conditions,
        string mapType, string sourcePath, string sourceRevision,
        ref bool hasUnsupportedConditions)
    {
        // The current retained condition evaluator proves source-row binding only for GeoJSON.
        // Other source families keep their serialized condition XML editable only as read-only data.
        if (!Path.GetExtension(sourcePath).Equals(".geojson", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(sourcePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            hasUnsupportedConditions = true;
            return styles;
        }

        if (hasUnsupportedConditions) return styles;
        try
        {
            VectorSymbologyInspection source = VectorSymbologyService.Inspect(
                new VectorSymbologyInspectRequest(sourcePath, sourceRevision));
            GeoJsonFeatureCollection collection = GeoJsonReader.Deserialize(File.ReadAllText(sourcePath));
            var featureTypes = (collection.Features ?? []).Select(feature => feature.Geometry?.Type).ToArray();
            var styleByID = styles.ToDictionary(style => style.DefinitionID, StringComparer.Ordinal);
            var effective = new Dictionary<(string DefinitionID, int FeatureIndex), MapVectorPlotStyleValues>();
            foreach (VectorSymbologyBucket bucket in source.Buckets)
            {
                MapVectorSymbologyConditionGroup[] relevant = conditions.Where(condition =>
                    bucket.SupportedDefinitionIDs.Contains(condition.DefinitionID, StringComparer.Ordinal)).ToArray();
                if (relevant.Length == 0) continue;

                string expectedGeometry = mapType switch
                {
                    "PointsVector" when bucket.GeometryType == "point" => "Point",
                    "PolylinesVector" when bucket.GeometryType == "line" => "LineString",
                    "PolygonsVector" when bucket.GeometryType == "polygon" => "Polygon",
                    _ => ""
                };
                if (expectedGeometry.Length == 0 || !bucket.OrdinalBindingProven || !bucket.HasAttributeTable ||
                    bucket.SourceFeatureIndices.Any(index => (uint)index >= (uint)featureTypes.Length) ||
                    bucket.SourceFeatureIndices.Any(index => !string.Equals(featureTypes[index], expectedGeometry,
                        StringComparison.Ordinal)))
                {
                    hasUnsupportedConditions = true;
                    continue;
                }

                VectorSymbologyPropertyConditions[] properties = relevant.Select(condition =>
                    new VectorSymbologyPropertyConditions(condition.DefinitionID, condition.Property,
                        condition.Conditions.Select(value => new VectorSymbologyConditionInput(
                            value.Column, value.Operator, value.Threshold, value.ResultValue)).ToArray())).ToArray();
                VectorSymbologyEvaluation evaluated = VectorSymbologyService.Evaluate(
                    new VectorSymbologyEvaluateRequest(sourcePath, sourceRevision,
                        bucket.GeometryType, bucket.SourceFeatureIndices, properties));
                foreach (VectorSymbologyOverride value in evaluated.Overrides)
                {
                    if (!value.HasMatch) continue;
                    var key = (value.DefinitionID, value.SourceFeatureIndex);
                    MapVectorPlotStyleValues current = effective.GetValueOrDefault(key,
                        styleByID[value.DefinitionID].Base);
                    MapVectorPlotStyleValues updated = value.Property switch
                    {
                        "PenColor" when value.ColorValue is not null => current with { PenColor = value.ColorValue },
                        "FillColor" when value.ColorValue is not null => current with { FillColor = value.ColorValue },
                        "PenWidth" when value.NumberValue is double width => current with { PenWidth = width },
                        "PointSize" when value.IntegerValue is int size => current with { PointSize = size },
                        _ => throw new NotSupportedException("The stored conditional style result is not supported by the renderer.")
                    };
                    effective[key] = updated;
                }
            }

            return styles.Select(style => style with
            {
                SourceOverrides = effective.Where(entry => entry.Key.DefinitionID == style.DefinitionID)
                    .OrderBy(entry => entry.Key.FeatureIndex)
                    .Select(entry => new MapVectorPlotStyleOverride(entry.Key.FeatureIndex, entry.Value))
                    .ToArray()
            }).ToList();
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidDataException or
                                           ArgumentException or FormatException or OverflowException)
        {
            hasUnsupportedConditions = true;
            return styles;
        }
    }

    private static MapVectorPlotStyle[] ValidateSubmittedStyles(
        MapVectorPlotStyle[]? styles, StyleState current)
    {
        if (styles is null || styles.Length != current.Styles.Length || styles.Any(style => style is null))
            throw new ArgumentException("The vector style save must include every currently supported definition.");
        var allowed = current.Styles.ToDictionary(style => style.DefinitionID, StringComparer.Ordinal);
        var submitted = new Dictionary<string, MapVectorPlotStyle>(StringComparer.Ordinal);
        foreach (MapVectorPlotStyle style in styles)
        {
            if (string.IsNullOrWhiteSpace(style.DefinitionID) || style.Base is null ||
                style.SourceOverrides is null)
                throw new ArgumentException("A vector style entry is incomplete.");
            if (!allowed.ContainsKey(style.DefinitionID) || !submitted.TryAdd(style.DefinitionID, style))
                throw new ArgumentException("The vector style save contains an unknown or duplicate definition.");
        }
        if (allowed.Keys.Any(id => !submitted.ContainsKey(id)))
            throw new ArgumentException("The vector style save is missing a supported definition.");
        // SourceOverrides are inspect-time render results, not persisted rules. They are intentionally
        // ignored here; the authoritative condition XML remains in .vis and is reevaluated on inspect.
        return current.Styles.Select(existing => submitted[existing.DefinitionID]).ToArray();
    }

    private static bool ApplyVectorBaseStyles(XElement node, StyleState current,
        MapVectorPlotStyle[] submitted)
    {
        Dictionary<string, MapVectorSymbologyDefinition> definitions = current.Definitions
            .ToDictionary(definition => definition.DefinitionID, StringComparer.Ordinal);
        Dictionary<string, MapVectorPlotStyle> styles = submitted
            .ToDictionary(style => style.DefinitionID, StringComparer.Ordinal);
        bool changed = false;
        foreach (MapVectorPlotStyle currentStyle in current.Styles)
        {
            MapVectorPlotStyle submittedStyle = styles[currentStyle.DefinitionID];
            MapVectorSymbologyDefinition definition = definitions[currentStyle.DefinitionID];
            GlobalSymbologyValues defaults = definition.GlobalValues;
            ValidateSubmittedValues(submittedStyle.Base, definition.EditableProperties);

            XElement? serialized = node.Elements(VectorSymbologyElement).SingleOrDefault(element =>
                string.Equals((string?)element.Attribute("ID"), currentStyle.DefinitionID,
                    StringComparison.Ordinal));
            MapVectorPlotStyleValues values = submittedStyle.Base;
            if (serialized is null && !DiffersFromGlobal(values, defaults, definition.EditableProperties))
                continue;
            if (serialized is null)
            {
                serialized = new XElement(VectorSymbologyElement, new XAttribute("ID", currentStyle.DefinitionID));
                node.Add(serialized);
                changed = true;
            }

            foreach (string property in definition.EditableProperties)
                changed |= WriteSparseAttribute(serialized, property, values, defaults);

            if (IsEmptyKnownStyle(serialized))
            {
                serialized.Remove();
                changed = true;
            }
        }
        return changed;
    }

    private static void ValidateSubmittedValues(MapVectorPlotStyleValues values, string[] editableProperties)
    {
        if (editableProperties.Contains("PenColor", StringComparer.Ordinal)) ValidateARGB(values.PenColor);
        if (editableProperties.Contains("FillColor", StringComparer.Ordinal)) ValidateARGB(values.FillColor);
        if (editableProperties.Contains("HaloColor", StringComparer.Ordinal)) ValidateARGB(values.HaloColor);
        if (editableProperties.Contains("PenWidth", StringComparer.Ordinal) &&
            (!double.IsFinite(values.PenWidth) || values.PenWidth < 0 || values.PenWidth > 10_000))
            throw new ArgumentException("Vector pen width must be finite and between 0 and 10000.");
        if (editableProperties.Contains("PointSize", StringComparer.Ordinal) && values.PointSize is < 0 or > 10_000)
            throw new ArgumentException("Vector point size must be between 0 and 10000.");
        if (editableProperties.Contains("HaloThickness", StringComparer.Ordinal) &&
            (!double.IsFinite(values.HaloThickness) || values.HaloThickness < 0 || values.HaloThickness > 10_000 ||
             values.HaloThickness > float.MaxValue))
            throw new ArgumentException("Vector halo thickness must be finite and between 0 and 10000.");
    }

    private static void ValidateARGB(string? value)
    {
        if (value is not { Length: 9 } || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException("Vector colors must use #AARRGGBB format.");
    }

    private static bool DiffersFromGlobal(MapVectorPlotStyleValues values,
        GlobalSymbologyValues defaults, string[] editableProperties) => editableProperties.Any(property => property switch
    {
        "PenColor" => !SameColor(values.PenColor, defaults.PenColor),
        "PenWidth" => values.PenWidth != defaults.PenWidth,
        "FillColor" => !SameColor(values.FillColor, defaults.FillColor),
        "PointSize" => values.PointSize != defaults.PointSize,
        "HaloColor" => !SameColor(values.HaloColor, defaults.HaloColor),
        "HaloThickness" => (float)values.HaloThickness != (float)defaults.HaloThickness,
        "TextEmphasis" => values.TextEmphasis != defaults.TextEmphasis,
        _ => throw new NotSupportedException($"Vector style property '{property}' is not supported.")
    });

    private static bool WriteSparseAttribute(XElement element, string property,
        MapVectorPlotStyleValues values, GlobalSymbologyValues defaults)
    {
        string? serializedValue;
        bool isDefault;
        switch (property)
        {
            case "PenColor":
                isDefault = SameColor(values.PenColor, defaults.PenColor);
                serializedValue = CanonicalColor(values.PenColor);
                break;
            case "PenWidth":
                isDefault = values.PenWidth == defaults.PenWidth;
                serializedValue = values.PenWidth.ToString("R", CultureInfo.InvariantCulture);
                break;
            case "FillColor":
                isDefault = SameColor(values.FillColor, defaults.FillColor);
                serializedValue = CanonicalColor(values.FillColor);
                break;
            case "PointSize":
                isDefault = values.PointSize == defaults.PointSize;
                serializedValue = values.PointSize.ToString(CultureInfo.InvariantCulture);
                break;
            case "HaloColor":
                isDefault = SameColor(values.HaloColor, defaults.HaloColor);
                serializedValue = CanonicalColor(values.HaloColor);
                break;
            case "HaloThickness":
                float thickness = checked((float)values.HaloThickness);
                isDefault = thickness == (float)defaults.HaloThickness;
                serializedValue = thickness.ToString("R", CultureInfo.InvariantCulture);
                break;
            case "TextEmphasis":
                isDefault = values.TextEmphasis == defaults.TextEmphasis;
                serializedValue = values.TextEmphasis.ToString(CultureInfo.InvariantCulture);
                break;
            default:
                throw new NotSupportedException($"Vector style property '{property}' is not supported.");
        }

        XAttribute? existing = element.Attribute(property);
        if (isDefault)
        {
            if (existing is null) return false;
            existing.Remove();
            return true;
        }
        if (existing?.Value == serializedValue) return false;
        element.SetAttributeValue(property, serializedValue);
        return true;
    }

    private static bool IsEmptyKnownStyle(XElement element) =>
        element.Attributes().All(attribute => attribute.Name == "ID") && !element.HasElements &&
        !element.Nodes().Any(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value));

    private static bool SameColor(string? left, string? right) =>
        string.Equals(CanonicalColor(left), CanonicalColor(right), StringComparison.OrdinalIgnoreCase);

    private static string CanonicalColor(string? value)
    {
        ValidateARGB(value);
        uint argb = uint.Parse(value!.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return "#" + argb.ToString("X8", CultureInfo.InvariantCulture);
    }

    private static void ValidateSHA256(string? value, string message)
    {
        if (!IsSHA256Revision(value)) throw new ArgumentException(message);
    }

    private static bool IsSHA256Revision(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');

    private sealed record StyleState(string StyleSHA256, MapVectorPlotStyle[] Styles,
        MapVectorSymbologyDefinition[] Definitions, MapVectorSymbologyConditionGroup[] ProjectConditions,
        bool HasUnsupportedProjectConditions, bool HasUnknownProjectSymbologyContent,
        string GlobalSymbologyRevision);
}
