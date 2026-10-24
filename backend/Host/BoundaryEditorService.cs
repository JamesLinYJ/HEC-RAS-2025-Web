using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Geospatial.PairedData;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Observables;
using Utility.Units;
using Utility.Extensions;
using Utility;

namespace Ras.Backend;

public sealed record BoundaryPayloadData(string Id, string Scope, string Name, string FriendlyName, string EditorSchemaId, bool IsHydraulic, bool IsEnabled, string Variable, string Description, DisplayUnitData? ValueUnits = null);
public sealed record BoundaryChoice(string Id, string Title);
public sealed record NewBoundaryPayloadData(Dictionary<string, string> Fields, Dictionary<string, BoundaryChoice[]> Choices,
    Dictionary<string, string> ConstantUnits, Dictionary<string, DisplayUnitData>? ValueUnits = null);
public sealed record BoundaryLineCoordinateData(double X, double Y);
public sealed record BoundaryLineData(string Id, string Scope, string Name, BoundaryPayloadData[] Payloads, string? ActiveHydraulicPayloadId, NewBoundaryPayloadData NewPayload, BoundaryLineCoordinateData[]? Geometry = null);
public sealed record BoundaryColumn(string Id, string Title, string SourceType, bool ReadOnly, double Width);
public sealed record BoundarySeriesData(string PageId, string Scope, string SchemaId, string? TableId, ModelSample[] Samples,
    ModelRow[] Rows, BoundaryColumn[] Columns, bool CanEditX, bool XIsDate, string[] Operations, ModelRow[] Stations, string XTitle = "Time", string YTitle = "Value", DisplayUnitData? ValueUnits = null);
public sealed record BoundaryEditorData(BoundaryLineData[] Lines, Dictionary<string, BoundarySeriesData> Series, string? SelectedLineId = null, string? SelectedPayloadId = null, Dictionary<string, SpatialPointData>? Spatial = null);

// The source file has ordered collections rather than persistent GUIDs. Opaque
// editor identities therefore belong to one editor's exact source revision;
// they travel with its draft, never through a shared provider cache or names.
internal sealed record BoundaryLineIdentity(string Id, string[] Payloads);
internal sealed record BoundaryPayloadMutation(string Operation, string LineId, string PayloadId, Dictionary<string, string> Fields);
internal sealed record BoundaryEditorState(string AssetPath, string Revision, BoundaryLineIdentity[] Lines, Dictionary<string, double?[]> Stations, BoundaryPayloadMutation[]? Mutations = null, SpatialEditorState? Spatial = null);

internal sealed partial class BoundaryEditorService
{
    private readonly BoundaryCondition boundary;
    private readonly ObservableBoundaryCondition observable;
    private BoundaryEditorState state;
    private BoundaryLineIdentity[] identities;
    private string? selectedLineId, selectedPayloadId;
    private readonly Dictionary<string, Binding> bindings = [];
    private readonly Dictionary<string, PayloadContext> payloads = [];
    private sealed record Binding(Func<string> Read, Action<string>? Write, string? Alias = null);
    private sealed record PayloadContext(BoundaryConditionLine Line, PayloadBase Payload, string Scope, string Schema, string PageId);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const int MaximumDisplayedRows = 1_000_000; // Original TimeSeriesPayloadDateTimeColumn limit.

    public BoundaryEditorService(BoundaryCondition boundary, ObservableBoundaryCondition observable, string assetPath, string revision, string? sourceState)
    {
        this.boundary = boundary; this.observable = observable;
        var lines = boundary.BoundaryConditionLines.Features.ToArray();
        state = sourceState == null
            ? new(Path.GetFullPath(assetPath), revision, lines.Select(line => new BoundaryLineIdentity(Guid.NewGuid().ToString("N"), line.Payloads.Select(_ => Guid.NewGuid().ToString("N")).ToArray())).ToArray(), [])
            : JsonSerializer.Deserialize<BoundaryEditorState>(sourceState, BackendJson.Options) ?? throw new ArgumentException("Missing boundary editor identities.");
        if (state.Revision != revision || state.AssetPath != Path.GetFullPath(assetPath)) throw new SourceFileChangedException();
        string[] ids = state.Lines.SelectMany(line => line.Payloads.Prepend(line.Id)).ToArray();
        if (state.Lines.Length != lines.Length || ids.Any(id => !Guid.TryParseExact(id, "N", out _)) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length
            || state.Lines.Where((line, index) => line.Payloads.Length != lines[index].Payloads.Count).Any())
            throw new ArgumentException("The boundary editor identities do not match the source model.");
        identities = state.Lines.Select(line => line with { Payloads = line.Payloads.ToArray() }).ToArray();
        foreach (var mutation in state.Mutations ?? []) ApplyMutation(mutation);
        InitializeSpatial();
        RebuildBindings();
    }

