using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Utility.Units;

namespace Ras.Backend;

public sealed record UnitCalculatorUnit(string Id, string Name, string UnitTypeName);
public sealed record UnitCalculatorLoadRequest(string? FromUnitId, string? ToUnitId = null);
public sealed record UnitCalculatorLoadResult(
    UnitCalculatorUnit[] FromUnits,
    UnitCalculatorUnit[] ToUnits,
    string? FromUnitId,
    string? FromUnitName,
    string? UnitTypeName,
    string? ToUnitId);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UnitCalculatorDirection
{
    FromTo,
    ToFrom
}

public sealed record UnitCalculatorConvertRequest(
    string FromUnitId,
    string ToUnitId,
    double Value,
    UnitCalculatorDirection Direction);
public sealed record UnitCalculatorConvertResult(double Value);

/// <summary>
/// Typed host boundary over the retained unit model. Conversion itself always
/// runs through BaseUnitType.TryConvertTo; this service contains no unit math.
/// </summary>
public static class UnitCalculatorService
{
    private static readonly Lazy<IReadOnlyList<BaseUnitType>> Units = new(LoadSourceUnits);

    public static UnitCalculatorLoadResult Load(UnitCalculatorLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<BaseUnitType> units = Units.Value;
        BaseUnitType? from = string.IsNullOrEmpty(request.FromUnitId) ? null : Resolve(request.FromUnitId, units);
        BaseUnitType[] targets = from is null
            ? []
            : BaseUnitType.GetAvailableTypesBase(from.GetType()).ToArray();
        string? toID = request.ToUnitId is not null && targets.Any(unit => Id(unit) == request.ToUnitId)
            ? request.ToUnitId : null;

        return new(
            units.Select(ToChoice).ToArray(),
            targets.Select(ToChoice).ToArray(),
            from is null ? null : Id(from),
            from?.Name,
            from?.GetType().Name,
            toID);
    }

    public static UnitCalculatorConvertResult Convert(UnitCalculatorConvertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!double.IsFinite(request.Value)) throw new ArgumentOutOfRangeException(nameof(request), "Enter a finite number.");
        if (!Enum.IsDefined(request.Direction)) throw new ArgumentOutOfRangeException(nameof(request.Direction), "Choose a supported conversion direction.");

        IReadOnlyList<BaseUnitType> units = Units.Value;
        BaseUnitType from = Resolve(request.FromUnitId, units);
        BaseUnitType to = Resolve(request.ToUnitId, units);
        BaseUnitType source = request.Direction == UnitCalculatorDirection.FromTo ? from : to;
        BaseUnitType target = request.Direction == UnitCalculatorDirection.FromTo ? to : from;
        if (!source.TryConvertTo(target, request.Value, out double result))
            throw new ArgumentException("FromUnit does not match ToUnit in UnitCalculator.");
        if (!double.IsFinite(result)) throw new ArgumentOutOfRangeException(nameof(request), "The converted value is outside the finite numeric range.");
        return new(result);
    }

    private static IReadOnlyList<BaseUnitType> LoadSourceUnits()
    {
        Assembly assembly = typeof(BaseUnitType).Assembly;
        foreach (Type type in assembly.GetTypes()
                     .Where(type => type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(BaseUnitType)))
                     .OrderBy(type => type.FullName ?? type.Name, StringComparer.Ordinal))
        {
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }

        return BaseUnitType.AvailableTypes
            .Distinct()
            .OrderBy(unit => unit.GetType().FullName ?? unit.GetType().Name, StringComparer.Ordinal)
            .ThenBy(unit => unit.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static BaseUnitType Resolve(string id, IReadOnlyList<BaseUnitType> units)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Choose a source and target unit.", nameof(id));
        return units.FirstOrDefault(unit => Id(unit) == id)
            ?? throw new ArgumentException("The selected source unit is no longer available.", nameof(id));
    }

    private static UnitCalculatorUnit ToChoice(BaseUnitType unit) =>
        new(Id(unit), unit.Name, unit.GetType().Name);

    internal static string SourceUnitID(BaseUnitType unit) => Id(unit);

    private static string Id(BaseUnitType unit) =>
        (unit.GetType().FullName ?? unit.GetType().Name) + "::" + unit.Name;
}
