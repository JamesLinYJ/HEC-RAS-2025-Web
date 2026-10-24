using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

public class GreaterThanZero : LeveledValidationAttribute
{
	protected override string DefaultErrorMessage => "The current input can not be less than or equal to zero.";

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Error;

	public GreaterThanZero()
	{
		ErrorMessage = DefaultErrorMessage;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		string textValue = localValue.ToString();
		if (textValue.Equals(""))
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
