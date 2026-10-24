using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ras.Layers;
using Utility.Files;

namespace Ras.Backend;

public sealed record CreationRequest(string Kind, string ProjectPath, string ProjectXml,
    Dictionary<string, string> Fields, string? Revision = null,
    CoreLayerRequestContext? CoreLayerContext = null, Guid? ProjectSessionID = null);
public sealed record CreationChoice(string Id, string Title);
public sealed record CreationWindow(DateTimeOffset Start, DateTimeOffset End);
public sealed record BoundaryCreationDefaults(string Name, DateTimeOffset Start, DateTimeOffset End);
public sealed record CreationInspection(string Revision, bool CanExecute, string? ValidationMessage,
    string? Warning, Dictionary<string, CreationChoice[]> Choices, Dictionary<string, CreationWindow> BoundaryTimeWindows,
    BoundaryCreationDefaults? BoundaryDefaults, Guid? ProjectSessionID, string ProjectPath);
public sealed record CreatedModel(string AssetPath, string Section, string? Group, string? Element,
    string? PrimaryKey, string Name, Dictionary<string, string> Associations);

// These operations use the same CoreLayerBase constructors, associations and
// HDF writers as Project.AddNew*. Only publication of the new file is staged.
// Existing models and the caller's project XML are never saved here.
public static class ModelCreationService
{
    public static CreationInspection Inspect(CreationRequest request)
    {
        using var context = new Context(request);
        string? validation = null;
        string? warning = null;
        try { context.Validate(out warning); }
        catch (ArgumentException error) { validation = error.Message; }
        string revisionBeforeDefaults = context.Revision();
        BoundaryCreationDefaults? boundaryDefaults = request.Kind == "newBoundary" ? context.BoundaryDefaults() : null;
        string revision = context.Revision();
        if (revision != revisionBeforeDefaults) throw new SourceFileChangedException();
        return new(revision, validation == null, validation, warning,
            context.Choices(), context.Project.BoundaryConditions.ToDictionary(
                item => Path.GetFullPath(item.Filename), item => new CreationWindow(ToOffset(item.StartTime), ToOffset(item.EndTime))),
            boundaryDefaults, request.ProjectSessionID, request.ProjectPath);
    }

    public static CreatedModel Create(CreationRequest request)
    {
        using var context = new Context(request);
        context.Validate(out _);
        string revision = context.Revision();
        if (request.Revision == null || revision != request.Revision) throw new SourceFileChangedException();
        string directory = Path.GetDirectoryName(context.Destination)!;
        Directory.CreateDirectory(directory);
        string stagingDirectory = Path.Combine(directory, ".model-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            string staging = Path.Combine(stagingDirectory, context.Name + ".h5");
            var associations = new Dictionary<string, string>();
            string? group = null, element = null, primaryKey = null;
            CoreLayerBase created;
            switch (request.Kind)
            {
                case "newGeometry":
                    var geometry = Geometry.CreateNew(staging);
                    context.Project.AddGeometry(geometry);
                    if (context.Terrain is Terrain terrain) geometry.GeometryAssociation.Terrain = terrain;
                    if (context.NValues is NValueLayer nvalues) geometry.GeometryAssociation.NValueLayer = nvalues;
                    associations["Terrain"] = context.Terrain?.Name ?? "";
                    associations["N_Values"] = context.NValues?.Name ?? "";
                    group = "GeometryAssociations"; element = "GeometryAssociation"; primaryKey = "Geometry";
                    created = geometry;
                    break;
                case "newBoundary":
                    var boundary = BoundaryCondition.CreateNew(staging);
                    context.Project.AddBoundaryCondition(boundary);
                    boundary.StartTime = context.Start;
                    boundary.EndTime = context.End;
                    created = boundary;
                    break;
                case "newPlan":
                    var plan = Plan.CreateNew(staging);
                    context.Project.AddPlan(plan);
                    var association = plan.AssertGetPlanAssocation();
                    association.Geometry = context.Geometry ?? throw new ArgumentException("Select an existing project model for this association.");
                    association.BoundaryCondition = context.Boundary ?? throw new ArgumentException("Select an existing project model for this association.");
                    plan.TimeWindowMode = context.InheritWindow ? PlanTimeWindowMode.BoundaryCondition : PlanTimeWindowMode.Plan;
                    plan.InternalStartTime = context.Start;
                    plan.InternalComputeDurationSeconds = (context.End - context.Start).TotalSeconds;
                    associations["Geometry"] = context.Geometry!.Name;
                    associations["BoundaryCondition"] = context.Boundary!.Name;
                    group = "PlanAssociations"; element = "PlanAssociation"; primaryKey = "Plan";
                    created = plan;
                    break;
                default: throw new NotSupportedException("Unsupported model creation kind.");
            }
            created.Save();
            created.Dispose();
            // Recheck selected source bytes and the project before publishing.
            // File.Move without overwrite is the final collision guard.
            if (context.Revision() != revision) throw new SourceFileChangedException();
            context.ValidateDestination();
            File.Move(staging, context.Destination, overwrite: false);
            return new(context.Destination, context.Section, group, element, primaryKey, context.Name, associations);
        }
        finally { Directory.Delete(stagingDirectory, recursive: true); }
    }

