using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ras.Observables;
using Ras.Observables.Layers;
using Utility.Validation;
using Visual.Mapping.Observables;

namespace Ras.Backend;

public sealed record SourcePropertyValidationRequest(ModelSaveRequest Model, string RequestID,
    string EditorID, int LoadRevision, int EditRevision, int SelectionRevision,
    string ValidationSource, string FieldID, string PropertyName, string Text,
    string[] SelectedRowIDs, string SourceStateFingerprint, string ParentDraftIdentity);

public sealed record SourcePropertyValidationMessage(string ID, string Text, string Level);
public sealed record SourcePropertyValidationResponse(string RequestID, string EditorID,
    int LoadRevision, int EditRevision, int SelectionRevision, string ValidationSource,
    string FieldID, string PropertyName, string Text, string[] SelectedRowIDs,
    string SourceRevision, string SourceStateFingerprint, string ParentDraftIdentity,
    Dictionary<string, string[]> TableSelections, PlanTimeWindowEdit[] PlanTimeWindowEdits,
    SourcePropertyValidationMessage[] Messages);

public static partial class ModelEditorAdapter
{
    private static readonly IReadOnlyDictionary<string, string> LeveeValidationRoutes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Name"] = "Name",
            ["TopWidth"] = "TopWidthString",
            ["LeftWidth"] = "LeftWidthString",
            ["RightWidth"] = "RightWidthString",
            ["LeftGrade"] = "LeftGradeString",
            ["RightGrade"] = "RightGradeString"
        };

    private static readonly IReadOnlyDictionary<string, string> RegionValidationRoutes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ConstantSize"] = "ConstantSizeString",
            ["SecondaryConstantSize"] = "SecondaryConstantSizeString",
            ["RotationDegrees"] = "RotationDegreesString",
            ["BiasInternal"] = "BiasInternalString",
            ["TinResolution"] = "TinResolutionString",
            ["SizeTolerance"] = "SizeToleranceString"
        };

    public static Task<SourcePropertyValidationResponse> ValidateSourcePropertyAsync(
        SourcePropertyValidationRequest request)
    {
        ValidateSourcePropertyEnvelope(request);
        if (request.ValidationSource != "LeveeModPropertiesDetailEditor")
            throw new ArgumentException("The requested source validation owner is unsupported.");

        ModelSaveRequest model = request.Model;
        if (model.Kind != "Terrain") throw new ArgumentException("Levee property validation requires a terrain model.");
        if (!LeveeValidationRoutes.TryGetValue(request.PropertyName, out string? route)
            || !request.FieldID.EndsWith("/LeveeModPropertiesDetailEditor/" + route,
                StringComparison.Ordinal))
            throw new ArgumentException("The requested Levee validation property does not match its field.");
        if (!string.Equals(model.Fields.GetValueOrDefault(request.FieldID), request.Text, StringComparison.Ordinal))
            throw new ArgumentException("The Levee validation text does not match the captured editor field.");

        string[] selected = CanonicalIDs(request.SelectedRowIDs, "Levee property selection");
        if (model.TableSelections is null
            || !model.TableSelections.TryGetValue("LeveeModPropertiesEditor/PropertySets", out string[]? selectedTableRows)
            || !CanonicalIDs(selectedTableRows, "Levee property table selection").SequenceEqual(selected, StringComparer.Ordinal))
            throw new ArgumentException("The Levee validation selection no longer matches the property table.");
        string expectedScope = "TerrainSelection/" + string.Join("+", selected) + "/";
        if (!request.FieldID.StartsWith(expectedScope, StringComparison.Ordinal))
            throw new ArgumentException("The Levee validation field does not belong to the selected property rows.");

        string revision = Revision(model.AssetPath);
        if (!string.Equals(revision, model.Revision, StringComparison.Ordinal)) throw new SourceFileChangedException();
        SourcePropertyValidationMessage[] messages;
        using (var context = new ModelContext(model))
        {
            TerrainEditor editor = TerrainInputs(context, model.AssetPath, revision, model.SourceState)
                ?? throw new ArgumentException("This model is not a terrain.");
            editor.CaptureSelectedInput(model.Fields);
            editor.ApplySelections(model.TableSelections);
            if (!editor.Fields().ContainsKey(request.FieldID))
                throw new ArgumentException("The selected Levee property set no longer exists.");
            messages = ValidateForwardedValue(typeof(ObservableLeveeModProperties), request.PropertyName,
                request.Text, request.FieldID);
        }
        if (!string.Equals(Revision(model.AssetPath), revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        return Task.FromResult(SourcePropertyValidationResponseFor(request, revision, messages));
    }

    public static Task<SourcePropertyValidationResponse> ValidateGeometryRegionPropertyAsync(
        SourcePropertyValidationRequest request)
    {
        ValidateSourcePropertyEnvelope(request);
        if (request.ValidationSource != "ConceptualMeshControlPointEditor")
            throw new ArgumentException("The requested source validation owner is unsupported.");

        ModelSaveRequest model = request.Model;
        if (model.Kind != "Geometry") throw new ArgumentException("Conceptual mesh property validation requires a geometry model.");
        if (!RegionValidationRoutes.TryGetValue(request.PropertyName, out string? route)
            || !request.FieldID.EndsWith("/ConceptualMeshControlPointEditor/" + route,
                StringComparison.Ordinal))
            throw new ArgumentException("The requested Conceptual Mesh validation property does not match its field.");
        if (!string.Equals(model.Fields.GetValueOrDefault(request.FieldID), request.Text, StringComparison.Ordinal))
            throw new ArgumentException("The Conceptual Mesh validation text does not match the captured editor field.");

        string[] selected = CanonicalIDs(request.SelectedRowIDs, "Conceptual Mesh region selection");
        if (model.TableSelections is null
            || !model.TableSelections.TryGetValue("Regions", out string[]? selectedTableRows)
            || !CanonicalIDs(selectedTableRows, "Conceptual Mesh table selection").SequenceEqual(selected, StringComparer.Ordinal))
            throw new ArgumentException("The validation selection no longer matches the Regions table.");

        string revision = Revision(model.AssetPath);
        if (!string.Equals(revision, model.Revision, StringComparison.Ordinal)) throw new SourceFileChangedException();
        SourcePropertyValidationMessage[] messages;
        using (var context = new ModelContext(new ModelRequest(model.AssetPath, model.Kind,
            model.ProjectPath, model.ProjectXml, model.SourceState, model.CoreLayerContext)))
        {
            GeometryTopologyDraftState state = string.IsNullOrWhiteSpace(model.GeometryTopologyState)
                ? NewTopologyDraft(model.AssetPath, revision)
                : ReadTopologyDraft(model.GeometryTopologyState, model.AssetPath, revision);
            var replay = ReplayGeometryTopologyDraft(context, model.AssetPath, revision, state);
            GeometryTopologyFeatureRow[] sourceRows = RegionTable(replay.Session.ObservableTopology).Rows;
            Dictionary<string, GeometryTopologyFeatureRow> rowsByID = sourceRows
                .ToDictionary(row => row.ID, StringComparer.Ordinal);
            if (selected.Any(id => !rowsByID.ContainsKey(id)))
                throw new ArgumentException("A selected Conceptual Mesh region no longer exists.");

            if (selected.Length == 0) throw new ArgumentException("Select at least one Conceptual Mesh region.");
            messages = ValidateForwardedValue(typeof(ObservableConceptualMeshControlPoint),
                request.PropertyName, request.Text, request.FieldID);
        }
        if (!string.Equals(Revision(model.AssetPath), revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        return Task.FromResult(SourcePropertyValidationResponseFor(request, revision, messages));
    }

    private static void ValidateSourcePropertyEnvelope(SourcePropertyValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ModelSaveRequest model = request.Model ?? throw new ArgumentException("Missing source validation model capture.");
        VerifyKind(model);
        if (string.IsNullOrWhiteSpace(request.RequestID) || string.IsNullOrWhiteSpace(request.EditorID)
            || string.IsNullOrWhiteSpace(request.FieldID) || string.IsNullOrWhiteSpace(request.PropertyName)
            || request.Text is null || request.SelectedRowIDs is null || request.SourceStateFingerprint is null
            || request.ParentDraftIdentity is null)
            throw new ArgumentException("The source validation capture is incomplete.");
        if (model.Images is null || model.Images.Values.Any(items => items is null || items.Length != 0)
            || model.Rows?.Values.Any(items => items is null || items.Length != 0) == true
            || model.Samples?.Values.Any(items => items is null || items.Length != 0) == true
            || model.ChangedCells?.Values.Any(items => items is null || items.Length != 0) == true)
            throw new ArgumentException("Source validation cannot carry image, row, sample, or table-cell edits.");
        if (!string.Equals(SourcePropertyFingerprint(model.SourceState), request.SourceStateFingerprint, StringComparison.Ordinal))
            throw new ArgumentException("The captured source-state identity does not match its draft.");
        if (model.TableSelections is null)
            throw new ArgumentException("The source validation capture is missing current table selections.");
        if (model.PlanTimeWindowEdits is null)
            throw new ArgumentException("The source validation capture is missing current time-window events.");
    }

    private static SourcePropertyValidationResponse SourcePropertyValidationResponseFor(
        SourcePropertyValidationRequest request, string revision, SourcePropertyValidationMessage[] messages)
    {
        Dictionary<string, string[]> selections = request.Model.TableSelections!
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        return new SourcePropertyValidationResponse(request.RequestID, request.EditorID,
            request.LoadRevision, request.EditRevision, request.SelectionRevision,
            request.ValidationSource, request.FieldID, request.PropertyName, request.Text,
            request.SelectedRowIDs.ToArray(), revision, SourcePropertyFingerprint(request.Model.SourceState),
            request.ParentDraftIdentity, selections, request.Model.PlanTimeWindowEdits!.ToArray(), messages);
    }

    private static SourcePropertyValidationMessage[] ValidateForwardedValue(Type ownerType,
        string propertyName, string text, string fieldID)
    {
        var validator = new ForwardedValidationAttribute(ownerType, propertyName);
        var validationContext = new ValidationContext(new object()) { MemberName = propertyName };
        IList<LeveledValidationResult> results = validator.ValidateValues(text, validationContext);
        return results.Select((result, index) => new SourcePropertyValidationMessage(
            fieldID + ":" + index.ToString(CultureInfo.InvariantCulture),
            result.ErrorMessage ?? string.Empty, result.ErrorLevel switch
            {
                ValidationErrorLevel.Success => "success",
                ValidationErrorLevel.Warn => "warn",
                ValidationErrorLevel.Error => "error",
                ValidationErrorLevel.TypeError => "typeError",
                _ => throw new ArgumentOutOfRangeException(nameof(result.ErrorLevel))
            })).ToArray();
    }

    private static string[] CanonicalIDs(string[] ids, string description)
    {
        if (ids is null || ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace)
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException(description + " is empty or contains invalid identities.");
        return ids.Order(StringComparer.Ordinal).ToArray();
    }

    private static string SourcePropertyFingerprint(string? value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
