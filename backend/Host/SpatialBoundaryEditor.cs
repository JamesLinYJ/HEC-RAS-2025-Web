using System.ComponentModel.DataAnnotations;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Meteorology;
using Utility;
using Utility.Units;

namespace Ras.Backend;

public sealed record SpatialStationData(string Id, string Scope, string Name, double X, double Y);
public sealed record SpatialGaugeData(string Id, string Scope, string? StationId, string? PageId);
public sealed record SpatialPointData(SpatialStationData[] Stations, SpatialGaugeData[] Inputs, string Unit, bool IsVector, DisplayUnitData ValueUnits, DisplayUnitData HeightUnits, DisplayUnitData IntervalUnits, double DefaultHeight);
internal sealed record SpatialMutation(string LayerId, string Operation, string Id, string? ItemId);
internal sealed record SpatialSeriesShape(bool Regular, int PrimaryCount, int SecondaryCount, int DisplayedRows, DateTime Start, double IntervalSeconds);
internal sealed record SpatialEditorState(string[] Stations, Dictionary<string, string[]> Gauges, SpatialMutation[] Mutations, Dictionary<string, SpatialSeriesShape>? Shapes = null);

internal sealed partial class BoundaryEditorService
{
    private string[] spatialStationIds = [];
    private Dictionary<string, string[]> spatialGaugeIds = [];
    private readonly Dictionary<string, BoundarySeriesData> spatialInputBaseline = [];
    private Dictionary<string, SpatialBoundaryConditionBase> SpatialLayers() => new() {
        ["precipitation"] = boundary.Precipitation, ["evapotranspiration"] = boundary.Evapotranspiration,
        ["wind"] = boundary.Wind, ["airPressure"] = boundary.AirPressure, ["airDensity"] = boundary.AirDensity
    };
    private static string StationScope(string id) => "Spatial/Station/" + id;
    private static string GaugeScope(string layer, string id) => "Spatial/" + layer + "/Gauge/" + id;
    private void InitializeSpatial()
    {
        var layers = SpatialLayers();
        state = state with { Spatial = state.Spatial ?? new(boundary.MetStations.Select(_ => Guid.NewGuid().ToString("N")).ToArray(),
            layers.ToDictionary(pair => pair.Key, pair => pair.Value.Gauges.Select(_ => Guid.NewGuid().ToString("N")).ToArray()), []) };
        var initial = state.Spatial!;
        var ids = initial.Stations.Concat(initial.Gauges.Values.SelectMany(value => value)).ToArray();
        if (initial.Stations.Length != boundary.MetStations.Count || !initial.Gauges.Keys.Order().SequenceEqual(layers.Keys.Order())
            || layers.Any(pair => initial.Gauges[pair.Key].Length != pair.Value.Gauges.Count)
            || ids.Any(id => !Guid.TryParseExact(id, "N", out _)) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Spatial identities do not match the source model.");
        spatialStationIds = initial.Stations.ToArray();
        spatialGaugeIds = initial.Gauges.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        foreach (var mutation in initial.Mutations) ApplySpatialMutation(mutation);
        var knownPages = Gauges().Select(item => GaugeScope(item.Layer, item.Id) + "/Time Series").ToHashSet();
        if (initial.Shapes?.Any(pair => !knownPages.Contains(pair.Key) || pair.Value.PrimaryCount < 0 || pair.Value.SecondaryCount < 0
            || pair.Value.DisplayedRows < Math.Max(pair.Value.PrimaryCount, pair.Value.SecondaryCount) || pair.Value.DisplayedRows > MaximumDisplayedRows
            || pair.Value.Regular && (!double.IsFinite(pair.Value.IntervalSeconds) || pair.Value.IntervalSeconds <= 0)) == true)
            throw new ArgumentException("Invalid spatial series draft shape.");
        foreach (var pair in SpatialSeries()) spatialInputBaseline.Add(pair.Key, pair.Value);
    }
    private SpatialEditorState SpatialState(bool persisted)
    {
        var shapes = Gauges().Where(item => TransportGaugeTable(item.Gauge)).ToDictionary(item => GaugeScope(item.Layer, item.Id) + "/Time Series", item =>
            new SpatialSeriesShape(item.Gauge.TableTimeSeries.TimeSeriesType == TimeSeriesType.Regular, item.Gauge.Table?.Length ?? 0, item.Gauge.Table2?.Length ?? 0,
                GaugeDisplayedCount(item.Gauge), GaugeStart(item.Gauge.TableTimeSeries), item.Gauge.TableTimeSeries.RTSTimeSpan.TotalSeconds));
        return persisted ? new(spatialStationIds, spatialGaugeIds, [], shapes) : state.Spatial! with { Shapes = shapes };
    }

    public void MutateSpatial(string layerId, string operation, string? itemId)
    {
        if (operation == "refresh") { if (!SpatialLayers().ContainsKey(layerId)) throw new ArgumentException("Unknown spatial layer."); return; }
        var mutation = new SpatialMutation(layerId, operation, Guid.NewGuid().ToString("N"), itemId);
        ApplySpatialMutation(mutation);
        state = state with { Spatial = state.Spatial! with { Mutations = [.. state.Spatial.Mutations, mutation] } };
        RebuildBindings();
    }
    private void ApplySpatialMutation(SpatialMutation mutation)
    {
        if (!SpatialLayers().TryGetValue(mutation.LayerId, out var layer) || !Guid.TryParseExact(mutation.Id, "N", out _)
            || spatialStationIds.Contains(mutation.Id) || spatialGaugeIds.Values.Any(ids => ids.Contains(mutation.Id)))
            throw new ArgumentException("Invalid spatial operation identity.");
        int stationIndex = Array.IndexOf(spatialStationIds, mutation.ItemId);
        switch (mutation.Operation)
        {
            case "addStation":
                string name = StringHelpers.NextUniqueName(boundary.MetStations.Stations, station => station.Name, "Station");
                boundary.MetStations.Add(new MeteorologicStation(name, new Point(0, 0)));
                spatialStationIds = [.. spatialStationIds, mutation.Id];
                break;
            case "removeStation":
                if (stationIndex < 0) throw new ArgumentException("The source station no longer exists.");
                boundary.MetStations.RemoveAt(stationIndex);
                spatialStationIds = spatialStationIds.Where(id => id != mutation.ItemId).ToArray();
                break;
            case "addGauge":
                if (stationIndex < 0) throw new ValidationException("Select a station before adding an input.");
                layer.Gauges.Add(new MetGaugedData(boundary.MetStations[stationIndex]));
                spatialGaugeIds[mutation.LayerId] = [.. spatialGaugeIds[mutation.LayerId], mutation.Id];
                break;
            case "removeGauge":
                int index = Array.IndexOf(spatialGaugeIds[mutation.LayerId], mutation.ItemId);
                if (index < 0) throw new ArgumentException("The source input no longer exists.");
                layer.Gauges.RemoveAt(index);
                spatialGaugeIds[mutation.LayerId] = spatialGaugeIds[mutation.LayerId].Where(id => id != mutation.ItemId).ToArray();
                break;
            default: throw new NotSupportedException("This spatial operation is unavailable.");
        }
    }

    private void RebuildSpatialBindings()
    {
        for (int index = 0; index < spatialStationIds.Length; index++)
        {
            var station = boundary.MetStations[index]; string scope = StationScope(spatialStationIds[index]);
            Bind(scope + "/Name", () => station.Name ?? "", value => {
                if (string.IsNullOrWhiteSpace(value) || boundary.MetStations.Any(other => !ReferenceEquals(station, other) && other.Name == value))
                    throw new ValidationException("Station names must be nonempty and unique.");
                station.Name = value;
            });
            Bind(scope + "/X", () => Number(station.Location.X), value => station.Location = new Point(Numeric(value), station.Location.Y));
            Bind(scope + "/Y", () => Number(station.Location.Y), value => station.Location = new Point(station.Location.X, Numeric(value)));
            Bind(scope + "/ElevationOffGround", () => float.IsNaN(station.ElevationOffGround) ? "" : station.ElevationOffGround.ToString("R", Invariant), value => {
                double elevation = Numeric(value, missing: true);
                if (!double.IsNaN(elevation) && (elevation < 0 || elevation > float.MaxValue)) throw new ValidationException("Station height must be nonnegative or empty.");
                station.ElevationOffGround = (float)elevation;
            });
        }
        foreach (var pair in SpatialLayers()) for (int index = 0; index < pair.Value.Gauges.Count; index++)
        {
            var gauge = pair.Value.Gauges[index]; string scope = GaugeScope(pair.Key, spatialGaugeIds[pair.Key][index]);
            Bind(scope + "/Station", () => StationId(gauge) ?? "", value => {
                int target = Array.IndexOf(spatialStationIds, value);
                if (value != "" && target < 0) throw new ArgumentException("The source station no longer exists.");
                gauge.Station = target < 0 ? null! : boundary.MetStations[target]; gauge.StationName = gauge.Station?.Name!;
            });
            Bind(scope + "/IsEnabled", () => Bool(gauge.IsEnabled), value => gauge.IsEnabled = bool.Parse(value));
            Bind(scope + "/DataLocation", () => gauge.DataLocation.ToString(), value => gauge.DataLocation = Choice<DataLocation>(value));
            Bind(scope + "/LocalDataType", () => gauge.LocalDataType.ToString(), value => {
                var mode = Choice<LocalDataType>(value);
                if (mode == LocalDataType.Functional && gauge.LocalDataType != mode) throw new NotSupportedException("The source does not support function input creation.");
                gauge.LocalDataType = mode; gauge.Table ??= []; gauge.Table2 ??= [];
                if (gauge.TableTimeSeries.RTSTimeSpan <= TimeSpan.Zero) gauge.TableTimeSeries.RTSTimeSpan = TimeSpan.FromHours(1);
            });
            Bind(scope + "/Constant", () => Number(gauge.Constant), value => gauge.Constant = Numeric(value, missing: true));
            Bind(scope + "/Constant2", () => Number(gauge.Constant2), value => gauge.Constant2 = Numeric(value, missing: true));
            Bind(scope + "/ConnectionName", () => gauge.ConnectionName ?? "", value => {
                if (value != "" && !observable.Project.Connections.Connections.Any(connection => connection.Name == value)) throw new ValidationException("The selected external connection no longer exists.");
                gauge.ConnectionName = value;
            });
            Bind(scope + "/ConnectionPath", () => gauge.ConnectionPath ?? "", value => gauge.ConnectionPath = value);
            var time = gauge.TableTimeSeries;
            Bind(scope + "/TimeSeriesType", () => time.TimeSeriesType.ToString(), value => {
                var type = Choice<TimeSeriesType>(value); if (time.TimeSeriesType == type) return;
                var dates = GaugeTimes(gauge, GaugeCount(gauge));
                time.TimeSeriesType = type;
                SetGaugeTimes(time, dates);
            });
            Bind(scope + "/ExpressedTimeType", () => time.ExpressedTimeType.ToString(), value => {
                var type = Choice<ExpressedTimeType>(value); if (time.ExpressedTimeType == type) return;
                var dates = GaugeTimes(gauge, GaugeCount(gauge)); time.ExpressedTimeType = type; SetGaugeTimes(time, dates);
            });
            Bind(scope + "/Interval", () => Number(time.RTSTimeSpan.TotalSeconds), value => {
                double seconds = Numeric(value); if (seconds <= 0 || seconds != Math.Truncate(seconds * 1000) / 1000) throw new ValidationException("Interval must be positive whole milliseconds.");
                time.RTSTimeSpan = TimeSpan.FromSeconds(seconds);
            });
            Bind(scope + "/UseFixedStartTime", () => Bool(time.FixedStartTime != TimeSeriesDefinition.NullTimestamp), value => {
                time.UseFixedStartTime = bool.Parse(value); time.FixedStartTime = time.UseFixedStartTime ? GaugeStart(time) : TimeSeriesDefinition.NullTimestamp;
            });
            Bind(scope + "/FixedStartTime", () => SpatialDate(GaugeStart(time)), value => time.FixedStartTime = ParseDate(value));
        }
    }
    private static T Choice<T>(string value) where T : struct, Enum => Enum.TryParse<T>(value, out var result) && Enum.IsDefined(result) ? result : throw new ValidationException("Invalid source option: " + value);
    private string? StationId(MetGaugedData gauge) { int index = gauge.Station == null ? -1 : boundary.MetStations.IndexOf(gauge.Station); return index < 0 ? null : spatialStationIds[index]; }
    private bool SpatialValueEquals(string field, string actual, string desired)
    {
        string property = field[(field.LastIndexOf('/') + 1)..];
        if (property is "X" or "Y" or "Constant" or "Constant2" or "Interval") return Equal(Numeric(actual, true), Numeric(desired, true));
        if (property == "ElevationOffGround") return Equal((float)Numeric(actual, true), (float)Numeric(desired, true));
        if (property is "IsEnabled" or "UseFixedStartTime") return bool.Parse(actual) == bool.Parse(desired);
        if (property == "FixedStartTime") return ParseDate(actual) == ParseDate(desired);
        return actual == desired;
    }
    private Dictionary<string, SpatialPointData> SpatialSnapshot() => SpatialLayers().ToDictionary(pair => pair.Key, pair => new SpatialPointData(
        spatialStationIds.Select((id, index) => new SpatialStationData(id, StationScope(id), boundary.MetStations[index].Name ?? "", boundary.MetStations[index].Location.X, boundary.MetStations[index].Location.Y)).ToArray(),
        spatialGaugeIds[pair.Key].Select((id, index) => new SpatialGaugeData(id, GaugeScope(pair.Key, id), StationId(pair.Value.Gauges[index]),
            IsGaugeTable(pair.Value.Gauges[index]) ? GaugeScope(pair.Key, id) + "/Time Series" : null)).ToArray(),
        pair.Key switch { "precipitation" => boundary.Project.Units == UnitSystem.USCustomary ? "in/hr" : "mm/hr", "evapotranspiration" => "mm/hr", "wind" => "m/s", "airPressure" => "mbar", _ => "kg/m³" }, pair.Key == "wind",
        SpatialValueUnits(pair.Key), DisplayUnitData.From(boundary.Project.Units == UnitSystem.USCustomary ? Length.Feet : Length.Meters, "stationHeight"),
        DisplayUnitData.From(Time.Seconds, "stationInterval"), boundary.Project.Units == UnitSystem.USCustomary ? MeteorologicStation.DefaultElevationUSC : MeteorologicStation.DefaultElevationSI));

    private DisplayUnitData SpatialValueUnits(string layerId) => DisplayUnitData.From(layerId switch {
        "precipitation" => boundary.Project.Units == UnitSystem.USCustomary ? PeriodAveragePrecipitation.InchesPerHour : PeriodAveragePrecipitation.MillimeterPerHour,
        "evapotranspiration" => PeriodAveragePrecipitation.MillimeterPerHour,
        "wind" => WindSpeed.MetersPerSecond, "airPressure" => AirPressure.MilliBars, "airDensity" => AirDensity.kilogramPerCubicMeter,
        _ => throw new ArgumentException("Unknown spatial unit family.")
    }, layerId);

    private static bool IsGaugeTable(MetGaugedData gauge) => gauge.DataLocation == DataLocation.Local && gauge.LocalDataType == LocalDataType.Table;
    private static bool TransportGaugeTable(MetGaugedData gauge) => IsGaugeTable(gauge) || GaugeCount(gauge) > 0
        && gauge.TableTimeSeries.IsValid(GaugeCount(gauge)).Result
        && (gauge.TableTimeSeries.TimeSeriesType != TimeSeriesType.Regular || gauge.TableTimeSeries.RTSTimeSpan > TimeSpan.Zero);
    private static int GaugeCount(MetGaugedData gauge) => Math.Max(gauge.Table?.Length ?? 0, gauge.Table2?.Length ?? 0);
    private int GaugeDisplayedCount(MetGaugedData gauge)
    {
        int count = GaugeCount(gauge);
        return gauge.TableTimeSeries.TimeSeriesType == TimeSeriesType.Regular && IsGaugeTable(gauge)
            ? Math.Max(count, (int)Math.Clamp((boundary.EndTime - boundary.StartTime) / gauge.TableTimeSeries.RTSTimeSpan + 1, 0, MaximumDisplayedRows)) : count;
    }
    private DateTime GaugeStart(TimeSeriesDefinition time) => time.FixedStartTime == TimeSeriesDefinition.NullTimestamp ? boundary.StartTime : time.FixedStartTime;
    private static string SpatialDate(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Local).ToString("O", Invariant);
    private DateTime[] GaugeTimes(MetGaugedData gauge, int count)
    {
        var time = gauge.TableTimeSeries;
        if (count > MaximumDisplayedRows) throw new ValidationException("The source row count exceeds the editor limit.");
        if (time.TimeSeriesType == TimeSeriesType.Regular)
        {
            if (time.RTSTimeSpan <= TimeSpan.Zero) throw new ValidationException("Interval value must be positive.");
            return time.Evaluate(count, boundary.StartTime);
        }
        if (time.ExpressedTimeType == ExpressedTimeType.Absolute && time.IrregularAbsoluteTimes == null
            || time.ExpressedTimeType == ExpressedTimeType.Relative && time.IrregularRelativeTimes == null)
        {
            if (count == 0) return [];
            throw new InvalidDataException("The source station input is missing its irregular timestamps.");
        }
        return time.Evaluate(count, boundary.StartTime);
    }
    private void SetGaugeTimes(TimeSeriesDefinition time, DateTime[] dates)
    {
        if (time.TimeSeriesType != TimeSeriesType.Irregular) return;
        if (time.ExpressedTimeType == ExpressedTimeType.Absolute) time.IrregularAbsoluteTimes = dates;
        else time.IrregularRelativeTimes = dates.Select(date => (date - GaugeStart(time)).TotalSeconds).ToArray();
    }
    private IEnumerable<(string Layer, string Id, MetGaugedData Gauge)> Gauges() => SpatialLayers().SelectMany(pair => pair.Value.Gauges.Select((gauge, index) => (pair.Key, spatialGaugeIds[pair.Key][index], gauge)));
    // Hidden local tables remain part of the draft when switching to constant
    // or remote mode; otherwise a later save would silently lose their edits.
    private Dictionary<string, BoundarySeriesData> SpatialSeries() => Gauges().Where(item => TransportGaugeTable(item.Gauge)).ToDictionary(item => GaugeScope(item.Layer, item.Id) + "/Time Series", item => ReadGaugeSeries(item.Layer, item.Id, item.Gauge));
    private BoundarySeriesData ReadGaugeSeries(string layerId, string id, MetGaugedData gauge)
    {
        string scope = GaugeScope(layerId, id), page = scope + "/Time Series";
        bool regular = gauge.TableTimeSeries.TimeSeriesType == TimeSeriesType.Regular, vector = layerId == "wind", visible = IsGaugeTable(gauge);
        int displayed = GaugeDisplayedCount(gauge);
        DateTime[] times = GaugeTimes(gauge, displayed);
        var rows = Enumerable.Range(0, displayed).Select(index => {
            var values = new Dictionary<string, string> { ["Time"] = SpatialDate(times[index]), ["Value"] = Number(index < (gauge.Table?.Length ?? 0) ? gauge.Table![index] : double.NaN) };
            if (vector) values["Value2"] = Number(index < (gauge.Table2?.Length ?? 0) ? gauge.Table2![index] : double.NaN);
            return new ModelRow(index.ToString(Invariant), values);
        }).ToArray();
        var columns = new List<BoundaryColumn> { new("Time", "Time", "DateTime", regular || !visible, 180), new("Value", vector ? "X Wind Velocity" : "Value", "double", !visible, 130) };
        if (vector) columns.Add(new("Value2", "Y Wind Velocity", "double", !visible, 130));
        var samples = rows.SelectMany((row, index) => (vector ? new[] { "Value", "Value2" } : new[] { "Value" }).Select(column =>
            new ModelSample(row.Id, Epoch(times[index]), Nullable(Numeric(row.Values[column], true)), vector ? (column == "Value" ? "X" : "Y") : gauge.Station?.Name ?? "Value", id + "/" + column))).ToArray();
        return new(page, scope, "MeteorologicGaugeSeries", page + "/Values", samples, rows, columns.ToArray(), !regular, true,
            !visible ? [] : regular ? ["clear", "refresh"] : ["clear", "refresh", "appendRow", "removeLastRow"], [], "Time", "Value", SpatialValueUnits(layerId));
    }
    private void ApplySpatialData(Dictionary<string, ModelRow[]>? rows, Dictionary<string, ModelSample[]>? samples, HashSet<string> allowedRows, HashSet<string> allowedPages)
    {
        foreach (var item in Gauges())
        {
            string page = GaugeScope(item.Layer, item.Id) + "/Time Series", table = page + "/Values";
            allowedRows.Add(table); allowedPages.Add(page);
            if (samples?.TryGetValue(page, out var points) == true && points.Length > 0) throw new ArgumentException("Station series are edited through their source table.");
            if (rows == null || !rows.TryGetValue(table, out var data)) continue;
            bool vector = item.Layer == "wind", regular = item.Gauge.TableTimeSeries.TimeSeriesType == TimeSeriesType.Regular;
            var current = ReadGaugeSeries(item.Layer, item.Id, item.Gauge);
            spatialInputBaseline.TryGetValue(page, out var previous);
            SpatialSeriesShape? shape = state.Spatial?.Shapes?.GetValueOrDefault(page);
            if (data.Length > MaximumDisplayedRows || !data.Select(row => row.Id).SequenceEqual(Enumerable.Range(0, data.Length).Select(index => index.ToString(Invariant)))
                || (regular && data.Length != current.Rows.Length && data.Length != previous?.Rows.Length && data.Length != shape?.DisplayedRows)
                || regular && data.Length < Math.Max(GaugeCount(item.Gauge), Math.Max(shape?.PrimaryCount ?? 0, shape?.SecondaryCount ?? 0))
                || data.Any(row => !row.Values.Keys.Order().SequenceEqual((vector ? new[] { "Time", "Value", "Value2" } : new[] { "Time", "Value" }).Order())))
                throw new ArgumentException("The station table must contain all ordered source rows and columns.");
            // Regular displays contain padded empty rows. A mode change must
            // preserve actual values without turning that padding into data.
            if (!regular && (shape?.Regular == true || shape == null && previous?.CanEditX == false))
            {
                int last = Array.FindLastIndex(data, row => Numeric(row.Values["Value"], true) is var value && !double.IsNaN(value)
                    || vector && !double.IsNaN(Numeric(row.Values["Value2"], true)));
                int count = Math.Max(GaugeCount(item.Gauge), Math.Max(shape?.PrimaryCount ?? 0, shape?.SecondaryCount ?? 0));
                data = data.Take(Math.Max(count, last + 1)).ToArray();
            }
            var dates = data.Select(row => ParseDate(row.Values["Time"])).ToArray();
            var oldDates = GaugeTimes(item.Gauge, regular ? data.Length : GaugeCount(item.Gauge));
            for (int index = 0; index < dates.Length; index++)
            {
                if (index < oldDates.Length && Epoch(oldDates[index]) == Epoch(dates[index])) dates[index] = oldDates[index];
                else if (regular && !(shape?.Regular == false || shape?.Regular == true && shape.Start.AddSeconds(shape.IntervalSeconds * index) == dates[index])
                    && (previous == null || index >= previous.Rows.Length || ParseDate(previous.Rows[index].Values["Time"]) != dates[index]))
                    throw new ArgumentException("Regular station timestamps are derived from their source interval.");
            }
            if (!regular && dates.Zip(dates.Skip(1)).Any(pair => pair.First >= pair.Second)) throw new ValidationException("Station timestamps must be strictly increasing.");
            foreach (string column in vector ? new[] { "Value", "Value2" } : new[] { "Value" })
            {
                double[] original = (column == "Value" ? item.Gauge.Table : item.Gauge.Table2) ?? [];
                double[] values = data.Select(row => Numeric(row.Values[column], true)).ToArray();
                int originalLength = Math.Max(original.Length, column == "Value" ? shape?.PrimaryCount ?? 0 : shape?.SecondaryCount ?? 0);
                int length = regular ? Math.Max(originalLength, Array.FindLastIndex(values, double.IsFinite) + 1) : values.Length;
                Array.Resize(ref values, length);
                if (column == "Value") item.Gauge.Table = values; else item.Gauge.Table2 = values;
            }
            SetGaugeTimes(item.Gauge.TableTimeSeries, dates);
        }
    }
    private bool TryTransformSpatialSeries(string pageId, string operation, out BoundarySeriesData? result)
    {
        result = null;
        var item = Gauges().FirstOrDefault(item => GaugeScope(item.Layer, item.Id) + "/Time Series" == pageId && IsGaugeTable(item.Gauge));
        if (item.Gauge == null) return false;
        var gauge = item.Gauge;
        if (operation == "clear") { if (gauge.Table != null) Array.Fill(gauge.Table, double.NaN); if (gauge.Table2 != null) Array.Fill(gauge.Table2, double.NaN); }
        else if (operation is "appendRow" or "removeLastRow")
        {
            if (gauge.TableTimeSeries.TimeSeriesType == TimeSeriesType.Regular) throw new NotSupportedException("Regular series rows are defined by their time window.");
            int count = GaugeCount(gauge), next = operation == "appendRow" ? count + 1 : count - 1;
            if (next < 0 || next > MaximumDisplayedRows) throw new ArgumentException("The source row count is outside the editor limit.");
            var dates = GaugeTimes(gauge, count); Array.Resize(ref dates, next);
            if (next > count) dates[count] = count == 0 ? boundary.StartTime : dates[count - 1].AddHours(1);
            double[] values = gauge.Table ?? []; Array.Resize(ref values, next); if (next > count) values[count] = double.NaN; gauge.Table = values;
            if (item.Layer == "wind") { values = gauge.Table2 ?? []; Array.Resize(ref values, next); if (next > count) values[count] = double.NaN; gauge.Table2 = values; }
            SetGaugeTimes(gauge.TableTimeSeries, dates);
        }
        else if (operation != "refresh") throw new NotSupportedException("This station series operation is unavailable.");
        result = ReadGaugeSeries(item.Layer, item.Id, gauge); return true;
    }
}
