using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

public class DoubleAttribute : LeveledValidationAttribute
{
	private bool _allowBlankAsNaN;

	protected override string DefaultErrorMessage => "The current input is not a valid number.";

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.TypeError;

	public DoubleAttribute(bool allowBlankAsNaN = true)
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
		if (!double.TryParse(textValue, out var _))
		{
			return new LeveledValidationResult(ErrorMessage, ErrorLevel);
		}
		return LeveledValidationResult.Success;
	}
}
