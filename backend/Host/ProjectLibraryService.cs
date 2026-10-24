using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ras.Layers;
using Ras.Synthetics;
using Ras.Synthetics.SyntheticParams;
using Ras.Updates;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Backend;

public static class ProjectLibraryService
{
    private sealed record SyntheticDefinition(string Id, string Title, string Description, string[] Tags,
        Func<ISyntheticTestCaseParams> Create);

    private static readonly Lazy<SyntheticDefinition[]> SyntheticDefinitions = new(LoadSyntheticDefinitions);

    public static async Task<ProjectLibraryCatalogResponse> CatalogAsync(ProjectLibraryCatalogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ExampleProjectCollection collection = await ExampleProjectHelper.TryGetAllExamplesAsync()
            ?? throw new InvalidOperationException("The example project catalog could not be loaded.");
        ExampleProjectDetails[] latest = LatestExamples(collection);
        var examples = latest.Select(ProjectRow).ToArray();
        var synthetic = request.DebugMode
            ? SyntheticDefinitions.Value.Select(definition => new ProjectLibraryProjectRow(
                definition.Id, definition.Title, definition.Description, null, definition.Tags, null, "synthetic")).ToArray()
            : Array.Empty<ProjectLibraryProjectRow>();
        EnsureUniqueIDs(examples, "example");
        EnsureUniqueIDs(synthetic, "synthetic");
        return new ProjectLibraryCatalogResponse(examples, synthetic);
    }

