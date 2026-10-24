using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Globalization;
using H5Assist;
using Ras.Layers;
using Ras.Layers.Classifiers;

namespace Ras.Backend;

public sealed record ClassificationInputData(string Id, string Scope, string Name, string Type, bool CanRemove, string? Filename,
    string? TableId, ModelRow[] Rows, ClassificationPolygonData[] Polygons, string? Extent);
public sealed record ClassificationVertex(double X, double Y, string? Z = null, string? M = null);
public sealed record ClassificationPolygonData(string Id, string Scope, string Classification, int VertexCount, ClassificationVertex[] Vertices, bool HasZ, bool HasM);
public sealed record ClassificationClipboardPolygon(ClassificationVertex[] Vertices, bool HasZ, bool HasM, string Classification);
public sealed record ClassificationGeometryMutation(string Operation, string? InputId = null, string[]? PolygonIds = null, ClassificationVertex[]? Vertices = null, bool HasZ = false, bool HasM = false, string? Classification = null, ClassificationClipboardPolygon[]? Features = null);
public sealed record ClassificationEditorData(ClassificationInputData[] Inputs, string? SelectedId, string[] Classes, bool CanUndoGeometry = false, bool CanRedoGeometry = false);
public sealed record ClassificationTransformRequest(ModelSaveRequest Model, string Operation, string? ItemId = null, int? From = null, int? To = null, ClassificationGeometryMutation? Geometry = null);

