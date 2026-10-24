using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ras.Backend;

internal static class OsmCpuStyleParser
{
    private const int HotPinkArgb = unchecked((int)0xFFFF69B4);
    private static readonly Regex NumericValues = new(@"(\d+\.\d+|\d+%?|\.\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static OsmCpuStyleDocument Parse(string styleJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(styleJson);
        using JsonDocument document = JsonDocument.Parse(styleJson);
        if (!document.RootElement.TryGetProperty("layers", out JsonElement layersElement)
            || layersElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The OSM style JSON must contain a layers array.");
        }

        OsmCpuColor background = OsmCpuColor.Transparent;
        List<OsmCpuStyleLayer> layers = [];
        int ordinal = 0;
        foreach (JsonElement layerElement in layersElement.EnumerateArray())
        {
            string id = RequiredString(layerElement, "id");
            string typeValue = RequiredString(layerElement, "type");
            if (!TryParseStyleType(typeValue, out OsmCpuStyleType type))
            {
                if (typeValue is "raster" or "fill-extrusion")
                    continue;
                throw new InvalidDataException($"Unsupported OSM style layer type '{typeValue}'.");
            }

            if (layerElement.TryGetProperty("layout", out JsonElement layout)
                && layout.ValueKind == JsonValueKind.Object
                && layout.TryGetProperty("visibility", out JsonElement visibility)
                && visibility.ValueEquals("none"))
            {
                continue;
            }

            JsonElement paintElement = layerElement.TryGetProperty("paint", out JsonElement paint)
                && paint.ValueKind == JsonValueKind.Object
                    ? paint
                    : default;
            if (type == OsmCpuStyleType.Background)
            {
                OsmCpuStylePaint backgroundPaint = ParsePaint(paintElement, type, default);
                background = backgroundPaint.FillColor;
                continue;
            }

            string sourceLayer = layerElement.TryGetProperty("source-layer", out JsonElement sourceLayerElement)
                && sourceLayerElement.ValueKind == JsonValueKind.String
                    ? sourceLayerElement.GetString() ?? string.Empty
                    : string.Empty;
            if (!IsOriginalSourceLayer(sourceLayer))
                continue;

            int minimumZoom = ReadInteger(layerElement, "minzoom", 0);
            int maximumZoom = ReadInteger(layerElement, "maxzoom", 99);
            if (maximumZoom >= 14)
                maximumZoom = 99;

            OsmCpuFilter? filter = layerElement.TryGetProperty("filter", out JsonElement filterElement)
                ? ParseFilter(filterElement)
                : null;
            OsmCpuStylePaint stylePaint = ParsePaint(paintElement, type, layout);
            layers.Add(new OsmCpuStyleLayer(id, sourceLayer, type, minimumZoom, maximumZoom,
                ordinal++, id.Contains("-casing", StringComparison.Ordinal), filter, stylePaint));
        }

        return new OsmCpuStyleDocument(background, ReassignCasingLayers(layers));
    }

    private static OsmCpuStyleLayer[] ReassignCasingLayers(List<OsmCpuStyleLayer> source)
    {
        if (source.Count == 0)
            return [];

        List<OsmCpuStyleLayer> result = new(source.Count);
        int index = 0;
        while (index < source.Count)
        {
            int groupStart = index;
            string sourceLayer = source[index].SourceLayer;
            while (index < source.Count && source[index].SourceLayer == sourceLayer)
                index++;

            int groupCount = index - groupStart;
            if (groupCount <= 1)
            {
                result.Add(source[groupStart]);
                continue;
            }

            for (int current = groupStart; current < index; current++)
            {
                if (source[current].IsCasing)
                    result.Add(source[current]);
            }
            for (int current = groupStart; current < index; current++)
            {
                if (!source[current].IsCasing)
                    result.Add(source[current]);
            }
        }

        return result.Select((layer, zIndex) => layer with { ZIndex = zIndex }).ToArray();
    }

