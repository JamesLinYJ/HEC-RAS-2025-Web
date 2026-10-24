using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

public class EqualToAttribute<T> : LeveledValidationAttribute
{
	private T[] _values;

	public override ValidationErrorLevel ErrorLevel => OverrideErrorLevel;

	public ValidationErrorLevel OverrideErrorLevel { get; set; } = ValidationErrorLevel.Error;

	protected override string DefaultErrorMessage => "This value is invalid.";

	public EqualToAttribute(params T[] values)
	{
		_values = values;
	}

	public override LeveledValidationResult ValidateValue(object localValue3, ValidationContext context)
	{
		if (!(localValue3 is T localValue))
		{
			return new LeveledValidationResult(ErrorMessage, ErrorLevel);
		}
		if (_values == null)
		{
			return new LeveledValidationResult(ErrorMessage, ErrorLevel);
		}
		T[] values = _values;
		for (int i = 0; i < values.Length; i++)
		{
			T localValue2 = values[i];
			if (localValue2.Equals(localValue))
			{
				return LeveledValidationResult.Success;
			}
		}
		return new LeveledValidationResult(ErrorMessage, ErrorLevel);
	}
}