    private void RebuildBindings()
    {
        bindings.Clear(); payloads.Clear();
        var lines = boundary.BoundaryConditionLines.Features.ToArray();
        for (int index = 0; index < lines.Length; index++)
        {
            var line = lines[index]; var identity = identities[index]; string lineScope = "Boundary/" + identity.Id;
            Bind(lineScope + "/DetailedBCLineHeaderControl/BoundaryConditionLine_Name", () => line.Name ?? "", value => line.Name = value);
            for (int payloadIndex = 0; payloadIndex < line.Payloads.Count; payloadIndex++)
            {
                var payload = line.Payloads[payloadIndex]; string id = identity.Payloads[payloadIndex], scope = lineScope + "/Payload/" + id;
                string schema = Schema(payload), pageId = scope + "/Time Series";
                payloads.Add(id, new(line, payload, scope, schema, pageId));
                if (state.Stations.TryGetValue(id, out double?[]? stations))
                {
                    if (payload is not SpatiallyVariableTimeSeriesPayload spatial || stations.Any(value => !value.HasValue || !double.IsFinite(value.Value)))
                        throw new ArgumentException("The station draft does not match its source payload.");
                    spatial.SafeSetStations(stations.Select(value => value!.Value).ToArray());
                }
                Bind(scope + "/PayloadListItem/Payload_Name", () => payload.Name ?? "", value => payload.Name = value);
                Bind(scope + "/PayloadDetails/Description", () => payload.Description ?? "", value => payload.Description = value);
                Bind(scope + "/PayloadDetails/Variable", () => payload.Variable ?? "", null);
                Bind(scope + "/PayloadListItem/Payload_IsEnabled", () => Bool(payload.IsEnabled), value => {
                    bool enabled = bool.Parse(value);
                    if (enabled && WellKnownPayloadVariables.IsHydraulicPayload(payload)) line.SetHydraulicPayload(payload);
                    else payload.IsEnabled = enabled;
                });
                if (payload is ConstantPayload constant)
                    Bind(scope + "/ConstantPayloadEditor/ConstantPayload_Value", () => Number(constant.Value), value => constant.Value = Numeric(value, missing: true));
                if (payload is ExternalPayload external)
                {
                    Bind(scope + "/ExternalPayloadEditor/ExternalPayload_ExternalPath", () => external.ExternalPath ?? "", value => external.ExternalPath = value);
                    // WellKnownExternalPathEditor resolves its connection from
                    // the exact owning payload. Expose the source connection
                    // name as a read-only sibling field; it is not the path
                    // field's editability flag.
                    Bind(scope + "/ExternalPayloadEditor/ExternalPayload_DataConnection", () => external.ExternalConnection ?? "", null);
                }
                if (Definition(payload) is RegularTimeSeriesDefinition regular)
                {
                    string prefix = scope + "/" + schema + "/";
                    var lineObserver = new ObservableBoundaryConditionLine(observable, line);
                    Bind(prefix + "BoundaryConditionLine_ActualStartTime", () => Date(lineObserver.ActualStartTime), null);
                    Bind(prefix + "BoundaryConditionLine_ActualEndTime", () => Date(lineObserver.ActualEndTime), null);
                    Bind(prefix + "TimeSeriesDefinition_UseFixedStartTime", () => Bool(regular.UseFixedStartTime), value => {
                        regular.FixedStartTime = bool.Parse(value) ? lineObserver.ActualStartTime : TimeSeriesDefinitionBase.NullTimestamp;
                    });
                    Bind(prefix + "TimeSeriesDefinition_FixedStartTime", () => Date(regular.UseFixedStartTime ? regular.FixedStartTime : boundary.StartTime), value => regular.FixedStartTime = ParseDate(value));
                    Bind(prefix + "TimeSeriesDefinition_Interval", () => Number(regular.Interval.TotalSeconds), value => {
                        double seconds = Numeric(value); if (seconds <= 0) throw new ValidationException("Interval value must be positive.");
                        regular.Interval = TimeSpan.FromSeconds(seconds);
                    });
                    Bind(prefix + "BoundaryConditionLine_BoundaryCondition_StartTime", () => Date(observable.StartTime), value => observable.StartTime = ParseDate(value), "BoundaryStartTime");
                    Bind(prefix + "BoundaryConditionLine_BoundaryCondition_EndTime", () => Date(observable.EndTime), value => observable.EndTime = ParseDate(value), "BoundaryEndTime");
                }
            }
        }
        if (state.Stations.Keys.Any(id => !payloads.ContainsKey(id))) throw new ArgumentException("A station draft refers to another payload.");
        RebuildSpatialBindings();
    }

