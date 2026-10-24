using System.IO.Compression;
using System.Text.RegularExpressions;
using VectorTile;

namespace Ras.Backend;

/// <summary>
/// Decodes the renamed HEC-RAS OSM vector-tile protocol into CPU-owned paths
/// and label candidates. Network access, style/tile caching, viewport selection
/// and screen-coordinate drawing remain with the caller.
/// </summary>
public static class OsmBackgroundCpuRenderer
{
    public const int OriginalTileSize = 4096;
    public const double OriginalHalfWorldWidth = 20037508.34;
    public const int LabelZIndex = 1073741823;

    private const int FirstGeneratedStyleZoom = 0;
    private const int LastGeneratedStyleZoom = 24;
    private const int StyleZoomLevelOffset = 2;
    private const int TileZIndexStride = 1000;
    private const int HotPinkArgb = unchecked((int)0xFFFF69B4);

    private static readonly Regex CensoredWords = new(
        @"\b(fuck|shit|bitch)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> WantedLayers = new(StringComparer.Ordinal)
    {
        "building", "aeroway", "water", "landcover", "landuse", "waterway", "boundary", "place",
        "aerodrome_label", "mountain_peak", "transportation_name", "water_name", "transportation",
        "nsiallpubnlgeojson", "park"
    };

    private static readonly HashSet<string> SymbolFeatureLayers = new(StringComparer.Ordinal)
    {
        "aerodrome_label", "housenumber", "poi", "transportation_name", "mountain_peak", "water_name", "place"
    };

    /// <summary>Parses the original style JSON once for reuse across its tiles.</summary>
    public static OsmCpuStyleDocument ParseStyle(string styleJson) => OsmCpuStyleParser.Parse(styleJson);

    /// <summary>
    /// Parses a real gzip-compressed Mapbox Vector Tile and returns styled
    /// project-world geometry. A null projector keeps output in EPSG:900913.
    /// </summary>
    public static OsmCpuTileFrame DecodeTile(
        ReadOnlyMemory<byte> gzipVectorTile,
        OsmCpuStyleDocument style,
        OsmCpuTileCoordinate tile,
        OsmCpuRenderOptions? options = null,
        Func<double, double, (double X, double Y)>? webMercatorToProjectWorld = null)
    {
        ArgumentNullException.ThrowIfNull(style);
        options ??= new OsmCpuRenderOptions();
        ValidateInput(gzipVectorTile, tile, options);

        TileParser parsedTile;
        using (MemoryStream compressed = new(gzipVectorTile.ToArray(), writable: false))
        using (GZipStream decompressed = new(compressed, CompressionMode.Decompress, leaveOpen: false))
            parsedTile = TileParser.Parser.ParseFrom(decompressed);

        int styleZoom = tile.Zoom - options.Density + StyleZoomLevelOffset;
        List<OsmCpuFeaturePath> features = [];
        List<OsmCpuLabel> labels = [];

        foreach (TileParser.Types.Layer sourceLayer in parsedTile.Layers)
        {
            // The source filters this exact case-sensitive allow-list before looking up styles.
            if (!WantedLayers.Contains(sourceLayer.Name))
                continue;

            OsmCpuStyleLayer[] matchingStyleLayers = style.Layers
                .Where(layer => string.Equals(layer.SourceLayer, sourceLayer.Name, StringComparison.Ordinal))
                .ToArray();
            if (matchingStyleLayers.Length == 0)
                continue;

            for (int featureIndex = 0; featureIndex < sourceLayer.Features.Count; featureIndex++)
            {
                TileParser.Types.Feature sourceFeature = sourceLayer.Features[featureIndex];
                OsmCpuGeometryType geometryType = ToGeometryType(sourceFeature.Type);
                OsmCpuFeatureTags tags = ParseTags(sourceLayer, sourceFeature);
                OsmCpuStyleType styleType = GetStyleType(sourceLayer.Name, sourceFeature.Type);
                LocalPathPart[] decodedParts = DecodeGeometry(sourceFeature.Geometry, options.TileSize);

                foreach (OsmCpuStyleLayer styleLayer in matchingStyleLayers)
                {
                    if (styleLayer.Type != styleType
                        || styleZoom < styleLayer.MinimumZoom
                        || styleZoom > styleLayer.MaximumZoom
                        || styleLayer.Id.Contains("pattern", StringComparison.Ordinal)
                        || !EvaluateFilter(tags, sourceFeature.Type, styleLayer.Filter))
                    {
                        continue;
                    }

                    OsmCpuAppearance appearance = ResolveAppearance(styleLayer.Paint, styleZoom);
                    if (appearance.FillColor.ToArgb() == HotPinkArgb)
                        continue;

                    string? text = styleType == OsmCpuStyleType.Symbol
                        ? ResolveLabelText(tags, styleLayer.Paint.TextTransform)
                        : null;
                    OsmCpuPathPart[] mappedParts = ConvertPartsToWorld(decodedParts, tile, options,
                        webMercatorToProjectWorld);

                    // The source stores point symbol geometry but DrawTile does not send points
                    // to the vector manager. Lines used by symbol layers become label paths.
                    if (styleType != OsmCpuStyleType.Symbol
                        || sourceFeature.Type == TileParser.Types.GeomType.Polygon)
                    {
                        if (mappedParts.Length > 0)
                        {
                            features.Add(new OsmCpuFeaturePath(
                                featureIndex,
                                sourceFeature.HasId ? sourceFeature.Id : null,
                                sourceLayer.Name,
                                styleLayer.Id,
                                geometryType,
                                styleLayer.ZIndex + TileZIndexStride * tile.Zoom,
                                appearance,
                                mappedParts));
                        }
                    }

                    if (!options.ShowLabels || styleType != OsmCpuStyleType.Symbol || string.IsNullOrEmpty(text))
                        continue;

                    switch (sourceFeature.Type)
                    {
                        case TileParser.Types.GeomType.Point:
                            foreach (OsmCpuPathPart part in mappedParts)
                            {
                                if (part.Points.Length == 0)
                                    continue;
                                labels.Add(CreateLabel(text, sourceLayer.Name, styleLayer, styleZoom,
                                    part.Points[0], null));
                            }
                            break;

                        case TileParser.Types.GeomType.Linestring:
                            foreach (OsmCpuPathPart part in mappedParts)
                            {
                                if (part.Points.Length == 0)
                                    continue;
                                labels.Add(CreateLabel(text, sourceLayer.Name, styleLayer, styleZoom,
                                    null, part.Points));
                            }
                            break;

                        case TileParser.Types.GeomType.Polygon:
                            foreach (LocalPathPart part in decodedParts)
                            {
                                if (part.Points.Length == 0)
                                    continue;
                                OsmCpuPoint webMercatorAnchor = PolygonCentroid(part.Points
                                    .Select(point => ToWebMercator(tile, point, options)).ToArray());
                                if (TryProject(webMercatorAnchor, webMercatorToProjectWorld, out OsmCpuPoint anchor))
                                {
                                    labels.Add(CreateLabel(text, sourceLayer.Name, styleLayer, styleZoom,
                                        anchor, null));
                                }
                            }
                            break;
                    }
                }
            }
        }

        features = features.OrderBy(feature => feature.ZIndex).ToList();
        OsmCpuPoint[] clipPolygon = CreateTileClipPolygon(tile, options, webMercatorToProjectWorld);
        return new OsmCpuTileFrame(
            tile,
            styleZoom,
            webMercatorToProjectWorld is null ? OsmCpuCoordinateSpace.WebMercator900913 : OsmCpuCoordinateSpace.ProjectWorld,
            style.BackgroundColor,
            clipPolygon,
            features.ToArray(),
            labels.ToArray());
    }

    private static void ValidateInput(ReadOnlyMemory<byte> tileBytes, OsmCpuTileCoordinate tile,
        OsmCpuRenderOptions options)
    {
        if (tileBytes.IsEmpty)
            throw new ArgumentException("The gzip OSM vector tile is empty.", nameof(tileBytes));
        if (tile.Zoom is < 0 or > 30)
            throw new ArgumentOutOfRangeException(nameof(tile), "Tile zoom must be between 0 and 30.");
        int tilesPerAxis = 1 << tile.Zoom;
        if (tile.Column < 0 || tile.Column >= tilesPerAxis || tile.TmsRow < 0 || tile.TmsRow >= tilesPerAxis)
            throw new ArgumentOutOfRangeException(nameof(tile), "Tile column and TMS row must be inside the zoom pyramid.");
        if (options.TileSize <= 0 || !double.IsFinite(options.HalfWorldWidth) || options.HalfWorldWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Tile size and WebMercator half-world width must be positive and finite.");
    }

    private static OsmCpuStyleType GetStyleType(string sourceLayer, TileParser.Types.GeomType geometryType)
    {
        if (SymbolFeatureLayers.Contains(sourceLayer))
            return OsmCpuStyleType.Symbol;
        return geometryType switch
        {
            TileParser.Types.GeomType.Linestring => OsmCpuStyleType.Line,
            TileParser.Types.GeomType.Polygon => OsmCpuStyleType.Fill,
            _ => OsmCpuStyleType.Symbol
        };
    }

    private static OsmCpuGeometryType ToGeometryType(TileParser.Types.GeomType geometryType) => geometryType switch
    {
        TileParser.Types.GeomType.Point => OsmCpuGeometryType.Point,
        TileParser.Types.GeomType.Linestring => OsmCpuGeometryType.LineString,
        TileParser.Types.GeomType.Polygon => OsmCpuGeometryType.Polygon,
        _ => OsmCpuGeometryType.Point
    };

    private static OsmCpuFeatureTags ParseTags(TileParser.Types.Layer layer, TileParser.Types.Feature feature)
    {
        OsmCpuFeatureTags tags = new();
        if ((feature.Tags.Count & 1) != 0)
            throw new InvalidDataException("An OSM vector feature has an odd number of tag indexes.");

        for (int index = 0; index < feature.Tags.Count; index += 2)
        {
            uint keyIndex = feature.Tags[index];
            uint valueIndex = feature.Tags[index + 1];
            if (keyIndex >= layer.Keys.Count || valueIndex >= layer.Values.Count)
                throw new InvalidDataException("An OSM vector feature has a tag index outside its layer tables.");

            string key = layer.Keys[(int)keyIndex];
            TileParser.Types.Value value = layer.Values[(int)valueIndex];
            if (value.HasBoolValue)
                tags.Bools[key] = value.BoolValue;
            else if (value.HasStringValue)
                tags.Strings[key] = value.StringValue;
            else if (value.HasDoubleValue)
                tags.Doubles[key] = value.DoubleValue;
            else if (value.HasFloatValue)
                tags.Doubles[key] = value.FloatValue;
            else if (value.HasSintValue)
                tags.Integers[key] = value.SintValue;
            else if (value.HasUintValue)
                tags.Integers[key] = unchecked((long)value.UintValue);
            // Match MapFeature.ParseTags: int_value is not placed in one of its attribute tables.
        }

        return tags;
    }

    private static bool EvaluateFilter(OsmCpuFeatureTags tags, TileParser.Types.GeomType geometryType,
        OsmCpuFilter? filter)
    {
        if (filter is null)
            return true;
        return EvaluateFilterNode(tags, geometryType, filter.Root);
    }

    private static bool EvaluateFilterNode(OsmCpuFeatureTags tags, TileParser.Types.GeomType geometryType,
        OsmCpuFilterNode node)
    {
        if (node is OsmCpuFilterGroup group)
        {
            return group.Operator switch
            {
                "all" => group.Children.All(child => EvaluateFilterNode(tags, geometryType, child)),
                "any" => group.Children.Any(child => EvaluateFilterNode(tags, geometryType, child)),
                _ => false
            };
        }

        if (node is not OsmCpuFilterCondition condition || condition.Arguments.Length < 1
            || condition.Arguments[0] is not string property)
        {
            return false;
        }

        object? expected = condition.Arguments.Length > 1 ? condition.Arguments[1] : null;
        return condition.Operator switch
        {
            "==" => FilterEquals(tags, geometryType, property, expected),
            "!=" => !FilterEquals(tags, geometryType, property, expected),
            "in" => FilterIn(tags, property, condition.Arguments),
            "!in" => !FilterIn(tags, property, condition.Arguments),
            "<=" => FilterInteger(tags, property, expected, comparison => comparison <= 0),
            "<" => FilterInteger(tags, property, expected, comparison => comparison < 0),
            ">=" => FilterInteger(tags, property, expected, comparison => comparison >= 0),
            ">" => FilterInteger(tags, property, expected, comparison => comparison > 0),
            "has" => tags.Contains(property),
            "!has" => !tags.Contains(property),
            _ => false
        };
    }

    private static bool FilterEquals(OsmCpuFeatureTags tags, TileParser.Types.GeomType geometryType,
        string property, object? expected)
    {
        if (property.Equals("$type", StringComparison.OrdinalIgnoreCase))
        {
            if (expected is not string typeName)
                return false;
            return typeName.Equals(geometryType switch
            {
                TileParser.Types.GeomType.Polygon => "Polygon",
                TileParser.Types.GeomType.Linestring => "Linestring",
                TileParser.Types.GeomType.Point => "Point",
                _ => string.Empty
            }, StringComparison.OrdinalIgnoreCase);
        }

        if (expected is null)
            return tags.GetString(property) is null;
        return expected switch
        {
            string stringValue => tags.GetString(property) == stringValue,
            int integerValue => tags.GetInteger(property) == integerValue,
            double doubleValue => tags.GetDouble(property) == doubleValue,
            _ => false
        };
    }

    private static bool FilterIn(OsmCpuFeatureTags tags, string property, object?[] candidates)
    {
        for (int index = 1; index < candidates.Length; index++)
        {
            object? candidate = candidates[index];
            if (candidate is string stringValue && tags.GetString(property) == stringValue)
                return true;
            if (candidate is int intValue && tags.GetInteger(property) == intValue)
                return true;
            if (candidate is double doubleValue && tags.GetDouble(property) == doubleValue)
                return true;
            if (candidate is bool boolValue && tags.GetBool(property) == boolValue)
                return true;
        }
        return false;
    }

    private static bool FilterInteger(OsmCpuFeatureTags tags, string property, object? expected,
        Func<int, bool> comparison)
    {
        if (expected is not int value || tags.GetInteger(property) is not long actual)
            return false;
        return comparison(actual.CompareTo(value));
    }

    private static string? ResolveLabelText(OsmCpuFeatureTags tags, OsmCpuTextTransform transform)
    {
        string? text = tags.GetString("name:en") ?? tags.GetString("name");
        if (text is null || CensoredWords.IsMatch(text))
            return null;
        return transform switch
        {
            OsmCpuTextTransform.Uppercase => text.ToUpper(),
            OsmCpuTextTransform.Lowercase => text.ToLower(),
            _ => text
        };
    }

    private static OsmCpuAppearance ResolveAppearance(OsmCpuStylePaint paint, int styleZoom)
    {
        int generatedZoom = Math.Clamp(styleZoom, FirstGeneratedStyleZoom, LastGeneratedStyleZoom);
        OsmCpuColor fill = paint.FillColors is { Count: > 0 }
            ? OsmCpuColor.FromArgb(ValueAtZoom(paint.FillColors, generatedZoom))
            : paint.FillColor;
        OsmCpuColor stroke = paint.StrokeColors is { Count: > 0 }
            ? OsmCpuColor.FromArgb(ValueAtZoom(paint.StrokeColors, generatedZoom))
            : paint.StrokeColor;
        double lineWidth = ValueAtZoom(paint.Size, generatedZoom, 1.0);
        double gapWidth = ValueAtZoom(paint.GapWidth, generatedZoom, 0.0);
        double strokeWidth = paint.GapWidth.Count > 0 ? gapWidth + 2.0 * lineWidth : lineWidth;

        if (paint.Opacity.Count > 0)
        {
            int opacity = ValueAtZoom(paint.Opacity, generatedZoom);
            fill = fill with { Alpha = (byte)(fill.Alpha * opacity / 255) };
            if (paint.StrokeColors is not { Count: > 0 })
                stroke = stroke with { Alpha = (byte)(stroke.Alpha * opacity / 255) };
        }

        float[] translation = ValueAtZoom(paint.Translation, generatedZoom, [0f, 0f]);
        OsmCpuPoint translationPoint = translation.Length >= 2
            ? new OsmCpuPoint(translation[0], translation[1])
            : default;
        return new OsmCpuAppearance(fill, stroke, strokeWidth, lineWidth, gapWidth,
            paint.IsDashed, paint.DashArray.ToArray(), paint.IsTranslated, translationPoint);
    }

    private static OsmCpuLabel CreateLabel(string text, string sourceLayer, OsmCpuStyleLayer styleLayer,
        int styleZoom, OsmCpuPoint? anchor, OsmCpuPoint[]? path)
    {
        int generatedZoom = Math.Clamp(styleZoom, FirstGeneratedStyleZoom, LastGeneratedStyleZoom);
        OsmCpuStylePaint paint = styleLayer.Paint;
        double fontSize = ValueAtZoom(paint.Size, generatedZoom, 10.0);
        return new OsmCpuLabel(text, sourceLayer, styleLayer.Id, anchor, path,
            LabelZIndex, paint.FontFamily, paint.Bold, paint.Italic, fontSize,
            paint.MaximumTextWidth, paint.StrokeColor, paint.HaloColor, paint.HaloWidth);
    }

    private static int ValueAtZoom(SortedDictionary<int, int> values, int zoom)
    {
        return ValueAtZoom(values, zoom, 0);
    }

    private static int ValueAtZoom(SortedDictionary<int, int> values, int zoom, int fallback)
    {
        if (values.Count == 0)
            return fallback;
        foreach (KeyValuePair<int, int> pair in values.Reverse())
        {
            if (pair.Key <= zoom)
                return pair.Value;
        }
        return values.First().Value;
    }

    private static double ValueAtZoom(SortedDictionary<int, double> values, int zoom, double fallback)
    {
        if (values.Count == 0)
            return fallback;
        foreach (KeyValuePair<int, double> pair in values.Reverse())
        {
            if (pair.Key <= zoom)
                return pair.Value;
        }
        return values.First().Value;
    }

    private static float[] ValueAtZoom(SortedDictionary<int, float[]> values, int zoom, float[] fallback)
    {
        if (values.Count == 0)
            return fallback;
        foreach (KeyValuePair<int, float[]> pair in values.Reverse())
        {
            if (pair.Key <= zoom)
                return pair.Value;
        }
        return values.First().Value;
    }

    private static LocalPathPart[] DecodeGeometry(Google.Protobuf.Collections.RepeatedField<uint> commands,
        int tileSize)
    {
        if (commands.Count == 0)
            return [];

        List<LocalPathPart> parts = [];
        List<OsmCpuPoint>? currentPart = null;
        int cursor = 0;
        int x = 0;
        int y = 0;
        while (cursor < commands.Count)
        {
            uint commandInteger = commands[cursor++];
            uint command = commandInteger & 7;
            uint count = commandInteger >> 3;
            if (count == 0 && command != 7)
                continue;

            switch (command)
            {
                case 1: // MoveTo
                    for (uint item = 0; item < count; item++)
                    {
                        if (cursor + 1 >= commands.Count)
                            throw new InvalidDataException("An OSM vector tile MoveTo command is truncated.");
                        if (currentPart is { Count: > 0 })
                            parts.Add(new LocalPathPart(currentPart.ToArray(), IsClosed: false));
                        currentPart = [];
                        x = checked(x + ZigZagDecode(commands[cursor++]));
                        y = checked(y + ZigZagDecode(commands[cursor++]));
                        currentPart.Add(new OsmCpuPoint(x, tileSize - y));
                    }
                    break;

                case 2: // LineTo
                    if (currentPart is null)
                        throw new InvalidDataException("An OSM vector tile LineTo command has no preceding MoveTo.");
                    for (uint item = 0; item < count; item++)
                    {
                        if (cursor + 1 >= commands.Count)
                            throw new InvalidDataException("An OSM vector tile LineTo command is truncated.");
                        x = checked(x + ZigZagDecode(commands[cursor++]));
                        y = checked(y + ZigZagDecode(commands[cursor++]));
                        currentPart.Add(new OsmCpuPoint(x, tileSize - y));
                    }
                    break;

                case 7: // ClosePath
                    if (currentPart is { Count: > 0 })
                    {
                        parts.Add(new LocalPathPart(currentPart.ToArray(), IsClosed: true));
                        currentPart = null;
                    }
                    break;

                default:
                    throw new InvalidDataException($"An OSM vector tile contains unsupported geometry command {command}.");
            }
        }

        if (currentPart is { Count: > 0 })
            parts.Add(new LocalPathPart(currentPart.ToArray(), IsClosed: false));
        return parts.ToArray();
    }

    private static int ZigZagDecode(uint value) => unchecked((int)((value >> 1) ^ (uint)-(int)(value & 1)));

    private static OsmCpuPathPart[] ConvertPartsToWorld(LocalPathPart[] parts,
        OsmCpuTileCoordinate tile, OsmCpuRenderOptions options,
        Func<double, double, (double X, double Y)>? projector)
    {
        List<OsmCpuPathPart> result = new(parts.Length);
        foreach (LocalPathPart part in parts)
        {
            OsmCpuPoint[] converted = new OsmCpuPoint[part.Points.Length];
            bool valid = true;
            for (int index = 0; index < part.Points.Length; index++)
            {
                OsmCpuPoint webMercator = ToWebMercator(tile, part.Points[index], options);
                if (!TryProject(webMercator, projector, out converted[index]))
                {
                    valid = false;
                    break;
                }
            }
            if (valid)
                result.Add(new OsmCpuPathPart(converted, part.IsClosed));
        }
        return result.ToArray();
    }

    private static OsmCpuPoint ToWebMercator(OsmCpuTileCoordinate tile, OsmCpuPoint tilePoint,
        OsmCpuRenderOptions options)
    {
        double worldWidth = 2.0 * options.HalfWorldWidth;
        double zoomScale = Math.Pow(2.0, tile.Zoom);
        double cellSize = worldWidth / (options.TileSize * zoomScale);
        double xWorldOffset = tile.Column / zoomScale * worldWidth;
        double yWorldOffset = tile.TmsRow / zoomScale * worldWidth;
        return new OsmCpuPoint(
            -options.HalfWorldWidth + xWorldOffset + cellSize * tilePoint.X,
            -options.HalfWorldWidth + yWorldOffset + cellSize * tilePoint.Y);
    }

    private static bool TryProject(OsmCpuPoint source,
        Func<double, double, (double X, double Y)>? projector, out OsmCpuPoint result)
    {
        try
        {
            if (projector is null)
            {
                result = source;
                return double.IsFinite(result.X) && double.IsFinite(result.Y);
            }
            (double x, double y) = projector(source.X, source.Y);
            result = new OsmCpuPoint(x, y);
            return double.IsFinite(x) && double.IsFinite(y);
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private static OsmCpuPoint[] CreateTileClipPolygon(OsmCpuTileCoordinate tile,
        OsmCpuRenderOptions options, Func<double, double, (double X, double Y)>? projector)
    {
        const int sourceEdgeStep = 512;
        List<OsmCpuPoint> polygon = new(options.TileSize / sourceEdgeStep * 4);
        for (int edge = 0; edge < 4; edge++)
        {
            for (int offset = 0; offset < options.TileSize; offset += sourceEdgeStep)
            {
                OsmCpuPoint tilePoint = edge switch
                {
                    0 => new OsmCpuPoint(0.0, offset),
                    1 => new OsmCpuPoint(offset, options.TileSize),
                    2 => new OsmCpuPoint(options.TileSize, options.TileSize - offset),
                    _ => new OsmCpuPoint(options.TileSize - offset, 0.0)
                };
                if (!TryProject(ToWebMercator(tile, tilePoint, options), projector, out OsmCpuPoint point))
                    return [];
                polygon.Add(point);
            }
        }
        return polygon.ToArray();
    }

    private static OsmCpuPoint PolygonCentroid(OsmCpuPoint[] points)
    {
        if (points.Length == 0)
            return new OsmCpuPoint(double.NaN, double.NaN);

        double areaTerm = 0;
        double xComponent = 0;
        double yComponent = 0;
        OsmCpuPoint origin = points[0];
        for (int index = 0; index < points.Length - 1; index++)
        {
            OsmCpuPoint first = points[index];
            OsmCpuPoint second = points[index + 1];
            double triangle = (first.X - origin.X) * (second.Y - origin.Y)
                - (second.X - origin.X) * (first.Y - origin.Y);
            xComponent += triangle * (origin.X + first.X + second.X);
            yComponent += triangle * (origin.Y + first.Y + second.Y);
            areaTerm += triangle;
        }
        return new OsmCpuPoint(xComponent / (3.0 * areaTerm), yComponent / (3.0 * areaTerm));
    }

    private sealed record LocalPathPart(OsmCpuPoint[] Points, bool IsClosed);

    private sealed class OsmCpuFeatureTags
    {
        internal Dictionary<string, bool> Bools { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, double> Doubles { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, long> Integers { get; } = new(StringComparer.Ordinal);

        internal bool Contains(string key) => Bools.ContainsKey(key) || Strings.ContainsKey(key)
            || Doubles.ContainsKey(key) || Integers.ContainsKey(key);
        internal bool? GetBool(string key) => Bools.TryGetValue(key, out bool value) ? value : null;
        internal string? GetString(string key) => Strings.TryGetValue(key, out string? value) ? value : null;
        internal double? GetDouble(string key) => Doubles.TryGetValue(key, out double value) ? value : null;
        internal long? GetInteger(string key) => Integers.TryGetValue(key, out long value) ? value : null;
    }
}

