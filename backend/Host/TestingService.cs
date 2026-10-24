using System.Globalization;
using Ras.Helpers;
using Ras.Layers;
using Testing.Core;
using Testing.Core.Regression;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Backend;

/// <summary>
/// Source-compatible TestingUtility operations. Network endpoints and upload
/// semantics follow Testing.Core.Regression.RegressionClient and
/// Ras.Interface.WinUI.Controls.Development.TestingUtility flows.
/// </summary>
public static class TestingService
{
    public const string DefaultBaseUrl = RegressionClientSettings.DefaultServerUrl;
    private static readonly TimeSpan CommitLookback = TimeSpan.FromDays(45);

    public static async Task<TestingWorkspaceResponse> LoadAsync(TestingWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        RegressionClient client = CreateClient(request.Credentials);
        cancellationToken.ThrowIfCancellationRequested();
        Task<List<ModelRepoIdName>> projectsTask = client.ListProjectsAsync();
        Task<List<ModelRepoIdName>> geometriesTask = client.ListGeometriesAsync();
        Task<List<CommitSummary>> commitsTask = client.ListCommitsAsync(
            sinceUnix: DateTimeOffset.UtcNow.Subtract(CommitLookback).ToUnixTimeMilliseconds());
        await Task.WhenAll(projectsTask, geometriesTask, commitsTask).WaitAsync(cancellationToken).ConfigureAwait(false);

        var objects = projectsTask.Result.Select(value => ToObject(value, "project"))
            .Concat(geometriesTask.Result.Select(value => ToObject(value, "geometry"))).ToArray();
        var commits = commitsTask.Result.Select(ToCommit).ToArray();
        return new TestingWorkspaceResponse(objects, commits, "", [], []);
    }

    public static async Task<TestingActionResponse> PerformAsync(TestingActionRequest request,
        CancellationToken cancellationToken = default)
    {
        (string objectType, int objectId) = ParseObjectID(request.Id);
        RegressionClient client = CreateClient(request.Credentials);
        string? projectURL = null;
        TestingPoint[] points = [];
        bool canEditPoints = false;
        string? pointOwnerID = null;
        string status;
        string[] log;

        switch (request.Action)
        {
            case "details":
            {
                TestingServerObject detail = await FetchDetailAsync(client, objectType, objectId, cancellationToken).ConfigureAwait(false);
                status = "Loaded " + detail.Name;
                log = [];
                break;
            }
            case "open":
            {
                if (objectType != "project") throw new ArgumentException("Only a testing project can be downloaded and opened.");
                client = await CreateConfiguredClientAsync(request.Credentials, cancellationToken).ConfigureAwait(false);
                ModelRepoProject project = await FetchProjectAsync(client, objectId, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(project.S3Key)) throw new InvalidDataException("This testing project has no downloadable content.");
                string destination = DownloadDirectory(project.Name);
                var reporter = CreateReporter(null);
                OperationResult downloaded = await client.DownloadProjectAsync(project.S3Key, destination, reporter)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                EnsureSuccess(downloaded, "Downloading the testing project");
                projectURL = Project.TryGetProjectFilenameFromDirectory(destination);
                if (string.IsNullOrWhiteSpace(projectURL) || !File.Exists(projectURL))
                    throw new InvalidDataException("The downloaded testing bundle does not contain a project file.");
                status = "Downloaded " + project.Name;
                log = [];
                break;
            }
            case "edit":
            {
                if (objectType != "project") throw new ArgumentException("Only a testing project has editable points of interest.");
                ModelRepoProject project = await FetchProjectAsync(client, objectId, cancellationToken).ConfigureAwait(false);
                points = (project.InterestPoints ?? []).Select(value => new TestingPoint(value.Id, value.Name, value.X, value.Y)).ToArray();
                canEditPoints = true;
                pointOwnerID = request.Id;
                status = "Loaded " + project.Name + " points of interest";
                log = [];
                break;
            }
            case "rename":
            {
                RequireText(request.Name, "Enter an object name.");
                OperationResult result = await client.RenameObjectAsync(ParseType(objectType), objectId, request.Name!)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                EnsureSuccess(result, "Renaming the testing object");
                status = "Renamed " + request.Id;
                log = [];
                break;
            }
            case "move":
            {
                OperationResult result = await client.MoveObjectFolderAsync(ParseType(objectType), objectId, request.Folder ?? "")
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                EnsureSuccess(result, "Moving the testing object");
                status = "Moved " + request.Id;
                log = [];
                break;
            }
            case "description":
            {
                OperationResult result = await client.SetObjectDescriptionAsync(ParseType(objectType), objectId, request.Text ?? "")
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                EnsureSuccess(result, "Updating the testing object description");
                status = "Updated " + request.Id;
                log = [];
                break;
            }
            case "savePoints":
            {
                if (objectType != "project") throw new ArgumentException("Points of interest can only be reconciled for a testing project.");
                ReconcileInterestPointRequest[] reconcile = (request.Points ?? []).Select(value =>
                {
                    EnsureCoordinate(value.X, value.Y);
                    return new ReconcileInterestPointRequest { X = value.X, Y = value.Y, Name = value.Name ?? "" };
                }).ToArray();
                OperationResult result = await client.ReconcileInterestPointsAsync(objectId, reconcile.ToList())
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                EnsureSuccess(result, "Updating testing project points of interest");
                ModelRepoProject updated = await FetchProjectAsync(client, objectId, cancellationToken).ConfigureAwait(false);
                points = (updated.InterestPoints ?? []).Select(value => new TestingPoint(value.Id, value.Name, value.X, value.Y)).ToArray();
                canEditPoints = true;
                pointOwnerID = request.Id;
                status = "Updated points of interest for " + updated.Name;
                log = [];
                break;
            }
            case "queueRun":
            {
                if (string.IsNullOrWhiteSpace(request.Commit)) throw new ArgumentException("Choose or enter a commit hash.");
                if (request.Iterations is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(request.Iterations), "Run iterations must be between 1 and 20.");
                TestingServerObject detail = await FetchDetailAsync(client, objectType, objectId, cancellationToken).ConfigureAwait(false);
                TestingRunRequest run = CreateRunRequest(request.Commit.Trim(), request.Tag ?? "",
                    request.Iterations, objectId, detail.Name, objectType);
                OperationResult result = await client.QueueRunAsync(CreateSourceRunRequest(run))
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                EnsureSuccess(result, "Queueing the testing run");
                status = "Queued " + detail.Name + " on commit " + ShortHash(request.Commit.Trim());
                log = [];
                break;
            }
            default:
                throw new ArgumentException("The requested Testing Utility action is not supported.");
        }

        TestingWorkspaceResponse snapshot = await LoadAsync(new TestingWorkspaceRequest(request.Credentials), cancellationToken)
            .ConfigureAwait(false);
        if (request.Action == "details")
            snapshot = WithDetail(snapshot, await FetchDetailAsync(client, objectType, objectId, cancellationToken).ConfigureAwait(false));
        if (request.Action is "edit" or "savePoints")
            snapshot = snapshot with { Points = points, CanEditPoints = canEditPoints, PointOwnerID = pointOwnerID };
        snapshot = snapshot with { Status = status, Log = log };
        return new TestingActionResponse(snapshot, projectURL);
    }

