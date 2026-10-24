using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Ras.Layers;
using Ras.Layers.Results;

namespace Ras.Backend;

public sealed record ResultPlanSettingsRequest(string ProjectPath, string ProjectXml,
    string ResultPath, string Revision);

public sealed record ResultPlanSettingsImageDraft(string Id, string Name, string? Description,
    string? DateLastModified, double X, double Y, byte[] Data);

public sealed record ResultPlanSettingsSaveRequest(string ProjectPath, string ProjectXml,
    string ProjectRevision, string ResultPath, string Revision, string PlanName,
    string? PlanDescription, ResultPlanSettingsImageDraft[] Images);

public sealed record ResultPlanSettingsSaveReceipt(string ResultPath, string PreviousRevision,
    string Revision, string PlanName, string PlanDescription, ResultPlanSettingsSnapshot Snapshot);

public sealed record ResultPlanEmbeddedImage(string Id, string Name, string? Description,
    string? DateLastModified, double X, double Y, long ByteCount, byte[] Data);

/// <summary>
/// Read-only view of the Plan embedded in one exact saved Result. Image IDs
/// are the original Plan HDF5 dataset paths; payloads are read from that Result
/// and are never passed through an editor save/ReplaceImages path.
/// </summary>
public sealed record ResultPlanSettingsSnapshot(string ProjectPath, string ProjectRevision,
    string ResultPath, string Revision, string ResultName, bool HasPlan, string? PlanName,
    string? PlanDescription, string? GeometryName, string? BoundaryConditionName,
    ResultPlanPropertyGroup[] EmbeddedPlanGroups, ResultPlanEmbeddedImage[] Images);

public static class ResultPlanSettingsService
{
    private const int MaximumImageCount = 256;
    private const int MaximumImageBytes = 12 * 1024 * 1024;
    private const long MaximumTotalImageBytes = 64 * 1024 * 1024;

    public static async Task<ResultPlanSettingsSnapshot> InspectAsync(ResultPlanSettingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || !Path.IsPathFullyQualified(request.ProjectPath))
            throw new ArgumentException("Open a saved project before inspecting its Result Plan.");
        if (string.IsNullOrWhiteSpace(request.ProjectXml))
            throw new ArgumentException("The current project XML context is unavailable.");
        if (string.IsNullOrWhiteSpace(request.ResultPath) || !Path.IsPathFullyQualified(request.ResultPath))
            throw new ArgumentException("Select a saved Result file before inspecting its Plan.");
        if (string.IsNullOrWhiteSpace(request.Revision))
            throw new ArgumentException("Refresh the Result before inspecting its Plan.");

        string projectPath = Path.GetFullPath(request.ProjectPath);
        string canonicalProjectPath = SourceFilePaths.Canonical(projectPath);
        string resultPath = SourceFilePaths.Canonical(request.ResultPath);
        if (!File.Exists(canonicalProjectPath))
            throw new FileNotFoundException("The saved project file could not be found.", canonicalProjectPath);
        if (!File.Exists(resultPath))
            throw new FileNotFoundException("The selected Result file could not be found.", resultPath);

        string projectRevision = Hash(canonicalProjectPath);
        string resultRevision = Hash(resultPath);
        VerifyRevision(resultRevision, request.Revision);

        using XmlReader reader = XmlReader.Create(new StringReader(request.ProjectXml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 8 * 1024 * 1024
        });
        XElement metadata = XElement.Load(reader, LoadOptions.None);

