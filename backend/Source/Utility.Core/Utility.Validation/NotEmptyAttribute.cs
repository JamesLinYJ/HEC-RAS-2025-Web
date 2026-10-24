using System;
using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

public class NotEmptyAttribute : LeveledValidationAttribute
{
	private string _errorMessage;

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Warn;

	protected override string DefaultErrorMessage => "Value cannot be empty.";

	public NotEmptyAttribute(string errorMessage = null)
	{
		_errorMessage = errorMessage;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		if (localValue == null)
		{
			return new LeveledValidationResult(_errorMessage ?? DefaultErrorMessage);
		}
		if (!((localValue as IEnumerable) ?? throw new ArgumentException("Can't be used on a non IEnumerable")).GetEnumerator().MoveNext())
		{
			return new LeveledValidationResult(_errorMessage ?? DefaultErrorMessage);
		}
		return LeveledValidationResult.Success;
	}
}
