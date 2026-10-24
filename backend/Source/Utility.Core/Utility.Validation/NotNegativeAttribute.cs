using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

public class NotNegativeAttribute : LeveledValidationAttribute
{
	protected override string DefaultErrorMessage => "The current input can not be negative.";

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Error;

	public NotNegativeAttribute()
	{
		ErrorMessage = DefaultErrorMessage;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		string textValue = localValue.ToString();
		if (textValue.Equals(""))
		{
			return new LeveledValidationResult(ErrorMessage, ErrorLevel);
		}
		if (!double.TryParse(textValue, out var result) || !(result >= 0.0))
		{
			return new LeveledValidationResult(ErrorMessage, ErrorLevel);
		}
		return LeveledValidationResult.Success;
	}
}
