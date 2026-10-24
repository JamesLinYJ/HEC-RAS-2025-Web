using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ras.Layers;
using Utility.Compression;
using Utility.Files;

namespace Ras.Backend;

public sealed record PlanExportRequest(string ProjectPath, string ProjectXml, string? PlanPath,
    string? OutputPath, bool Zip, bool ClipRasters, float? ResampleCellsize, string ExternalConnections,
    bool ReplaceFile = false, string? Revision = null,
    CoreLayerRequestContext? CoreLayerContext = null, string? ProjectSessionID = null);
public sealed record PlanExportInspection(string Revision, bool CanExecute, string? ValidationMessage,
    CreationChoice[] Plans, string? SelectedPlanPath, string? SuggestedOutputPath, string[] SourceFiles, string[] RequiredConnections);
public sealed record PlanExportResult(string OutputPath, bool Zip, string ProjectFilename, int FileCount,
    string ProjectPath, string PlanPath, string ProjectSessionID, string SourceRevision,
    string? CoreLayerIdentityToken, string? OutputSHA256, long OutputBytes, string ReceiptID);
public sealed record PlanExportDuplexRequest(PlanExportRequest Export, bool UploadAfterExport);
public sealed record PlanExportDuplexResult(string State, PlanExportResult? Receipt);

public static class PlanExportService
{
    // Discover external folders without enumerating their contents. A desktop
    // host can obtain access before any recursive directory read is started.
    public static string[] AccessFolders(PlanExportRequest request)
    {
        using var context = new Context(request);
        return context.AccessFolders();
    }
    public static PlanExportInspection Inspect(PlanExportRequest request)
    {
        using var context = new Context(request);
        string? validation = null;
        try { context.Validate(); }
        catch (ArgumentException error) { validation = error.Message; }
        return new(validation == null ? context.Revision() : "", validation == null, validation,
            context.Project.Plans.Select(plan => new CreationChoice(Path.GetFullPath(plan.Filename), plan.Name)).ToArray(),
            context.Plan?.Filename, context.SuggestedPath(), context.SourceFiles().ToArray(), context.RequiredConnections());
    }

    public static PlanExportResult Export(PlanExportRequest request)
        => ExportCore(request, reporter: null);