    private static OsmCpuStylePaint ParsePaint(JsonElement paint, OsmCpuStyleType type, JsonElement layout)
    {
        OsmCpuColor fillColor = OsmCpuColor.Transparent;
        OsmCpuColor strokeColor = OsmCpuColor.Transparent;
        SortedDictionary<int, int>? fillColors = null;
        SortedDictionary<int, int>? strokeColors = null;
        SortedDictionary<int, double> size = [];
        SortedDictionary<int, double> gapWidth = [];
        SortedDictionary<int, int> opacity = [];
        bool isDashed = false;
        float[] dashArray = [];
        bool isTranslated = false;
        SortedDictionary<int, float[]> translation = [];
        string fontFamily = "Segoe UI";
        bool bold = false;
        bool italic = false;
        OsmCpuTextTransform textTransform = OsmCpuTextTransform.None;
        int maximumTextWidth = int.MaxValue;
        float haloWidth = 0;
        OsmCpuColor haloColor = OsmCpuColor.Transparent;

        if (type == OsmCpuStyleType.Background)
        {
            if (TryProperty(paint, "background-color", out JsonElement backgroundColor))
                fillColor = ParseColor(backgroundColor, out _);
            strokeColor = fillColor;
        }
        else if (type == OsmCpuStyleType.Fill)
        {
            if (TryProperty(paint, "line-dasharray", out JsonElement fillDash))
            {
                isDashed = true;
                dashArray = ParseFloatArray(fillDash);
            }
            if (TryProperty(paint, "fill-translate", out JsonElement fillTranslate))
            {
                isTranslated = true;
                translation = ParseTranslation(fillTranslate);
            }
            if (!TryProperty(paint, "fill-color", out JsonElement fillColorElement))
            {
                fillColor = OsmCpuColor.FromArgb(HotPinkArgb);
            }
            else
            {
                fillColor = ParseColor(fillColorElement, out fillColors);
                if (TryProperty(paint, "fill-outline", out JsonElement fillOutline))
                    strokeColor = ParseColor(fillOutline, out _);
                if (TryProperty(paint, "fill-outline-color", out JsonElement fillOutlineColor))
                    strokeColor = ParseColor(fillOutlineColor, out strokeColors);
                if (TryProperty(paint, "fill-opacity", out JsonElement fillOpacity))
                    opacity = ParseIntegerStops(fillOpacity, value => ToOpacityByte(value));
            }
        }
        else if (type == OsmCpuStyleType.Line)
        {
            if (TryProperty(paint, "line-dasharray", out JsonElement lineDash))
            {
                isDashed = true;
                dashArray = ParseFloatArray(lineDash);
            }
            strokeColor = TryProperty(paint, "line-color", out JsonElement lineColor)
                ? ParseColor(lineColor, out strokeColors)
                : new OsmCpuColor(0, 0, 0, 255);
            if (TryProperty(paint, "line-opacity", out JsonElement lineOpacity))
                opacity = ParseIntegerStops(lineOpacity, value => ToOpacityByte(value));
            if (TryProperty(paint, "line-width", out JsonElement lineWidth))
                size = ParseDoubleStops(lineWidth);
            else
                size[0] = 1.0;
            if (TryProperty(paint, "line-gap-width", out JsonElement lineGapWidth))
                gapWidth = ParseDoubleStops(lineGapWidth);
        }
        else if (type == OsmCpuStyleType.Symbol)
        {
            if (!TryProperty(paint, "text-color", out JsonElement textColor))
            {
                // ColorScheme leaves FillColor transparent for a symbol without a
                // text-color but sets its pen (the label color) to HotPink.
                strokeColor = OsmCpuColor.FromArgb(HotPinkArgb);
            }
            else
            {
                strokeColor = ParseColor(textColor, out _);
                fillColor = strokeColor;
                if (TryProperty(paint, "text-halo-color", out JsonElement textHaloColor))
                    haloColor = ParseColor(textHaloColor, out _);
                if (TryProperty(paint, "text-halo-width", out JsonElement textHaloWidth)
                    && textHaloWidth.ValueKind == JsonValueKind.Number)
                {
                    haloWidth = (float)textHaloWidth.GetDouble();
                }
            }

            if (layout.ValueKind == JsonValueKind.Object)
            {
                if (TryProperty(layout, "text-size", out JsonElement textSize))
                    size = ParseDoubleStops(textSize);
                else
                    size[0] = 10.0;

                fontFamily = ParseFont(layout, out bold, out italic);
                textTransform = ParseTextTransform(layout);
                maximumTextWidth = TryProperty(layout, "text-max-width", out JsonElement maxWidth)
                    && maxWidth.ValueKind == JsonValueKind.Number
                        ? ReadNumberAsInt(maxWidth)
                        : int.MaxValue;
            }
            else
            {
                size[0] = 10.0;
            }
        }

        return new OsmCpuStylePaint(fillColor, strokeColor, fillColors, strokeColors, size, gapWidth,
            opacity, isDashed, dashArray, isTranslated, translation, fontFamily, bold, italic,
            textTransform, maximumTextWidth, haloWidth, haloColor);
    }

