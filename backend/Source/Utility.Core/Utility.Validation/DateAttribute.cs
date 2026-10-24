using System;
using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class DateAttribute : LeveledValidationAttribute
{
	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Error;

	protected override string DefaultErrorMessage => "Improperly formatted date.";

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		if (!DateTime.TryParse((string)localValue, out var _))
		{
			return new LeveledValidationResult(DefaultErrorMessage);
		}
		return LeveledValidationResult.Success;
	}
}
