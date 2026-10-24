using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Files;

namespace Ras.Backend;

public sealed record BoundaryInitialConditionsInspectRequest(
    ModelSaveRequest Model,
    string ProjectRevision,
    string ResultPath,
    string? Timestamp = null,
    string? NumberCulture = null,
    string? ProjectXmlFingerprint = null);

public sealed record BoundaryInitialConditionsAttachRequest(
    ModelSaveRequest Model,
    string ProjectRevision,
    string ResultPath,
    string ResultRevision,
    string OperationID,
    string? Timestamp = null,
    string? NumberCulture = null,
    string? ProjectXmlFingerprint = null);

public sealed record BoundaryInitialConditionsStatusRequest(
    ModelSaveRequest Model,
    string ProjectRevision,
    string ResultPath,
    string ResultRevision,
    string OperationID,
    string? Timestamp = null,
    string? NumberCulture = null,
    string? ProjectXmlFingerprint = null);

public sealed record BoundaryInitialConditionsInspection(
    string AssetPath,
    string AssetRevision,
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlFingerprint,
    string ResultPath,
    string? ResultRevision,
    string Timestamp,
    string NumberCulture,
    bool CanAttach,
    string? ErrorMessage);

public sealed record BoundaryInitialConditionsReceipt(
    string OperationID,
    string AssetPath,
    string ProjectPath,
    string ResultPath,
    string ResultRevision,
    string ProjectRevision,
    string ProjectXml,
    string ConnectionName,
    string Timestamp,
    ModelSnapshot Model,
    bool AlreadyCommitted);

public sealed record BoundaryInitialConditionsStatus(
    string State,
    BoundaryInitialConditionsReceipt? Receipt,
    string? ErrorMessage);

/// <summary>
/// Typed, source-backed attachment of a RAS Result as a boundary condition's
/// initial Stage and Velocity source. Attaching stores references only; the
/// retained PrepareForCompute path remains responsible for later ingestion.
/// </summary>
public static partial class ModelEditorAdapter
{
    private const string InitialConditionsInvalidTarget = "The selected item is not an editable project Boundary Condition.";
    private const string InitialConditionsInvalidResult = "The selected file is not a valid RAS Result.";
    private const string InitialConditionsInvalidTimestamp = "The result timestamp is not valid for the selected number culture.";
    private const string InitialConditionsConflictMessage = "The initial-conditions transaction conflicts with a newer project or model edit.";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal sealed record ValidatedCapture(
        string AssetPath,
        string AssetRevision,
        string ProjectPath,
        string ProjectRevision,
        string ProjectXml,
        string ProjectXmlFingerprint,
        string ResultPath,
        string ResultRevision,
        string Timestamp,
        string NumberCulture,
        CultureInfo Culture);