    private static string ParseFont(JsonElement layout, out bool bold, out bool italic)
    {
        bold = false;
        italic = false;
        if (!TryProperty(layout, "text-font", out JsonElement fonts)
            || fonts.ValueKind != JsonValueKind.Array)
        {
            return "Segoe UI";
        }

        foreach (JsonElement font in fonts.EnumerateArray())
        {
            if (font.ValueKind != JsonValueKind.String)
                continue;
            string? candidate = font.GetString();
            if (string.IsNullOrEmpty(candidate))
                continue;
            bold = candidate.Contains("bold", StringComparison.OrdinalIgnoreCase);
            italic = !bold && candidate.Contains("italic", StringComparison.OrdinalIgnoreCase);
            return candidate.Replace(" Regular", string.Empty, StringComparison.Ordinal)
                .Replace(" Bold", string.Empty, StringComparison.Ordinal);
        }

        return "Segoe UI";
    }

    private static OsmCpuTextTransform ParseTextTransform(JsonElement layout)
    {
        if (!TryProperty(layout, "text-transform", out JsonElement transform)
            || transform.ValueKind != JsonValueKind.String)
        {
            return OsmCpuTextTransform.None;
        }

        return transform.GetString() switch
        {
            "uppercase" => OsmCpuTextTransform.Uppercase,
            "lowercase" => OsmCpuTextTransform.Lowercase,
            _ => OsmCpuTextTransform.None
        };
    }