    internal static async Task<PlanExportDuplexResult> ExportDuplexAsync(
        PlanExportDuplexRequest duplexRequest, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader duplexControls)
    {
        ArgumentNullException.ThrowIfNull(duplexRequest);
        ArgumentNullException.ThrowIfNull(duplexControls);
        if (duplexRequest.UploadAfterExport && !duplexRequest.Export.Zip)
            throw new ArgumentException("Model repository upload requires a ZIP export.");
        var reporter = new Utility.Progress.ProgressReporter();
        reporter.ProgressReported += (sender, percent) => report?.Invoke(new(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.FullyQualifiedTaskName));
        reporter.MessageReported += (sender, message) => report?.Invoke(new(
            Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));

        using var cancellation = new CancellationTokenSource();
        using var stopControls = new CancellationTokenSource();
        Task controlPump = Task.Run(async () =>
        {
            try
            {
                await foreach (BackendDuplexControlFrame frame in duplexControls.ReadAllAsync(stopControls.Token)
                                   .ConfigureAwait(false))
                {
                    if (!frame.Control.TryGetProperty("type", out System.Text.Json.JsonElement type) ||
                        type.ValueKind != System.Text.Json.JsonValueKind.String ||
                        !string.Equals(type.GetString(), "cancel", StringComparison.Ordinal))
                        throw new InvalidDataException("The plan export received an unsupported control type.");
                    cancellation.Cancel();
                    reporter.RequestCancel();
                    return;
                }
            }
            catch (OperationCanceledException) when (stopControls.IsCancellationRequested) { }
            catch
            {
                cancellation.Cancel();
                reporter.RequestCancel();
            }
        }, CancellationToken.None);

        try
        {
            PlanExportResult result = await Task.Run(() => ExportCore(duplexRequest.Export,
                reporter.WithProgressModifier(0, duplexRequest.UploadAfterExport ? 0.5f : 1f)),
                CancellationToken.None).ConfigureAwait(false);
            return new("completed", result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || reporter.CancelRequested)
        {
            return new("cancelled", null);
        }
        finally
        {
            stopControls.Cancel();
            try { await controlPump.ConfigureAwait(false); }
            catch { }
        }
    }

    private static PlanExportResult ExportCore(PlanExportRequest request,
        Utility.Progress.ProgressReporter? reporter)
    {
        using var context = new Context(request);
        context.Validate();
        string revision = context.Revision();
        if (request.Revision == null || request.Revision != revision) throw new SourceFileChangedException();
        string output = Path.GetFullPath(request.OutputPath!);
        string parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".plan-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            ThrowIfCancelled(reporter);
            string exportDirectory = Path.Combine(staging, context.Project.Name);
            Directory.CreateDirectory(exportDirectory);
            var config = new Project.ProjectExportConfig {
                ClipRasters = request.ClipRasters, ResampleTerrainCellsize = request.ResampleCellsize,
                ExternalConnectionBehavior = request.ExternalConnections == "CopyToProject"
                    ? Project.MigrateExternalConnectionBehavior.CopyToProject : Project.MigrateExternalConnectionBehavior.ReferenceByAbsolutePath,
                SaveSourceChanges = false
            };
            var result = context.Project.ExportPlanAsNewProject(context.Plan!, exportDirectory, config, reporter);
            ThrowIfCancelled(reporter);
            if (!result) throw new InvalidDataException(result.GetConcatenatedMessages());
            int files = Directory.EnumerateFiles(exportDirectory, "*", SearchOption.AllDirectories).Count();
            string stagedOutput = exportDirectory;
            if (request.Zip)
            {
                stagedOutput = Path.Combine(staging, "export.zip");
                CompressionHelper.ZipDirectory(exportDirectory, stagedOutput, CompressionLevel.Fastest,
                    filename => Path.GetFileName(filename) != ".lock",
                    filename => Path.GetExtension(filename).ToLowerInvariant() is not (".h5" or ".tif" or ".tiff"),
                    reporter);
            }
            ThrowIfCancelled(reporter);
            if (context.Revision() != revision) throw new SourceFileChangedException();
            context.ValidateDestination();
            string? outputHash = request.Zip ? HashFile(stagedOutput) : null;
            long outputBytes = request.Zip ? new FileInfo(stagedOutput).Length : 0;
            string projectPath = Path.GetFullPath(request.ProjectPath);
            string planPath = Path.GetFullPath(context.Plan!.Filename);
            string sessionID = request.ProjectSessionID ?? request.CoreLayerContext?.ProjectSessionID ?? string.Empty;
            string? coreLayerIdentity = request.CoreLayerContext?.IdentityToken;
            string receiptID = CreateReceiptID(output, projectPath, planPath, sessionID, revision,
                coreLayerIdentity, outputHash, outputBytes);
            // Cancellation is honored until the commit point. Once the rename
            // succeeds, keep the receipt and leave the published export intact.
            ThrowIfCancelled(reporter);
            if (request.Zip) File.Move(stagedOutput, output, overwrite: request.ReplaceFile);
            else Directory.Move(stagedOutput, output);
            return new(output, request.Zip, context.Project.Name + ".ras", files, projectPath,
                planPath, sessionID, revision, coreLayerIdentity, outputHash, outputBytes, receiptID);
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    internal static string CreateReceiptID(string outputPath, string projectPath, string planPath,
        string projectSessionID, string sourceRevision, string? coreLayerIdentityToken,
        string? outputSHA256, long outputBytes)
    {
        string material = string.Join("\n", Path.GetFullPath(outputPath), Path.GetFullPath(projectPath),
            Path.GetFullPath(planPath), projectSessionID, sourceRevision, coreLayerIdentityToken ?? "",
            outputSHA256 ?? "", outputBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static string HashFile(string path)
    {
        using FileStream input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static void ThrowIfCancelled(Utility.Progress.ProgressReporter? reporter)
    {
        if (reporter?.CancelRequested == true) throw new OperationCanceledException();
    }

    private sealed class Context : IDisposable
    {
        private readonly PlanExportRequest request;
        public Project Project { get; }
        public Plan? Plan { get; }
        private Geometry? Geometry => Plan?.Geometry;
        private BoundaryCondition? Boundary => Plan?.BoundaryCondition;
        public Context(PlanExportRequest request)
        {
            this.request = request;
            if (!Path.IsPathFullyQualified(request.ProjectPath)) throw new ArgumentException("Open a saved project before exporting a plan.");
            Project = CoreLayerImportService.OpenProject(Path.GetFullPath(request.ProjectPath),
                request.ProjectXml, request.CoreLayerContext);
            try
            {
                if (request.CoreLayerContext is { } coreContext &&
                    (!string.Equals(coreContext.ProjectSessionID, request.ProjectSessionID ?? coreContext.ProjectSessionID, StringComparison.Ordinal) ||
                     !PathsEqual(coreContext.ProjectPath, request.ProjectPath)))
                    throw new ArgumentException("The external core-layer context belongs to another project session.");
                Plan = Project.Plans.FirstOrDefault(plan => Path.GetFullPath(plan.Filename) == request.PlanPath);
            }
            catch { Dispose(); throw; }
        }
        public string? SuggestedPath()
        {
            if (Plan == null) return null;
            string candidate = Path.Combine(Project.DefaultExportDirectory, Plan.Name + ".export" + (request.Zip ? ".zip" : ""));
            string stem = Path.GetFileNameWithoutExtension(candidate), extension = Path.GetExtension(candidate);
            if (!request.Zip) { stem = Path.GetFileName(candidate); extension = ""; }
            int ordinal = 1;
            while (File.Exists(candidate) || Directory.Exists(candidate))
                candidate = Path.Combine(Project.DefaultExportDirectory, stem + " (" + ordinal++ + ")" + extension);
            return candidate;
        }
        public void ValidateDestination()
        {
            if (string.IsNullOrWhiteSpace(request.OutputPath)) throw new ArgumentException("Choose an export destination.");
            string path = Path.GetFullPath(request.OutputPath);
            if (!Path.IsPathFullyQualified(request.OutputPath) || string.IsNullOrEmpty(Path.GetFileName(path))) throw new ArgumentException("Choose a valid absolute export destination.");
            if (request.Zip)
            {
                if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a ZIP file for archive export.");
                if (Directory.Exists(path) || (File.Exists(path) && !request.ReplaceFile)) throw new ArgumentException("The export destination already exists. Choose another destination.");
            }
            else if (File.Exists(path) || Directory.Exists(path)) throw new ArgumentException("The export folder already exists. Choose a new folder name.");
        }
        public void Validate()
        {
            if (Plan == null) throw new ArgumentException("Select a plan to export.");
            if (Geometry == null || Boundary == null) throw new ArgumentException("Attach a geometry and boundary condition before exporting this plan.");
            if (request.ResampleCellsize is float cell && (!float.IsFinite(cell) || cell <= 0)) throw new ArgumentException("Enter a finite resample cell size greater than zero.");
            if (request.ExternalConnections is not ("ReferenceByAbsolutePath" or "CopyToProject")) throw new ArgumentException("Choose a valid external connection export option.");
            foreach (var layer in Layers())
                if (layer.HasLoadErrors) throw new ArgumentException("A model required by this plan has load errors.");
            var terrain = Geometry.AssociatedTerrain;
            var classifier = Geometry.AssociatedNValueLayer?.AssociatedClassificationLayer;
            if (request.ClipRasters && ((terrain?.AllRasterInputs().Count ?? 0) > 0 || (classifier?.AllRasterInputs().Count ?? 0) > 0) && !Geometry.Extent.IsValidAndFinite())
                throw new ArgumentException("The geometry has no valid area for clipping rasters.");
            foreach (string name in RequiredConnections())
            {
                if (!Project.ConnectionSource.TryFindConnection(name, out var connection)) throw new ArgumentException("A required external connection is missing from the project.");
                var resolved = connection switch {
                    FileExternalConnection file => file.ResourceExists(),
                    DirectoryExternalConnection directory => directory.TryGetFiles(out _),
                    _ => throw new NotSupportedException("This external connection type cannot be exported.")
                };
                if (!resolved) throw new ArgumentException("A required external connection could not be found. Check its path or path mapping.");
            }
            ValidateDestination();
        }
        private IEnumerable<CoreLayerBase> Layers()
        {
            if (Plan != null) yield return Plan;
            if (Geometry != null)
            {
                yield return Geometry;
                if (Geometry.AssociatedTerrain is Terrain terrain) yield return terrain;
                if (Geometry.AssociatedNValueLayer is NValueLayer values)
                {
                    yield return values;
                    if (values.Mode == NValueSource.ClassificationLayer && values.AssociatedClassificationLayer is ClassificationLayer classification) yield return classification;
                }
            }
            if (Boundary != null) yield return Boundary;
        }
        public string[] RequiredConnections() => Boundary?.AllExternalConnectionsNeeded().Distinct(StringComparer.Ordinal).ToArray() ?? [];
        public string[] AccessFolders()
        {
            var folders = new List<string>();
            if (Geometry?.AssociatedTerrain is Terrain terrain)
                folders.AddRange(terrain.AllRasterInputs().Select(input => Path.GetDirectoryName(Path.GetFullPath(input.Filename))!));
            if (Geometry?.AssociatedNValueLayer is NValueLayer values && values.Mode == NValueSource.ClassificationLayer && values.AssociatedClassificationLayer is ClassificationLayer classification)
                folders.AddRange(classification.AllRasterInputs().Select(input => Path.GetDirectoryName(Path.GetFullPath(input.Filename))!));
            foreach (string name in RequiredConnections())
            {
                if (!Project.ConnectionSource.TryFindConnection(name, out var connection)) continue;
                if (connection is FileExternalConnection file && file.TryGetFile(out string path)) folders.Add(Path.GetDirectoryName(Path.GetFullPath(path))!);
                else if (connection is DirectoryExternalConnection directory && directory.TryGetDirectory(out string pathValue)) folders.Add(Path.GetFullPath(pathValue));
            }
            string projectFolder = Path.GetDirectoryName(Path.GetFullPath(request.ProjectPath))!;
            static bool Contains(string parent, string child) => child == parent || child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            string[] external = folders.Distinct(StringComparer.Ordinal).Where(folder => !Contains(projectFolder, folder)).ToArray();
            return external.Where(folder => !external.Any(other => folder != other && Contains(other, folder))).Order(StringComparer.Ordinal).ToArray();
        }
        public IEnumerable<string> SourceFiles()
        {
            var files = Layers().Select(layer => layer.Filename).ToList();
            files.Add(request.ProjectPath);
            if (Geometry?.AssociatedTerrain is Terrain terrain) files.AddRange(terrain.AllRasterInputs().Select(input => input.Filename));
            if (Geometry?.AssociatedNValueLayer is NValueLayer values && values.Mode == NValueSource.ClassificationLayer && values.AssociatedClassificationLayer is ClassificationLayer classification)
                files.AddRange(classification.AllRasterInputs().Select(input => input.Filename));
            foreach (string name in RequiredConnections())
            {
                if (!Project.ConnectionSource.TryFindConnection(name, out var connection)) continue;
                if (connection is FileExternalConnection file && file.TryGetFile(out string path)) files.Add(path);
                else if (connection is DirectoryExternalConnection directory && directory.TryGetFiles(out string[] paths)) files.AddRange(paths);
            }
            return files.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        }
        public string Revision()
        {
            static string FileHash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
            var sources = SourceFiles().Select(path => new { path, hash = FileHash(path) });
            string? destinationHash = request.OutputPath != null && File.Exists(request.OutputPath) ? FileHash(request.OutputPath) : null;
            string json = JsonSerializer.Serialize(new { request.ProjectPath, request.ProjectXml, request.PlanPath, request.OutputPath,
                request.ProjectSessionID, coreLayerContext = request.CoreLayerContext?.IdentityToken,
                request.Zip, request.ClipRasters, request.ResampleCellsize, request.ExternalConnections, request.ReplaceFile, sources, destinationHash });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        }

        private static bool PathsEqual(string first, string second) =>
            string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        public void Dispose()
        {
            foreach (var layer in Project.Plans.Cast<CoreLayerBase>().Concat(Project.Geometries).Concat(Project.Terrains)
                .Concat(Project.BoundaryConditions).Concat(Project.NValues).Concat(Project.ClassificationLayers).Cast<IDisposable>().Concat(Project.Results)) layer.Dispose();
        }
    }
}
