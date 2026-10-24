using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ras.Layers;
using Ras.Observables;

namespace Ras.Backend;

public sealed record ManningEditorData(BoundaryChoice[] Classifications, ModelRow[] Rows, string? Warning);

public static partial class ModelEditorAdapter
{
    private static ManningEditor? Manning(ModelContext context, string asset, string revision, string? state) =>
        context.Layer is NValueLayer ? new(context, asset, revision, state) : null;

    public static async Task<ModelSnapshot> TransformManningAsync(ManningTransformRequest request)
    {
        ModelSaveRequest model = request.Model;
        VerifyKind(model);
        if (string.IsNullOrEmpty(model.ProjectPath)) throw new ArgumentException("Manning edits require the owning project.");
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
        using var context = new ModelContext(model);
        var editor = Manning(context, model.AssetPath, model.Revision, model.SourceState) ?? throw new ArgumentException("This model is not a Manning layer.");
        ApplyFields(context, null, model.Fields);
        editor.Apply(model.Fields, model.Rows);
        switch (request.Operation)
        {
            case "refresh": break;
            case "setNLCD": editor.SetNLCD(); break;
            default: throw new ArgumentException("Unknown Manning operation.");
        }
        var snapshot = await Snapshot(context, model.Revision, true, null, editor);
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
        return snapshot;
    }

