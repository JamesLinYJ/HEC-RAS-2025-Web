using System.ComponentModel.DataAnnotations;
using Utility.Extensions;

namespace Utility.Validation;

public class GreaterThanZeroOrNaN : LeveledValidationAttribute
{
	protected override string DefaultErrorMessage => "Input must be greater than 0 (or blank)";

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Error;

	public GreaterThanZeroOrNaN()
	{
		ErrorMessage = DefaultErrorMessage;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		string textValue = localValue?.ToString();
		if (string.IsNullOrEmpty(textValue) || textValue.RoughlyEquals("NaN"))
		{
			return LeveledValidationResult.Success;
		}
		if (!double.TryParse(textValue, out var result) || !(result > 0.0))
		{
			return new LeveledValidationResult(ErrorMessage, ErrorLevel);
		}
		return LeveledValidationResult.Success;
	}
}