    private static SortedDictionary<int, float[]> ParseTranslation(JsonElement element)
    {
        SortedDictionary<int, float[]> result = [];
        if (element.ValueKind == JsonValueKind.Array)
        {
            if (element.GetArrayLength() >= 2
                && element[0].ValueKind == JsonValueKind.Number
                && element[1].ValueKind == JsonValueKind.Number)
            {
                result[0] = [(float)element[0].GetDouble(), (float)element[1].GetDouble()];
            }
            return result;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return result;
        double interpolationBase = ReadNumber(element, "base", 1.0);
        if (!TryProperty(element, "stops", out JsonElement stops) || stops.ValueKind != JsonValueKind.Array)
            return result;

        foreach (JsonElement stop in stops.EnumerateArray())
        {
            if (stop.ValueKind != JsonValueKind.Array || stop.GetArrayLength() < 2
                || stop[0].ValueKind != JsonValueKind.Number || stop[1].ValueKind != JsonValueKind.Array
                || stop[1].GetArrayLength() < 2)
            {
                continue;
            }
            int zoom = ReadNumberAsInt(stop[0]);
            if (stop[1][0].ValueKind == JsonValueKind.Number && stop[1][1].ValueKind == JsonValueKind.Number)
                result[zoom] = [(float)stop[1][0].GetDouble(), (float)stop[1][1].GetDouble()];
        }

        InterpolateTranslationGaps(result, interpolationBase);
        return result;
    }

    private static void InterpolateTranslationGaps(SortedDictionary<int, float[]> stops, double interpolationBase)
    {
        if (stops.Count < 2)
            return;
        KeyValuePair<int, float[]>[] original = stops.ToArray();
        for (int stopIndex = 0; stopIndex < original.Length - 1; stopIndex++)
        {
            KeyValuePair<int, float[]> lower = original[stopIndex];
            KeyValuePair<int, float[]> upper = original[stopIndex + 1];
            for (int zoom = lower.Key + 1; zoom < upper.Key; zoom++)
            {
                double ratio = ZoomInterpolationRatio(lower.Key, upper.Key, zoom, interpolationBase);
                stops[zoom] =
                [
                    lower.Value[0] + (float)((upper.Value[0] - lower.Value[0]) * ratio),
                    lower.Value[1] + (float)((upper.Value[1] - lower.Value[1]) * ratio)
                ];
            }
        }
    }

    private static SortedDictionary<int, double> ParseDoubleStops(JsonElement element)
    {
        SortedDictionary<int, double> values = [];
        if (element.ValueKind == JsonValueKind.Number)
        {
            values[0] = element.GetDouble();
            return values;
        }
        if (element.ValueKind != JsonValueKind.Object)
            return values;

        double interpolationBase = ReadNumber(element, "base", 1.0);
        if (!TryProperty(element, "stops", out JsonElement stops) || stops.ValueKind != JsonValueKind.Array)
            return values;
        foreach (JsonElement stop in stops.EnumerateArray())
        {
            if (stop.ValueKind == JsonValueKind.Array && stop.GetArrayLength() >= 2
                && stop[0].ValueKind == JsonValueKind.Number && stop[1].ValueKind == JsonValueKind.Number)
            {
                values[ReadNumberAsInt(stop[0])] = stop[1].GetDouble();
            }
        }
        InterpolateDoubleGaps(values, interpolationBase);
        return values;
    }

    private static void InterpolateDoubleGaps(SortedDictionary<int, double> stops, double interpolationBase)
    {
        if (stops.Count < 2)
            return;
        KeyValuePair<int, double>[] original = stops.ToArray();
        for (int stopIndex = 0; stopIndex < original.Length - 1; stopIndex++)
        {
            KeyValuePair<int, double> lower = original[stopIndex];
            KeyValuePair<int, double> upper = original[stopIndex + 1];
            for (int zoom = lower.Key + 1; zoom < upper.Key; zoom++)
            {
                double ratio = ZoomInterpolationRatio(lower.Key, upper.Key, zoom, interpolationBase);
                stops[zoom] = lower.Value + (upper.Value - lower.Value) * ratio;
            }
        }
    }

    private static SortedDictionary<int, int> ParseIntegerStops(JsonElement element, Func<double, int> convert)
    {
        SortedDictionary<int, int> values = [];
        if (element.ValueKind == JsonValueKind.Number)
        {
            values[0] = convert(element.GetDouble());
            return values;
        }
        if (element.ValueKind != JsonValueKind.Object)
            return values;

        double interpolationBase = ReadNumber(element, "base", 1.0);
        if (!TryProperty(element, "stops", out JsonElement stops) || stops.ValueKind != JsonValueKind.Array)
            return values;
        foreach (JsonElement stop in stops.EnumerateArray())
        {
            if (stop.ValueKind == JsonValueKind.Array && stop.GetArrayLength() >= 2
                && stop[0].ValueKind == JsonValueKind.Number && stop[1].ValueKind == JsonValueKind.Number)
            {
                values[ReadNumberAsInt(stop[0])] = convert(stop[1].GetDouble());
            }
        }
        if (values.Count < 2)
            return values;
        KeyValuePair<int, int>[] original = values.ToArray();
        for (int stopIndex = 0; stopIndex < original.Length - 1; stopIndex++)
        {
            KeyValuePair<int, int> lower = original[stopIndex];
            KeyValuePair<int, int> upper = original[stopIndex + 1];
            for (int zoom = lower.Key + 1; zoom < upper.Key; zoom++)
            {
                double ratio = ZoomInterpolationRatio(lower.Key, upper.Key, zoom, interpolationBase);
                values[zoom] = Convert.ToInt32(lower.Value + (upper.Value - lower.Value) * ratio);
            }
        }
        return values;
    }

    private static double ZoomInterpolationRatio(int lowerZoom, int upperZoom, int zoom, double interpolationBase)
    {
        if (Math.Abs(interpolationBase - 1.0) < 1E-10)
            return (double)(zoom - lowerZoom) / (upperZoom - lowerZoom);
        return (Math.Pow(interpolationBase, zoom - lowerZoom) - 1.0)
            / (Math.Pow(interpolationBase, upperZoom - lowerZoom) - 1.0);
    }

    private static float[] ParseFloatArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return [];
        List<float> values = [];
        foreach (JsonElement value in element.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.Number)
                values.Add((float)value.GetDouble());
        }
        return values.ToArray();
    }

    private static OsmCpuColor ParseColor(JsonElement element, out SortedDictionary<int, int>? stops)
    {
        stops = null;
        try
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (!TryProperty(element, "stops", out JsonElement colorStops)
                    || colorStops.ValueKind != JsonValueKind.Array)
                    return new OsmCpuColor(0, 0, 0, 255);
                stops = new SortedDictionary<int, int>();
                foreach (JsonElement stop in colorStops.EnumerateArray())
                {
                    if (stop.ValueKind != JsonValueKind.Array || stop.GetArrayLength() < 2
                        || stop[0].ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }
                    OsmCpuColor color = ParseColor(stop[1], out _);
                    stops[ReadNumberAsInt(stop[0])] = color.ToArgb();
                }
                if (colorStops.GetArrayLength() >= 2)
                    return ParseColor(colorStops[1][1], out _);
                return new OsmCpuColor(0, 0, 0, 255);
            }
            if (element.ValueKind == JsonValueKind.Array)
                return new OsmCpuColor(0, 0, 0, 255);
            if (element.ValueKind != JsonValueKind.String)
                return new OsmCpuColor(0, 0, 0, 255);
            return ParseColorString(element.GetString() ?? string.Empty);
        }
        catch
        {
            return new OsmCpuColor(0, 0, 0, 255);
        }
    }

    private static OsmCpuColor ParseColorString(string value)
    {
        if (value.Contains("rgba", StringComparison.Ordinal))
        {
            float[] numbers = ExtractNumbers(value);
            if (numbers.Length < 4)
                return new OsmCpuColor(0, 0, 0, 255);
            return new OsmCpuColor(ClampByte((int)numbers[0]), ClampByte((int)numbers[1]),
                ClampByte((int)numbers[2]), ClampByte((int)(numbers[3] * 255f)));
        }
        if (value.Contains("hsla", StringComparison.Ordinal))
        {
            float[] numbers = ExtractNumbers(value);
            if (numbers.Length < 3)
                return new OsmCpuColor(0, 0, 0, 255);
            OsmCpuColor color = HslToRgb(numbers[0] / 360f, numbers[1] / 100f, numbers[2] / 100f);
            byte hslaAlpha = numbers.Length > 3 ? ClampByte((int)(numbers[3] * 255f)) : byte.MaxValue;
            return color with { Alpha = hslaAlpha };
        }
        if (value.Contains("hsl", StringComparison.Ordinal))
        {
            float[] numbers = ExtractNumbers(value);
            if (numbers.Length < 3)
                return new OsmCpuColor(0, 0, 0, 255);
            return HslToRgb(numbers[0] / 360f, numbers[1] / 100f, numbers[2] / 100f);
        }
        if (value.Contains("rgb", StringComparison.Ordinal))
        {
            float[] numbers = ExtractNumbers(value);
            if (numbers.Length < 3)
                return new OsmCpuColor(0, 0, 0, 255);
            return new OsmCpuColor(ClampByte((int)numbers[0]), ClampByte((int)numbers[1]),
                ClampByte((int)numbers[2]), byte.MaxValue);
        }

        string hex = value.Replace("#", string.Empty, StringComparison.Ordinal);
        byte alpha = byte.MaxValue;
        int start = 0;
        int componentWidth = 2;
        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }
        else if (hex.Length == 8)
        {
            alpha = byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            start = 2;
        }
        else if (hex.Length != 6)
        {
            return new OsmCpuColor(0, 0, 0, 255);
        }
        return new OsmCpuColor(
            byte.Parse(hex.AsSpan(start, componentWidth), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(start + componentWidth, componentWidth), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(start + componentWidth * 2, componentWidth), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            alpha);
    }

    private static float[] ExtractNumbers(string value)
    {
        MatchCollection matches = NumericValues.Matches(value);
        float[] numbers = new float[matches.Count];
        for (int index = 0; index < matches.Count; index++)
        {
            string token = matches[index].Value.TrimEnd('%');
            numbers[index] = float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        return numbers;
    }

    private static OsmCpuColor HslToRgb(float hue, float saturation, float luminance)
    {
        float p;
        float q;
        if (saturation == 0f)
        {
            p = q = luminance;
        }
        else
        {
            q = luminance < 0.5f
                ? luminance * (1f + saturation)
                : luminance + saturation - luminance * saturation;
            p = 2f * luminance - q;
        }
        int red = (int)(PqtToRgb(p, q, hue + 1f / 3f) * 255f);
        int green = (int)(PqtToRgb(p, q, hue) * 255f);
        int blue = (int)(PqtToRgb(p, q, hue - 1f / 3f) * 255f);
        return new OsmCpuColor(ClampByte(red), ClampByte(green), ClampByte(blue), byte.MaxValue);
    }

    private static float PqtToRgb(float p, float q, float t)
    {
        if (t < 0f)
            t++;
        if (t > 1f)
            t--;
        if (t < 1f / 6f)
            return p + (q - p) * 6f * t;
        if (t < 0.5f)
            return q;
        if (t < 2f / 3f)
            return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }

    private static OsmCpuFilter? ParseFilter(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return null;
        if (element.GetArrayLength() == 0)
            return new OsmCpuFilter(new OsmCpuFilterGroup("all", []));

        bool containsNestedArray = element.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Array);
        string? first = element[0].ValueKind == JsonValueKind.String ? element[0].GetString() : null;
        if (containsNestedArray && first is "all" or "any")
        {
            OsmCpuFilterNode[] children = element.EnumerateArray()
                .Skip(1)
                .Where(item => item.ValueKind == JsonValueKind.Array)
                .Select(ParseFilterNode)
                .ToArray();
            return new OsmCpuFilter(new OsmCpuFilterGroup(first, children));
        }

        if (containsNestedArray)
        {
            // The source FilterParser does not evaluate expression-style get/case filters.
            return new OsmCpuFilter(new OsmCpuFilterCondition("__unsupported_nested__", []));
        }

        object?[] arguments = element.EnumerateArray().Skip(1).Select(ParseFilterValue).ToArray();
        return new OsmCpuFilter(new OsmCpuFilterCondition(first ?? string.Empty, arguments));
    }

    private static OsmCpuFilterNode ParseFilterNode(JsonElement element)
    {
        string? operation = element.GetArrayLength() > 0 && element[0].ValueKind == JsonValueKind.String
            ? element[0].GetString()
            : null;
        if (operation is "all" or "any")
        {
            OsmCpuFilterNode[] children = element.EnumerateArray()
                .Skip(1)
                .Where(item => item.ValueKind == JsonValueKind.Array)
                .Select(ParseFilterNode)
                .ToArray();
            return new OsmCpuFilterGroup(operation, children);
        }
        return new OsmCpuFilterCondition(operation ?? string.Empty,
            element.EnumerateArray().Skip(1).Select(ParseFilterValue).ToArray());
    }

    private static object? ParseFilterValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => ParseFilterNumber(element),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => null
    };

    private static object ParseFilterNumber(JsonElement element)
    {
        if (element.TryGetInt32(out int integerValue))
            return integerValue;
        return element.GetDouble();
    }

    private static int ToOpacityByte(double opacity) => (int)(opacity * 255.0);

    private static bool TryParseStyleType(string value, out OsmCpuStyleType type)
    {
        type = value switch
        {
            "background" => OsmCpuStyleType.Background,
            "fill" => OsmCpuStyleType.Fill,
            "line" => OsmCpuStyleType.Line,
            "symbol" => OsmCpuStyleType.Symbol,
            _ => default
        };
        return value is "background" or "fill" or "line" or "symbol";
    }

    private static bool IsOriginalSourceLayer(string value) => value is
        "aerodrome_label" or "aeroway" or "boundary" or "building" or "housenumber" or "landcover" or "landuse"
        or "mountain_peak" or "park" or "place" or "poi" or "transportation" or "transportation_name"
        or "water" or "water_name" or "waterway" or "nsiallpubnlgeojson";

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"The OSM style layer is missing its '{property}' string.");
        return value.GetString() ?? string.Empty;
    }

    private static bool TryProperty(JsonElement element, string property, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            return element.TryGetProperty(property, out value);
        value = default;
        return false;
    }

    private static int ReadInteger(JsonElement element, string property, int fallback)
    {
        return TryProperty(element, property, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? ReadNumberAsInt(value)
            : fallback;
    }

    private static int ReadNumberAsInt(JsonElement element) => (int)element.GetDouble();

    private static double ReadNumber(JsonElement element, string property, double fallback)
    {
        return TryProperty(element, property, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : fallback;
    }

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
}

