using System.Globalization;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Vectors;
using H5Assist;
using Ras.Layers;
using Ras.Layers.Classifiers;

namespace Ras.Backend;

public sealed record ClassificationLegend(int Id, string Name, string Color);
public sealed record ClassificationMapData(SpatialGridPreview? Raster, ClassificationLegend[] Legend,
    SpatialGridPreview? SelectedRaster = null, string? SelectedInputId = null, string? Projection = null);
public sealed record ClassificationMapRequest(ModelSaveRequest Model);

public static partial class ModelEditorAdapter
{
    public static Task<ClassificationMapData> ClassificationMapAsync(ClassificationMapRequest request)
    {
        CheckClassificationImport(request.Model);
        using var context = new ModelContext(request.Model);
        var editor = Classifications(context, request.Model.AssetPath, request.Model.Revision, request.Model.SourceState) ?? throw new ArgumentException("This model is not a classification layer.");
        ApplyFields(context, null, request.Model.Fields); editor.Apply(request.Model.Fields, request.Model.Rows);
        editor.VerifyDependencies();
        var result = RenderClassification((ClassificationLayer)context.Layer, editor.SelectedResampler,
            editor.SelectedInputID, context.Project.Projection?.ToString());
        editor.VerifyDependencies(); CheckClassificationImport(request.Model);
        return Task.FromResult(result);
    }
    private static ClassificationMapData RenderClassification(ClassificationLayer layer,
        IResample<int>? selected, string? selectedID, string? projection)
    {
        // ClassificationRasterEditor.FixSymbology installs this retained color
        // map on both layers. Its selected-input callback dims only the full
        // layer, then draws the actual input resampler above it.
        var colors = ColorMaps.NLCD2011();
        var ids = layer.ClassificationMap.Map.Forward.OrderBy(pair => pair.Key).ToArray();
        int[] legendValues = ids.Select(pair => pair.Key).ToArray(), legendPixels = new int[ids.Length];
        colors.ParallelPaint(legendValues, legendPixels);
        var legend = ids.Select((pair, index) => {
            uint argb = unchecked((uint)legendPixels[index]);
            return new ClassificationLegend(pair.Key, pair.Value,
                $"#{(byte)(argb >> 16):X2}{(byte)(argb >> 8):X2}{(byte)argb:X2}{(byte)(argb >> 24):X2}");
        }).ToArray();
        if (layer.LayerCount == 0) return new(null, legend, Projection: projection);
        var extent = layer.ValidExtent;
        if (!double.IsFinite(extent.MinX) || !double.IsFinite(extent.MinY) || !double.IsFinite(extent.MaxX) || !double.IsFinite(extent.MaxY) || extent.MaxX <= extent.MinX || extent.MaxY <= extent.MinY) return new(null, legend, Projection: projection);
        const int width = 256, height = 256;
        var points = new Point[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) points[y * width + x] = new(extent.MinX + (x + .5) * (extent.MaxX - extent.MinX) / width, extent.MaxY - (y + .5) * (extent.MaxY - extent.MinY) / height);
        SpatialGridPreview Sample(IResample<int> source, ColorMap<int> colorMap)
        {
            int[] values = new int[points.Length], pixels = new int[points.Length];
            source.SamplePoints(points, values);
            colorMap.ParallelPaint(values, pixels);
            byte[] rgba = new byte[pixels.Length * 4];
            for (int index = 0; index < pixels.Length; index++)
            {
                uint argb = unchecked((uint)pixels[index]); int offset = index * 4;
                rgba[offset] = (byte)(argb >> 16); rgba[offset + 1] = (byte)(argb >> 8);
                rgba[offset + 2] = (byte)argb; rgba[offset + 3] = (byte)(argb >> 24);
            }
            return new(width, height, rgba, extent.MinX, extent.MinY, extent.MaxX, extent.MaxY, null, null);
        }
        return new(Sample(layer, selected == null ? colors : colors.CloneRatioSaturation(0.5f, 0.5f)),
            legend, selected == null ? null : Sample(selected, colors), selected == null ? null : selectedID, projection);
    }

