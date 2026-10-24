using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

public class IntegerAttribute : LeveledValidationAttribute
{
	private bool _allowBlankAsNaN;

	protected override string DefaultErrorMessage => "The current input is not a valid whole number.";

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.TypeError;

	public IntegerAttribute(bool allowBlankAsNaN = true)
	{
		ErrorMessage = DefaultErrorMessage;
		_allowBlankAsNaN = allowBlankAsNaN;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		string textValue = localValue.ToString();
		if (_allowBlankAsNaN && textValue.Equals(""))
		{
			return LeveledValidationResult.Success;
		}
		if (!int.TryParse(textValue, out var _))
		{
			return new LeveledValidationResult(ErrorMessage, ErrorLevel);
		}
		return LeveledValidationResult.Success;
	}
}
