using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ras.Layers;

namespace Ras.Backend;

public sealed record SurfaceCreationRequest(string Kind, string ProjectPath, string ProjectXml,
    string Name, string? Revision = null, CoreLayerRequestContext? CoreLayerContext = null);
public sealed record SurfaceCreationInspection(string Revision, bool CanExecute, string? ValidationMessage,
    string SuggestedName, string TargetDirectory);

/// <summary>
/// Creates source Terrain, ClassificationLayer, and NValueLayer HDF files.
/// The Project instance only applies the caller's current XML draft to the
/// newly created file; this service never saves the project document.
/// </summary>
public static class SurfaceCreationService
{
    public static SurfaceCreationInspection Inspect(SurfaceCreationRequest request)
    {
        using var context = new Context(request);
        string? validation = null;
        try { context.ValidateNameAndDestination(); }
        catch (ArgumentException error) { validation = error.Message; }
        return new(context.Revision(), validation is null, validation, context.SuggestedName, context.TargetDirectory);
    }

    public static CreatedModel Create(SurfaceCreationRequest request)
    {
        using var context = new Context(request);
        context.ValidateNameAndDestination();
        string revision = context.Revision();
        if (string.IsNullOrEmpty(request.Revision) || revision != request.Revision)
            throw new SourceFileChangedException();

        bool destinationDirectoryExisted = Directory.Exists(context.TargetDirectory);
        string? stagingDirectory = null;
        bool published = false;
        CoreLayerBase? created = null;
        try
        {
            Directory.CreateDirectory(context.TargetDirectory);
            string stageDirectory = Path.Combine(context.TargetDirectory, ".surface-create-" + Guid.NewGuid().ToString("N"));
            stagingDirectory = stageDirectory;
            Directory.CreateDirectory(stageDirectory);
            string stagePath = Path.Combine(stageDirectory, context.Name + ".h5");

            switch (request.Kind)
            {
                case "newTerrain":
                    var terrain = Terrain.CreateNew(stagePath);
                    created = terrain;
                    context.Project.AddTerrain(terrain);
                    terrain.Project = context.Project;
                    break;
                case "newClassification":
                    var classification = ClassificationLayer.CreateNew(stagePath);
                    created = classification;
                    context.Project.AddClassification(classification);
                    break;
                case "newNValue":
                    var nValue = NValueLayer.CreateNew(stagePath);
                    created = nValue;
                    context.Project.AddNValueLayer(nValue);
                    break;
                default:
                    throw new NotSupportedException("Unsupported surface creation kind.");
            }

            // The retained CreateNew methods write the real HDF model and its
            // default data. Saving after project attachment adds the current
            // project metadata to the HDF header, including unsaved XML edits.
            CoreLayerBase createdModel = created ?? throw new InvalidOperationException("The surface model was not initialized.");
            createdModel.Save();
            context.Detach(createdModel);
            createdModel.Dispose();
            created = null;

            context.Dispose();
            if (context.Revision() != revision) throw new SourceFileChangedException();
            context.ValidateNameAndDestination();

            // Keep staging on the same volume as the destination. File.Move
            // without overwrite is the atomic last collision guard.
            File.Move(stagePath, context.Destination, overwrite: false);
            published = true;
            Directory.Delete(stageDirectory);
            stagingDirectory = null;
            return new CreatedModel(context.Destination, context.Section, null, null, null, context.Name, new());
        }
        catch
        {
            if (created is not null)
            {
                try { context.Detach(created); } catch { /* Context disposal still owns the source object. */ }
                try { created.Dispose(); } catch { /* Preserve the creation failure. */ }
            }
            if (published)
            {
                try { File.Delete(context.Destination); } catch { /* Best effort rollback after a failed publication. */ }
            }
            throw;
        }
        finally
        {
            if (stagingDirectory is { } staging && Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            if (!destinationDirectoryExisted && Directory.Exists(context.TargetDirectory)
                && !Directory.EnumerateFileSystemEntries(context.TargetDirectory).Any())
                Directory.Delete(context.TargetDirectory);
        }
    }

    private sealed class Context : IDisposable
    {
        private readonly SurfaceCreationRequest request;
        private bool disposed;

        public Project Project { get; }
        public string Name => request.Name;
        public string Section { get; }
        public string TargetDirectory { get; }
        public string SuggestedName { get; }
        public string Destination => Path.Combine(TargetDirectory, Name + ".h5");

        public Context(SurfaceCreationRequest request)
        {
            this.request = request;
            string defaultName;
            switch (request.Kind)
            {
                case "newTerrain": Section = "Terrains"; defaultName = "Terrain"; break;
                case "newClassification": Section = "Surface Layers"; defaultName = "Classifications"; break;
                case "newNValue": Section = "Surface Layers"; defaultName = "N Values"; break;
                default: throw new NotSupportedException("Unsupported surface creation kind.");
            }
            if (!Path.IsPathFullyQualified(request.ProjectPath))
                throw new ArgumentException("Open a saved project before creating a surface layer.");

            string projectPath = Path.GetFullPath(request.ProjectPath);
            if (!File.Exists(projectPath)) throw new FileNotFoundException("The project file was not found.", projectPath);
            Project = CoreLayerImportService.OpenProject(projectPath, request.ProjectXml,
                request.CoreLayerContext);
            try
            {
                TargetDirectory = request.Kind switch
                {
                    "newTerrain" => Project.TerrainsDirectory,
                    "newClassification" => Project.ClassificationLayersDirectory,
                    "newNValue" => Project.NValueLayersDirectory,
                    _ => throw new NotSupportedException("Unsupported surface creation kind.")
                };
                SuggestedName = GetUniqueName(TargetDirectory, defaultName);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void ValidateNameAndDestination()
        {
            if (!CoreLayerBase.IsLegalName(Name, out string sourceMessage))
                throw new ArgumentException(sourceMessage);
            if (Name != Name.Trim() || Name is "." or ".." || Name.EndsWith('.')
                || Encoding.UTF8.GetByteCount(Name + ".h5") > 255
                || Name.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
                throw new ArgumentException("Enter a valid name without path separators or reserved filename characters.");

            if (IsNameTaken(TargetDirectory, Name))
                throw new ArgumentException("A model with this name already exists.");
        }

        public string Revision()
        {
            static string HashFile(string path)
            {
                using var input = File.OpenRead(path);
                return Convert.ToHexString(SHA256.HashData(input));
            }

            string projectPath = Path.GetFullPath(request.ProjectPath);
            string payload = JsonSerializer.Serialize(new
            {
                request.Kind,
                request.Name,
                projectPath,
                request.ProjectXml,
                projectHash = HashFile(projectPath)
            });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        }

        public void Detach(CoreLayerBase layer)
        {
            switch (request.Kind)
            {
                case "newTerrain": Project.RemoveTerrain((Terrain)layer); break;
                case "newClassification": Project.RemoveClassification((ClassificationLayer)layer); break;
                case "newNValue": Project.RemoveNValue((NValueLayer)layer); break;
                default: throw new NotSupportedException("Unsupported surface creation kind.");
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var layer in Project.Plans.Cast<CoreLayerBase>().Concat(Project.Geometries).Concat(Project.Terrains)
                .Concat(Project.BoundaryConditions).Concat(Project.NValues).Concat(Project.ClassificationLayers).Cast<IDisposable>().Concat(Project.Results))
                layer.Dispose();
        }

        private static string GetUniqueName(string directory, string baseName)
        {
            if (!IsNameTaken(directory, baseName)) return baseName;
            for (int suffix = 1; suffix <= 1000; suffix++)
            {
                string candidate = $"{baseName} ({suffix})";
                if (!IsNameTaken(directory, candidate)) return candidate;
            }
            throw new IOException("Too many files use the default surface-layer name.");
        }

        private static bool IsNameTaken(string directory, string name)
        {
            if (!Directory.Exists(directory)) return false;
            string fileName = name + ".h5";
            return Directory.EnumerateFileSystemEntries(directory).Any(path =>
                string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
