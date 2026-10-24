using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

public class NotNullAttribute : LeveledValidationAttribute
{
	private string? _errorMessage;

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Warn;

	protected override string DefaultErrorMessage => "Value cannot be null.";

	public NotNullAttribute(string? errorMessage = null)
	{
		_errorMessage = errorMessage;
	}

	public override LeveledValidationResult ValidateValue(object? localValue, ValidationContext context)
	{
		if (localValue == null)
		{
			return new LeveledValidationResult(_errorMessage ?? DefaultErrorMessage);
		}
		return LeveledValidationResult.Success;
	}
}