        // Keep the caller's path spelling as the project base, matching the
        // existing result context and association readers.
        Project project = new(projectPath);
        try
        {
            project.ApplyEditorMetadata(metadata);
            Result[] matches = project.Results.Where(candidate => !candidate.IsInMemory
                && !string.IsNullOrWhiteSpace(candidate.Filename)
                && SourceFilePaths.SameSourceFile(candidate.Filename, resultPath)).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("The selected file is not exactly one saved Result in the current project.");

            Result result = matches[0];
            Plan? plan = result.Plan;
            ResultPlanPropertyGroup[] groups = plan == null
                ? []
                : ResultPlanContextService.BuildEmbeddedPlanGroups(plan);
            ResultPlanEmbeddedImage[] images = plan == null
                ? []
                : await ReadImagesAsync(plan, resultPath, resultRevision);

            var snapshot = new ResultPlanSettingsSnapshot(projectPath, projectRevision,
                resultPath, resultRevision, result.Name, plan != null, plan?.Name,
                plan?.Description, plan?.Geometry?.Name, plan?.BoundaryCondition?.Name,
                groups, images);

            VerifyFile(canonicalProjectPath, projectRevision);
            VerifyFile(resultPath, resultRevision);
            return snapshot;
        }
        finally
        {
            DisposeProject(project);
        }
    }

    /// <summary>
    /// Applies the editable fields from the Result's embedded Plan to a same-directory
    /// HDF copy, then atomically replaces the selected Result only after the exact
    /// project, Result and embedded Plan identities have been rechecked.
    /// Existing image entries are immutable and must be returned unchanged; the
    /// source Plan Settings UI adds images but does not expose image removal.
    /// </summary>
    public static async Task<ResultPlanSettingsSaveReceipt> SaveAsync(ResultPlanSettingsSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || !Path.IsPathFullyQualified(request.ProjectPath))
            throw new ArgumentException("Open a saved project before saving its Result Plan.");
        if (string.IsNullOrWhiteSpace(request.ProjectXml))
            throw new ArgumentException("The current project XML context is unavailable.");
        if (string.IsNullOrWhiteSpace(request.ProjectRevision))
            throw new ArgumentException("Reload the project before saving its Result Plan.");
        if (string.IsNullOrWhiteSpace(request.ResultPath) || !Path.IsPathFullyQualified(request.ResultPath))
            throw new ArgumentException("Select a saved Result before saving its Plan.");
        if (string.IsNullOrWhiteSpace(request.Revision) || string.IsNullOrWhiteSpace(request.PlanName))
            throw new ArgumentException("Reload the selected Result Plan before saving.");
        if (request.Images is null) throw new ArgumentException("The embedded image draft is unavailable.");

        string projectPath = SourceFilePaths.Canonical(request.ProjectPath);
        string resultPath = SourceFilePaths.Canonical(request.ResultPath);
        if (!File.Exists(projectPath)) throw new FileNotFoundException("The saved project file could not be found.", projectPath);
        if (!File.Exists(resultPath)) throw new FileNotFoundException("The selected Result file could not be found.", resultPath);

        string projectRevision = Hash(projectPath);
        string resultRevision = Hash(resultPath);
        VerifyRevision(projectRevision, request.ProjectRevision);
        VerifyRevision(resultRevision, request.Revision);

        string projectXml = request.ProjectXml;
        XElement metadata;
        using (XmlReader reader = XmlReader.Create(new StringReader(projectXml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 8 * 1024 * 1024
        }))
        {
            metadata = XElement.Load(reader, LoadOptions.None);
        }

        string staging = Path.Combine(Path.GetDirectoryName(resultPath)!, ".result-plan-edit-"
            + Guid.NewGuid().ToString("N") + ".pending");
        try
        {
            File.Copy(resultPath, staging);
            string stagedPlanName;
            string stagedDescription;
            string protectedPlanState;
            Project project = new(projectPath);
            try
            {
                project.ApplyEditorMetadata(metadata);
                Result[] matches = project.Results.Where(candidate => !candidate.IsInMemory
                    && !string.IsNullOrWhiteSpace(candidate.Filename)
                    && SourceFilePaths.SameSourceFile(candidate.Filename, resultPath)).ToArray();
                if (matches.Length != 1)
                    throw new ArgumentException("The selected file is not exactly one saved Result in the current project.");

                using (Result stagedResult = new(staging))
                {
                    stagedResult.Project = project;
                    Plan? plan = stagedResult.Plan;
                    if (plan == null || !plan.IsResult)
                        throw new ArgumentException("The selected Result no longer contains an embedded Plan.");
                    if (!string.Equals(plan.Name, request.PlanName, StringComparison.Ordinal))
                        throw new SourceFileChangedException();

                    protectedPlanState = ProtectedPlanState(plan);
                    List<EmbeddedImage> existing = await plan.EmbeddedImages.LoadImageDataAsync();
                    await ValidateAndAppendImagesAsync(plan, existing, request.Images);
                    plan.Description = request.PlanDescription ?? string.Empty;
                    plan.Save();
                    stagedPlanName = plan.Name;
                    stagedDescription = plan.Description;
                }
            }
            finally { DisposeProject(project); }

            VerifyFile(projectPath, request.ProjectRevision);
            VerifyFile(resultPath, request.Revision);
            await VerifyStagedPlanAsync(staging, stagedPlanName, stagedDescription,
                protectedPlanState, request.Images);
            VerifyFile(projectPath, request.ProjectRevision);
            VerifyFile(resultPath, request.Revision);
            File.Move(staging, resultPath, overwrite: true);
            string savedRevision = Hash(resultPath);
            ResultPlanSettingsSnapshot savedSnapshot = await InspectAsync(new ResultPlanSettingsRequest(
                projectPath, request.ProjectXml, resultPath, savedRevision));
            return new ResultPlanSettingsSaveReceipt(resultPath, request.Revision,
                savedRevision, stagedPlanName, stagedDescription, savedSnapshot);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    private static async Task ValidateAndAppendImagesAsync(Plan plan, IReadOnlyList<EmbeddedImage> existing,
        ResultPlanSettingsImageDraft[] requested)
    {
        if (requested.Length > MaximumImageCount)
            throw new ArgumentException("The embedded Plan has too many images to save safely.");
        if (requested.Select(image => image.Id).Distinct(StringComparer.Ordinal).Count() != requested.Length)
            throw new ArgumentException("Image identities must be unique.");

        string expectedPrefix = Plan.HDF5GroupName + "/Images/";
        var byID = new Dictionary<string, EmbeddedImage>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (EmbeddedImage image in existing)
        {
            string id = image.H5Dataset;
            if (string.IsNullOrWhiteSpace(image.Name) || image.Name.Contains('/') || image.Name.Contains('\\')
                || !string.Equals(id, expectedPrefix + image.Name, StringComparison.Ordinal)
                || !byID.TryAdd(id, image) || !names.Add(image.Name))
                throw new IOException("An embedded Plan image has a duplicate or invalid HDF5 identity.");
            byte[] data = await image.TryLoadImageAsync()
                ?? throw new IOException("Cannot read existing embedded image " + image.Name + ".");
            totalBytes = checked(totalBytes + data.LongLength);
            if (data.Length > MaximumImageBytes || totalBytes > MaximumTotalImageBytes)
                throw new IOException("The existing embedded Plan images exceed the save size limit.");
        }

        var includedExisting = new HashSet<string>(StringComparer.Ordinal);
        foreach (ResultPlanSettingsImageDraft image in requested)
        {
            if (string.IsNullOrWhiteSpace(image.Id) || image.Data is null || image.Data.Length == 0)
                throw new ArgumentException("Embedded image data must not be empty.");
            if (image.Data.Length > MaximumImageBytes)
                throw new ArgumentException("An embedded image exceeds the save size limit.");
            if (byID.TryGetValue(image.Id, out EmbeddedImage? source))
            {
                byte[] sourceData = await source.TryLoadImageAsync()
                    ?? throw new IOException("Cannot read existing embedded image " + source.Name + ".");
                if (!includedExisting.Add(image.Id)
                    || !string.Equals(source.Name, image.Name, StringComparison.Ordinal)
                    || !string.Equals(source.Description, image.Description, StringComparison.Ordinal)
                    || !string.Equals(source.DateLastModified, image.DateLastModified, StringComparison.Ordinal)
                    || !source.X.Equals(image.X) || !source.Y.Equals(image.Y)
                    || !sourceData.AsSpan().SequenceEqual(image.Data))
                    throw new ArgumentException("Existing embedded image data and metadata must be preserved exactly.");
                continue;
            }

            totalBytes = checked(totalBytes + image.Data.LongLength);
            if (totalBytes > MaximumTotalImageBytes)
                throw new ArgumentException("The embedded Plan images exceed the save size limit.");

            if (!image.Id.StartsWith("draft:", StringComparison.Ordinal)
                || !Guid.TryParseExact(image.Id["draft:".Length..], "N", out _))
                throw new ArgumentException("An added image has an invalid draft identity.");
            string name = image.Name;
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name)
                || name.Contains('/') || name.Contains('\\') || !names.Add(name))
                throw new ArgumentException("An added image name must be unique and must not contain a path.");
            if (!double.IsFinite(image.X) || !double.IsFinite(image.Y))
                throw new ArgumentException("Image coordinates must be finite.");

            EmbeddedImage added = plan.EmbeddedImages.Add(name, image.Description ?? string.Empty, image.Data);
            added.DateLastModified = image.DateLastModified ?? string.Empty;
            added.X = image.X;
            added.Y = image.Y;
        }

        if (includedExisting.Count != byID.Count)
            throw new ArgumentException("Existing embedded images cannot be removed from this Plan Settings action.");

    }

    private static async Task VerifyStagedPlanAsync(string staging, string expectedName, string expectedDescription,
        string expectedProtectedState, ResultPlanSettingsImageDraft[] requested)
    {
        using Result result = new(staging);
        Plan plan = result.Plan ?? throw new IOException("The staged Result lost its embedded Plan.");
        if (!string.Equals(plan.Name, expectedName, StringComparison.Ordinal)
            || !string.Equals(plan.Description ?? string.Empty, expectedDescription, StringComparison.Ordinal)
            || !string.Equals(ProtectedPlanState(plan), expectedProtectedState, StringComparison.Ordinal))
            throw new IOException("The staged Result Plan did not retain the requested description.");
        List<EmbeddedImage> images = await plan.EmbeddedImages.LoadImageDataAsync();
        if (images.Count != requested.Length)
            throw new IOException("The staged Result Plan did not retain all embedded images.");
        foreach (ResultPlanSettingsImageDraft entry in requested)
        {
            EmbeddedImage? saved = images.SingleOrDefault(image => image.Name == entry.Name);
            byte[]? savedData = saved == null ? null : await saved.TryLoadImageAsync();
            if (savedData == null || !savedData.AsSpan().SequenceEqual(entry.Data))
                throw new IOException("The staged Result Plan image did not retain its original bytes.");
        }
    }

    private static string ProtectedPlanState(Plan plan) => JsonSerializer.Serialize(new
    {
        plan.Name,
        Geometry = plan.Geometry?.Name,
        BoundaryCondition = plan.BoundaryCondition?.Name,
        Groups = ResultPlanContextService.BuildEmbeddedPlanGroups(plan)
    }, BackendJson.Options);

    private static async Task<ResultPlanEmbeddedImage[]> ReadImagesAsync(Plan plan, string resultPath,
        string resultRevision)
    {
        var collection = plan.EmbeddedImages;
        var sourceImages = await collection.LoadImageDataAsync();
        if (sourceImages.Count > MaximumImageCount)
            throw new IOException("The embedded Plan has too many images to display safely.");

        string expectedPrefix = Plan.HDF5GroupName + "/Images/";
        var images = new List<ResultPlanEmbeddedImage>(sourceImages.Count);
        var seenIDs = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (EmbeddedImage image in sourceImages)
        {
            string id = image.H5Dataset;
            if (string.IsNullOrWhiteSpace(image.Name)
                || image.Name.Contains('/') || image.Name.Contains('\\')
                || !string.Equals(id, expectedPrefix + image.Name, StringComparison.Ordinal)
                || !seenIDs.Add(id))
                throw new IOException("An embedded Plan image has an invalid HDF5 identity.");

            byte[] data = await image.TryLoadImageAsync()
                ?? throw new IOException("Cannot read embedded Plan image " + image.Name + ".");
            if (data.Length > MaximumImageBytes)
                throw new IOException("Embedded Plan image " + image.Name + " exceeds the display size limit.");
            totalBytes = checked(totalBytes + data.LongLength);
            if (totalBytes > MaximumTotalImageBytes)
                throw new IOException("The embedded Plan images exceed the display size limit.");

            images.Add(new ResultPlanEmbeddedImage(id, image.Name, image.Description,
                image.DateLastModified, image.X, image.Y, data.LongLength, data));
        }

        // Image HDF reads can be asynchronous. Recheck the exact Result after
        // the last dataset so no payload is returned from a moving source.
        VerifyFile(resultPath, resultRevision);
        return images.ToArray();
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void VerifyFile(string path, string revision) => VerifyRevision(Hash(path), revision);

    private static void VerifyRevision(string actual, string expected)
    {
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static void DisposeProject(Project project)
    {
        foreach (Result result in project.Results) result.Dispose();
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                     .Concat(project.Geometries).Concat(project.Terrains)
                     .Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers))
            layer.Dispose();
    }
}
