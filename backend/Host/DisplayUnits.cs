using Utility.Units;

namespace Ras.Backend;

// Only affine display conversions are exposed here. Source values and file
// units are unchanged; a display-unit selection is presentation state.
public sealed record DisplayUnitChoice(string Id, double Scale, double Offset, string? UnitTypeId = null, string? Name = null);
public sealed record DisplayUnitData(string SourceId, string SelectionId, DisplayUnitChoice[] Choices,
    string? SourceUnitTypeId = null)
{
    internal static DisplayUnitData From(BaseUnitType source, string selectionId)
    {
        var choices = BaseUnitType.GetAvailableTypesBase(source.GetType()).Select(unit => {
            var values = source.TryConvertTo(unit, new double[] { 0, 1, 2 });
            double offset = values[0], scale = values[1] - offset;
            if (!double.IsFinite(offset) || !double.IsFinite(scale) || scale <= 0
                || Math.Abs(values[2] - (offset + 2 * scale)) > Math.Abs(scale) * 1e-12)
                throw new InvalidDataException("The source unit does not provide a finite affine display conversion.");
            return new DisplayUnitChoice(unit.Name, scale, offset, UnitCalculatorService.SourceUnitID(unit), unit.Name);
        }).ToArray();
        if (!choices.Any(choice => choice.Id == source.Name)) throw new InvalidDataException("The source unit is missing from its unit family.");
        return new(source.Name, selectionId, choices, UnitCalculatorService.SourceUnitID(source));
    }
}