public static partial class ModelEditorAdapter
{
    private static ClassificationEditor? Classifications(ModelContext context, string asset, string revision, string? state) =>
        context.Layer is ClassificationLayer layer ? new(layer, asset, revision, state) : null;
    public static async Task<ModelSnapshot> TransformClassificationAsync(ClassificationTransformRequest request)
    {
        var model = request.Model;
        VerifyKind(model);
        if (model.ProjectPath == null) throw new ArgumentException("Classification edits require the owning project.");
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
        using var context = new ModelContext(model);
        var editor = Classifications(context, model.AssetPath, model.Revision, model.SourceState) ?? throw new ArgumentException("This model is not a classification layer.");
        ApplyFields(context, null, model.Fields); editor.Apply(model.Fields, model.Rows);
        editor.Transform(request);
        var snapshot = await Snapshot(context, model.Revision, true, null, classification: editor);
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
        return snapshot;
    }
    private sealed partial class ClassificationEditor
    {
        internal const string SourceField = "ClassificationLayersEditor/ClassificationLayer_SelectedClassificationInput";
        private sealed record DraftState(string Kind, string Asset, string Revision, string[] InputOrder, Dictionary<string, string> Dependencies, NewPolygonInput[]? NewPolygonInputs = null, GeometryCommand[]? Geometry = null, GeometryCommand[]? Redo = null, Dictionary<string, string>? PolygonClasses = null);
        private readonly ClassificationLayer layer;
        private readonly string asset, revision;
        private readonly Dictionary<string, IClassificationInput> inputs;
        private readonly Dictionary<string, string> dependencies;
        private string? selected;
        public int? SelectedIndex => selected == null ? null : Array.IndexOf(OrderedIDs(), selected);
        private string Identity(string suffix) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(asset + "\n" + revision + "\n" + suffix)));
        public static bool ContainsField(string field) => field == SourceField || field.StartsWith("ClassificationInputs/", StringComparison.Ordinal);
        public ClassificationEditor(ClassificationLayer layer, string asset, string revision, string? state)
        {
            this.layer = layer; this.asset = Path.GetFullPath(asset); this.revision = revision;
            inputs = layer.Layers.Select((input, index) => (input, id: Identity("input:" + index))).ToDictionary(item => item.id, item => item.input, StringComparer.Ordinal);
            dependencies = ReadDependencies();
            if (state != null)
            {
                var draft = JsonSerializer.Deserialize<DraftState>(state, BackendJson.Options) ?? throw new ArgumentException("Missing classification draft state.");
                if (draft.Kind != "Classification" || draft.Asset != this.asset || draft.Revision != revision) throw new ArgumentException("The classification draft belongs to another model revision.");
                if (!SameDependencies(draft.Dependencies)) throw new SourceFileChangedException();
                RestorePolygonInputs(draft.NewPolygonInputs ?? []);
                CaptureOriginalPolygons();
                if (draft.InputOrder.Distinct(StringComparer.Ordinal).Count() != draft.InputOrder.Length || draft.InputOrder.Any(id => !inputs.ContainsKey(id))) throw new ArgumentException("Invalid classification input identities.");
                foreach (var pair in inputs.Where(pair => !draft.InputOrder.Contains(pair.Key)).ToArray()) Remove(pair.Key);
                for (int index = 0; index < draft.InputOrder.Length; index++) layer.Reorder(Array.IndexOf(OrderedIDs(), draft.InputOrder[index]), index);
                geometryCommands.AddRange(draft.Geometry ?? []); geometryRedo.AddRange(draft.Redo ?? []);
                RebuildPolygons(); RestorePolygonClasses(draft.PolygonClasses ?? []);
            }
            else CaptureOriginalPolygons();
        }
        private Dictionary<string, string> ReadDependencies() => inputs.Values.OfType<RasterClassificationInput>().Select(input => SourceFilePaths.Canonical(input.Filename))
            .Distinct(StringComparer.Ordinal).ToDictionary(path => path, path => File.Exists(path) ? Revision(path) : "missing", StringComparer.Ordinal);
        private bool SameDependencies(Dictionary<string, string> value) => value.Count == dependencies.Count && dependencies.All(pair => value.TryGetValue(pair.Key, out var current) && current == pair.Value);
        public void VerifyDependencies() { if (!SameDependencies(ReadDependencies())) throw new SourceFileChangedException(); }
        private string[] OrderedIDs() => layer.Layers.Select(input => inputs.Single(pair => ReferenceEquals(pair.Value, input)).Key).ToArray();
        private static string Scope(string id) => "ClassificationInputs/" + id;
        private static string Table(string id) => Scope(id) + "/RasterClassificationInputProperties/RasterClassificationInput_Classification";
        private string RowID(string inputID, int key) => Identity("map:" + inputID + ":" + key);
        private string PolygonID(string inputID, int index) => livePolygons.Single(pair => pair.Value.InputID == inputID && ReferenceEquals(pair.Value.Feature, ((PolygonClassificationInput)inputs[inputID]).Features[index])).Key;
        private string PolygonScope(string inputID, int index) => Scope(inputID) + "/Polygons/" + PolygonID(inputID, index);
        private static string ClassField(string scope) => scope + "/PolygonClassificationInputProperties/ClassifierMod_Classification";
        public Dictionary<string, string> Fields()
        {
            Dictionary<string, string> result = new() { [SourceField] = selected ?? "" };
            foreach (var id in OrderedIDs())
            {
                var input = inputs[id]; string scope = Scope(id);
                result[scope + "/Name"] = input.Name;
                if (input is RasterClassificationInput)
                {
                    result[scope + "/RasterClassificationInputProperties/RasterClassificationInput_Name"] = input.Name;
                    result[scope + "/RasterClassificationInputProperties/RasterClassificationInput_TypeName"] = input.TypeName;
                }
                if (input is PolygonClassificationInput polygons)
                    for (int index = 0; index < polygons.Features.Count; index++) result[ClassField(PolygonScope(id, index))] = polygons.Features[index].Classification;
                if (input is ConstantClassificationInput constant) result[scope + "/ConstantClassification/Value"] = constant.Value;
            }
            return result;
        }
        public Dictionary<string, string[]> Groups() => OrderedIDs().Where(id => inputs[id] is RasterClassificationInput).ToDictionary(id => Scope(id) + "/Name",
            id => new[] { Scope(id) + "/Name", Scope(id) + "/RasterClassificationInputProperties/RasterClassificationInput_Name" });
        public string State() => JsonSerializer.Serialize(new DraftState("Classification", asset, revision, OrderedIDs(), dependencies, createdPolygonInputs.Select(id => new NewPolygonInput(id, inputs[id].Name)).ToArray(), geometryCommands.ToArray(), geometryRedo.ToArray(), CurrentPolygonClasses()), BackendJson.Options);
        public ClassificationEditorData Snapshot() => new(OrderedIDs().Select(id =>
        {
            var input = inputs[id];
            var raster = input as RasterClassificationInput; var polygon = input as PolygonClassificationInput;
            return new ClassificationInputData(id, Scope(id), input.Name, input.TypeName, input is not PolygonClassificationInput, raster?.Filename,
                raster == null ? null : Table(id), raster?.Classification.GetAllClassifications().Select(pair =>
                    new ModelRow(RowID(id, pair.Key), new() { ["Key"] = pair.Key.ToString(CultureInfo.InvariantCulture), ["Value"] = pair.Value })).ToArray() ?? [],
                polygon?.Features.Select((feature, index) => new ClassificationPolygonData(PolygonID(id, index), PolygonScope(id, index), feature.Classification, feature.Polygon?.Points.Count ?? 0, Vertices(feature.Polygon), feature.Polygon?.HasZ == true, feature.Polygon?.HasM == true)).ToArray() ?? [],
                input is ConstantClassificationInput constant ? constant.ValidExtent.ToString() : null);
        }).ToArray(), selected, layer.ClassificationMap.Values.ToArray(), geometryCommands.Count > 0, geometryRedo.Count > 0);
        public void Apply(Dictionary<string, string> fields, Dictionary<string, ModelRow[]>? rows)
        {
            var expected = Fields();
            foreach (var pair in fields.Where(pair => ContainsField(pair.Key)))
            {
                if (!expected.ContainsKey(pair.Key)) throw new ArgumentException("Unknown classification input field.");
                if (pair.Key == SourceField)
                {
                    if (pair.Value.Length > 0 && !OrderedIDs().Contains(pair.Value)) throw new ArgumentException("The selected classification input no longer exists.");
                    selected = pair.Value.Length == 0 ? null : pair.Value; continue;
                }
                bool applied = false;
                foreach (var id in OrderedIDs())
                {
                    var input = inputs[id]; string scope = Scope(id);
                    if (pair.Key == scope + "/Name")
                    {
                        if (string.IsNullOrWhiteSpace(pair.Value) || string.IsNullOrEmpty(H5Reader.SanitizeObjectName(pair.Value))) throw new ArgumentException("Classification input names must not be empty.");
                        input.Name = pair.Value; applied = true;
                    }
                    if (input is ConstantClassificationInput constant && pair.Key == scope + "/ConstantClassification/Value") { constant.Value = pair.Value; applied = true; }
                    if (input is PolygonClassificationInput polygons)
                        for (int index = 0; index < polygons.Features.Count; index++)
                            if (pair.Key == ClassField(PolygonScope(id, index))) { polygons.Features[index].Classification = pair.Value; applied = true; }
                }
                if (!applied) throw new ArgumentException("The classification input field is read-only.");
            }
            if (rows != null)
            {
                var tables = OrderedIDs().Where(id => inputs[id] is RasterClassificationInput).ToDictionary(Table, id => id, StringComparer.Ordinal);
                if (rows.Any(pair => pair.Value.Length > 0 && !tables.ContainsKey(pair.Key))) throw new ArgumentException("Unsupported classification table.");
                foreach (var table in tables)
                {
                    if (!rows.TryGetValue(table.Key, out var requested)) continue;
                    var raster = (RasterClassificationInput)inputs[table.Value];
                    var mapping = raster.Classification.GetAllClassifications().ToDictionary(pair => RowID(table.Value, pair.Key), pair => pair.Key, StringComparer.Ordinal);
                    if (requested.Length != mapping.Count || requested.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != requested.Length) throw new ArgumentException("Classification mapping rows cannot be inserted or removed.");
                    foreach (var row in requested)
                    {
                        if (!mapping.TryGetValue(row.Id, out int key) || row.Values.Count != 2 || !row.Values.TryGetValue("Key", out var keyText) || keyText != key.ToString(CultureInfo.InvariantCulture) || !row.Values.TryGetValue("Value", out var value))
                            throw new ArgumentException("Classification raster IDs are read-only.");
                        raster.Classification.Set(key, value);
                    }
                }
            }
            ValidateNames();
        }
        private void ValidateNames()
        {
            if (layer.Layers.Select(input => H5Reader.SanitizeObjectName(input.Name)).Distinct(StringComparer.Ordinal).Count() != layer.LayerCount)
                throw new ArgumentException("Classification input names must map to distinct HDF groups.");
        }
        private void Remove(string id)
        {
            if (!inputs.TryGetValue(id, out var input) || !layer.Layers.Contains(input)) throw new ArgumentException("The classification input no longer exists.");
            if (input is PolygonClassificationInput) throw new ArgumentException("Polygon classification inputs cannot be removed by this editor.");
            layer.Remove(input); if (input is IDisposable disposable) disposable.Dispose();
            if (selected == id) selected = null;
        }
        public void Transform(ClassificationTransformRequest request)
        {
            switch (request.Operation)
            {
                case "geometry": EditGeometry(request.Geometry ?? throw new ArgumentException("Missing polygon operation.")); break;
                case "refresh": break;
                case "remove": Remove(request.ItemId ?? throw new ArgumentException("Missing classification input.")); break;
                case "reorder":
                    if (request.From is not { } from || request.To is not { } to || from < 0 || to < 0 || from >= layer.LayerCount || to >= layer.LayerCount) throw new ArgumentException("Invalid classification input order.");
                    layer.Reorder(from, to); break;
                default: throw new ArgumentException("Unknown classification operation.");
            }
            VerifyDependencies();
        }
    }
}