    private sealed class ManningEditor
    {
        internal const string Mode = "ManningsNEditor/NValueLayer_Mode";
        internal const string Constant = "ConstantManningsNEditor/NValueLayer_ConstantValue";
        internal const string Association = "ClassificationLayerManningNEditor/NValueLayer_ClassificationLayer";
        internal const string Table = "ClassificationLayerManningNEditor/InterpretationTable";
        private sealed record DraftState(string Kind, string Asset, string Revision, string Classification, bool AssociationEdited, NValueSource Mode, float Constant, Dictionary<string, string> Dependencies);
        private readonly NValueLayer layer;
        private readonly ObservableNValueLayer observable;
        private readonly ModelContext context;
        private readonly string asset, revision;
        private readonly Dictionary<string, string> dependencies;
        private bool associationEdited;
        public bool CanSetDefaults => layer.Mode == NValueSource.ClassificationLayer && observable.ClassificationLayer != null;
        public static bool ContainsField(string field) => field is Mode or Constant or Association;
        public ManningEditor(ModelContext context, string asset, string revision, string? sourceState)
        {
            this.context = context; layer = (NValueLayer)context.Layer; observable = (ObservableNValueLayer)context.Observable;
            this.asset = Path.GetFullPath(asset); this.revision = revision;
            dependencies = Dependencies();
            if (sourceState != null)
            {
                var state = JsonSerializer.Deserialize<DraftState>(sourceState, BackendJson.Options) ?? throw new ArgumentException("Missing Manning draft state.");
                if (state.Kind != "Manning" || state.Asset != this.asset || state.Revision != revision) throw new ArgumentException("The Manning draft belongs to another model revision.");
                if (!SameDependencies(state.Dependencies)) throw new SourceFileChangedException();
                if (!Enum.IsDefined(state.Mode) || !float.IsFinite(state.Constant) || state.Constant < 0) throw new ArgumentException("Invalid Manning draft state.");
                layer.Mode = state.Mode; layer.ConstantValue = state.Constant;
                associationEdited = state.AssociationEdited;
                if (associationEdited) SetAssociation(state.Classification);
                else if (state.Classification != ClassificationID()) throw new SourceFileChangedException();
            }
        }
        private Dictionary<string, string> Dependencies() => context.Project.ClassificationLayers.Select(item => SourceFilePaths.Canonical(item.Filename))
            .Distinct(StringComparer.Ordinal).ToDictionary(path => path, Revision, StringComparer.Ordinal);
        private bool SameDependencies(Dictionary<string, string> incoming) => incoming.Count == dependencies.Count && dependencies.All(pair => incoming.TryGetValue(pair.Key, out var value) && value == pair.Value);
        public void VerifyDependencies()
        {
            var current = Dependencies();
            if (!SameDependencies(current)) throw new SourceFileChangedException();
        }
        private string ClassificationID() => observable.ClassificationLayer is { } classification ? Path.GetFullPath(classification.Layer.Filename)
            : string.IsNullOrEmpty(layer.ClassificationLayerName) ? "" : "missing:" + layer.ClassificationLayerName;
        private void SetAssociation(string id)
        {
            if (id.Length == 0) observable.ClassificationLayer = null!;
            else
            {
                var selected = observable.Project.Classifications.SingleOrDefault(item => Path.GetFullPath(item.Layer.Filename) == id)
                    ?? throw new ArgumentException("The selected classification layer no longer exists.");
                if (selected.Layer.HasLoadErrors) throw new IOException("The selected classification layer has load errors.");
                observable.ClassificationLayer = selected;
            }
        }
        private string RowID(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(asset + "\n" + revision + "\n" + key)));
        public Dictionary<string, string> Fields() => new()
        {
            [Mode] = layer.Mode.ToString(), [Constant] = layer.ConstantValue.ToString("R", CultureInfo.InvariantCulture), [Association] = ClassificationID()
        };
        public string State() => JsonSerializer.Serialize(new DraftState("Manning", asset, revision, ClassificationID(), associationEdited, layer.Mode, layer.ConstantValue, dependencies), BackendJson.Options);
        public ManningEditorData Snapshot()
        {
            string? warning = !string.IsNullOrEmpty(layer.ClassificationLayerName) && observable.ClassificationLayer == null
                ? "The associated classification layer is missing. Its stored name and interpretation table are preserved."
                : null;
            return new(context.Project.ClassificationLayers.Select(item => new BoundaryChoice(Path.GetFullPath(item.Filename), item.Name)).ToArray(),
                layer.InterpretationTable.RawData.Select(pair => new ModelRow(RowID(pair.Key), new() { ["Key"] = pair.Key, ["Value"] = pair.Value.ToString("R", CultureInfo.InvariantCulture) })).ToArray(), warning);
        }
        public void Apply(Dictionary<string, string> fields, Dictionary<string, ModelRow[]>? rows)
        {
            // Restore the previous draft association before its table; then reconcile
            // any new selection. Common class names retain their edited values.
            if (rows != null)
            {
                if (rows.Any(pair => pair.Key != Table && pair.Value.Length != 0)) throw new ArgumentException("Unsupported Manning table.");
                if (rows.TryGetValue(Table, out var values))
                {
                    var expected = layer.InterpretationTable.RawData.Keys.ToDictionary(RowID, key => key, StringComparer.Ordinal);
                    if (values.Length != expected.Count || values.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != values.Length)
                        throw new ArgumentException("Manning interpretation rows cannot be inserted or removed directly.");
                    foreach (var row in values)
                    {
                        if (!expected.TryGetValue(row.Id, out var key) || row.Values.Count != 2 || !row.Values.TryGetValue("Key", out var supplied) || supplied != key || !row.Values.TryGetValue("Value", out var value))
                            throw new ArgumentException("Manning classification keys are read-only.");
                        observable.InterpretationTable.Rows.Single(item => item.Key == key).Value = (float)Parse(value, typeof(float), true);
                    }
                }
            }
            if (fields.TryGetValue(Constant, out var constant))
            {
                float value = (float)Parse(constant, typeof(float), true);
                if (value < 0) throw new ValidationException("The Manning's N value is invalid.");
                layer.ConstantValue = value;
            }
            if (fields.TryGetValue(Mode, out var mode) && mode != layer.Mode.ToString())
            {
                if (mode is not ("Constant" or "ClassificationLayer")) throw new ArgumentException("This Manning mode is not offered by the original editor.");
                layer.Mode = Enum.Parse<NValueSource>(mode);
                if (layer.Mode == NValueSource.ClassificationLayer && observable.ClassificationLayer != null)
                { observable.ClassificationLayer = observable.ClassificationLayer; associationEdited = true; }
            }
            if (fields.TryGetValue(Association, out var association) && association != ClassificationID())
            { SetAssociation(association); associationEdited = true; }
        }
        public void SetNLCD()
        {
            if (!CanSetDefaults) throw new ArgumentException("Select a classification layer before applying NLCD values.");
            // Reconciliation used to happen implicitly in the observer constructor.
            // Perform it for this explicit edit so a missing/stale table is repairable
            // without making model observation destructive.
            observable.ClassificationLayer = observable.ClassificationLayer;
            associationEdited = true;
            // ClassificationLayerManningNEditor.SetDefaultNValues: exact source
            // names and values. Unrecognized user classifications remain unchanged.
            Dictionary<string, float> defaults = new(StringComparer.Ordinal)
            {
                ["Open Water"] = .035f, ["Perennial Ice/Snow"] = .022f, ["Developed, Open Space"] = .04f,
                ["Developed, Low Intensity"] = .06f, ["Developed, Medium Intensity"] = .1f, ["Developed, High Intensity"] = .16f,
                ["Barren Land Rock/Sand/Clay"] = .026f, ["Deciduous Forest"] = .15f, ["Evergreen Forest"] = .12f,
                ["Mixed Forest"] = .14f, ["Dwarf Scrub"] = .036f, ["Shrub/Scrub"] = .11f,
                ["Grassland/Herbaceous"] = .04f, ["Sedge/Herbaceous"] = .04f, ["Lichens"] = .027f,
                ["Moss"] = .025f, ["Pasture/Hay"] = .04f, ["Cultivated Crops"] = .04f,
                ["Woody Wetlands"] = .1f, ["Emergent Herbaceous Wetlands"] = .07f
            };
            foreach (var row in observable.InterpretationTable.Rows)
                if (defaults.TryGetValue(row.Key, out var value)) row.Value = value;
        }
    }
}