    public static async Task<ProjectLibraryOpenResponse> OpenAsync(ProjectLibraryOpenRequest request,
        Action<BackendOperationProgress>? report = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            throw new ArgumentException("A project library identity is required.", nameof(request));
        if (request.Synthetic && !request.DebugMode)
            throw new InvalidOperationException("Synthetic projects are available only in debug mode.");

        string destinationRoot = ValidateDestination(request.DestinationDirectory);
        string title;
        ExampleProjectManifest? manifest = null;
        SyntheticDefinition? synthetic = null;

        if (request.Synthetic)
        {
            synthetic = SyntheticDefinitions.Value.SingleOrDefault(definition =>
                string.Equals(definition.Id, request.ProjectId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("The selected synthetic project is no longer available.");
            title = synthetic.Title;
        }
        else
        {
            ExampleProjectCollection collection = await ExampleProjectHelper.TryGetAllExamplesAsync()
                ?? throw new InvalidOperationException("The example project catalog could not be loaded.");
            ExampleProjectDetails details = LatestExamples(collection).SingleOrDefault(project =>
                string.Equals(project.ManifestJSONKey, request.ProjectId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("The selected example project is no longer available.");
            title = details.Title;
            manifest = await ExampleProjectHelper.TryGetExampleManifestAsync(details)
                ?? throw new InvalidOperationException("The selected example project manifest could not be loaded.");
            ValidateManifestFiles(manifest);
            if (manifest.Details is null ||
                !string.Equals(manifest.Details.ManifestJSONKey, details.ManifestJSONKey, StringComparison.Ordinal) ||
                !string.Equals(manifest.Details.Title, details.Title, StringComparison.Ordinal))
                throw new InvalidDataException("The selected example project manifest does not match its catalog entry.");
        }

        string stageDirectory = Path.Combine(destinationRoot, ".hecras-library-" + Guid.NewGuid().ToString("N") + ".partial");
        Directory.CreateDirectory(stageDirectory);
        try
        {
            report?.Invoke(new BackendOperationProgress(TaskName: request.Synthetic
                ? "Creating synthetic project" : "Downloading example project"));
            if (synthetic is not null)
            {
                // SyntheticTestCases.Save preserves the retained project generation and default export sizes.
                Project saved = SyntheticTestCases.Save(synthetic.Create(), stageDirectory);
                DisposeProjectResources(saved);
            }
            else
            {
                ProgressReporter reporter = new(new LibraryProgressTarget(report));
                OperationResult result = await ExampleProjectHelper.TryGetExampleProjectAsync(manifest!, stageDirectory, reporter);
                if (!result.Result)
                    throw new InvalidOperationException(result.GetConcatenatedMessages().Length == 0
                        ? "The example project download did not complete." : result.GetConcatenatedMessages());
                VerifyCopiedFiles(stageDirectory, manifest!);
            }

            string[] rasFiles = Directory.EnumerateFiles(stageDirectory, "*", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetExtension(path), ".ras", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (rasFiles.Length != 1)
                throw new InvalidDataException("The project library entry did not produce exactly one .ras project.");
            string relativeProjectPath = Path.GetRelativePath(stageDirectory, rasFiles[0]);
            EnsureContained(stageDirectory, Path.GetFullPath(rasFiles[0]));

            // Match the retained UI's Project construction validation before exposing the result.
            var openedProject = new Project(rasFiles[0]);
            try
            {
                if (!File.Exists(openedProject.Filename) || !Path.GetFullPath(openedProject.Filename).Equals(
                        Path.GetFullPath(rasFiles[0]), PathComparison))
                    throw new InvalidDataException("The downloaded project could not be reopened from its staged location.");
            }
            finally { DisposeProjectResources(openedProject); }

            string finalDirectory = MoveStageToUniqueDirectory(stageDirectory, destinationRoot, title);
            stageDirectory = string.Empty;
            string finalProject = Path.GetFullPath(Path.Combine(finalDirectory, relativeProjectPath));
            EnsureContained(finalDirectory, finalProject);
            if (!File.Exists(finalProject))
                throw new IOException("The validated project was not present after publishing its library folder.");
            report?.Invoke(new BackendOperationProgress(Fraction: 1, Message: "Project is ready to open."));
            return new ProjectLibraryOpenResponse(finalProject);
        }
        finally
        {
            if (stageDirectory.Length > 0 && Directory.Exists(stageDirectory))
            {
                try { Directory.Delete(stageDirectory, recursive: true); }
                catch { /* Cleanup is best effort after a failed staged operation. */ }
            }
        }
    }

    internal static ProjectLibraryProjectRow[] SyntheticCatalogForChecks() => SyntheticDefinitions.Value
        .Select(definition => new ProjectLibraryProjectRow(definition.Id, definition.Title,
            definition.Description, null, definition.Tags, null, "synthetic")).ToArray();

    internal static string SaveSyntheticForChecks(string projectId, string directory,
        int terrainCellCount, int analyticalCellCount)
    {
        SyntheticDefinition definition = SyntheticDefinitions.Value.SingleOrDefault(item =>
            string.Equals(item.Id, projectId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The selected synthetic project is no longer available.");
        Project project = SyntheticTestCases.Save(definition.Create(), directory, terrainCellCount, analyticalCellCount);
        string path = project.Filename;
        DisposeProjectResources(project);
        return path;
    }

    internal static string[] ValidateRelativeManifestPaths(IEnumerable<MappedFile> files)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(PathComparison == StringComparison.Ordinal
            ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (MappedFile file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Filename) || Path.IsPathRooted(file.Filename) ||
                !IsSHA256(file.SHA256) || file.Length < 0)
                throw new InvalidDataException("The example project manifest contains an invalid file entry.");
            string normalized = file.Filename.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            if (segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".."))
                throw new InvalidDataException("The example project manifest contains a path outside its project folder.");
            if (!seen.Add(normalized))
                throw new InvalidDataException("The example project manifest contains duplicate file paths.");
            result.Add(normalized);
        }
        return result.ToArray();
    }

    private static ExampleProjectDetails[] LatestExamples(ExampleProjectCollection collection)
    {
        if (collection.AllProjects is null)
            throw new InvalidDataException("The example project catalog has no project list.");
        var latest = new List<ExampleProjectDetails>();
        var byTitle = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ExampleProjectDetails project in collection.AllProjects)
        {
            if (project is null || string.IsNullOrWhiteSpace(project.Title) || string.IsNullOrWhiteSpace(project.ManifestJSONKey))
                continue;
            if (byTitle.TryGetValue(project.Title, out int index))
            {
                if (project.PublishDate > latest[index].PublishDate)
                    latest[index] = project;
            }
            else
            {
                byTitle.Add(project.Title, latest.Count);
                latest.Add(project);
            }
        }
        return latest.ToArray();
    }

    private static ProjectLibraryProjectRow ProjectRow(ExampleProjectDetails project)
    {
        string? thumbnailUrl = null;
        if (!string.IsNullOrWhiteSpace(project.Server) && !string.IsNullOrWhiteSpace(project.Bucket) &&
            !string.IsNullOrWhiteSpace(project.ThumbnailKey) &&
            Uri.TryCreate(project.Server.TrimEnd('/') + "/" + project.Bucket.Trim('/') + "/" + project.ThumbnailKey.TrimStart('/'),
                UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
            thumbnailUrl = uri.AbsoluteUri;
        return new ProjectLibraryProjectRow(project.ManifestJSONKey, project.Title, project.Description ?? string.Empty,
            project.PublishDate, project.Tags?.ToArray() ?? Array.Empty<string>(), thumbnailUrl, "example");
    }

    private static void EnsureUniqueIDs(IEnumerable<ProjectLibraryProjectRow> rows, string kind)
    {
        string[] ids = rows.Select(row => row.Id).ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidDataException($"The {kind} project catalog contains duplicate or missing identities.");
    }

    private static string ValidateDestination(string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Path.IsPathFullyQualified(destinationDirectory))
            throw new ArgumentException("Choose an existing destination folder for the project.", nameof(destinationDirectory));
        string path = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException("The selected project destination folder is no longer available.");
        return path;
    }

    private static void ValidateManifestFiles(ExampleProjectManifest manifest)
    {
        if (manifest.Files is null || manifest.Files.Count == 0)
            throw new InvalidDataException("The example project manifest contains no files.");
        _ = ValidateRelativeManifestPaths(manifest.Files);
    }

    private static void VerifyCopiedFiles(string stageDirectory, ExampleProjectManifest manifest)
    {
        foreach (MappedFile mappedFile in manifest.Files)
        {
            string normalized = mappedFile.Filename.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(stageDirectory, normalized));
            EnsureContained(stageDirectory, path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != mappedFile.Length)
                throw new InvalidDataException("A downloaded example project file did not match its manifest length.");
            using FileStream stream = File.OpenRead(path);
            string sha = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(sha, mappedFile.SHA256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A downloaded example project file did not match its manifest SHA-256.");
        }
    }

    private static string MoveStageToUniqueDirectory(string stageDirectory, string root, string title)
    {
        string safeTitle = SafeDirectoryName(title);
        for (int suffix = 1; suffix <= 1000; suffix++)
        {
            string name = suffix == 1 ? safeTitle : safeTitle + " " + suffix;
            string candidate = Path.Combine(root, name);
            if (Directory.Exists(candidate) || File.Exists(candidate)) continue;
            try
            {
                Directory.Move(stageDirectory, candidate);
                return candidate;
            }
            catch (IOException) when (Directory.Exists(candidate) || File.Exists(candidate)) { }
        }
        throw new IOException("A unique destination folder could not be created for this project.");
    }

    private static string SafeDirectoryName(string title)
    {
        string safe = new string(title.Select(character => Path.GetInvalidFileNameChars().Contains(character) ||
            character is '/' or '\\' or ':' ? '_' : character).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(safe) ? "HEC-RAS Project" : safe;
    }

    private static void EnsureContained(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(fullRoot, PathComparison))
            throw new InvalidDataException("The example project manifest contains a path outside its project folder.");
    }

    private static bool IsSHA256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void DisposeProjectResources(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
            .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
            .Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (Result result in project.Results)
            result.Dispose();
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static SyntheticDefinition[] LoadSyntheticDefinitions()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("editor-schemas.json")
            ?? throw new InvalidOperationException("The bundled synthetic project catalog is unavailable.");
        using JsonDocument document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("syntheticProjects", out JsonElement projects) || projects.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The bundled synthetic project catalog is invalid.");
        var definitions = new List<SyntheticDefinition>();
        foreach (JsonElement project in projects.EnumerateArray())
        {
            string id = project.GetProperty("id").GetString() ?? string.Empty;
            string title = project.GetProperty("title").GetString() ?? string.Empty;
            string description = project.TryGetProperty("description", out JsonElement descriptionValue)
                ? descriptionValue.GetString() ?? string.Empty : string.Empty;
            string[] tags = project.TryGetProperty("tags", out JsonElement tagsValue) && tagsValue.ValueKind == JsonValueKind.Array
                ? tagsValue.EnumerateArray().Select(tag => tag.GetString() ?? string.Empty).ToArray()
                : Array.Empty<string>();
            Func<ISyntheticTestCaseParams>? factory = SyntheticFactory(id);
            if (factory is not null)
                definitions.Add(new SyntheticDefinition(id, title, description, tags, factory));
        }
        if (definitions.Count == 0 || definitions.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new InvalidDataException("The bundled synthetic project catalog has no usable unique entries.");
        return definitions.ToArray();
    }

    private static Func<ISyntheticTestCaseParams>? SyntheticFactory(string id) => id switch
    {
        "TestThackerAction" => Thacker2DParams.Default,
        "TestGPUThackerAction" => Thacker2DParams.GPU,
        "TestGPUSmallThackerAction" => Thacker2DParams.GPUSmall,
        "TestPerlin" => () => new PerlinTerrainParams(),
        "TestCoonsPatchAction" => CoonsRiverParams.Default,
        "TestSemiCircleAction" => SemiCircleParams.Default,
        "TestDamBreakDryAction" => DamBreak1DParams.DryTest,
        "TestDamBreakWetAction" => DamBreak1DParams.WetTest,
        "TestFlowOverTrapezoidAction" => FlowOverTrapezoidParams.Default,
        "TestBumpAction" => BumpParams.Subcritical,
        "TestInOutPlanarAction" => InOutPlanarParams.Default,
        "TestGPUInOutPlanarAction" => InOutPlanarParams.GPU,
        "TestNormalDepthCase" => NormalDepthParams.Default,
        "TestGPUNormalDepthCase" => NormalDepthParams.GPU,
        "TestBackwaterCase" => BackwaterParams.Default,
        "TestSloshBoxAction" => SloshBoxParams.Default,
        "TestVortexAction" => VortexParams.Default,
        _ => null
    };

    private sealed class LibraryProgressTarget(Action<BackendOperationProgress>? report) : IProgressTarget
    {
        public void WriteMessage(ProgressReporter sender, Message message) =>
            report?.Invoke(new BackendOperationProgress(Message: message.EvaluatedMessage,
                TaskName: sender.FullyQualifiedTaskName));

        public void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress) =>
            report?.Invoke(new BackendOperationProgress(Fraction: Math.Clamp(globalProgress, 0, 100) / 100d,
                TaskName: sender.FullyQualifiedTaskName));
    }
}
