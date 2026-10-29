using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Ras.Backend;

public static partial class FeatureQueryService
{
    // Leave two MiB for the worker protocol envelope. The limit below is checked
    // against the complete serialized result, not just the GeoJSON coordinates.
    private static readonly FeatureQueryPreviewBudget PreviewBudget =
        new(2_000, 100_000, 6 * 1024 * 1024, 128L * 1024 * 1024);

    private sealed class PreviewLayerData(StageLayer source)
    {
        public StageLayer Source { get; } = source;
        public List<JsonElement> Features { get; } = [];
        public List<long> CoordinateCounts { get; } = [];
        public long TotalCoordinates { get; set; }

        public FeatureQueryPreviewDataLayer ToResponse() => new(
            Source.FileName, Source.Name, Source.Revision, Source.MapType,
            Source.LineColor, Source.FillColor, Source.LineWidth,
            new("FeatureCollection", Features.ToArray()), Source.FeatureCount, Features.Count,
            TotalCoordinates, CoordinateCounts.Sum(), Features.Count != Source.FeatureCount);
    }

    public static FeatureQueryPreviewResponse Preview(FeatureQueryPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        MapTreeRequest mapRequest = request.Map ?? throw new ArgumentException("缺少查询结果所属地图。");
        MapSourceMapCapture expectedMap = request.ExpectedMap
            ?? throw new ArgumentException("缺少查询时的地图身份，请重新查询。");
        string resultId = ValidateResultId(request.ResultId);
        MapSourceMapCapture mapCapture = MapSourceIdentity.CaptureMap(mapRequest);
        if (mapCapture != expectedMap) throw new SourceFileChangedException();

        string stageDirectory = StageDirectory(resultId);
        using StageLease stageLease = AcquireStageLease(stageDirectory, resultId);
        StageManifest manifest = ReadManifest(stageDirectory, resultId, out string manifestRevision,
            verifySources: false);
        if (manifest.Map != expectedMap) throw new SourceFileChangedException();
        MapSourceIdentity.VerifyMap(mapRequest, mapCapture);
        if (manifest.PolygonSourceFamily is { } polygonFamily) MapSourceIdentity.VerifyFamily(polygonFamily);

        long sourceBytes = 0;
        foreach (StageLayer layer in manifest.Layers)
        {
            sourceBytes = checked(sourceBytes + new FileInfo(OwnedStageFile(stageDirectory, layer.FileName)).Length);
            if (sourceBytes > PreviewBudget.MaxSourceBytes)
                throw new ValidationException("查询暂存超过 128 MiB 的只读预览读取上限，请缩小查询范围。完整导出不受此预览上限影响。");
        }
        if (manifest.NldAlignmentResponseFile is { } responseFile)
            sourceBytes = checked(sourceBytes + new FileInfo(OwnedStageFile(stageDirectory, responseFile)).Length);
        if (sourceBytes > PreviewBudget.MaxSourceBytes)
            throw new ValidationException("查询暂存超过 128 MiB 的只读预览读取上限，请缩小查询范围。完整导出不受此预览上限影响。");
        VerifyStageSnapshot(stageDirectory, manifest, manifestRevision);

        var layers = new List<PreviewLayerData>(manifest.Layers.Length);
        int returnedFeatures = 0;
        long returnedCoordinates = 0;
        long returnedBytes = 0;
        long remainingSourceBytes = PreviewBudget.MaxSourceBytes;
        foreach (StageLayer layer in manifest.Layers)
        {
            string path = OwnedStageFile(stageDirectory, layer.FileName);
            byte[] bytes = ReadStageBytes(path, remainingSourceBytes);
            remainingSourceBytes -= bytes.LongLength;
            if (!string.Equals(HashStageBytes(bytes), layer.Revision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            using JsonDocument document = JsonDocument.Parse(WithoutUtf8Bom(bytes),
                new JsonDocumentOptions { MaxDepth = 48 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasJsonType(root, "FeatureCollection") ||
                !root.TryGetProperty("features", out JsonElement features) ||
                features.ValueKind != JsonValueKind.Array || features.GetArrayLength() != layer.FeatureCount)
                throw new InvalidDataException("查询暂存的 GeoJSON 要素数量或集合结构与清单不一致，请重新查询。");

            var data = new PreviewLayerData(layer);
            foreach (JsonElement feature in features.EnumerateArray())
            {
                if (feature.ValueKind != JsonValueKind.Object || !HasJsonType(feature, "Feature") ||
                    !feature.TryGetProperty("geometry", out JsonElement geometry) ||
                    !feature.TryGetProperty("properties", out JsonElement properties) ||
                    properties.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                    throw new InvalidDataException("查询暂存包含无效的 GeoJSON 要素，请重新查询。");
                long coordinates = CountGeometryCoordinates(geometry);
                data.TotalCoordinates = checked(data.TotalCoordinates + coordinates);
                if (returnedFeatures >= PreviewBudget.MaxFeatures ||
                    coordinates > PreviewBudget.MaxCoordinates - returnedCoordinates)
                    continue;

                long featureBytes = SerializedSize(feature);
                // Conservative first pass; the exact complete response is checked
                // below, including escaped properties, styles, map and warnings.
                if (featureBytes > PreviewBudget.MaxPayloadBytes - 64 * 1024 - returnedBytes)
                    continue;
                data.Features.Add(feature.Clone());
                data.CoordinateCounts.Add(coordinates);
                returnedFeatures++;
                returnedCoordinates += coordinates;
                returnedBytes += featureBytes + 1;
            }
            layers.Add(data);
        }

        FeatureQueryPreviewResponse response = BuildPreviewResponse(mapCapture, resultId, layers);
        while (SerializedSize(response) > PreviewBudget.MaxPayloadBytes)
        {
            PreviewLayerData? last = layers.LastOrDefault(layer => layer.Features.Count > 0);
            if (last is null)
                throw new ValidationException("查询预览的元数据超过传输预算，请重新查询。");
            last.Features.RemoveAt(last.Features.Count - 1);
            last.CoordinateCounts.RemoveAt(last.CoordinateCounts.Count - 1);
            response = BuildPreviewResponse(mapCapture, resultId, layers);
        }

        VerifyStageSnapshot(stageDirectory, manifest, manifestRevision);
        if (manifest.PolygonSourceFamily is { } sourceFamily) MapSourceIdentity.VerifyFamily(sourceFamily);
        MapSourceIdentity.VerifyMap(mapRequest, mapCapture);
        return response;
    }

    public static FeatureQueryDiscardResponse Discard(FeatureQueryDiscardRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string resultId = ValidateResultId(request.ResultId);
        MapSourceMapCapture expectedMap = request.ExpectedMap
            ?? throw new ArgumentException("缺少查询时的地图身份，无法确认暂存结果归属。");
        string stageDirectory = StageDirectory(resultId);
        if (!Directory.Exists(stageDirectory))
        {
            RejectStageLink(stageDirectory);
            return new(resultId, Discarded: false, AlreadyMissing: true);
        }
        using StageLease stageLease = AcquireStageLease(stageDirectory, resultId);
        StageManifest manifest = ReadManifest(stageDirectory, resultId, out string manifestRevision,
            allowExpired: true, verifySources: false);
        if (manifest.Map != expectedMap) throw new SourceFileChangedException();
        VerifyStageSnapshot(stageDirectory, manifest, manifestRevision, verifySources: false);
        // Do not require the current project to still exist: cleanup must work
        // after navigation or a source edit, using the original exact capture.
        DeleteStageDirectory(stageDirectory, resultId);
        return new(resultId, Discarded: true, AlreadyMissing: false);
    }

    private static FeatureQueryPreviewResponse BuildPreviewResponse(MapSourceMapCapture map, string resultId,
        IReadOnlyList<PreviewLayerData> layers)
    {
        FeatureQueryPreviewDataLayer[] result = layers.Select(layer => layer.ToResponse()).ToArray();
        bool truncated = result.Any(layer => layer.Truncated);
        return new(map, resultId, "ProjectWorld", result, PreviewBudget,
            result.Sum(layer => (long)layer.TotalFeatureCount), result.Sum(layer => layer.ReturnedFeatureCount),
            result.Sum(layer => layer.TotalCoordinateCount), result.Sum(layer => layer.ReturnedCoordinateCount),
            truncated, truncated
                ? ["预览达到共享预算，仅显示部分完整要素；各图层已列出实际总数和显示数。完整导出仍包含全部查询结果。"]
                : []);
    }

    private static bool HasJsonType(JsonElement value, string expected) =>
        value.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String &&
        type.GetString() == expected;

    private static long CountGeometryCoordinates(JsonElement geometry)
    {
        if (geometry.ValueKind == JsonValueKind.Null) return 0;
        if (geometry.ValueKind != JsonValueKind.Object ||
            !geometry.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("查询暂存包含无效的 GeoJSON 几何，请重新查询。");
        if (type.GetString() == "GeometryCollection")
        {
            if (!geometry.TryGetProperty("geometries", out JsonElement children) ||
                children.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("查询暂存的几何集合无效。");
            long total = 0;
            foreach (JsonElement child in children.EnumerateArray())
                total = checked(total + CountGeometryCoordinates(child));
            return total;
        }
        int depth = type.GetString() switch
        {
            "Point" => 0,
            "MultiPoint" or "LineString" => 1,
            "MultiLineString" or "Polygon" => 2,
            "MultiPolygon" => 3,
            _ => throw new InvalidDataException("查询暂存包含不支持的 GeoJSON 几何类型。")
        };
        if (!geometry.TryGetProperty("coordinates", out JsonElement coordinates))
            throw new InvalidDataException("查询暂存缺少几何坐标。");
        return CountCoordinateArray(coordinates, depth);
    }

    private static long CountCoordinateArray(JsonElement coordinates, int depth)
    {
        if (coordinates.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("查询暂存的几何坐标结构无效。");
        if (coordinates.GetArrayLength() == 0) return 0;
        if (depth == 0)
        {
            if (coordinates.GetArrayLength() < 2)
                throw new InvalidDataException("查询暂存的坐标至少需要两个分量。");
            foreach (JsonElement component in coordinates.EnumerateArray())
                if (component.ValueKind != JsonValueKind.Number || !component.TryGetDouble(out double value) ||
                    !double.IsFinite(value))
                    throw new InvalidDataException("查询暂存含有非有限坐标。");
            return 1;
        }
        long total = 0;
        foreach (JsonElement child in coordinates.EnumerateArray())
            total = checked(total + CountCoordinateArray(child, depth - 1));
        return total;
    }

    private static long SerializedSize<T>(T value)
    {
        using var stream = new JsonSizeStream();
        JsonSerializer.Serialize(stream, value, BackendJson.Options);
        return stream.Length;
    }

    private sealed class JsonSizeStream : Stream
    {
        private long length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => length;
        public override long Position { get => length; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => length = checked(length + count);
        public override void Write(ReadOnlySpan<byte> buffer) => length = checked(length + buffer.Length);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
