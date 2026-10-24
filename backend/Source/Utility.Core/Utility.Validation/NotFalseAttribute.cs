using System;
using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

public class NotFalseAttribute : LeveledValidationAttribute
{
	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Error;

	protected override string DefaultErrorMessage => "Value can not be false.";

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		if (!(localValue is bool))
		{
			throw new ArgumentException("NotFalseAttribute can only be applied to bools.");
		}
		if (!(bool)localValue)
		{
			return new LeveledValidationResult(ErrorMessage);
		}
		return LeveledValidationResult.Success;
	}
}