    private sealed partial class ClassificationEditor
    {
        public string? SelectedInputID => selected;
        public IResample<int>? SelectedResampler => selected != null ? inputs[selected].GetResampler() : null;
        private sealed record NewPolygonInput(string Id, string Name);
        private sealed record GeometryChange(string Operation, string InputID, string PolygonID, ClassificationVertex[] Vertices, bool HasZ, bool HasM, string Classification);
        private sealed record GeometryCommand(GeometryChange[] Changes);
        private sealed record PolygonBaseline(string Id, ClassificationVertex[] Vertices, bool HasZ, bool HasM, string Classification);
        private sealed record LivePolygon(string InputID, PolygonClassifierMod Feature);
        private readonly Dictionary<string, PolygonBaseline[]> originalPolygons = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LivePolygon> livePolygons = new(StringComparer.Ordinal);
        private readonly HashSet<string> createdPolygonInputs = new(StringComparer.Ordinal);
        private readonly List<GeometryCommand> geometryCommands = [];
        private readonly List<GeometryCommand> geometryRedo = [];
        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static ClassificationVertex[] Vertices(Polygon? polygon) => polygon == null ? [] : polygon.Points.Select((point, index) => new ClassificationVertex(point.X, point.Y, polygon.HasZ ? Number(polygon.Z[index]) : null, polygon.HasM ? Number(polygon.M[index]) : null)).ToArray();
        private static bool NewID(string id, string prefix) => id.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(id[prefix.Length..], "N", out _);
        private void RestorePolygonInputs(NewPolygonInput[] values)
        {
            foreach (var value in values)
            {
                if (!NewID(value.Id, "new-input-") || inputs.ContainsKey(value.Id) || string.IsNullOrWhiteSpace(value.Name)) throw new ArgumentException("Invalid polygon input identity.");
                var input = layer.AddNewPolygonLayer(value.Name); inputs.Add(value.Id, input); createdPolygonInputs.Add(value.Id);
            }
        }
        private void CaptureOriginalPolygons()
        {
            foreach (var pair in inputs) if (pair.Value is PolygonClassificationInput polygons)
            {
                originalPolygons[pair.Key] = polygons.Features.Select((feature, index) => new PolygonBaseline(Identity("polygon:" + pair.Key + ":" + index), Vertices(feature.Polygon), feature.Polygon?.HasZ == true, feature.Polygon?.HasM == true, feature.Classification)).ToArray();
                for (int index = 0; index < polygons.Features.Count; index++) livePolygons.Add(originalPolygons[pair.Key][index].Id, new(pair.Key, polygons.Features[index]));
            }
        }
        private static double Ordinate(string? value)
        {
            if (value == null || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)) throw new ArgumentException("Invalid polygon Z or M ordinate.");
            return result;
        }
        private static Polygon BuildPolygon(ClassificationVertex[] vertices, bool hasZ, bool hasM, bool validate)
        {
            if (vertices.Any(vertex => !double.IsFinite(vertex.X) || !double.IsFinite(vertex.Y))) throw new ArgumentException("Polygon coordinates must be finite.");
            var polygon = new Polygon(vertices.Select(vertex => new Point(vertex.X, vertex.Y)).ToArray());
            if (hasZ) polygon.TrackZ(vertices.Select(vertex => Ordinate(vertex.Z)));
            if (hasM) polygon.TrackM(vertices.Select(vertex => Ordinate(vertex.M)));
            if (validate && (polygon.Points.Count < 3 || polygon.Points.Select(point => (point.X, point.Y)).Distinct().Count() < 3 || !double.IsFinite(polygon.Area) || polygon.Area == 0))
                throw new ArgumentException("A polygon needs at least three distinct vertices and a nonzero area.");
            return polygon;
        }
        private Dictionary<string, string> CurrentPolygonClasses() => livePolygons.Where(pair => ((PolygonClassificationInput)inputs[pair.Value.InputID]).Features.Contains(pair.Value.Feature)).ToDictionary(pair => pair.Key, pair => pair.Value.Feature.Classification, StringComparer.Ordinal);
        private void RestorePolygonClasses(Dictionary<string, string> values)
        {
            foreach (var pair in values)
            {
                if (!livePolygons.TryGetValue(pair.Key, out var item) || !((PolygonClassificationInput)inputs[item.InputID]).Features.Contains(item.Feature)) throw new ArgumentException("The selected polygon no longer exists.");
                if (item.Feature.Classification != pair.Value) item.Feature.Classification = pair.Value;
            }
        }
        private void RebuildPolygons()
        {
            livePolygons.Clear();
            foreach (var pair in originalPolygons)
            {
                var input = (PolygonClassificationInput)inputs[pair.Key];
                while (input.Count > 0) input.RemoveAt(input.Count - 1);
                foreach (var baseline in pair.Value)
                {
                    input.Add(BuildPolygon(baseline.Vertices, baseline.HasZ, baseline.HasM, false), baseline.Classification);
                    livePolygons.Add(baseline.Id, new(pair.Key, input.Features[^1]));
                }
            }
            foreach (var command in geometryCommands) foreach (var change in command.Changes) ApplyGeometryChange(change);
        }
        private LivePolygon RequirePolygon(string id)
        {
            if (!livePolygons.TryGetValue(id, out var item) || !((PolygonClassificationInput)inputs[item.InputID]).Features.Contains(item.Feature)) throw new ArgumentException("The selected polygon no longer exists.");
            return item;
        }
        private void ApplyGeometryChange(GeometryChange change)
        {
            if (!inputs.TryGetValue(change.InputID, out var input) || input is not PolygonClassificationInput polygonInput) throw new ArgumentException("The selected polygon input no longer exists.");
            if (change.Operation == "create")
            {
                if (!NewID(change.PolygonID, "new-polygon-") || livePolygons.ContainsKey(change.PolygonID)) throw new ArgumentException("Invalid polygon identity.");
                polygonInput.Add(BuildPolygon(change.Vertices, change.HasZ, change.HasM, true), change.Classification);
                livePolygons.Add(change.PolygonID, new(change.InputID, polygonInput.Features[^1]));
                return;
            }
            var item = RequirePolygon(change.PolygonID);
            if (item.InputID != change.InputID) throw new ArgumentException("The polygon belongs to another input.");
            if (change.Operation == "replace") item.Feature.Polygon = BuildPolygon(change.Vertices, change.HasZ, change.HasM, true);
            else if (change.Operation == "delete") polygonInput.RemoveAt(polygonInput.Features.ToList().IndexOf(item.Feature));
            else throw new ArgumentException("Unknown polygon operation.");
        }
        private void EditGeometry(ClassificationGeometryMutation mutation)
        {
            if (mutation.Operation is "undo" or "redo")
            {
                var from = mutation.Operation == "undo" ? geometryCommands : geometryRedo;
                var to = mutation.Operation == "undo" ? geometryRedo : geometryCommands;
                if (from.Count == 0) throw new ArgumentException("No polygon operation is available to undo or redo.");
                var currentClasses = CurrentPolygonClasses(); var command = from[^1];
                // Source edit operations retain their feature objects. Mirror
                // that lifetime across serialized drafts: a classification
                // changed after a paste/undo-delete must survive the next
                // redo/undo rather than restoring the old command text.
                command = command with { Changes = command.Changes.Select(change =>
                    ((mutation.Operation == "undo" && change.Operation == "create")
                     || (mutation.Operation == "redo" && change.Operation == "delete"))
                    && currentClasses.TryGetValue(change.PolygonID, out var value)
                        ? change with { Classification = value } : change).ToArray() };
                from.RemoveAt(from.Count - 1); to.Add(command);
                RebuildPolygons();
                var current = CurrentPolygonClasses();
                foreach (var pair in currentClasses.Where(pair => current.ContainsKey(pair.Key))) current[pair.Key] = pair.Value;
                if (mutation.Operation == "undo") foreach (var change in command.Changes.Where(change => change.Operation == "delete" && current.ContainsKey(change.PolygonID))) current[change.PolygonID] = change.Classification;
                RestorePolygonClasses(current); return;
            }
            List<GeometryChange> changes = [];
            if (mutation.Operation == "paste")
            {
                if (mutation.InputId == null || !inputs.TryGetValue(mutation.InputId, out var destination)
                    || destination is not PolygonClassificationInput)
                    throw new ArgumentException("Select the exact polygon classification input before pasting features.");
                if (mutation.Features is not { Length: > 0 } features)
                    throw new ArgumentException("The clipboard does not have polygons copied to it.");
                // Validate the entire import before applying any feature. A
                // paste is one source edit operation, with one batch undo.
                foreach (var feature in features)
                {
                    if (feature.Classification == null) throw new ArgumentException("A pasted classification value is missing.");
                    _ = BuildPolygon(feature.Vertices, feature.HasZ, feature.HasM, true);
                    changes.Add(new("create", mutation.InputId, "new-polygon-" + Guid.NewGuid().ToString("N"),
                        feature.Vertices, feature.HasZ, feature.HasM, feature.Classification));
                }
                selected = mutation.InputId;
            }
            else if (mutation.Operation == "create")
            {
                string? inputID = string.IsNullOrEmpty(mutation.InputId) ? null : mutation.InputId;
                if (inputID != null && !inputs.ContainsKey(inputID)) throw new ArgumentException("The selected polygon input no longer exists.");
                if (inputID == null || !inputs.TryGetValue(inputID, out var input) || input is not PolygonClassificationInput)
                {
                    inputID = OrderedIDs().FirstOrDefault(id => inputs[id] is PolygonClassificationInput);
                    if (inputID == null)
                    {
                        string name = "Classification Polygons"; int suffix = 2;
                        while (layer.Layers.Any(item => H5Reader.SanitizeObjectName(item.Name) == H5Reader.SanitizeObjectName(name))) name = "Classification Polygons " + suffix++;
                        inputID = "new-input-" + Guid.NewGuid().ToString("N");
                        RestorePolygonInputs([new(inputID, name)]); originalPolygons.Add(inputID, []);
                    }
                }
                changes.Add(new("create", inputID, "new-polygon-" + Guid.NewGuid().ToString("N"), mutation.Vertices ?? [], mutation.HasZ, mutation.HasM, mutation.Classification ?? "NoData")); selected = inputID;
            }
            else if (mutation.Operation is "replace" or "delete")
            {
                string[] ids = mutation.PolygonIds ?? [];
                if (ids.Length == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || mutation.Operation == "replace" && ids.Length != 1) throw new ArgumentException("Select valid polygons for this operation.");
                foreach (string id in ids)
                {
                    var item = RequirePolygon(id);
                    if (mutation.InputId != null && item.InputID != mutation.InputId) throw new ArgumentException("The polygon belongs to another input.");
                    var polygon = item.Feature.Polygon;
                    changes.Add(new(mutation.Operation, item.InputID, id, mutation.Operation == "replace" ? mutation.Vertices ?? [] : Vertices(polygon), mutation.Operation == "replace" ? mutation.HasZ : polygon.HasZ, mutation.Operation == "replace" ? mutation.HasM : polygon.HasM, item.Feature.Classification));
                }
            }
            else throw new ArgumentException("Unknown polygon operation.");
            foreach (var change in changes) ApplyGeometryChange(change);
            geometryCommands.Add(new(changes.ToArray())); geometryRedo.Clear(); ValidateNames();
        }
    }
}
