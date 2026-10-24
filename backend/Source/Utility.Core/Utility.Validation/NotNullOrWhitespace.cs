using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

public class NotNullOrWhitespace : LeveledValidationAttribute
{
	private string _errorMessage;

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Warn;

	protected override string DefaultErrorMessage => "Value null or whitespace.";

	public NotNullOrWhitespace(string errorMessage = null)
	{
		_errorMessage = errorMessage;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		if (localValue == null || !(localValue is string textValue) || string.IsNullOrWhiteSpace(textValue))
		{
			return new LeveledValidationResult(_errorMessage ?? DefaultErrorMessage);
		}
		return LeveledValidationResult.Success;
	}
}