    public static BoundaryInitialConditionsInspection Inspect(BoundaryInitialConditionsInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            ValidatedCapture capture = ValidateCapture(
                request.Model, request.ProjectRevision, request.ResultPath,
                expectedResultRevision: null, request.Timestamp, request.NumberCulture,
                request.ProjectXmlFingerprint);
            return new(capture.AssetPath, capture.AssetRevision, capture.ProjectPath,
                capture.ProjectRevision, capture.ProjectXmlFingerprint, capture.ResultPath,
                capture.ResultRevision, capture.Timestamp, capture.NumberCulture,
                CanAttach: true, ErrorMessage: null);
        }
        catch (SourceFileChangedException)
        {
            throw;
        }
        catch (Exception error) when (IsInspectionValidationFailure(error))
        {
            string assetPath = SafeFullPath(request.Model.AssetPath);
            string projectPath = SafeFullPath(request.Model.ProjectPath ?? "");
            string resultPath = SafeFullPath(request.ResultPath);
            string projectXml = TryGetProjectXml(request.Model, projectPath);
            string? fingerprint = TryFingerprint(projectXml) ?? NormalizeOptionalFingerprint(request.ProjectXmlFingerprint);
            string? resultRevision = TryRevision(resultPath);
            string culture = TryCulture(request.NumberCulture)?.Name ?? CultureInfo.CurrentCulture.Name;
            return new(assetPath, request.Model.Revision, projectPath, request.ProjectRevision,
                fingerprint ?? "", resultPath, resultRevision, request.Timestamp ?? "", culture,
                CanAttach: false, ErrorMessage: error.Message);
        }
    }

    public static async Task<BoundaryInitialConditionsReceipt> AttachAsync(BoundaryInitialConditionsAttachRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string projectPath = SafeFullPath(request.Model.ProjectPath ?? "");
        // Resolve a repeated, exact attach request before checking old source
        // revisions: a committed pair has already advanced both target SHAs.
        string projectXmlFingerprint = RequestProjectXmlFingerprint(
            request.Model, projectPath, request.ProjectXmlFingerprint);
        string digest = RequestDigest(request.Model, request.ProjectRevision, projectXmlFingerprint,
            request.ResultPath, request.ResultRevision, request.Timestamp, request.NumberCulture);

        // A lost response must be recoverable before checking the old project/model
        // revisions, because a completed attach has already advanced both revisions.
        InitialConditionsTransactionStatus prior = InitialConditionsTransaction.ReadStatus(
            projectPath, request.OperationID, digest);
        if (prior.State == "committed" && prior.Receipt is { } committed)
            return await ReceiptFromCommittedOutcome(request, committed, alreadyCommitted: true);
        if (prior.State is "prepared" or "rolledBack")
        {
            InitialConditionsTransactionOutcome recovered = InitialConditionsTransaction.Recover(
                projectPath, request.OperationID, digest);
            if (recovered.State == "committed")
                return await ReceiptFromCommittedOutcome(request, recovered, alreadyCommitted: true);
            if (recovered.State != "notFound")
                throw TransactionFailure(recovered);
        }
        else if (prior.State != "notFound")
        {
            throw new InvalidOperationException(prior.ErrorMessage ?? InitialConditionsConflictMessage);
        }

        string projectXml = ResolveProjectXml(request.Model, projectPath);
        VerifyProvidedFingerprint(request.ProjectXmlFingerprint, Fingerprint(projectXml));
        ValidatedCapture capture = ValidateCapture(request.Model, request.ProjectRevision,
            request.ResultPath, request.ResultRevision, request.Timestamp,
            request.NumberCulture, projectXmlFingerprint);
        var stagingContext = new BoundaryInitialConditionsStagingContext(
            request.OperationID, digest, capture);

        ModelSnapshot snapshot = await SaveAsync(request.Model,
            initialConditions: stagingContext);
        InitialConditionsTransactionOutcome outcome = stagingContext.Outcome
            ?? throw new InvalidOperationException("The paired initial-conditions transaction did not produce a receipt.");
        return await ReceiptFromCommittedOutcome(request, outcome,
            alreadyCommitted: outcome.AlreadyCommitted, snapshotOverride: snapshot);
    }

    public static async Task<BoundaryInitialConditionsStatus> Status(BoundaryInitialConditionsStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string projectPath = SafeFullPath(request.Model.ProjectPath ?? "");
        string fingerprint = RequestProjectXmlFingerprint(request.Model, projectPath, request.ProjectXmlFingerprint);
        string digest = RequestDigest(request.Model, request.ProjectRevision, fingerprint,
            request.ResultPath, request.ResultRevision, request.Timestamp, request.NumberCulture);
        InitialConditionsTransactionStatus status = InitialConditionsTransaction.ReadStatus(
            projectPath, request.OperationID, digest);
        if (status.State != "committed" || status.Receipt is not { } committed)
            return new(status.State, null, status.ErrorMessage);

        try
        {
            BoundaryInitialConditionsReceipt receipt = await ReceiptFromCommittedOutcome(
                ToAttachRequest(request), committed, alreadyCommitted: true);
            return new("committed", receipt, null);
        }
        catch (SourceFileChangedException error)
        {
            // ReadStatus is read-only. A concurrent later edit turns this response
            // into a conflict without attempting to repair or overwrite either file.
            return new("conflict", null, error.Message);
        }
    }

    private static BoundaryInitialConditionsAttachRequest ToAttachRequest(BoundaryInitialConditionsStatusRequest request) =>
        new(request.Model, request.ProjectRevision, request.ResultPath, request.ResultRevision,
            request.OperationID, request.Timestamp, request.NumberCulture, request.ProjectXmlFingerprint);

    private static async Task<BoundaryInitialConditionsReceipt> ReceiptFromCommittedOutcome(
        BoundaryInitialConditionsAttachRequest request,
        InitialConditionsTransactionOutcome outcome,
        bool alreadyCommitted,
        ModelSnapshot? snapshotOverride = null)
    {
        if (outcome.State != "committed" || outcome.ProjectRevision is not { Length: > 0 } projectRevision ||
            outcome.ModelRevision is not { Length: > 0 } modelRevision || outcome.ProjectXml is null ||
            outcome.ConnectionName is not { Length: > 0 } connectionName)
            throw TransactionFailure(outcome);
        if (!SourceFilePaths.SameSourceFile(outcome.ProjectPath, request.Model.ProjectPath ?? "") ||
            !SourceFilePaths.SameSourceFile(outcome.ModelPath, request.Model.AssetPath))
            throw new InvalidOperationException("The committed initial-conditions receipt belongs to another source.");

        if (!string.Equals(Revision(outcome.ProjectPath), projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Revision(outcome.ModelPath), modelRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        CoreLayerRequestContext? committedCoreLayerContext = CoreLayerImportService.RebaseContext(
            request.Model.CoreLayerContext, projectRevision, outcome.ModelPath, modelRevision);
        ModelSnapshot snapshot = snapshotOverride ?? await LoadAsync(new ModelRequest(
            outcome.ModelPath, "Boundary Conditions", outcome.ProjectPath,
            outcome.ProjectXml, SourceState: null, CoreLayerContext: committedCoreLayerContext));
        if (!string.Equals(snapshot.Revision, modelRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        return new(request.OperationID, outcome.ModelPath, outcome.ProjectPath,
            SourceFilePaths.Canonical(request.ResultPath), request.ResultRevision,
            projectRevision, outcome.ProjectXml, connectionName,
            request.Timestamp ?? "", snapshot, alreadyCommitted);
    }

    private static ValidatedCapture ValidateCapture(
        ModelSaveRequest model,
        string expectedProjectRevision,
        string resultPath,
        string? expectedResultRevision,
        string? timestamp,
        string? numberCulture,
        string? projectXmlFingerprint)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Kind != "Boundary Conditions" || string.IsNullOrWhiteSpace(model.ProjectPath) ||
            string.IsNullOrWhiteSpace(model.AssetPath) || string.IsNullOrWhiteSpace(resultPath))
            throw new ValidationException(InitialConditionsInvalidTarget);

        string assetPath = SourceFilePaths.Canonical(model.AssetPath);
        string projectPath = SourceFilePaths.Canonical(model.ProjectPath);
        string normalizedAssetRevision = NormalizeSHA256(model.Revision, nameof(model.Revision));
        string normalizedProjectRevision = NormalizeSHA256(expectedProjectRevision, nameof(expectedProjectRevision));
        string currentAssetRevision = Revision(assetPath);
        string currentProjectRevision = Revision(projectPath);
        if (!EqualsRevision(currentAssetRevision, normalizedAssetRevision) ||
            !EqualsRevision(currentProjectRevision, normalizedProjectRevision))
            throw new SourceFileChangedException();
        if (SourceFilePaths.SameSourceFile(assetPath, projectPath))
            throw new ValidationException(InitialConditionsInvalidTarget);

        string capturedProjectXml = ResolveProjectXml(model, projectPath);
        string actualFingerprint = Fingerprint(capturedProjectXml);
        VerifyProvidedFingerprint(projectXmlFingerprint, actualFingerprint);
        XDocument parsedProject = XDocument.Parse(capturedProjectXml, LoadOptions.PreserveWhitespace);
        if (parsedProject.Root is not { } root || root.Name.LocalName != Project.Keys.Project)
            throw new ValidationException("The project XML draft is not a RAS Project document.");

        string resultFile = SourceFilePaths.Canonical(resultPath);
        if (!File.Exists(resultFile)) throw new ValidationException(InitialConditionsInvalidResult);
        string currentResultRevision = Revision(resultFile);
        if (expectedResultRevision is not null &&
            !EqualsRevision(currentResultRevision, NormalizeSHA256(expectedResultRevision, nameof(expectedResultRevision))))
            throw new SourceFileChangedException();
        if (SourceFilePaths.SameSourceFile(resultFile, assetPath) ||
            SourceFilePaths.SameSourceFile(resultFile, projectPath))
            throw new ValidationException(InitialConditionsInvalidResult);
        if (!Result.IsValid(resultFile)) throw new ValidationException(InitialConditionsInvalidResult);

        CultureInfo culture = ResolveCulture(numberCulture);
        if (!string.IsNullOrWhiteSpace(timestamp) &&
            !DateTime.TryParse(timestamp, culture, DateTimeStyles.None, out _))
            throw new ValidationException(InitialConditionsInvalidTimestamp);

        // Check actual saved membership before ModelContext's staging behavior can
        // temporarily add a copied layer to the in-memory project.
        VerifySavedBoundaryMembership(projectPath, assetPath, model.CoreLayerContext);
        var sourceRequest = new ModelRequest(assetPath, model.Kind, projectPath, capturedProjectXml,
            model.SourceState, model.CoreLayerContext);
        using (var context = new ModelContext(sourceRequest))
        {
            if (context.Layer is not BoundaryCondition boundary || boundary.IsResult ||
                !SourceFilePaths.SameSourceFile(boundary.Filename, assetPath) ||
                !ReferenceEquals(boundary.Project, context.Project) ||
                !context.Project.BoundaryConditions.Any(item => ReferenceEquals(item, boundary)))
                throw new ValidationException(InitialConditionsInvalidTarget);
            _ = Boundary(context, assetPath, currentAssetRevision, model.SourceState)
                ?? throw new ValidationException(InitialConditionsInvalidTarget);
        }
        if (!EqualsRevision(Revision(assetPath), currentAssetRevision) ||
            !EqualsRevision(Revision(projectPath), currentProjectRevision))
            throw new SourceFileChangedException();
        if (!EqualsRevision(Revision(resultFile), currentResultRevision))
            throw new SourceFileChangedException();

        return new(assetPath, currentAssetRevision, projectPath, currentProjectRevision,
            capturedProjectXml, actualFingerprint, resultFile, currentResultRevision,
            timestamp ?? "", culture.Name, culture);
    }

    private static void VerifySavedBoundaryMembership(string projectPath, string assetPath,
        CoreLayerRequestContext? coreLayerContext)
    {
        Project project = CoreLayerImportService.OpenProject(projectPath, null, coreLayerContext);
        try
        {
            BoundaryCondition[] matches = project.BoundaryConditions
                .Where(boundary => SourceFilePaths.SameSourceFile(boundary.Filename, assetPath)).ToArray();
            if (matches.Length != 1 || matches[0].IsResult || !ReferenceEquals(matches[0].Project, project))
                throw new ValidationException(InitialConditionsInvalidTarget);
        }
        finally
        {
            foreach (IDisposable item in ProjectLayers(project)) item.Dispose();
        }
    }

    private static IEnumerable<IDisposable> ProjectLayers(Project project) => project.Plans.Cast<object>()
        .Concat(project.Geometries).Concat(project.Terrains).Concat(project.BoundaryConditions)
        .Concat(project.NValues).Concat(project.ClassificationLayers).Concat(project.Results)
        .OfType<IDisposable>();

    private static string ResolveProjectXml(ModelSaveRequest model, string projectPath)
    {
        if (model.ProjectXml is { } supplied) return supplied;
        return File.ReadAllText(projectPath, StrictUtf8);
    }

    private static string RequestProjectXmlFingerprint(ModelSaveRequest model, string projectPath, string? provided)
    {
        if (model.ProjectXml is { } draft)
        {
            string actual = Fingerprint(draft);
            VerifyProvidedFingerprint(provided, actual);
            return actual;
        }
        if (provided is not null) return NormalizeSHA256(provided, nameof(provided));
        return Fingerprint(ResolveProjectXml(model, projectPath));
    }

    private static string TryGetProjectXml(ModelSaveRequest model, string projectPath)
    {
        try { return ResolveProjectXml(model, projectPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { return ""; }
    }

    private static void VerifyProvidedFingerprint(string? provided, string actual)
    {
        if (provided is null) return;
        string normalized = NormalizeSHA256(provided, nameof(provided));
        if (!EqualsRevision(normalized, actual)) throw new SourceFileChangedException();
    }

    private static CultureInfo ResolveCulture(string? numberCulture)
    {
        if (numberCulture is null) return CultureInfo.CurrentCulture;
        if (string.IsNullOrWhiteSpace(numberCulture))
            throw new ValidationException("The requested number culture is not supported.");
        try { return CultureInfo.GetCultureInfo(numberCulture); }
        catch (CultureNotFoundException error)
        {
            throw new ValidationException("The requested number culture is not supported.", error);
        }
    }

    private static CultureInfo? TryCulture(string? value)
    {
        if (value is null) return CultureInfo.CurrentCulture;
        try { return string.IsNullOrWhiteSpace(value) ? null : CultureInfo.GetCultureInfo(value); }
        catch (CultureNotFoundException) { return null; }
    }

    private static bool IsInspectionValidationFailure(Exception error) => error is
        ArgumentException or ValidationException or IOException or UnauthorizedAccessException or
        XmlException or InvalidOperationException or NotSupportedException;

    private static string RequestDigest(
        ModelSaveRequest model,
        string projectRevision,
        string projectXmlFingerprint,
        string resultPath,
        string resultRevision,
        string? timestamp,
        string? numberCulture)
    {
        var identity = new
        {
            protocol = "boundary.initialConditions.attach.v1",
            model,
            projectRevision = NormalizeSHA256(projectRevision, nameof(projectRevision)),
            projectXmlFingerprint,
            resultPath = SourceFilePaths.Canonical(resultPath),
            resultRevision = NormalizeSHA256(resultRevision, nameof(resultRevision)),
            dependencyPath = SourceFilePaths.Canonical(resultPath),
            dependencyRevision = NormalizeSHA256(resultRevision, nameof(resultRevision)),
            timestamp,
            numberCulture
        };
        JsonElement element = JsonSerializer.SerializeToElement(identity, BackendJson.Options);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            WriteCanonicalJson(writer, element);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray()) WriteCanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.Number: writer.WriteRawValue(element.GetRawText(), skipInputValidation: false); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new ArgumentException("The initial-conditions request contains an unsupported JSON value.");
        }
    }

    private static InvalidOperationException TransactionFailure(InitialConditionsTransactionOutcome outcome) =>
        new(outcome.ErrorMessage ?? InitialConditionsConflictMessage);

    private static string NormalizeSHA256(string value, string parameterName)
    {
        if (value is null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 revision is required.", parameterName);
        return value.ToUpperInvariant();
    }

    private static bool EqualsRevision(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(value)));

    private static string? TryFingerprint(string value)
    {
        try { return Fingerprint(value); }
        catch (EncoderFallbackException) { return null; }
    }

    private static string? NormalizeOptionalFingerprint(string? value)
    {
        if (value is null) return null;
        try { return NormalizeSHA256(value, nameof(value)); }
        catch (ArgumentException) { return null; }
    }

    private static string? TryRevision(string path)
    {
        try { return File.Exists(path) ? Revision(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string SafeFullPath(string path)
    {
        try { return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path); }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        { return path; }
    }
}

/// <summary>
/// Created only by the typed attach workflow. It applies the retained setter to
/// the staged BoundaryCondition and holds the exact two-file commit identity.
/// </summary>
public sealed class BoundaryInitialConditionsStagingContext
{
    private readonly ModelEditorAdapter.ValidatedCapture capture;
    private readonly string operationID;
    private readonly string requestDigest;
    private XElement? connectionXml;

    internal string OperationID => operationID;
    internal string RequestDigest => requestDigest;
    internal string ProjectPath => capture.ProjectPath;
    internal string ProjectRevision => capture.ProjectRevision;
    internal string ProjectXml => capture.ProjectXml;
    internal string AssetPath => capture.AssetPath;
    internal string AssetRevision => capture.AssetRevision;
    internal string ResultPath => capture.ResultPath;
    internal string ResultRevision => capture.ResultRevision;
    internal string Timestamp => capture.Timestamp;
    internal string CommittedProjectXml => Outcome?.ProjectXml
        ?? throw new InvalidOperationException("The initial-condition transaction has not committed.");
    internal string ConnectionName { get; private set; } = "";
    internal InitialConditionsTransactionOutcome? Outcome { get; private set; }

    internal BoundaryInitialConditionsStagingContext(
        string operationID,
        string requestDigest,
        ModelEditorAdapter.ValidatedCapture project)
    {
        this.operationID = operationID;
        this.requestDigest = requestDigest;
        capture = project;
    }

    internal ModelSaveRequest UseCapturedProjectXml(ModelSaveRequest request)
    {
        if (!SourceFilePaths.SameSourceFile(request.AssetPath, AssetPath) ||
            request.Kind != "Boundary Conditions" || request.ProjectPath is null ||
            !SourceFilePaths.SameSourceFile(request.ProjectPath, ProjectPath) ||
            !string.Equals(request.Revision, AssetRevision, StringComparison.OrdinalIgnoreCase) ||
            (request.ProjectXml is not null && !string.Equals(request.ProjectXml, ProjectXml, StringComparison.Ordinal)))
            throw new SourceFileChangedException();
        return request with { ProjectPath = ProjectPath, ProjectXml = ProjectXml };
    }

    internal void Prepare(BoundaryCondition condition, Project project, string stagingPath)
    {
        if (condition.IsResult || !ReferenceEquals(condition.Project, project) ||
            !ReferenceEquals(project.BoundaryConditions.SingleOrDefault(item => ReferenceEquals(item, condition)), condition) ||
            !SourceFilePaths.SameSourceFile(condition.Filename, stagingPath))
            throw new ValidationException("The staged Boundary Condition no longer belongs to the captured project source.");
        if (!string.Equals(ModelEditorAdapter.RevisionForInitialConditions(AssetPath), AssetRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ModelEditorAdapter.RevisionForInitialConditions(ProjectPath), ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ModelEditorAdapter.RevisionForInitialConditions(ResultPath), ResultRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        ExternalConnectionBase[] before = project.ConnectionSource.Connections.ToArray();
        var operation = condition.InitialConditions.SetRASResultExternalSource(ResultPath, Timestamp);
        if (!operation)
            throw new ValidationException(operation.GetConcatenatedMessages());
        ExternalConnectionBase[] created = project.ConnectionSource.Connections
            .Where(connection => !before.Any(item => ReferenceEquals(item, connection))).ToArray();
        if (created.Length != 1 || created[0] is not FileExternalConnection fileConnection ||
            string.IsNullOrWhiteSpace(fileConnection.Name) ||
            !SourceFilePaths.SameSourceFile(fileConnection.AbsolutePath, ResultPath))
            throw new InvalidOperationException("The retained initial-condition setter did not create exactly one file connection.");
        ConnectionName = fileConnection.Name;
        connectionXml = fileConnection.SaveXML();
    }

    internal void Commit(string stagingPath)
    {
        if (connectionXml is null || string.IsNullOrWhiteSpace(ConnectionName))
            throw new InvalidOperationException("The retained initial-condition setter has not been applied to the staged model.");
        string newProjectXml = BoundaryInitialConditionsService.AppendConnection(ProjectXml, connectionXml, ConnectionName);
        string newProjectRevision = BoundaryInitialConditionsService.Fingerprint(newProjectXml);
        if (newProjectRevision.Length != 64) throw new InvalidOperationException("The staged project XML fingerprint is invalid.");
        Outcome = InitialConditionsTransaction.Commit(new InitialConditionsTransactionRequest(
            ProjectPath, AssetPath, stagingPath, OperationID, RequestDigest,
            ProjectRevision, AssetRevision, newProjectXml, ConnectionName,
            ResultPath, ResultRevision));
        if (Outcome.State != "committed")
            throw new InvalidOperationException(Outcome.ErrorMessage ?? "The project and Boundary Condition were not committed together.");
    }

}

internal static class BoundaryInitialConditionsService
{
    internal static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(value)));

    internal static string AppendConnection(string projectXml, XElement connectionXml, string connectionName)
    {
        XDocument document = XDocument.Parse(projectXml, LoadOptions.PreserveWhitespace);
        XElement root = document.Root ?? throw new ValidationException("The project XML has no root element.");
        if (root.Name.LocalName != Project.Keys.Project)
            throw new ValidationException("The project XML draft is not a RAS Project document.");
        XName connectionsName = root.Name.Namespace + Project.Keys.ConnectionGroup;
        XElement[] matchingContainers = root.Elements(connectionsName).ToArray();
        if (matchingContainers.Length > 1)
            throw new ValidationException("The project XML contains more than one Connections section.");
        XElement connections;
        if (matchingContainers.Length == 1) connections = matchingContainers[0];
        else
        {
            connections = new XElement(connectionsName);
            AppendPreservingWhitespace(root, connections);
        }
        if (connections.Elements().Any(element => element.Name.LocalName == ExternalConnectionBase.XmlElementName &&
                string.Equals((string?)element.Attribute("Name"), connectionName, StringComparison.Ordinal)))
            throw new InvalidOperationException("The retained setter generated a connection name that already exists in the project draft.");
        XElement sourceConnection = new(connectionXml);
        if (!string.Equals((string?)sourceConnection.Attribute("Name"), connectionName, StringComparison.Ordinal) ||
            !string.Equals((string?)sourceConnection.Attribute("Type"), FileExternalConnection.TypeName, StringComparison.Ordinal))
            throw new InvalidOperationException("The retained file connection XML does not match the staged connection.");
        AppendPreservingWhitespace(connections, sourceConnection);
        if (document.Declaration is { } declaration) declaration.Encoding = "utf-8";
        using var text = new Utf8StringWriter();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = document.Declaration is null,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            CheckCharacters = true
        };
        using (XmlWriter writer = XmlWriter.Create(text, settings)) document.Save(writer);
        return text.ToString();
    }

    private static void AppendPreservingWhitespace(XContainer parent, XElement element)
    {
        XNode? last = parent.Nodes().LastOrDefault();
        if (last is XText trailing && string.IsNullOrWhiteSpace(trailing.Value))
        {
            string? existingChildIndent = parent.Elements().LastOrDefault()?.PreviousNode is XText before &&
                string.IsNullOrWhiteSpace(before.Value) ? before.Value : null;
            string indent = existingChildIndent ?? (trailing.Value + "  ");
            trailing.AddBeforeSelf(new XText(indent), element);
            return;
        }
        parent.Add(element);
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => new UTF8Encoding(false, true);
    }
}