    public Dictionary<string, string> Fields() => bindings.ToDictionary(pair => pair.Key, pair => pair.Value.Read());
    public string[] ReadOnlyFields() => bindings.Where(pair => pair.Value.Write == null).Select(pair => pair.Key).ToArray();
    public bool ContainsField(string field) => bindings.ContainsKey(field);
    public void SetField(string field, string value)
    {
        if (!bindings.TryGetValue(field, out var binding) || binding.Write == null) throw new ArgumentException("The model field is read-only: " + field);
        binding.Write(value);
    }
    public void VerifyFields(Dictionary<string, string> fields)
    {
        foreach (var pair in fields.Where(pair => bindings.ContainsKey(pair.Key)))
        {
            string actual = bindings[pair.Key].Read();
            if (pair.Key.StartsWith("Spatial/", StringComparison.Ordinal))
            {
                if (!SpatialValueEquals(pair.Key, actual, pair.Value)) throw new ValidationException("The current spatial settings do not accept " + pair.Key + ".");
                continue;
            }
            bool matches = pair.Key.EndsWith("_Name", StringComparison.Ordinal) || pair.Key.EndsWith("_ExternalPath", StringComparison.Ordinal) || pair.Key.EndsWith("/Description", StringComparison.Ordinal) ? actual == pair.Value
                : pair.Key.EndsWith("_IsEnabled", StringComparison.Ordinal) || pair.Key.EndsWith("_UseFixedStartTime", StringComparison.Ordinal) ? bool.Parse(actual) == bool.Parse(pair.Value)
                : pair.Key.EndsWith("_Value", StringComparison.Ordinal) || pair.Key.EndsWith("_Interval", StringComparison.Ordinal) ? Equal(Numeric(actual, missing: true), Numeric(pair.Value, missing: true))
                : ParseDate(actual) == ParseDate(pair.Value);
            if (!matches) throw new ValidationException("The current model settings do not accept " + pair.Key + ".");
        }
    }
    public Dictionary<string, string[]> Groups() => bindings.Where(pair => pair.Value.Alias != null).GroupBy(pair => pair.Value.Alias!)
        .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).Concat(group.Key == "BoundaryStartTime"
            ? new[] { "BoundaryConditionsSummary/BoundaryCondition_StartTime", "BoundaryConditionTimeWindow/BoundaryCondition_StartTime" }
            : new[] { "BoundaryConditionsSummary/BoundaryCondition_EndTime", "BoundaryConditionTimeWindow/BoundaryCondition_EndTime" }).ToArray());
    public string State(string? revision = null, bool persisted = false) => JsonSerializer.Serialize(state with {
        Revision = revision ?? state.Revision, Stations = persisted ? [] : state.Stations,
        Lines = persisted ? identities : state.Lines, Mutations = persisted ? [] : state.Mutations,
        Spatial = SpatialState(persisted)
    }, BackendJson.Options);

    public BoundaryEditorData Snapshot()
    {
        var lines = boundary.BoundaryConditionLines.Features.Select((line, index) => {
            var identity = identities[index];
            var payloadData = line.Payloads.Select((payload, payloadIndex) => {
                string id = identity.Payloads[payloadIndex]; var context = payloads[id];
                DisplayUnitData? valueUnits = payload is ConstantPayload constant
                    ? ConstantValueUnits(constant.Variable, observable.Project.Units, context.Scope + "/ConstantPayload_Value")
                    : null;
                return new BoundaryPayloadData(id, context.Scope, payload.Name ?? "", Friendly(payload), context.Schema,
                    WellKnownPayloadVariables.IsHydraulicPayload(payload), payload.IsEnabled, payload.Variable ?? "", payload.Description ?? "", valueUnits);
            }).ToArray();
            string? active = line.TryGetHydraulicPayload(out var selected) ? payloadData.FirstOrDefault(item => ReferenceEquals(payloads[item.Id].Payload, selected))?.Id : null;
            BoundaryLineCoordinateData[]? geometry = null;
            if (line.Polyline is { } polyline)
            {
                var coordinates = polyline.Points.Select(point => new BoundaryLineCoordinateData(point.X, point.Y)).ToArray();
                // Invalid coordinates must not break the boundary snapshot's JSON
                // response or be bridged into a synthetic segment by the map UI.
                if (coordinates.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y))) geometry = coordinates;
            }
            return new BoundaryLineData(identity.Id, "Boundary/" + identity.Id, line.Name ?? "", payloadData, active, NewPayload(line, identity.Id), geometry);
        }).ToArray();
        var series = payloads.Where(pair => Definition(pair.Value.Payload) != null || pair.Value.Payload is RatingCurvePayload)
            .ToDictionary(pair => pair.Value.PageId, pair => ReadSeries(pair.Key, pair.Value));
        foreach (var pair in SpatialSeries()) series.Add(pair.Key, pair.Value);
        return new(lines, series, selectedLineId, selectedPayloadId, SpatialSnapshot());
    }

    // Replaying source constructors/removals keeps structural edits in this
    // editor's draft. The original HDF is touched only by ModelEditorAdapter.Save.
    public void MutatePayload(string lineId, string operation, string? payloadId, Dictionary<string, string> fields)
    {
        string id = operation == "create" ? Guid.NewGuid().ToString("N") : payloadId ?? throw new ArgumentException("Missing payload identity.");
        var mutation = new BoundaryPayloadMutation(operation, lineId, id, new(fields));
        ApplyMutation(mutation);
        state = state with { Mutations = [.. state.Mutations ?? [], mutation] };
        selectedLineId = lineId;
        int index = Array.FindIndex(identities, line => line.Id == lineId);
        selectedPayloadId = operation == "create" ? id : identities[index].Payloads.FirstOrDefault();
        RebuildBindings();
    }

    private void ApplyMutation(BoundaryPayloadMutation mutation)
    {
        int index = Array.FindIndex(identities, line => line.Id == mutation.LineId);
        if (index < 0 || !Guid.TryParseExact(mutation.PayloadId, "N", out _)) throw new ArgumentException("The payload operation belongs to another boundary line.");
        var identity = identities[index]; var line = boundary.BoundaryConditionLines.Features.ElementAt(index);
        var wrapper = new ObservableBoundaryConditionLine(observable, line);
        if (mutation.Operation == "create")
        {
            if (identities.Any(item => item.Id == mutation.PayloadId || item.Payloads.Contains(mutation.PayloadId))) throw new ArgumentException("Duplicate payload identity.");
            PayloadBase payload = CreatePayload(mutation.Fields);
            wrapper.AddPayload(ObservablePayload.From(observable.Project, payload));
            identities[index] = identity with { Payloads = [.. identity.Payloads, mutation.PayloadId] };
        }
        else if (mutation.Operation == "remove")
        {
            int payloadIndex = Array.IndexOf(identity.Payloads, mutation.PayloadId);
            if (payloadIndex < 0 || mutation.Fields.Count != 0) throw new ArgumentException("The payload no longer exists.");
            wrapper.RemovePayload(wrapper.Payloads[payloadIndex]);
            identities[index] = identity with { Payloads = identity.Payloads.Where(id => id != mutation.PayloadId).ToArray() };
            state.Stations.Remove(mutation.PayloadId);
        }
        else throw new NotSupportedException("This payload operation is unavailable.");
    }

    private NewBoundaryPayloadData NewPayload(BoundaryConditionLine line, string lineID)
    {
        const string prefix = "NewPayloadPage/ViewModel_";
        string name = StringHelpers.NextUniqueName(line.Payloads, payload => payload.Name, "Hydraulic Data");
        string[] connectionNames = observable.Project.Connections.Connections.Select(connection => connection.Name).ToArray();
        bool us = observable.Project.Units == UnitSystem.USCustomary;
        return new(new() { [prefix + "PayloadName"] = name, [prefix + "PayloadVariable"] = "Hydraulic", [prefix + "HydraulicType"] = "Flow",
            [prefix + "HydraulicDataType"] = "Tabular", [prefix + "TimeSeriesType"] = "Regular", [prefix + "HydraulicConstantValue"] = "0",
            [prefix + "SelectedExternalConnection"] = "", [prefix + "Path"] = "" },
            new() { [prefix + "PayloadVariable"] = [new("Hydraulic", "Hydraulic")],
                [prefix + "HydraulicType"] = WellKnownPayloadVariables.HydraulicPayloadVariables.Select(value => new BoundaryChoice(value, value)).ToArray(),
                [prefix + "HydraulicDataType"] = [new("Constant", "Constant"), new("Tabular", "Tabular"), new("SpatiallyVariableTabular", "Tabular (Spatially Variable)"), new("Remote", "External Connection")],
                [prefix + "TimeSeriesType"] = [new("Regular", "Regular"), new("Irregular", "Irregular")],
                [prefix + "SelectedExternalConnection"] = connectionNames.Select(value => new BoundaryChoice(value, value)).ToArray() },
            new() { ["Flow"] = us ? "ft³/s" : "m³/s", ["Stage"] = us ? "ft" : "m" },
            new Dictionary<string, DisplayUnitData>(StringComparer.Ordinal) {
                ["Flow"] = DisplayUnitData.From(us ? Flow.CubicFeetPerSecond : Flow.CubicMetersPerSecond,
                    "Boundary/" + lineID + "/NewPayload/Flow"),
                ["Stage"] = DisplayUnitData.From(us ? Length.Feet : Length.Meters,
                    "Boundary/" + lineID + "/NewPayload/Stage")
            });
    }

    private static DisplayUnitData? ConstantValueUnits(string? variable, UnitSystem unitSystem, string selectionID)
    {
        if (variable?.RoughlyEquals("Stage") == true)
            return DisplayUnitData.From(unitSystem == UnitSystem.USCustomary ? Length.Feet : Length.Meters, selectionID);
        if (variable?.RoughlyEquals("Flow") == true)
            return DisplayUnitData.From(unitSystem == UnitSystem.USCustomary ? Flow.CubicFeetPerSecond : Flow.CubicMetersPerSecond, selectionID);
        return null;
    }

    private PayloadBase CreatePayload(Dictionary<string, string> fields)
    {
        const string prefix = "NewPayloadPage/ViewModel_";
        string Read(string key) => fields.TryGetValue(prefix + key, out string? value) ? value : throw new ArgumentException("Missing payload field: " + key);
        string[] known = ["PayloadName", "PayloadVariable", "HydraulicType", "HydraulicDataType", "TimeSeriesType", "HydraulicConstantValue", "SelectedExternalConnection", "Path"];
        if (fields.Keys.Any(key => !known.Any(name => key == prefix + name))) throw new ArgumentException("Unknown payload creation field.");
        if (Read("PayloadVariable") != "Hydraulic") throw new NotSupportedException("The original source does not implement this payload variable.");
        string variable = Read("HydraulicType"), type = Read("HydraulicDataType"), name = Read("PayloadName");
        if (!WellKnownPayloadVariables.HydraulicPayloadVariables.Contains(variable)) throw new ArgumentException("Unknown hydraulic variable.");
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationException("Enter a payload name.");
        if (variable == "Normal Depth" && type != "Constant" || variable == "StageVelocityVelocity" && type != "SpatiallyVariableTabular") throw new ValidationException("The hydraulic variable does not support this data type.");
        TimeSeriesDefinitionBase Definition() => Read("TimeSeriesType") switch { "Regular" => new RegularTimeSeriesDefinition(TimeSpan.FromHours(1)), "Irregular" => new IrregularAbsoluteTimeSeriesDefinition([]), _ => throw new ArgumentException("Unknown time series type.") };
        PayloadBase payload = type switch {
            "Constant" => new ConstantPayload(variable, Numeric(Read("HydraulicConstantValue"))),
            "Tabular" => new TimeSeriesPayload(variable, Definition(), Memory<double>.Empty),
            "SpatiallyVariableTabular" => new SpatiallyVariableTimeSeriesPayload(variable, Definition(), Memory<double>.Empty, []),
            "Remote" => External(variable, Read("SelectedExternalConnection"), Read("Path")),
            _ => throw new NotSupportedException("The original source does not implement this payload data type.")
        };
        payload.Name = name; return payload;
    }
    private ExternalPayload External(string variable, string connection, string path)
    {
        if (!observable.Project.Connections.Connections.Any(item => item.Name == connection)) throw new ValidationException("Pick (or add) an external connection before creating this payload.");
        return new(variable, connection, path);
    }

    public void ApplyData(Dictionary<string, ModelRow[]>? rows, Dictionary<string, ModelSample[]>? samples)
    {
        var allowedRows = new HashSet<string>(StringComparer.Ordinal); var allowedPages = new HashSet<string>(StringComparer.Ordinal);
        ApplySpatialData(rows, samples, allowedRows, allowedPages);
        foreach (var pair in payloads)
        {
            var context = pair.Value; var definition = Definition(context.Payload); if (definition == null && context.Payload is not RatingCurvePayload) continue;
            allowedPages.Add(context.PageId);
            if (context.Payload is SpatiallyVariableTimeSeriesPayload spatial)
            {
                string table = context.PageId + "/Values", stationsTable = context.PageId + "/Stations";
                allowedRows.Add(table); allowedRows.Add(stationsTable);
                if (rows?.TryGetValue(stationsTable, out var stationRows) == true) ApplyStations(spatial, stationRows);
                if (rows?.TryGetValue(table, out var dataRows) == true) ApplySpatialRows(spatial, dataRows);
            }
            // Spatial plots are a projection of the editable station table.
            // An older plot must not overwrite a newly edited table cell.
            if (context.Payload is TimeSeriesPayload && samples?.TryGetValue(context.PageId, out var values) == true) ApplySamples(pair.Key, context, values);
            if (context.Payload is RatingCurvePayload curve && samples?.TryGetValue(context.PageId, out var points) == true)
            {
                ValidateCompleteSamples(pair.Key, points);
                curve.InputValues = points.Select(point => point.X).ToArray();
                curve.OutputValues = points.Select(point => point.Y ?? double.NaN).ToArray();
            }
        }
        if (rows != null && rows.Any(pair => pair.Value.Length > 0 && !allowedRows.Contains(pair.Key))) throw new ArgumentException("The table does not belong to this boundary editor.");
        if (samples != null && samples.Keys.Any(key => !allowedPages.Contains(key))) throw new ArgumentException("The series does not belong to this boundary editor.");
    }

    public BoundarySeriesData Transform(string pageId, string operation)
    {
        if (TryTransformSpatialSeries(pageId, operation, out var spatialResult)) return spatialResult!;
        var pair = payloads.SingleOrDefault(pair => pair.Value.PageId == pageId);
        if (pair.Value == null) throw new ArgumentException("The selected source time series no longer exists.");
        if (operation is "appendRow" or "removeLastRow") ResizeSeries(pair.Value.Payload, operation == "appendRow");
        else if (pair.Value.Payload is RatingCurvePayload curve)
        {
            if (operation == "clear") { var values = new double[curve.Count]; Array.Fill(values, double.NaN); curve.OutputValues = values; }
            else if (operation != "refresh") throw new NotSupportedException("This curve operation is unavailable.");
        }
        else if (pair.Value.Payload is TimeSeriesPayload scalar)
        {
            var wrapper = new ObservableTimeSeriesPayload(scalar);
            switch (operation) { case "clear": wrapper.Clear(); break; case "interpolate": wrapper.InterpolateData(); break; case "refresh": break; default: throw new NotSupportedException("This series operation is unavailable."); }
        }
        else if (pair.Value.Payload is SpatiallyVariableTimeSeriesPayload spatial)
        {
            var wrapper = new ObservableSpatiallyVariableTimeSeriesPayload(spatial);
            switch (operation) { case "clear": wrapper.Clear(); break; case "interpolate": wrapper.InterpolateData(); break; case "addStation": wrapper.AddNewStation(); break; case "refresh": break; default: throw new NotSupportedException("This series operation is unavailable."); }
            state.Stations[pair.Key] = spatial.Stations.ToArray().Select(value => (double?)value).ToArray();
        }
        else throw new NotSupportedException("This payload does not contain an editable time series.");
        return ReadSeries(pair.Key, pair.Value);
    }

    private void ResizeSeries(PayloadBase payload, bool append)
    {
        int count = payload is RatingCurvePayload rating ? rating.Count : ValueCount(payload);
        int next = append ? count + 1 : count - 1;
        if (next < 0 || next > MaximumDisplayedRows) throw new ArgumentException("The source row count is outside the editor limit.");
        if (payload is RatingCurvePayload curve)
        {
            var x = curve.InputValues.ToArray(); var y = curve.OutputValues.ToArray();
            Array.Resize(ref x, next); Array.Resize(ref y, next);
            if (append) { x[count] = count == 0 ? 0 : x[count - 1] + 1; y[count] = double.NaN; }
            curve.InputValues = x; curve.OutputValues = y;
            return;
        }
        var definition = Definition(payload) ?? throw new NotSupportedException("This payload has no editable rows.");
        if (definition is RegularTimeSeriesDefinition) throw new NotSupportedException("Regular series rows are defined by their time window.");
        DateTime time = !append || count == 0 ? boundary.StartTime : definition.TimeFor(boundary.StartTime, count - 1).AddHours(1);
        ResizeTime(definition, next);
        if (append) SetTime(definition, count, Epoch(time));
        if (payload is TimeSeriesPayload scalar)
        {
            var values = scalar.Values.ToArray(); Array.Resize(ref values, next); if (append) values[count] = double.NaN;
            scalar.Values = values;
        }
        else if (payload is SpatiallyVariableTimeSeriesPayload spatial)
        {
            if (append) { var values = new double[spatial.StationCount]; Array.Fill(values, double.NaN); spatial.Values.Add(values); }
            else spatial.Values.RemoveAt(count - 1);
        }
    }

    private void ResizeTime(TimeSeriesDefinitionBase definition, int count)
    {
        if (count > MaximumDisplayedRows) throw new ArgumentException("The source row count exceeds the editor limit.");
        if (definition is IrregularAbsoluteTimeSeriesDefinition absolute)
        {
            if (absolute.Count == count) return;
            var times = absolute.Times.ToArray(); int previous = times.Length; Array.Resize(ref times, count);
            for (int index = previous; index < count; index++) times[index] = boundary.StartTime;
            absolute.Times = times;
        }
        else if (definition is IrregularRelativeTimeSeriesDefinition relative)
        {
            if (relative.Count == count) return;
            var times = relative.Times.ToArray(); Array.Resize(ref times, count); relative.Times = times;
        }
    }
    private static void ValidateCompleteSamples(string id, ModelSample[] samples)
    {
        if (samples.Length > MaximumDisplayedRows || !samples.Select(sample => sample.Id).SequenceEqual(Enumerable.Range(0, samples.Length).Select(index => index.ToString(Invariant)))
            || samples.Any(sample => sample.DatasetId != id || !double.IsFinite(sample.X) || sample.Y.HasValue && !double.IsFinite(sample.Y.Value)))
            throw new ArgumentException("The editable series must contain all ordered finite source rows and nullable values.");
    }

    private BoundarySeriesData ReadSeries(string id, PayloadContext context)
    {
        if (context.Payload is RatingCurvePayload curve)
        {
            if (curve.InputValues.Length != curve.OutputValues.Length) throw new InvalidDataException("Rating curve inputs and outputs have different lengths.");
            return new(context.PageId, context.Scope, context.Schema, null,
                Enumerable.Range(0, curve.Count).Select(index => new ModelSample(index.ToString(Invariant), curve.InputValues.Span[index], Nullable(curve.OutputValues.Span[index]), curve.Name ?? curve.Variable, id)).ToArray(),
                [], [], true, false, ["appendRow", "removeLastRow", "clear", "refresh"], [], curve.IndependentVariable, curve.Variable);
        }
        var definition = Definition(context.Payload)!; int count = ValueCount(context.Payload);
        int displayed = count;
        if (definition is RegularTimeSeriesDefinition regular)
        {
            if (regular.Interval <= TimeSpan.Zero) throw new ValidationException("Interval value must be positive.");
            double requested = (boundary.EndTime - boundary.StartTime) / regular.Interval + 1;
            displayed = Math.Max(count, (int)Math.Clamp(requested, 0, MaximumDisplayedRows));
        }
        if (count > MaximumDisplayedRows) throw new NotSupportedException("The source series exceeds the original editor's row limit.");
        var samples = new List<ModelSample>(); var rows = new List<ModelRow>(); var columns = new List<BoundaryColumn>(); var stationRows = new List<ModelRow>();
        bool spatial = context.Payload is SpatiallyVariableTimeSeriesPayload;
        if (spatial)
        {
            var variable = (SpatiallyVariableTimeSeriesPayload)context.Payload;
            columns.Add(new("Time", "Time", "DateTime", definition is RegularTimeSeriesDefinition, 180));
            for (int station = 0; station < variable.StationCount; station++)
            {
                columns.Add(new("station/" + station, "Station " + station, "double", false, 100));
                stationRows.Add(new("station/" + station, new() { ["Station"] = Number(variable.Stations.Span[station]) }));
            }
            for (int index = 0; index < displayed; index++)
            {
                DateTime time = definition.TimeFor(boundary.StartTime, index); var values = new Dictionary<string, string> { ["Time"] = Date(time) };
                for (int station = 0; station < variable.StationCount; station++)
                {
                    double value = index < variable.Values.Count && station < variable.Values[index].Length ? variable.Values[index].Span[station] : double.NaN;
                    values["station/" + station] = Number(value);
                    if (index < count) samples.Add(new(index.ToString(Invariant), Epoch(time), Nullable(value), "Station " + station, id + "/station/" + station));
                }
                rows.Add(new(index.ToString(Invariant), values));
            }
        }
        else
        {
            var scalar = (TimeSeriesPayload)context.Payload;
            for (int index = 0; index < displayed; index++)
            {
                double value = index < scalar.Count ? scalar.Values.Span[index] : double.NaN;
                samples.Add(new(index.ToString(Invariant), Epoch(definition.TimeFor(boundary.StartTime, index)), Nullable(value), scalar.Name ?? scalar.Variable, id));
            }
        }
        string[] operations = spatial ? ["clear", "interpolate", "addStation", "refresh"] : ["clear", "interpolate", "refresh"];
        if (definition is not RegularTimeSeriesDefinition) operations = [.. operations, "appendRow", "removeLastRow"];
        return new(context.PageId, context.Scope, context.Schema, spatial ? context.PageId + "/Values" : null,
            samples.ToArray(), rows.ToArray(), columns.ToArray(), definition is not RegularTimeSeriesDefinition, true, operations, stationRows.ToArray());
    }

    private void ApplySamples(string payloadId, PayloadContext context, ModelSample[] samples)
    {
        if (samples.Select(sample => sample.Id + "\0" + sample.DatasetId).Distinct(StringComparer.Ordinal).Count() != samples.Length)
            throw new ArgumentException("Series sample identities must be unique.");
        var definition = Definition(context.Payload)!;
        if (definition is not RegularTimeSeriesDefinition && context.Payload is TimeSeriesPayload irregular)
        {
            ValidateCompleteSamples(payloadId, samples);
            ResizeTime(definition, samples.Length);
            irregular.Values = samples.Select(sample => sample.Y ?? double.NaN).ToArray();
        }
        foreach (var sample in samples)
        {
            int index = Index(sample.Id); if (!double.IsFinite(sample.X) || sample.Y.HasValue && !double.IsFinite(sample.Y.Value)) throw new ArgumentException("Series values must be finite or missing.");
            if (context.Payload is TimeSeriesPayload scalar)
            {
                if (sample.DatasetId != payloadId) throw new ArgumentException("The sample belongs to another dataset.");
                if (definition is not RegularTimeSeriesDefinition && index >= scalar.Count) throw new ArgumentException("The irregular series row no longer exists.");
                double desired = sample.Y ?? double.NaN, current = index < scalar.Count ? scalar.Values.Span[index] : double.NaN;
                if (!Equal(current, desired))
                {
                    var wrapper = new ObservableTimeSeriesPayload(scalar); wrapper.Values[index] = desired;
                }
            }
            else if (context.Payload is SpatiallyVariableTimeSeriesPayload spatial)
            {
                string prefix = payloadId + "/station/";
                if (sample.DatasetId == null || !sample.DatasetId.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("The sample belongs to another dataset.");
                int station = Index(sample.DatasetId[prefix.Length..]); if (station >= spatial.StationCount) throw new ArgumentException("The source station no longer exists.");
                SetSpatialValue(spatial, index, station, sample.Y ?? double.NaN);
            }
            SetTime(definition, index, sample.X);
        }
    }
    private static void ApplyStations(SpatiallyVariableTimeSeriesPayload payload, ModelRow[] rows)
    {
        if (rows.Length != payload.StationCount || rows.Select(row => row.Id).Distinct().Count() != rows.Length) throw new ArgumentException("Station rows do not match the source payload.");
        foreach (var row in rows)
        {
            if (!row.Id.StartsWith("station/", StringComparison.Ordinal)) throw new ArgumentException("Invalid source station identity.");
            int index = Index(row.Id[8..]); if (index >= payload.StationCount || row.Values.Count != 1 || !row.Values.TryGetValue("Station", out string? value)) throw new ArgumentException("Invalid source station values.");
            if (value != Number(payload.Stations.Span[index])) payload.Stations.Span[index] = Numeric(value);
        }
    }
    private void ApplySpatialRows(SpatiallyVariableTimeSeriesPayload payload, ModelRow[] rows)
    {
        if (rows.Select(row => row.Id).Distinct().Count() != rows.Length) throw new ArgumentException("Series row identities must be unique.");
        if (payload.TimeSeriesDefinition is not RegularTimeSeriesDefinition)
        {
            if (!rows.Select(row => row.Id).SequenceEqual(Enumerable.Range(0, rows.Length).Select(index => index.ToString(Invariant))) || rows.Any(row => !row.Values.ContainsKey("Time")))
                throw new ArgumentException("Irregular series must contain all ordered source rows.");
            ResizeTime(payload.TimeSeriesDefinition, rows.Length);
            while (payload.Values.Count > rows.Length) payload.Values.RemoveAt(payload.Values.Count - 1);
            while (payload.Values.Count < rows.Length) { var values = new double[payload.StationCount]; Array.Fill(values, double.NaN); payload.Values.Add(values); }
        }
        foreach (var row in rows)
        {
            int index = Index(row.Id);
            foreach (var value in row.Values)
            {
                if (value.Key == "Time") {
                    if (payload.TimeSeriesDefinition is not RegularTimeSeriesDefinition && value.Value != Date(payload.TimeSeriesDefinition.TimeFor(boundary.StartTime, index)))
                        SetTime(payload.TimeSeriesDefinition, index, Epoch(ParseDate(value.Value)));
                    continue;
                }
                if (!value.Key.StartsWith("station/", StringComparison.Ordinal)) throw new ArgumentException("Unknown source station column.");
                int station = Index(value.Key[8..]); if (station >= payload.StationCount) throw new ArgumentException("The source station no longer exists.");
                SetSpatialValue(payload, index, station, Numeric(value.Value, missing: true));
            }
        }
    }
    private static void SetSpatialValue(SpatiallyVariableTimeSeriesPayload payload, int index, int station, double value)
    {
        double current = index < payload.Values.Count && station < payload.Values[index].Length ? payload.Values[index].Span[station] : double.NaN;
        if (Equal(current, value)) return;
        if (payload.TimeSeriesDefinition is not RegularTimeSeriesDefinition && index >= payload.Values.Count) throw new ArgumentException("The irregular series row no longer exists.");
        while (payload.Values.Count <= index) { var row = new double[payload.StationCount]; Array.Fill(row, double.NaN); payload.Values.Add(row); }
        payload.Values[index].Span[station] = value;
    }
    private void SetTime(TimeSeriesDefinitionBase definition, int index, double epoch)
    {
        if (definition is RegularTimeSeriesDefinition) return;
        DateTime current = definition.TimeFor(boundary.StartTime, index); if (Epoch(current) == epoch) return;
        DateTime desired = new DateTimeOffset(DateTime.UnixEpoch).AddSeconds(epoch).LocalDateTime;
        if (definition is IrregularAbsoluteTimeSeriesDefinition absolute) absolute.Times.Span[index] = desired;
        else if (definition is IrregularRelativeTimeSeriesDefinition relative) relative.Times.Span[index] = desired - boundary.StartTime;
    }
    private void Bind(string key, Func<string> read, Action<string>? write, string? alias = null) => bindings.Add(key, new(read, write, alias));
    private static int Index(string value) => int.TryParse(value, NumberStyles.None, Invariant, out int index) && index >= 0 && index < MaximumDisplayedRows ? index : throw new ArgumentException("Invalid source row identity.");
    private static string Schema(PayloadBase payload) => payload switch {
        ConstantPayload => "ConstantPayloadEditor", ExternalPayload => "ExternalPayloadEditor",
        RatingCurvePayload => "RatingCurvePayloadEditor",
        TimeSeriesPayload series => series.TimeSeriesDefinition is RegularTimeSeriesDefinition ? "RegularTimeSeriesPayloadEditor" : "IrregularTimeSeriesPayloadEditor",
        SpatiallyVariableTimeSeriesPayload => "RegularSpatiallyVariableTimeSeriesPayloadEditor", _ => ""
    };
    private string Friendly(PayloadBase payload) => ObservablePayload.From(observable.Project, payload).FriendlyName;
    private static TimeSeriesDefinitionBase? Definition(PayloadBase payload) => payload switch { TimeSeriesPayload series => series.TimeSeriesDefinition, SpatiallyVariableTimeSeriesPayload spatial => spatial.TimeSeriesDefinition, _ => null };
    private static int ValueCount(PayloadBase payload) => payload switch { TimeSeriesPayload series => series.Count, SpatiallyVariableTimeSeriesPayload spatial => spatial.Values.Count, _ => 0 };
    private static string Date(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Local).ToString("yyyy-MM-dd'T'HH:mm:sszzz", Invariant);
    private static DateTime ParseDate(string value) => DateTimeOffset.Parse(value, Invariant).LocalDateTime;
    private static double Epoch(DateTime value) => (new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local)).ToUniversalTime() - DateTimeOffset.UnixEpoch).TotalSeconds;
    private static string Bool(bool value) => value ? "true" : "false";
    private static string Number(double value) => double.IsNaN(value) ? "" : value.ToString("R", Invariant);
    private static double? Nullable(double value) => double.IsNaN(value) ? null : double.IsFinite(value) ? value : throw new ArgumentException("Series values must be finite or missing.");
    private static bool Equal(double left, double right) => left == right || double.IsNaN(left) && double.IsNaN(right);
    private static double Numeric(string value, bool missing = false)
    {
        if (missing && string.IsNullOrWhiteSpace(value)) return double.NaN;
        double number = double.Parse(value, NumberStyles.Float, Invariant);
        return double.IsFinite(number) ? number : throw new ArgumentException("Numeric values must be finite.");
    }
}