    private static DateTimeOffset ToOffset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Local));

    private sealed class Context : IDisposable
    {
        private readonly CreationRequest request;
        private readonly string[] boundaryConditionSourcePaths;
        public Project Project { get; }
        public string Name { get; }
        public string Section { get; }
        public string Destination { get; }
        public Terrain? Terrain { get; private set; }
        public NValueLayer? NValues { get; private set; }
        public Geometry? Geometry { get; private set; }
        public BoundaryCondition? Boundary { get; private set; }
        public DateTime Start { get; private set; }
        public DateTime End { get; private set; }
        public bool InheritWindow { get; private set; }
        private string Field(string schema, string field) => request.Fields.GetValueOrDefault(schema + "/" + field, "");
        public Context(CreationRequest request)
        {
            this.request = request;
            Section = request.Kind switch { "newGeometry" => "Geometries", "newPlan" => "Plans", "newBoundary" => "Boundary Conditions", _ => throw new NotSupportedException("This tool is not connected to model creation.") };
            if (!Path.IsPathFullyQualified(request.ProjectPath)) throw new ArgumentException("Open a saved project before creating a model.");
            Project = CoreLayerImportService.OpenProject(Path.GetFullPath(request.ProjectPath),
                request.ProjectXml, request.CoreLayerContext);
            try
            {
                boundaryConditionSourcePaths = request.Kind == "newBoundary"
                    ? Project.BoundaryConditions.Select(item => item.Filename)
                        .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray()
                    : Array.Empty<string>();
                Name = request.Kind switch { "newGeometry" => Field("NewGeometryControl", "GeometryName"), "newPlan" => Field("NewPlanControl", "PlanName"), _ => Field("NewBoundaryConditionControl", "BoundaryConditionName") };
                string directory = request.Kind switch { "newGeometry" => Project.GeometriesDirectory, "newPlan" => Project.PlansDirectory, _ => Project.BoundaryConditionDirectory };
                // Never combine an unvalidated user filename into a write path.
                Destination = ValidName(Name) ? Path.Combine(directory, Name + ".h5") : "";
            }
            catch { Dispose(); throw; }
        }
        private static bool ValidName(string value) => !string.IsNullOrWhiteSpace(value)
            && value == value.Trim() && !value.EndsWith('.') && !value.StartsWith('.')
            && Encoding.UTF8.GetByteCount(value + ".h5") <= 255
            && !value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c));
        public void ValidateDestination()
        {
            if (!ValidName(Name)) throw new ArgumentException("Enter a valid model name without path separators or reserved filename characters.");
            string directory = Path.GetDirectoryName(Destination)!;
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any(path => string.Equals(Path.GetFileName(path), Name + ".h5", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A model with this name already exists.");
        }
        private T? Select<T>(IEnumerable<T> items, string key, bool required) where T : CoreLayerBase
        {
            string value = request.Fields.GetValueOrDefault(key, "");
            if (value.Length == 0 && !required) return null;
            var selected = items.FirstOrDefault(item => Path.GetFullPath(item.Filename) == value)
                ?? throw new ArgumentException("Select an existing project model for this association.");
            if (selected.HasLoadErrors) throw new IOException("The selected model has load errors. Resolve them before creating a related model.");
            return selected;
        }
        public void Validate(out string? warning)
        {
            warning = null;
            ValidateDestination();
            if (request.Kind == "newGeometry")
            {
                Terrain = Select(Project.Terrains, "NewGeometryControl/SelectedTerrain", false);
                NValues = Select(Project.NValues, "NewGeometryControl/SelectedNValue", false);
                return;
            }
            string schema = request.Kind == "newBoundary" ? "NewBoundaryConditionControl" : "NewPlanControl";
            if (request.Kind == "newPlan")
            {
                Geometry = Select(Project.Geometries, schema + "/SelectedGeometry", true);
                Boundary = Select(Project.BoundaryConditions, schema + "/SelectedBoundaryCondition", true);
                string mode = Field(schema, "TimeWindowMode");
                if (mode is not ("BoundaryCondition" or "Plan")) throw new ArgumentException("Select a valid time window mode.");
                InheritWindow = mode == "BoundaryCondition";
            }
            if (InheritWindow) { Start = Boundary!.StartTime; End = Boundary.EndTime; }
            else
            {
                Start = ParseDate(Field(schema, "StartTime")); End = ParseDate(Field(schema, "EndTime"));
            }
            if (End <= Start) throw new ArgumentException("End time must be later than start time.");
            if (Boundary != null && (Start < Boundary.StartTime || End > Boundary.EndTime))
                warning = "The custom time window extends beyond the boundary condition time window.";
        }
        public BoundaryCreationDefaults BoundaryDefaults()
        {
            string uniquePath = FileHelpers.GetUniqueFileName(
                Path.Combine(Project.BoundaryConditionDirectory, "Boundary Condition"),
                ".h5", path => File.Exists(path));
            Project.GetReasonableProjectTimeWindow(out var start, out var end);
            return new(Path.GetFileNameWithoutExtension(uniquePath), ToOffset(start), ToOffset(end));
        }
        private static DateTime ParseDate(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.LocalDateTime : throw new ArgumentException("Enter a valid start and end time.");
        public Dictionary<string, CreationChoice[]> Choices()
        {
            static CreationChoice[] ChoicesFor(IEnumerable<CoreLayerBase> layers) => layers.Select(item => new CreationChoice(Path.GetFullPath(item.Filename), item.Name)).ToArray();
            return new() {
                ["NewGeometryControl/SelectedTerrain"] = ChoicesFor(Project.Terrains),
                ["NewGeometryControl/SelectedNValue"] = ChoicesFor(Project.NValues),
                ["NewPlanControl/SelectedGeometry"] = ChoicesFor(Project.Geometries),
                ["NewPlanControl/SelectedBoundaryCondition"] = ChoicesFor(Project.BoundaryConditions)
            };
        }
        public string Revision()
        {
            static string HashFile(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
            IEnumerable<string> sourcePaths = new CoreLayerBase?[] { Terrain, NValues, Geometry, Boundary }
                .Where(item => item != null).Cast<CoreLayerBase>().Select(item => item.Filename);
            if (request.Kind == "newBoundary") sourcePaths = sourcePaths.Concat(boundaryConditionSourcePaths);
            var sources = sourcePaths.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new { path, hash = HashFile(path) });
            string payload = JsonSerializer.Serialize(new { request.Kind, request.ProjectXml, fields = request.Fields.OrderBy(item => item.Key, StringComparer.Ordinal), project = HashFile(request.ProjectPath), sources });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        }
        public void Dispose()
        {
            foreach (var layer in Project.Plans.Cast<CoreLayerBase>().Concat(Project.Geometries).Concat(Project.Terrains)
                .Concat(Project.BoundaryConditions).Concat(Project.NValues).Concat(Project.ClassificationLayers).Cast<IDisposable>().Concat(Project.Results)) layer.Dispose();
        }
    }
}
