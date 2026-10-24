using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Runtime;

namespace Ras.Backend.ServerChecks;

internal sealed record BoundaryInlineConnectionAcceptanceReport(
    string State,
    string ManifestPath,
    string FixtureDirectory,
    string ProjectPath,
    Guid ProjectSessionID,
    string InitialProjectRevision,
    string ConnectionProjectRevision,
    string BoundaryPath,
    string InitialBoundaryRevision,
    string SavedBoundaryRevision,
    string ConnectionName,
    string ConnectionSourcePath,
    string BoundaryLineID,
    string OriginalPayloadID,
    string DraftPayloadID,
    string RemotePayloadID,
    string SamplePageID,
    string SampleID,
    double SampleValueBefore,
    double SampleValueDraft,
    int VisualizationFileCount,
    bool BoundaryDraftStayedOnSourceRevision,
    bool ConnectionCommitOnlyChangedProjectXml,
    bool RebasedCoreContextReadback,
    bool StaleCoreContextRejected,
    bool BoundaryRefreshPreservedLineAndPayloadIdentityOrder,
    bool BoundaryRefreshPreservedFieldAndSampleDraft,
    bool BoundaryRefreshIncludedCommittedConnectionChoice,
    bool RemotePayloadCapturedCommittedConnection,
    bool BoundaryAndProjectStayedUnchangedBeforeParentSave,
    bool ParentSaveChangedOnlyBoundaryHdf,
    bool ReopenedRemoteAndEarlierDraftReadback,
    bool StaleBoundaryRevisionRejected,
    bool VisualizationFilesUnchanged,
    bool ConnectionSourceUnchanged);

/// <summary>
/// Exercises the real Boundary Condition draft refresh after a Project file
/// connection is committed. The generated fixture and external source live
/// only below the new caller-selected output directory.
/// </summary>
internal static partial class ServerAcceptance
{
    private const string BoundaryKind = "Boundary Conditions";
    private const string BoundaryConnectionFieldSuffix = "/ExternalPayloadEditor/ExternalPayload_DataConnection";

