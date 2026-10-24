using System.ComponentModel.DataAnnotations;
using Utility.Filtering;

namespace Utility.Validation;

public class ValidFilterAttribute : LeveledValidationAttribute
{
	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Error;

	protected override string DefaultErrorMessage => "Not a valid filter.";

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		if (!(localValue is string textValue) || string.IsNullOrWhiteSpace(textValue))
		{
			return LeveledValidationResult.Success;
		}
		if (!FilterExpression.TryParse(textValue, out var expression, out var error))
		{
			return new LeveledValidationResult(error ?? DefaultErrorMessage, ErrorLevel);
		}
		if (!(context?.ObjectInstance is IFilterFieldSource filterFieldSource))
		{
			return LeveledValidationResult.Success;
		}
		foreach (FilterField field in expression.GetFields())
		{
			if (!filterFieldSource.HasField(field.NormalizedName))
			{
				string closest = FilterNames.Closest(field.NormalizedName, filterFieldSource.KnownNames);
				return new LeveledValidationResult((closest == null) ? ("Unknown column '" + field.RawName + "'.") : $"Unknown column '{field.RawName}' - did you mean '{closest}'?", ErrorLevel);
			}
		}
		return LeveledValidationResult.Success;
	}
}