    /// <summary>Imports POIs with the retained Testing.Core shapefile reader.</summary>
    public static Task<TestingPoint[]> ImportPointsAsync(TestingPoiImportRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.SourcePath) || !Path.IsPathFullyQualified(request.SourcePath)
            || !File.Exists(request.SourcePath))
            throw new FileNotFoundException("Choose a readable POI shapefile.", request.SourcePath);
        if (!PoiShapefile.TryRead(Path.GetFullPath(request.SourcePath), out List<PoiPoint> points))
            throw new InvalidDataException("The selected shapefile does not contain readable point features.");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(points.Select(value => new TestingPoint(null, value.Name ?? "", value.X, value.Y)).ToArray());
    }

    public static async Task<TestingUploadPrepareResponse> PrepareUploadAsync(TestingUploadPrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        RegressionClient client = CreateClient(request.Credentials);
        bool projectUpload = request.Kind == "project";
        bool geometryUpload = request.Kind == "geometry";
        if (!projectUpload && !geometryUpload) throw new ArgumentException("Choose a project or geometry upload.");

        TestingServerObject[] existing = projectUpload
            ? (await client.ListProjectsAsync().WaitAsync(cancellationToken).ConfigureAwait(false)).Select(value => ToObject(value, "project")).ToArray()
            : (await client.ListGeometriesAsync().WaitAsync(cancellationToken).ConfigureAwait(false)).Select(value => ToObject(value, "geometry")).ToArray();
        string[] names = existing.Select(value => value.Name).ToArray();
        TestingCommit[] commits = (await client.ListCommitsAsync(
            sinceUnix: DateTimeOffset.UtcNow.Subtract(CommitLookback).ToUnixTimeMilliseconds())
            .WaitAsync(cancellationToken).ConfigureAwait(false))
            .Select(ToCommit).ToArray();

        if (request.SourcePath is null)
            return new TestingUploadPrepareResponse(null, "", "", "", [], [], [], null, names, null, false, "", commits);
        string path = Path.GetFullPath(request.SourcePath);
        if (projectUpload)
        {
            Project project = LoadProject(path);
            try
            {
                var plans = project.Plans.Where(plan => !plan.IsResult).Select(plan =>
                {
                    PlanAssociation? association = project.GetPlanAssociation(plan);
                    return new TestingPlanChoice(Path.GetFullPath(plan.Filename), plan.Name,
                        association?.Geometry?.Name, association?.BoundaryCondition?.Name);
                }).ToArray();
                var geometries = project.Geometries.Select(geometry => new TestingGeometryChoice(
                    Path.GetFullPath(geometry.Filename), geometry.Name)).ToArray();
                string poiPath = PoiShapefile.PathFor(project.ProjectDirectory);
                TestingPoint[] points = PoiShapefile.TryRead(poiPath, out List<PoiPoint> sourcePoints)
                    ? sourcePoints.Select(value => new TestingPoint(null, value.Name, value.X, value.Y)).ToArray()
                    : [];
                var pointEditor = new TestingPointEditorSnapshot(points);
                string suggestedName = project.Name;
                return new TestingUploadPrepareResponse(path, suggestedName, "", project.Description ?? "", plans,
                    [], geometries, null, names, pointEditor, false, project.Name, commits);
            }
            finally { DisposeProjectLayers(project); }
        }

        if (Project.TryGetProjectFilenameFromDirectory(Path.GetDirectoryName(path) ?? "") is string projectFile
            && !string.IsNullOrEmpty(projectFile) && string.Equals(Path.GetFullPath(projectFile), path, PathComparison))
        {
            Project project = LoadProject(path);
            try
            {
                var geometries = project.Geometries.Select(geometry => new TestingGeometryChoice(
                    Path.GetFullPath(geometry.Filename), geometry.Name)).ToArray();
                return new TestingUploadPrepareResponse(path, "", "", "", [], [], geometries,
                    request.GeometryID, names, null, false, project.Name, commits);
            }
            finally { DisposeProjectLayers(project); }
        }

        bool sourceGeometryValid = Geometry.IsValid(path);
        if (!sourceGeometryValid) throw new InvalidDataException("Choose a valid HEC-RAS geometry backup or a saved project geometry.");
        return new TestingUploadPrepareResponse(path, Path.GetFileNameWithoutExtension(path), "", "", [], [], [],
            null, names, null, true, "", commits);
    }

    public static async Task<TestingUploadOutcome> ExecuteUploadAsync(TestingUploadExecuteRequest request,
        Action<BackendOperationProgress>? report = null, CancellationToken cancellationToken = default)
    {
        Action<BackendOperationProgress>? safeReport = SanitizeProgressReporter(report, request.Credentials);
        try
        {
            TestingUploadOutcome outcome = await ExecuteUploadCoreAsync(request, safeReport, cancellationToken,
                new TestingUploadExecutionState()).ConfigureAwait(false);
            return SanitizeOutcome(outcome, request.Credentials);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { throw SanitizeException(error, request.Credentials); }
    }

    internal static async Task<TestingUploadOutcome> ExecuteUploadDuplexAsync(TestingUploadExecuteRequest request,
        Action<BackendOperationProgress>? report, BackendDuplexControlReader duplexControls)
    {
        ArgumentNullException.ThrowIfNull(duplexControls);
        Action<BackendOperationProgress>? safeReport = SanitizeProgressReporter(report, request.Credentials);
        var cancellation = new CancellationTokenSource();
        var execution = new TestingUploadExecutionState();
        using var stopControlPump = new CancellationTokenSource();
        Exception? controlError = null;
        object controlErrorGate = new();
        Task controlPump = Task.Run(async () =>
        {
            try
            {
                await foreach (BackendDuplexControlFrame frame in duplexControls.ReadAllAsync(stopControlPump.Token)
                                   .ConfigureAwait(false))
                {
                    if (!frame.Control.TryGetProperty("type", out System.Text.Json.JsonElement type)
                        || type.ValueKind != System.Text.Json.JsonValueKind.String
                        || !string.Equals(type.GetString(), "cancel", StringComparison.Ordinal))
                        throw new InvalidDataException("The Testing Utility upload received an unsupported control type.");
                    cancellation.Cancel();
                    return;
                }
            }
            catch (OperationCanceledException) when (stopControlPump.IsCancellationRequested) { }
            catch (Exception error)
            {
                lock (controlErrorGate) controlError = error;
                cancellation.Cancel();
            }
        }, CancellationToken.None);

        try
        {
            TestingUploadOutcome outcome = await ExecuteUploadCoreAsync(request, safeReport, cancellation.Token,
                execution).ConfigureAwait(false);
            Exception? invalidControl;
            lock (controlErrorGate) invalidControl = controlError;
            if (invalidControl is not null)
                throw new InvalidDataException("The Testing Utility upload received an invalid cancellation control.", invalidControl);
            return SanitizeOutcome(outcome, request.Credentials);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Exception? invalidControl;
            lock (controlErrorGate) invalidControl = controlError;
            if (invalidControl is not null)
                throw new InvalidDataException("The Testing Utility upload received an invalid cancellation control.", invalidControl);
            string message = execution.UploadCompleted
                ? "Upload completed; the run continuation was cancelled."
                : "Upload was cancelled before completion.";
            return SanitizeOutcome(new TestingUploadOutcome(message, execution.ServerObjectID, false,
                Cancelled: true, UploadCompleted: execution.UploadCompleted), request.Credentials);
        }
        catch (Exception error)
        {
            throw SanitizeException(error, request.Credentials);
        }
        finally
        {
            stopControlPump.Cancel();
            try { await controlPump.ConfigureAwait(false); }
            catch { }
            cancellation.Dispose();
        }
    }

    private static async Task<TestingUploadOutcome> ExecuteUploadCoreAsync(TestingUploadExecuteRequest request,
        Action<BackendOperationProgress>? report, CancellationToken cancellationToken,
        TestingUploadExecutionState execution)
    {
        RegressionClient client = CreateClient(request.Credentials);
        ProgressReporter progress = CreateReporter(report);
        using CancellationTokenRegistration cancellation = cancellationToken.Register(progress.RequestCancel);
        bool projectUpload = request.Kind == "project";
        bool geometryUpload = request.Kind == "geometry";
        if (!projectUpload && !geometryUpload) throw new ArgumentException("Choose a project or geometry upload.");
        RequireText(request.Name, "Enter an upload name.");
        if (request.Name!.Trim() != request.Name) throw new ArgumentException("The upload name cannot start or end with whitespace.");
        if (request.QueueRunAfterUpload)
        {
            if (string.IsNullOrWhiteSpace(request.RunCommit)) throw new ArgumentException("Choose or enter a commit hash.");
            if (request.RunIterations is < 1 or > 20)
                throw new ArgumentOutOfRangeException(nameof(request.RunIterations), "Run iterations must be between 1 and 20.");
        }

        if (projectUpload && request.ServerObjectID is not null && (request.PlanIDs?.Length ?? 0) == 0)
        {
            (string pointOwnerType, int projectId) = ParseObjectID(request.ServerObjectID);
            if (pointOwnerType != "project") throw new ArgumentException("Points-only updates require a testing project.");
            ModelRepoProject seed = await FetchProjectAsync(client, projectId, cancellationToken).ConfigureAwait(false);
            if (seed.Id.ToString(CultureInfo.InvariantCulture) != projectId.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("The selected server project changed before its POIs were saved.");
            var points = (request.Points ?? []).Select(value =>
            {
                EnsureCoordinate(value.X, value.Y);
                return new ReconcileInterestPointRequest { X = value.X, Y = value.Y, Name = value.Name ?? "" };
            }).ToList();
            OperationResult reconcile = await client.ReconcileInterestPointsAsync(projectId, points)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            EnsureSuccess(reconcile, "Updating project points of interest");
            string requestedFolder = (request.Folder ?? "").Trim();
            if (!string.Equals(requestedFolder, (seed.Folder ?? "").Trim(), StringComparison.Ordinal))
            {
                OperationResult move = await client.MoveObjectFolderAsync(ModelRepoObjectType.Project, projectId, request.Folder ?? "")
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                EnsureSuccess(move, "Moving the testing project");
            }
            string requestedDescription = (request.Description ?? "").Trim();
            if (!string.Equals(requestedDescription, (seed.Description ?? "").Trim(), StringComparison.Ordinal))
            {
                OperationResult description = await client.SetObjectDescriptionAsync(ModelRepoObjectType.Project, projectId, request.Description ?? "")
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                EnsureSuccess(description, "Updating the testing project description");
            }
            execution.UploadCompleted = true;
            execution.ServerObjectID = request.ServerObjectID;
            return await FinishUploadAsync(client, request, "project", report, cancellationToken,
                uploaded: false, existingObjectID: request.ServerObjectID).ConfigureAwait(false);
        }

        if (projectUpload)
        {
            client = await CreateConfiguredClientAsync(request.Credentials, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(request.SourcePath)) throw new ArgumentException("Choose a saved project to upload.");
            if (request.PlanIDs is not { Length: > 0 }) throw new ArgumentException("Select at least one exportable plan.");
            Project project = LoadProject(request.SourcePath);
            string tempDirectory = Path.Combine(Path.GetTempPath(), "hecras-testing-upload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            try
            {
                Dictionary<string, Plan> plansByPath = project.Plans.Where(plan => !plan.IsResult)
                    .ToDictionary(plan => Path.GetFullPath(plan.Filename), StringComparerForPaths);
                Plan[] selected = request.PlanIDs.Distinct(StringComparerForPaths).Select(id =>
                {
                    if (!Path.IsPathFullyQualified(id) || !plansByPath.TryGetValue(Path.GetFullPath(id), out Plan? plan))
                        throw new InvalidDataException("A selected plan no longer belongs to this project.");
                    PlanAssociation? association = project.GetPlanAssociation(plan);
                    if (association?.Geometry is null || association.BoundaryCondition is null)
                        throw new InvalidDataException("A selected plan needs both an associated geometry and boundary condition.");
                    return plan;
                }).ToArray();

                progress.ReportMessage(selected.Length == 1 ? "Exporting 1 plan as a standalone project..."
                    : $"Exporting {selected.Length} plans as a standalone project...");
                OperationResult exported = await Task.Run(() => project.ExportPlansAsNewProject(selected, tempDirectory,
                    pr: progress.WithProgressModifier(0, 0.2f))).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    return new TestingUploadOutcome("Plan export was cancelled before upload.", null, false,
                        Cancelled: true, UploadCompleted: false);
                EnsureSuccess(exported, "Exporting selected plans");
                progress.ReportProgress(20);
                progress.ReportMessage("Writing interest points...");
                PoiPoint[] points = (request.Points ?? []).Select(value =>
                {
                    EnsureCoordinate(value.X, value.Y);
                    return new PoiPoint(value.X, value.Y, value.Name ?? "");
                }).ToArray();
                PoiShapefile.Write(PoiShapefile.PathFor(tempDirectory), points, project.Projection);
                OperationResult uploaded = await UploadProjectPreservingOptionalMetadataAsync(client,
                    tempDirectory, request.Name!, request.Folder, request.Description,
                    progress.WithProgressModifier(20, 0.8f)).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    if (uploaded.Result)
                        return new TestingUploadOutcome("Project upload completed; its run continuation was cancelled.",
                            null, false, Cancelled: true, UploadCompleted: true);
                    return new TestingUploadOutcome("Project upload was cancelled before completion.", null, false,
                        Cancelled: true, UploadCompleted: false);
                }
                EnsureSuccess(uploaded, "Uploading the testing project");
                execution.UploadCompleted = true;
            }
            finally
            {
                DisposeProjectLayers(project);
                try { if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        else
        {
            if (request.ServerObjectID is not null) throw new ArgumentException("A geometry upload cannot target a server project.");
            if (string.IsNullOrWhiteSpace(request.SourcePath)) throw new ArgumentException("Choose a valid HEC-RAS geometry backup or a saved project geometry.");
            bool fromProject = request.GeometryID is not null;
            string geometryPath = fromProject
                ? ResolveGeometryPath(request.SourcePath, request.GeometryID)
                : Path.GetFullPath(request.SourcePath);
            if (!fromProject && !Geometry.IsValid(geometryPath))
                throw new InvalidDataException("The selected source is not a valid HEC-RAS geometry.");
            string uploadPath = geometryPath;
            string? tempGeometryPath = null;
            Project? sourceProject = null;
            try
            {
                if (fromProject)
                {
                    sourceProject = LoadProject(request.SourcePath);
                    Geometry geometry = sourceProject.Geometries.FirstOrDefault(value =>
                        PathsEqual(value.Filename, geometryPath))
                        ?? throw new InvalidDataException("The selected geometry no longer belongs to this project.");
                    tempGeometryPath = Path.Combine(Path.GetTempPath(), "hecras-testing-geometry-" + Guid.NewGuid().ToString("N") + ".hdf");
                    geometry.SaveGeometryBackup(tempGeometryPath);
                    uploadPath = tempGeometryPath;
                }
                progress.ReportMessage("Uploading geometry backup...");
                OperationResult uploaded = await UploadGeometryPreservingOptionalMetadataAsync(client,
                    uploadPath, request.Name!, request.Folder, request.Description).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    if (uploaded.Result)
                        return new TestingUploadOutcome("Geometry upload completed; its run continuation was cancelled.",
                            null, false, Cancelled: true, UploadCompleted: true);
                    throw new OperationCanceledException(cancellationToken);
                }
                EnsureSuccess(uploaded, "Uploading the testing geometry");
                execution.UploadCompleted = true;
                report?.Invoke(new BackendOperationProgress(Fraction: 1, Message: "Geometry upload complete"));
            }
            finally
            {
                if (sourceProject is not null) DisposeProjectLayers(sourceProject);
                if (tempGeometryPath is not null)
                {
                    try { File.Delete(tempGeometryPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        if (cancellationToken.IsCancellationRequested)
            return new TestingUploadOutcome("Upload completed; its run continuation was cancelled.", execution.ServerObjectID,
                false, Cancelled: true, UploadCompleted: execution.UploadCompleted);
        return await FinishUploadAsync(client, request, request.Kind, report, cancellationToken, uploaded: true)
            .ConfigureAwait(false);
    }

    public static TestingRunRequest CreateRunRequest(string commit, string tag, int iterations,
        int objectID, string name, string objectType)
    {
        if (string.IsNullOrWhiteSpace(commit)) throw new ArgumentException("Choose or enter a commit hash.");
        if (iterations is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (objectType is not ("project" or "geometry")) throw new ArgumentException("The testing object type is not supported.");
        if (objectID <= 0) throw new ArgumentOutOfRangeException(nameof(objectID));
        RequireText(name, "The testing object name is missing.");
        return new TestingRunRequest(commit, tag, iterations, new TestingRunTarget(objectID, name, objectType));
    }

    public static AddTestRequest CreateSourceRunRequest(TestingRunRequest request) => new()
    {
        CommitHash = request.Commit,
        TestObjectIdNames =
        [
            new ModelRepoIdName
            {
                Id = request.Target.Id,
                Name = request.Target.Name,
                ObjectTypeName = request.Target.ObjectType
            }
        ],
        NumRuntimeIterations = request.Iterations,
        Tag = request.Tag
    };

    private static async Task<TestingUploadOutcome> FinishUploadAsync(RegressionClient client,
        TestingUploadExecuteRequest request, string objectType, Action<BackendOperationProgress>? report,
        CancellationToken cancellationToken, bool uploaded, string? existingObjectID = null)
    {
        if (!request.QueueRunAfterUpload)
            return new TestingUploadOutcome(uploaded ? "Upload completed." : "Points of interest updated.", existingObjectID, false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            report?.Invoke(new BackendOperationProgress(Message: "Reading the uploaded object back from the testing server..."));
            TestingServerObject target;
            if (existingObjectID is not null)
            {
                (_, int id) = ParseObjectID(existingObjectID);
                target = await FetchDetailAsync(client, objectType, id, cancellationToken).ConfigureAwait(false);
            }
            else if (objectType == "project")
            {
                ModelRepoProject? project = await client.FetchProjectByNameAsync(request.Name!)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                if (project is null) throw new InvalidDataException("The uploaded testing project could not be read back.");
                target = ToObject(project);
            }
            else
            {
                ModelRepoGeometry? geometry = await client.FetchGeometryByNameAsync(request.Name!)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                if (geometry is null) throw new InvalidDataException("The uploaded testing geometry could not be read back.");
                target = ToObject(geometry);
            }

            string commit = request.RunCommit?.Trim() ?? "";
            (string targetType, int targetID) = ParseObjectID(target.Id);
            if (!string.Equals(targetType, objectType, StringComparison.Ordinal))
                throw new InvalidDataException("The testing server returned a different object type for the uploaded name.");
            TestingRunRequest run = CreateRunRequest(commit, request.RunTag ?? "", request.RunIterations,
                targetID, target.Name, objectType);
            cancellationToken.ThrowIfCancellationRequested();
            report?.Invoke(new BackendOperationProgress(Message: "Queueing the uploaded object on the testing server..."));
            OperationResult queued = await client.QueueRunAsync(CreateSourceRunRequest(run))
                .ConfigureAwait(false);
            EnsureSuccess(queued, "Queueing the uploaded testing object");
            report?.Invoke(new BackendOperationProgress(Fraction: 1,
                Message: "Queued " + target.Name + " on commit " + ShortHash(commit)));
            return new TestingUploadOutcome("Queued " + target.Name + " on commit " + ShortHash(commit),
                target.Id, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            string detail = string.IsNullOrWhiteSpace(error.Message)
                ? "The testing server could not read back the uploaded object or queue its run."
                : error.Message;
            return new TestingUploadOutcome("Upload completed; run queue status could not be confirmed.", existingObjectID, false,
                ContinuationError: "Upload completed, but the run continuation could not be confirmed: " + detail,
                UploadCompleted: true);
        }
    }

    private static RegressionClient CreateClient(TestingUtilityCredentials credentials)
    {
        string baseUrl = ValidateBaseUrl(credentials.BaseUrl);
        RegressionClientSettings settings = new() { BaseUrl = baseUrl };
        return new RegressionClient(() => settings);
    }

    private static Task<OperationResult> UploadProjectPreservingOptionalMetadataAsync(RegressionClient client,
        string projectDirectory, string uploadName, string? folder, string? description, ProgressReporter reporter)
    {
        if (folder is null)
        {
            return description is null
                ? client.UploadProjectAsync(projectDirectory, uploadName, reporter: reporter)
                : client.UploadProjectAsync(projectDirectory, uploadName, description: description, reporter: reporter);
        }

        return description is null
            ? client.UploadProjectAsync(projectDirectory, uploadName, folder: folder, reporter: reporter)
            : client.UploadProjectAsync(projectDirectory, uploadName, folder, description, reporter);
    }

    private static Task<OperationResult> UploadGeometryPreservingOptionalMetadataAsync(RegressionClient client,
        string geometryPath, string uploadName, string? folder, string? description)
    {
        if (folder is null)
        {
            return description is null
                ? client.UploadGeometryAsync(geometryPath, uploadName)
                : client.UploadGeometryAsync(geometryPath, uploadName, description: description);
        }

        return description is null
            ? client.UploadGeometryAsync(geometryPath, uploadName, folder: folder)
            : client.UploadGeometryAsync(geometryPath, uploadName, folder, description);
    }

    private static async Task<RegressionClient> CreateConfiguredClientAsync(TestingUtilityCredentials credentials,
        CancellationToken cancellationToken)
    {
        RegressionClientSettings settings = new() { BaseUrl = ValidateBaseUrl(credentials.BaseUrl) };
        RegressionClient client = new(() => settings);
        RegressionClientConfig? config = await client.GetClientConfigAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (config is null || string.IsNullOrWhiteSpace(config.S3Server) || string.IsNullOrWhiteSpace(config.Bucket))
            throw new InvalidOperationException("Couldn't reach the testing utility server for its S3 configuration.");
        if (string.IsNullOrWhiteSpace(credentials.S3Key) || string.IsNullOrWhiteSpace(credentials.S3Secret))
            throw new InvalidOperationException("Enter developer S3 credentials before downloading or uploading Testing Utility content.");
        settings.S3Server = config.S3Server;
        settings.Bucket = config.Bucket;
        settings.S3Key = credentials.S3Key;
        settings.S3Secret = credentials.S3Secret;
        return client;
    }

    private static async Task<ModelRepoProject> FetchProjectAsync(RegressionClient client, int id,
        CancellationToken cancellationToken)
    {
        ModelRepoProject? result = await client.FetchProjectAsync(id).WaitAsync(cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidDataException("The testing project could not be fetched from the server.");
    }

    private static async Task<TestingServerObject> FetchDetailAsync(RegressionClient client, string type, int id,
        CancellationToken cancellationToken)
    {
        if (type == "project") return ToObject(await FetchProjectAsync(client, id, cancellationToken).ConfigureAwait(false));
        ModelRepoGeometry? geometry = await client.FetchGeometryAsync(id).WaitAsync(cancellationToken).ConfigureAwait(false);
        return geometry is null ? throw new InvalidDataException("The testing geometry could not be fetched from the server.") : ToObject(geometry);
    }

    private static TestingWorkspaceResponse WithDetail(TestingWorkspaceResponse snapshot, TestingServerObject detail)
    {
        TestingServerObject[] objects = snapshot.Objects.Select(value => value.Id == detail.Id && value.Kind == detail.Kind
            ? detail : value).ToArray();
        return snapshot with { Objects = objects };
    }

    private static TestingServerObject ToObject(ModelRepoIdName value, string kind) =>
        new(ObjectID(kind, value.Id), value.Name ?? "", value.Folder ?? "", kind, "", null, "", null);

    private static TestingServerObject ToObject(ModelRepoProject value) => new(
        ObjectID("project", value.Id), value.Name ?? "", value.Folder ?? "", "project",
        value.ObjectVersion > 0 ? "v" + value.ObjectVersion.ToString(CultureInfo.InvariantCulture) : "",
        value.UnixPublishDate > 0 ? value.UnixPublishDate : null,
        value.Description ?? "", value.InterestPoints?.Count ?? 0);

    private static TestingServerObject ToObject(ModelRepoGeometry value) => new(
        ObjectID("geometry", value.Id), value.Name ?? "", value.Folder ?? "", "geometry",
        value.ObjectVersion > 0 ? "v" + value.ObjectVersion.ToString(CultureInfo.InvariantCulture) : "",
        value.UnixPublishDate > 0 ? value.UnixPublishDate : null,
        value.Description ?? "", null);

    private static TestingCommit ToCommit(CommitSummary value) => new(
        value.Id ?? "", value.DisplayId ?? "", value.Message ?? "", value.CommitterUnixTimestamp);

    private static string ObjectID(string type, int id) => type + ":" + id.ToString(CultureInfo.InvariantCulture);

    public static (string Type, int ID) ParseObjectID(string value)
    {
        string[] parts = value.Split(':', 2);
        if (parts.Length != 2 || parts[0] is not ("project" or "geometry")
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
            throw new ArgumentException("The selected testing object identity is invalid.");
        return (parts[0], id);
    }

    private static ModelRepoObjectType ParseType(string type) => type switch
    {
        "project" => ModelRepoObjectType.Project,
        "geometry" => ModelRepoObjectType.Geometry,
        _ => throw new ArgumentException("The testing object type is not supported.")
    };

    private static Project LoadProject(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new FileNotFoundException("Choose a saved HEC-RAS project.", path);
        OperationResult result = Project.TryLoad(Path.GetFullPath(path), out Project project);
        EnsureSuccess(result, "Opening the source project");
        return project;
    }

    private static void DisposeProjectLayers(Project project)
    {
        foreach (object layer in project.Plans.Cast<object>().Concat(project.Geometries).Concat(project.BoundaryConditions)
            .Concat(project.NValues).Concat(project.Terrains).Concat(project.Results))
            (layer as IDisposable)?.Dispose();
    }

    private static string ResolveGeometryPath(string sourcePath, string? geometryID)
    {
        if (string.IsNullOrWhiteSpace(geometryID)) return Path.GetFullPath(sourcePath);
        if (!Path.IsPathFullyQualified(sourcePath)) throw new ArgumentException("Choose a saved project before selecting one of its geometries.");
        Project project = LoadProject(sourcePath);
        try
        {
            Geometry geometry = project.Geometries.FirstOrDefault(value => PathsEqual(value.Filename, geometryID))
                ?? throw new InvalidDataException("The selected geometry no longer belongs to this project.");
            return Path.GetFullPath(geometry.Filename);
        }
        finally { DisposeProjectLayers(project); }
    }

    private static string DownloadDirectory(string name)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = Path.GetTempPath();
        string safe = FileHelpers.SanitizeFilename(name);
        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..") throw new InvalidDataException("The testing project name cannot be used as a local folder name.");
        return Path.Combine(root, "HEC-RAS", "TestingUtility", "Downloads", safe);
    }

    private static string ValidateBaseUrl(string? raw)
    {
        string candidate = string.IsNullOrWhiteSpace(raw) ? DefaultBaseUrl : raw.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? value)
            || value.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(value.UserInfo)
            || !string.IsNullOrEmpty(value.Query) || !string.IsNullOrEmpty(value.Fragment))
            throw new ArgumentException("The Testing Utility URL must be an absolute HTTP or HTTPS base URL.");
        return value.ToString().TrimEnd('/');
    }

    private static void EnsureSuccess(OperationResult? result, string operation)
    {
        if (result is null || !result.Result)
        {
            string? messages = result?.GetConcatenatedMessages();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(messages)
                ? operation + " failed." : operation + ": " + messages);
        }
    }

    private static void RequireText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(message);
    }

    private static void EnsureCoordinate(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentException("Point coordinates must be finite values.");
    }

    private static bool PathsEqual(string first, string second)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), comparison);
    }

    private static IEqualityComparer<string> StringComparerForPaths =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static ProgressReporter CreateReporter(Action<BackendOperationProgress>? report)
    {
        return new ProgressReporter(new TestingProgressTarget(report));
    }

    private static string ShortHash(string value) => value.Length <= 9 ? value : value[..9];

    private static Action<BackendOperationProgress>? SanitizeProgressReporter(
        Action<BackendOperationProgress>? report, TestingUtilityCredentials credentials)
    {
        if (report is null) return null;
        return progress => report(RedactProgress(progress, credentials));
    }

    private static BackendOperationProgress RedactProgress(BackendOperationProgress progress,
        TestingUtilityCredentials credentials)
    {
        IReadOnlyDictionary<string, string>? values = null;
        if (progress.Values is not null)
        {
            var redacted = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string key, string value) in progress.Values)
                redacted[Redact(key, credentials)!] = Redact(value, credentials)!;
            values = redacted;
        }
        return progress with
        {
            Message = Redact(progress.Message, credentials),
            TaskName = Redact(progress.TaskName, credentials),
            Values = values
        };
    }

    private static TestingUploadOutcome SanitizeOutcome(TestingUploadOutcome outcome,
        TestingUtilityCredentials credentials) => outcome with
    {
        Message = Redact(outcome.Message, credentials) ?? "Upload completed.",
        ContinuationError = Redact(outcome.ContinuationError, credentials)
    };

    private static Exception SanitizeException(Exception error, TestingUtilityCredentials credentials)
    {
        string message = Redact(error.Message, credentials) ?? "The Testing Utility upload failed.";
        return new InvalidOperationException(message);
    }

    private static string? Redact(string? value, TestingUtilityCredentials credentials)
    {
        if (value is null) return null;
        if (!string.IsNullOrEmpty(credentials.S3Key))
            value = value.Replace(credentials.S3Key, "[redacted]", StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(credentials.S3Secret))
            value = value.Replace(credentials.S3Secret, "[redacted]", StringComparison.Ordinal);
        return value;
    }

    private sealed class TestingUploadExecutionState
    {
        public bool UploadCompleted { get; set; }
        public string? ServerObjectID { get; set; }
    }

    private sealed class TestingProgressTarget(Action<BackendOperationProgress>? report) : IProgressTarget
    {
        public void WriteMessage(ProgressReporter sender, Message message) =>
            report?.Invoke(new BackendOperationProgress(Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));
        public void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress) =>
            report?.Invoke(new BackendOperationProgress(Fraction: Math.Clamp(globalProgress, 0, 100) / 100d,
                TaskName: sender.FullyQualifiedTaskName));
    }
}

public sealed record TestingUtilityCredentials(string? BaseUrl, string? S3Key, string? S3Secret);
public sealed record TestingWorkspaceRequest(TestingUtilityCredentials Credentials);
public sealed record TestingWorkspaceResponse(TestingServerObject[] Objects, TestingCommit[] Commits,
    string Status, string[] Log, TestingPoint[] Points, bool CanEditPoints = false, string? PointOwnerID = null);
public sealed record TestingPoiImportRequest(string SourcePath);
public sealed record TestingActionRequest(TestingUtilityCredentials Credentials, string Action, string Id,
    string? Name = null, string? Folder = null, string? Text = null, TestingPoint[]? Points = null,
    string? Commit = null, string? Tag = null, int Iterations = 1);
public sealed record TestingActionResponse(TestingWorkspaceResponse Snapshot, string? ProjectURL);
public sealed record TestingServerObject(string Id, string Name, string Folder, string Kind, string Version,
    long? PublicationDateUnixMilliseconds, string Description, int? PoiCount);
public sealed record TestingCommit(string Id, string DisplayID, string Message, long CommitterUnixTimestamp);
public sealed record TestingPoint(int? ServerID, string Name, double X, double Y);
public sealed record TestingPlanChoice(string Id, string Name, string? Geometry, string? BoundaryCondition);
public sealed record TestingGeometryChoice(string Id, string Title);
public sealed record TestingPointEditorSnapshot(TestingPoint[] Points);
public sealed record TestingUploadPrepareRequest(TestingUtilityCredentials Credentials, string Kind,
    string? SourcePath, string? GeometryID = null);
public sealed record TestingUploadPrepareResponse(string? SourcePath, string SuggestedName, string Folder,
    string Description, TestingPlanChoice[] Plans, string[] SelectedPlanIDs, TestingGeometryChoice[] Geometries,
    string? SelectedGeometryID, string[] ExistingNames, TestingPointEditorSnapshot? PointEditor,
    bool SourceGeometryValid, string ProjectName, TestingCommit[] Commits);
public sealed record TestingUploadExecuteRequest(TestingUtilityCredentials Credentials, string Kind,
    string? SourcePath, string? GeometryID, string? ServerObjectID, string Name, string? Folder,
    string? Description, string[]? PlanIDs, TestingPoint[]? Points, bool QueueRunAfterUpload = false,
    string? RunCommit = null, string? RunTag = null, int RunIterations = 1);
public sealed record TestingUploadOutcome(string Message, string? ServerObjectID, bool RunQueued,
    bool Cancelled = false, bool UploadCompleted = true, string? ContinuationError = null);
public sealed record TestingRunTarget(int Id, string Name, string ObjectType);
public sealed record TestingRunRequest(string Commit, string Tag, int Iterations, TestingRunTarget Target);