    public static async Task<BoundaryInlineConnectionAcceptanceReport> RunBoundaryInlineConnectionAsync(
        Uri baseAddress, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Boundary inline-connection acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Boundary inline-connection acceptance requires a new output directory; existing data is never overwritten.");

        string fixtureDirectory = Path.Combine(outputDirectory, "boundary-project-fixture");
        EditorFixtureManifest fixture = PlanComputeFixture.CreateEditor(fixtureDirectory);
        AddBoundaryTimeSeriesLine(fixture.ProjectPath, fixture.BoundaryPath);

        string sourceDirectory = Path.Combine(outputDirectory, "External Connection Sources");
        Directory.CreateDirectory(sourceDirectory);
        string connectionSourcePath = Path.Combine(sourceDirectory, "boundary-inline-series.txt");
        await File.WriteAllTextAsync(connectionSourcePath,
            "time,flow\n2026-10-16T00:00:00,2.5\n2026-10-16T01:00:00,3.5\n",
            new UTF8Encoding(false));
        string connectionSourceSHA = FileSHA256(connectionSourcePath);

        string[] visualizationPaths = Directory.EnumerateFiles(fixtureDirectory, "*.vis", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray();
        Dictionary<string, string> visualizationHashes = visualizationPaths.ToDictionary(
            path => path, FileSHA256, StringComparer.Ordinal);
        Dictionary<string, string> fixtureFilesBeforeConnection = FixtureFileHashes(fixtureDirectory);
        Dictionary<string, string> hdfHashesBeforeConnection = HdfFileHashes(fixtureDirectory);
        string initialProjectRevision = FileSHA256(fixture.ProjectPath);
        string initialBoundaryRevision = FileSHA256(fixture.BoundaryPath);
        string manifestPath = Path.Combine(outputDirectory, "boundary-inline-connection-acceptance.json");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        JsonElement opened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath = fixture.ProjectPath }, cancellation)).Result;
        string projectXml = StringProperty(opened, "projectXml");
        string projectRevision = StringProperty(opened, "revision");
        string projectXmlSHA = HashText(projectXml);
        if (!PathEquals(StringProperty(opened, "projectPath"), fixture.ProjectPath) ||
            !projectRevision.Equals(initialProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !projectRevision.Equals(FileSHA256(fixture.ProjectPath), StringComparison.OrdinalIgnoreCase) ||
            !projectXml.Equals(File.ReadAllText(fixture.ProjectPath), StringComparison.Ordinal))
            throw new SourceFileChangedException();

        Guid projectSessionID = Guid.NewGuid();
        CoreLayerRequestContext initialCoreLayerContext = DeserializeResult<SessionReferenceComposeReceipt>(
            (await SubmitOperationAsync(client, "workspace.references.compose", new SessionReferenceComposeRequest(
                fixture.ProjectPath, projectSessionID.ToString("D"), projectRevision, projectXml,
                projectXmlSHA), cancellation)).Result).NextContext;
        if (initialCoreLayerContext.References.Length != 0 ||
            !initialCoreLayerContext.ProjectRevision!.Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !initialCoreLayerContext.ProjectSessionID.Equals(projectSessionID.ToString("D"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The initial project session did not return a real empty-reference capability for this saved Project.");

        ModelSnapshot initialBoundary = await LoadBoundaryAsync(client, fixture.BoundaryPath,
            fixture.ProjectPath, projectXml, initialCoreLayerContext, sourceState: null, cancellation);
        BoundaryLineData initialLine = initialBoundary.Boundary?.Lines.SingleOrDefault()
            ?? throw new InvalidDataException("The isolated Boundary fixture must contain exactly one real Boundary Condition line.");
        BoundaryPayloadData originalPayload = initialLine.Payloads.SingleOrDefault()
            ?? throw new InvalidDataException("The isolated Boundary line must start with exactly one real payload.");
        if (initialBoundary.Revision != initialBoundaryRevision || initialLine.Geometry is not { Length: >= 2 })
            throw new InvalidDataException("The generated Boundary line or its starting HDF revision is invalid.");

        Dictionary<string, string> firstStructuralPayloadFields = new(initialLine.NewPayload.Fields,
            StringComparer.Ordinal)
        {
            ["NewPayloadPage/ViewModel_PayloadName"] = "Pending stage payload",
            ["NewPayloadPage/ViewModel_HydraulicType"] = "Stage",
            ["NewPayloadPage/ViewModel_HydraulicDataType"] = "Constant",
            ["NewPayloadPage/ViewModel_HydraulicConstantValue"] = "44.25"
        };
        ModelSnapshot firstStructuralDraft = await TransformBoundaryAsync(client, initialBoundary,
            fixture.BoundaryPath, fixture.ProjectPath, projectXml, initialCoreLayerContext,
            initialLine, firstStructuralPayloadFields, cancellation);
        BoundaryLineData lineWithDraftPayload = FindBoundaryLine(firstStructuralDraft, initialLine.Id);
        BoundaryPayloadData draftPayload = lineWithDraftPayload.Payloads.LastOrDefault()
            ?? throw new InvalidDataException("The first structural Boundary payload draft was not returned.");
        if (draftPayload.Id == originalPayload.Id || firstStructuralDraft.Revision != initialBoundaryRevision)
            throw new InvalidDataException("Creating the initial payload draft changed the source revision or reused the original payload identity.");

        string lineNameField = initialLine.Scope + "/DetailedBCLineHeaderControl/BoundaryConditionLine_Name";
        string initialPayloadNameField = initialLine.Scope + "/Payload/" + draftPayload.Id + "/PayloadListItem/Payload_Name";
        string draftLineName = "内联连接前的边界线草稿";
        string draftPayloadName = "待保留的阶段草稿";
        if (!firstStructuralDraft.Fields.ContainsKey(lineNameField) ||
            !firstStructuralDraft.Fields.ContainsKey(initialPayloadNameField))
            throw new InvalidDataException("The real Boundary snapshot omitted the line-name or payload-name fields required for this draft round trip.");

        BoundarySeriesData originalSeries = (firstStructuralDraft.Boundary?.Series.Values.ToArray() ?? [])
            .SingleOrDefault(series => !series.PageId.StartsWith("Spatial/", StringComparison.Ordinal) &&
                series.Samples.Length >= 2 && series.Samples.All(sample => sample.DatasetId == originalPayload.Id))
            ?? throw new InvalidDataException("The generated Boundary fixture did not expose the original payload's real editable time-series samples.");
        ModelSample[] beforeSamples = originalSeries.Samples;
        ModelSample sourceSample = beforeSamples[0];
        if (sourceSample.Y is not { } sampleBefore || !double.IsFinite(sampleBefore))
            throw new InvalidDataException("The original Boundary line's first sample must be a finite source value.");
        double sampleDraftValue = sampleBefore + 1.25;
        Dictionary<string, ModelSample[]> draftSamples = BoundarySamples(firstStructuralDraft);
        draftSamples[originalSeries.PageId] = beforeSamples.Select(sample => sample.Id == sourceSample.Id
            ? sample with { Y = sampleDraftValue }
            : sample).ToArray();
        Dictionary<string, string> editableFields = new(StringComparer.Ordinal)
        {
            [lineNameField] = draftLineName,
            [initialPayloadNameField] = draftPayloadName
        };
        ModelSaveRequest fieldAndSampleDraftRequest = CreateBoundaryRequest(firstStructuralDraft,
            fixture.BoundaryPath, fixture.ProjectPath, projectXml, initialCoreLayerContext,
            editableFields, draftSamples);
        ModelSnapshot editableDraft = DeserializeResult<ModelSnapshot>((await SubmitOperationAsync(client,
            "model.seriesTransform", new SeriesTransformRequest(fieldAndSampleDraftRequest,
                originalSeries.PageId, "refresh"), cancellation)).Result);
        BoundaryLineData editableDraftLine = FindBoundaryLine(editableDraft, initialLine.Id);
        BoundarySeriesData returnedEditableSeries = FindSeries(editableDraft, originalSeries.PageId);
        ModelSample returnedDraftSample = returnedEditableSeries.Samples.Single(sample => sample.Id == sourceSample.Id);
        if (editableDraft.Revision != initialBoundaryRevision || editableDraftLine.Name != draftLineName ||
            editableDraftLine.Payloads.Single(payload => payload.Id == draftPayload.Id).Name != draftPayloadName ||
            returnedDraftSample.Y != sampleDraftValue)
            throw new InvalidDataException("The real Boundary transform did not retain the pre-connection field, sample and structural drafts on the source HDF revision.");

        bool boundaryDraftStayedOnSourceRevision = FileSHA256(fixture.BoundaryPath)
            .Equals(initialBoundaryRevision, StringComparison.OrdinalIgnoreCase) &&
            FixtureHashesEqual(fixtureFilesBeforeConnection, FixtureFileHashes(fixtureDirectory));
        if (!boundaryDraftStayedOnSourceRevision)
            throw new InvalidDataException("Boundary draft operations changed source files before the Project connection transaction.");

        string connectionNameRequest = "边界内联外部序列";
        const string connectionDescription = "Boundary inline-connection acceptance source";
        var connectionInspectionRequest = new ProjectConnectionInspectRequest(
            Action: "add", Kind: "file", ProjectPath: fixture.ProjectPath, ProjectXml: projectXml,
            ProjectXmlSHA: projectXmlSHA, ProjectRevision: projectRevision,
            ProjectSessionID: projectSessionID, ConnectionName: null, Name: connectionNameRequest,
            Path: connectionSourcePath, Description: connectionDescription, ResultName: null,
            CoreLayerContext: initialCoreLayerContext);
        ProjectConnectionInspection connectionInspection = DeserializeResult<ProjectConnectionInspection>(
            (await SubmitOperationAsync(client, "project.connection.inspect", connectionInspectionRequest,
                cancellation)).Result);
        if (!connectionInspection.CanCommit ||
            !connectionInspection.ProjectRevision.Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !connectionInspection.ProjectXmlSHA.Equals(projectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
            connectionInspection.ProjectSessionID != projectSessionID ||
            connectionInspection.SourceReferencesSHA256.Length != 64 ||
            !connectionInspection.SourceReferencesSHA256.All(Uri.IsHexDigit) ||
            !PathEquals(connectionInspection.CanonicalPath!, connectionSourcePath) ||
            !connectionInspection.SourceSHA256!.Equals(connectionSourceSHA, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The real Project connection inspection did not capture this exact new external file and project session.");

        var commitRequest = new ProjectConnectionCommitRequest(
            Action: connectionInspectionRequest.Action,
            Kind: connectionInspectionRequest.Kind,
            ProjectPath: connectionInspectionRequest.ProjectPath,
            ProjectXml: connectionInspectionRequest.ProjectXml,
            ProjectXmlSHA: connectionInspectionRequest.ProjectXmlSHA,
            ProjectRevision: connectionInspectionRequest.ProjectRevision,
            ProjectSessionID: connectionInspectionRequest.ProjectSessionID,
            ConnectionName: connectionInspectionRequest.ConnectionName,
            Name: connectionInspectionRequest.Name,
            Path: connectionInspection.CanonicalPath,
            Description: connectionInspectionRequest.Description,
            ResultName: connectionInspectionRequest.ResultName,
            ExpectedConnectionSHA256: connectionInspection.ConnectionSHA256,
            ExpectedSourceSHA256: connectionInspection.SourceSHA256,
            ExpectedSourceReferencesSHA256: connectionInspection.SourceReferencesSHA256,
            ExpectedNormalizedName: connectionInspection.NormalizedName,
            CoreLayerContext: initialCoreLayerContext);
        ProjectConnectionCommitReceipt connectionReceipt = DeserializeResult<ProjectConnectionCommitReceipt>(
            (await SubmitOperationAsync(client, "project.connection.commit", commitRequest,
                cancellation)).Result);
        ProjectConnectionSnapshot committedProject = connectionReceipt.Project;
        CoreLayerRequestContext rebasedCoreLayerContext = committedProject.CoreLayerContext
            ?? throw new InvalidDataException("The connection receipt omitted the session context that was supplied for rebase.");
        string committedProjectRevision = committedProject.Revision;
        string committedProjectXml = committedProject.ProjectXml;
        string committedProjectXmlSHA = committedProject.ProjectXmlSHA;
        string connectionName = connectionReceipt.After?.Name
            ?? throw new InvalidDataException("The real connection commit did not return its persisted connection identity.");
        if (!connectionReceipt.Success || connectionReceipt.Action != "add" || connectionReceipt.Kind != "file" ||
            !connectionReceipt.PreviousProjectRevision.Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !committedProject.Success || committedProject.ProjectSessionID != projectSessionID ||
            !PathEquals(committedProject.ProjectPath, fixture.ProjectPath) ||
            !committedProjectRevision.Equals(FileSHA256(fixture.ProjectPath), StringComparison.OrdinalIgnoreCase) ||
            !committedProjectXmlSHA.Equals(HashText(committedProjectXml), StringComparison.OrdinalIgnoreCase) ||
            !rebasedCoreLayerContext.ProjectRevision!.Equals(committedProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            rebasedCoreLayerContext.ProjectSessionID != projectSessionID.ToString("D") ||
            rebasedCoreLayerContext.IdentityToken.Equals(initialCoreLayerContext.IdentityToken, StringComparison.OrdinalIgnoreCase) ||
            !connectionReceipt.After!.Name.Equals(connectionInspection.NormalizedName, StringComparison.Ordinal) ||
            connectionReceipt.After.Type != "File" ||
            !PathEquals(connectionReceipt.After.Path!, connectionSourcePath) ||
            !connectionReceipt.SourceCapture.SHA256!.Equals(connectionSourceSHA, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Project connection receipt or core-context rebase did not match its inspected source and project identity.");

        bool connectionCommitOnlyChangedProjectXml = OnlyProjectXmlChanged(
            fixtureFilesBeforeConnection, FixtureFileHashes(fixtureDirectory), fixtureDirectory, fixture.ProjectPath);
        bool hdfUnchangedAfterConnection = HdfHashesEqual(hdfHashesBeforeConnection, HdfFileHashes(fixtureDirectory));
        bool connectionSourceUnchanged = FileSHA256(connectionSourcePath)
            .Equals(connectionSourceSHA, StringComparison.OrdinalIgnoreCase);
        if (!connectionCommitOnlyChangedProjectXml || !hdfUnchangedAfterConnection || !connectionSourceUnchanged)
            throw new InvalidDataException("The Project connection commit changed files outside the copied .ras project XML or touched an HDF/external source.");

        var staleCoreInspectionRequest = connectionInspectionRequest with
        {
            ProjectXml = committedProjectXml,
            ProjectXmlSHA = committedProjectXmlSHA,
            ProjectRevision = committedProjectRevision,
            CoreLayerContext = initialCoreLayerContext,
            Name = "Must reject stale core context"
        };
        byte[] committedProjectBytes = await File.ReadAllBytesAsync(fixture.ProjectPath, cancellation);
        bool staleCoreContextRejected = await OperationRejectedAsync(client, "project.connection.inspect",
            staleCoreInspectionRequest, cancellation);
        if (!staleCoreContextRejected || !File.ReadAllBytes(fixture.ProjectPath).AsSpan().SequenceEqual(committedProjectBytes))
            throw new InvalidDataException("A pre-commit core context was accepted for the new Project revision or changed the committed .ras file.");

        ModelSaveRequest refreshedBoundaryRequest = CreateBoundaryRequest(editableDraft,
            fixture.BoundaryPath, fixture.ProjectPath, committedProjectXml, rebasedCoreLayerContext,
            editableFields, BoundarySamples(editableDraft));
        ModelSnapshot refreshedBoundary = DeserializeResult<ModelSnapshot>((await SubmitOperationAsync(client,
            "model.boundaryRefresh", refreshedBoundaryRequest, cancellation)).Result);
        BoundaryLineData refreshedLine = FindBoundaryLine(refreshedBoundary, initialLine.Id);
        bool boundaryRefreshPreservedLineAndPayloadIdentityOrder =
            BoundaryLinesHaveSameIdentities(editableDraft, refreshedBoundary);
        BoundarySeriesData refreshedSeries = FindSeries(refreshedBoundary, originalSeries.PageId);
        ModelSample refreshedSample = refreshedSeries.Samples.Single(sample => sample.Id == sourceSample.Id);
        bool boundaryRefreshPreservedFieldAndSampleDraft = refreshedLine.Name == draftLineName &&
            refreshedLine.Payloads.Single(payload => payload.Id == draftPayload.Id).Name == draftPayloadName &&
            refreshedSample.Y == sampleDraftValue &&
            refreshedBoundary.Fields.GetValueOrDefault(lineNameField) == draftLineName &&
            refreshedBoundary.Fields.GetValueOrDefault(initialPayloadNameField) == draftPayloadName;
        string connectionChoiceKey = "NewPayloadPage/ViewModel_SelectedExternalConnection";
        bool boundaryRefreshIncludedCommittedConnectionChoice =
            refreshedLine.NewPayload.Choices.TryGetValue(connectionChoiceKey, out BoundaryChoice[]? choices) &&
            choices is not null && choices.Any(choice => choice.Id == connectionName && choice.Title == connectionName);
        if (refreshedBoundary.Revision != initialBoundaryRevision ||
            !boundaryRefreshPreservedLineAndPayloadIdentityOrder ||
            !boundaryRefreshPreservedFieldAndSampleDraft || !boundaryRefreshIncludedCommittedConnectionChoice ||
            refreshedBoundary.CoreLayerContext?.IdentityToken != rebasedCoreLayerContext.IdentityToken)
            throw new InvalidDataException("model.boundaryRefresh failed to preserve the complete Boundary draft while returning the committed connection choice and rebased context.");

        Dictionary<string, string> remotePayloadFields = new(refreshedLine.NewPayload.Fields, StringComparer.Ordinal)
        {
            ["NewPayloadPage/ViewModel_PayloadName"] = "远程流量 - " + connectionName,
            ["NewPayloadPage/ViewModel_PayloadVariable"] = "Hydraulic",
            ["NewPayloadPage/ViewModel_HydraulicType"] = "Flow",
            ["NewPayloadPage/ViewModel_HydraulicDataType"] = "Remote",
            ["NewPayloadPage/ViewModel_SelectedExternalConnection"] = connectionName,
            ["NewPayloadPage/ViewModel_Path"] = string.Empty
        };
        string remotePayloadName = remotePayloadFields["NewPayloadPage/ViewModel_PayloadName"];
        ModelSnapshot remoteDraft = await TransformBoundaryAsync(client, refreshedBoundary,
            fixture.BoundaryPath, fixture.ProjectPath, committedProjectXml,
            rebasedCoreLayerContext, refreshedLine, remotePayloadFields, cancellation);
        BoundaryLineData remoteLine = FindBoundaryLine(remoteDraft, initialLine.Id);
        BoundaryPayloadData remotePayload = remoteLine.Payloads.SingleOrDefault(payload => payload.Name == remotePayloadName)
            ?? throw new InvalidDataException("The real Boundary transform did not return the Remote payload selected from the refreshed Project choices.");
        string remoteConnectionField = remotePayload.Scope + BoundaryConnectionFieldSuffix;
        bool remotePayloadCapturedCommittedConnection = remotePayload.Variable == "Flow" && remotePayload.IsHydraulic &&
            remoteDraft.Fields.GetValueOrDefault(remoteConnectionField) == connectionName;
        if (!remotePayloadCapturedCommittedConnection || remoteDraft.Revision != initialBoundaryRevision)
            throw new InvalidDataException("The Remote payload did not capture the committed exact file-connection name as a source-revision-stable draft.");

        bool boundaryAndProjectStayedUnchangedBeforeParentSave =
            FileSHA256(fixture.BoundaryPath).Equals(initialBoundaryRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(fixture.ProjectPath).Equals(committedProjectRevision, StringComparison.OrdinalIgnoreCase) &&
            FixtureHashesEqualExceptBoundary(fixtureFilesBeforeConnection,
                FixtureFileHashes(fixtureDirectory), fixtureDirectory, fixture.ProjectPath) &&
            FileSHA256(connectionSourcePath).Equals(connectionSourceSHA, StringComparison.OrdinalIgnoreCase);
        if (!boundaryAndProjectStayedUnchangedBeforeParentSave)
            throw new InvalidDataException("A Boundary refresh or structural draft changed source HDF, Project XML or the external file before model.save.");

        ModelSaveRequest parentSaveRequest = CreateBoundaryRequest(remoteDraft,
            fixture.BoundaryPath, fixture.ProjectPath, committedProjectXml, rebasedCoreLayerContext,
            editableFields, BoundarySamples(remoteDraft));
        ModelSnapshot savedBoundary = DeserializeResult<ModelSnapshot>((await SubmitOperationAsync(client,
            "model.save", parentSaveRequest, cancellation)).Result);
        string savedBoundaryRevision = FileSHA256(fixture.BoundaryPath);
        Dictionary<string, string> fixtureFilesAfterSave = FixtureFileHashes(fixtureDirectory);
        Dictionary<string, string> hdfHashesAfterSave = HdfFileHashes(fixtureDirectory);
        bool parentSaveChangedOnlyBoundaryHdf =
            !savedBoundaryRevision.Equals(initialBoundaryRevision, StringComparison.OrdinalIgnoreCase) &&
            savedBoundary.Revision.Equals(savedBoundaryRevision, StringComparison.OrdinalIgnoreCase) &&
            FileSHA256(fixture.ProjectPath).Equals(committedProjectRevision, StringComparison.OrdinalIgnoreCase) &&
            OnlyBoundaryHdfChanged(hdfHashesBeforeConnection, hdfHashesAfterSave,
                fixtureDirectory, fixture.BoundaryPath) &&
            FixtureHashesEqualExceptBoundary(fixtureFilesBeforeConnection, fixtureFilesAfterSave,
                fixtureDirectory, fixture.ProjectPath, fixture.BoundaryPath);
        if (!parentSaveChangedOnlyBoundaryHdf)
            throw new InvalidDataException("The parent model.save did not change only the expected Boundary HDF while preserving the committed Project and other fixture assets.");

        JsonElement reopenedProject = (await SubmitOperationAsync(client, "project.open",
            new { projectPath = fixture.ProjectPath }, cancellation)).Result;
        if (!StringProperty(reopenedProject, "revision").Equals(committedProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !String.Equals(StringProperty(reopenedProject, "projectXml"), committedProjectXml, StringComparison.Ordinal))
            throw new InvalidDataException("Reopening the project changed or lost the committed file connection.");
        ModelSnapshot reopenedBoundary = await LoadBoundaryAsync(client, fixture.BoundaryPath,
            fixture.ProjectPath, committedProjectXml, savedBoundary.CoreLayerContext ?? rebasedCoreLayerContext,
            savedBoundary.SourceState, cancellation);
        BoundaryLineData reopenedLine = FindBoundaryLine(reopenedBoundary, initialLine.Id);
        BoundaryPayloadData reopenedRemote = reopenedLine.Payloads.SingleOrDefault(payload => payload.Id == remotePayload.Id)
            ?? throw new InvalidDataException("Reopening the saved Boundary HDF did not recover the Remote payload identity.");
        BoundaryPayloadData reopenedDraftPayload = reopenedLine.Payloads.SingleOrDefault(payload => payload.Id == draftPayload.Id)
            ?? throw new InvalidDataException("Reopening the saved Boundary HDF did not recover the earlier structural payload draft identity.");
        ModelSample reopenedSample = FindSeries(reopenedBoundary, originalSeries.PageId).Samples
            .Single(sample => sample.Id == sourceSample.Id);
        bool reopenedRemoteAndEarlierDraftReadback =
            reopenedLine.Name == draftLineName && reopenedDraftPayload.Name == draftPayloadName &&
            reopenedRemote.Name == remotePayloadName && reopenedRemote.Variable == "Flow" &&
            reopenedBoundary.Fields.GetValueOrDefault(reopenedRemote.Scope + BoundaryConnectionFieldSuffix) == connectionName &&
            reopenedSample.Y == sampleDraftValue &&
            reopenedBoundary.Revision.Equals(savedBoundaryRevision, StringComparison.OrdinalIgnoreCase);
        if (!reopenedRemoteAndEarlierDraftReadback)
            throw new InvalidDataException("Reopening the saved Boundary model did not recover both the new Remote payload and pre-existing field/sample/payload drafts.");

        ModelSaveRequest staleBoundaryRequest = CreateBoundaryRequest(savedBoundary,
            fixture.BoundaryPath, fixture.ProjectPath, committedProjectXml,
            savedBoundary.CoreLayerContext ?? rebasedCoreLayerContext,
            editableFields, BoundarySamples(savedBoundary)) with { Revision = initialBoundaryRevision };
        byte[] savedBoundaryBytes = await File.ReadAllBytesAsync(fixture.BoundaryPath, cancellation);
        bool staleBoundaryRevisionRejected = await OperationRejectedAsync(client,
            "model.boundaryRefresh", staleBoundaryRequest, cancellation);
        if (!staleBoundaryRevisionRejected || !File.ReadAllBytes(fixture.BoundaryPath).AsSpan().SequenceEqual(savedBoundaryBytes))
            throw new InvalidDataException("The refreshed Boundary endpoint accepted its pre-save HDF revision or modified the saved Boundary file during rejection.");

        bool visualizationFilesUnchanged = SameFileSetAndHashes(visualizationHashes,
            Directory.EnumerateFiles(fixtureDirectory, "*.vis", SearchOption.AllDirectories)
                .ToDictionary(path => path, FileSHA256, StringComparer.Ordinal));
        connectionSourceUnchanged = FileSHA256(connectionSourcePath)
            .Equals(connectionSourceSHA, StringComparison.OrdinalIgnoreCase);
        if (!visualizationFilesUnchanged || !connectionSourceUnchanged)
            throw new InvalidDataException("Boundary inline-connection acceptance changed the visualization or external source file.");

        var report = new BoundaryInlineConnectionAcceptanceReport(
            State: "passed",
            ManifestPath: manifestPath,
            FixtureDirectory: fixtureDirectory,
            ProjectPath: fixture.ProjectPath,
            ProjectSessionID: projectSessionID,
            InitialProjectRevision: initialProjectRevision,
            ConnectionProjectRevision: committedProjectRevision,
            BoundaryPath: fixture.BoundaryPath,
            InitialBoundaryRevision: initialBoundaryRevision,
            SavedBoundaryRevision: savedBoundaryRevision,
            ConnectionName: connectionName,
            ConnectionSourcePath: connectionSourcePath,
            BoundaryLineID: initialLine.Id,
            OriginalPayloadID: originalPayload.Id,
            DraftPayloadID: draftPayload.Id,
            RemotePayloadID: remotePayload.Id,
            SamplePageID: originalSeries.PageId,
            SampleID: sourceSample.Id,
            SampleValueBefore: sampleBefore,
            SampleValueDraft: sampleDraftValue,
            VisualizationFileCount: visualizationPaths.Length,
            BoundaryDraftStayedOnSourceRevision: boundaryDraftStayedOnSourceRevision,
            ConnectionCommitOnlyChangedProjectXml: connectionCommitOnlyChangedProjectXml && hdfUnchangedAfterConnection,
            RebasedCoreContextReadback: rebasedCoreLayerContext.ProjectRevision.Equals(committedProjectRevision, StringComparison.OrdinalIgnoreCase) &&
                refreshedBoundary.CoreLayerContext?.IdentityToken == rebasedCoreLayerContext.IdentityToken,
            StaleCoreContextRejected: staleCoreContextRejected,
            BoundaryRefreshPreservedLineAndPayloadIdentityOrder: boundaryRefreshPreservedLineAndPayloadIdentityOrder,
            BoundaryRefreshPreservedFieldAndSampleDraft: boundaryRefreshPreservedFieldAndSampleDraft,
            BoundaryRefreshIncludedCommittedConnectionChoice: boundaryRefreshIncludedCommittedConnectionChoice,
            RemotePayloadCapturedCommittedConnection: remotePayloadCapturedCommittedConnection,
            BoundaryAndProjectStayedUnchangedBeforeParentSave: boundaryAndProjectStayedUnchangedBeforeParentSave,
            ParentSaveChangedOnlyBoundaryHdf: parentSaveChangedOnlyBoundaryHdf,
            ReopenedRemoteAndEarlierDraftReadback: reopenedRemoteAndEarlierDraftReadback,
            StaleBoundaryRevisionRejected: staleBoundaryRevisionRejected,
            VisualizationFilesUnchanged: visualizationFilesUnchanged,
            ConnectionSourceUnchanged: connectionSourceUnchanged);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(report, JsonOptions), cancellation);
        return report;
    }

    private static void AddBoundaryTimeSeriesLine(string projectPath, string boundaryPath)
    {
        RasRuntimeBootstrap.Configure();
        Project project = new(projectPath);
        try
        {
            BoundaryCondition boundary = project.BoundaryConditions.Single(item =>
                PathEquals(item.Filename, boundaryPath));
            BoundaryConditionLine line = boundary.BoundaryConditionLines.Add(new Polyline(
                new Point[] { new(0.2, 0.5), new(0.5, 0.5), new(0.8, 0.5) }));
            line.Name = "Boundary inline connection line";
            line.Add(new TimeSeriesPayload("Flow",
                new IrregularAbsoluteTimeSeriesDefinition(new DateTime[]
                { new(2026, 10, 16, 0, 0, 0, DateTimeKind.Local),
                    new DateTime(2026, 10, 16, 0, 30, 0, DateTimeKind.Local),
                    new DateTime(2026, 10, 16, 1, 0, 0, DateTimeKind.Local) }),
                new double[] { 2.5, 3.5, 4.5 }) { Name = "Original line flow" });
            boundary.Save();
        }
        finally
        {
            foreach (Result result in project.Results) result.Dispose();
            foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                         .Concat(project.Geometries).Concat(project.BoundaryConditions)
                         .Concat(project.Terrains).Concat(project.NValues).Concat(project.ClassificationLayers))
                layer.Dispose();
        }
    }

    private static async Task<ModelSnapshot> LoadBoundaryAsync(HttpClient client, string boundaryPath,
        string projectPath, string projectXml, CoreLayerRequestContext coreLayerContext,
        string? sourceState, CancellationToken cancellation)
    {
        ModelSnapshot snapshot = DeserializeResult<ModelSnapshot>((await SubmitOperationAsync(client,
            "model.load", new ModelRequest(boundaryPath, BoundaryKind, projectPath, projectXml,
                sourceState, coreLayerContext), cancellation)).Result);
        if (snapshot.Boundary is null || !snapshot.Revision.Equals(FileSHA256(boundaryPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Boundary model load did not return its exact current source revision and editor data.");
        return snapshot;
    }

    private static async Task<ModelSnapshot> TransformBoundaryAsync(HttpClient client,
        ModelSnapshot snapshot, string boundaryPath, string projectPath, string projectXml,
        CoreLayerRequestContext coreLayerContext, BoundaryLineData line,
        Dictionary<string, string> payloadFields, CancellationToken cancellation)
    {
        ModelSaveRequest model = CreateBoundaryRequest(snapshot, boundaryPath, projectPath,
            projectXml, coreLayerContext, new Dictionary<string, string>(StringComparer.Ordinal),
            BoundarySamples(snapshot));
        return DeserializeResult<ModelSnapshot>((await SubmitOperationAsync(client,
            "model.boundaryTransform", new BoundaryTransformRequest(model, line.Id,
                "create", null, payloadFields), cancellation)).Result);
    }

    private static ModelSaveRequest CreateBoundaryRequest(ModelSnapshot snapshot,
        string boundaryPath, string projectPath, string projectXml,
        CoreLayerRequestContext coreLayerContext, Dictionary<string, string> fields,
        Dictionary<string, ModelSample[]> samples) => new(
        AssetPath: boundaryPath,
        Kind: BoundaryKind,
        ProjectPath: projectPath,
        Revision: snapshot.Revision,
        Fields: fields,
        Images: snapshot.Images,
        ProjectXml: projectXml,
        SourceState: snapshot.SourceState,
        Rows: BoundaryRows(snapshot),
        Samples: samples,
        CoreLayerContext: coreLayerContext);

    private static Dictionary<string, ModelRow[]> BoundaryRows(ModelSnapshot snapshot)
    {
        var rows = new Dictionary<string, ModelRow[]>(StringComparer.Ordinal);
        foreach (BoundarySeriesData series in snapshot.Boundary?.Series.Values.ToArray() ?? [])
        {
            if (series.TableId is { Length: > 0 } tableId && series.Rows.Length > 0)
                rows[tableId] = series.Rows;
            if (series.Stations.Length > 0)
                rows[series.PageId + "/Stations"] = series.Stations;
        }
        return rows;
    }

    private static Dictionary<string, ModelSample[]> BoundarySamples(ModelSnapshot snapshot)
    {
        var samples = new Dictionary<string, ModelSample[]>(StringComparer.Ordinal);
        foreach (BoundarySeriesData series in snapshot.Boundary?.Series.Values.ToArray() ?? [])
        {
            // Spatial gauge tables are edited through their original source
            // rows; their projected plot samples are intentionally read-only.
            if (series.SchemaId == "MeteorologicGaugeSeries") continue;
            samples[series.PageId] = series.Samples;
        }
        return samples;
    }

    private static BoundaryLineData FindBoundaryLine(ModelSnapshot snapshot, string lineID) =>
        snapshot.Boundary?.Lines.SingleOrDefault(line => line.Id == lineID)
        ?? throw new InvalidDataException("The Boundary draft lost its stable line identity.");

    private static BoundarySeriesData FindSeries(ModelSnapshot snapshot, string pageID) =>
        snapshot.Boundary?.Series.GetValueOrDefault(pageID)
        ?? throw new InvalidDataException("The Boundary draft lost its actual source series page.");

    private static bool BoundaryLinesHaveSameIdentities(ModelSnapshot left, ModelSnapshot right)
    {
        BoundaryLineData[] leftLines = left.Boundary?.Lines ?? [];
        BoundaryLineData[] rightLines = right.Boundary?.Lines ?? [];
        return leftLines.Length == rightLines.Length && leftLines.Zip(rightLines).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Payloads.Select(payload => payload.Id)
                .SequenceEqual(pair.Second.Payloads.Select(payload => payload.Id), StringComparer.Ordinal));
    }

    private static T DeserializeResult<T>(JsonElement result) where T : class =>
        result.Deserialize<T>(JsonOptions)
        ?? throw new InvalidDataException("The Host operation returned an empty " + typeof(T).Name + " result.");

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static Dictionary<string, string> FixtureFileHashes(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path), FileSHA256, StringComparer.Ordinal);

    private static Dictionary<string, string> HdfFileHashes(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).Equals(".h5", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".hdf", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => Path.GetRelativePath(directory, path), FileSHA256, StringComparer.Ordinal);

    private static bool FixtureHashesEqual(Dictionary<string, string> expected,
        Dictionary<string, string> actual) => SameFileSetAndHashes(expected, actual);

    private static bool OnlyProjectXmlChanged(Dictionary<string, string> before,
        Dictionary<string, string> after, string fixtureDirectory, string projectPath)
    {
        string projectRelativePath = Path.GetRelativePath(fixtureDirectory, projectPath);
        if (!before.Keys.Order(StringComparer.Ordinal).SequenceEqual(after.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            !before.ContainsKey(projectRelativePath) || before[projectRelativePath] == after[projectRelativePath])
            return false;
        return before.All(pair => pair.Key == projectRelativePath ||
            pair.Value.Equals(after[pair.Key], StringComparison.OrdinalIgnoreCase));
    }

    private static bool FixtureHashesEqualExceptBoundary(Dictionary<string, string> before,
        Dictionary<string, string> after, string fixtureDirectory, string projectPath,
        string? boundaryPath = null)
    {
        if (!before.Keys.Order(StringComparer.Ordinal).SequenceEqual(after.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            return false;
        string projectRelativePath = Path.GetRelativePath(fixtureDirectory, projectPath);
        string? boundaryRelativePath = boundaryPath is null ? null : Path.GetRelativePath(fixtureDirectory, boundaryPath);
        return before.All(pair => pair.Key == projectRelativePath || pair.Key == boundaryRelativePath ||
            pair.Value.Equals(after[pair.Key], StringComparison.OrdinalIgnoreCase));
    }

    private static bool OnlyBoundaryHdfChanged(Dictionary<string, string> before,
        Dictionary<string, string> after, string fixtureDirectory, string boundaryPath)
    {
        if (!before.Keys.Order(StringComparer.Ordinal).SequenceEqual(after.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            return false;
        string[] changed = before.Keys.Where(key =>
            !before[key].Equals(after[key], StringComparison.OrdinalIgnoreCase)).ToArray();
        return changed.Length == 1 && PathEquals(Path.Combine(fixtureDirectory, changed[0]), boundaryPath);
    }

    private static bool HdfHashesEqual(Dictionary<string, string> before,
        Dictionary<string, string> after) => SameFileSetAndHashes(before, after);

    private static bool SameFileSetAndHashes(Dictionary<string, string> before,
        Dictionary<string, string> after) =>
        before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? value) &&
            pair.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
}
