using System;
using System.ComponentModel.DataAnnotations;
using Utility.Extensions;

namespace Utility.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class TimeAttribute : LeveledValidationAttribute
{
	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Error;

	protected override string DefaultErrorMessage => "Improperly formatted time.";

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		if (!TimeSpan.TryParse(((string)localValue).StripWhitespace(), out var _))
		{
			return new LeveledValidationResult(DefaultErrorMessage);
		}
		return LeveledValidationResult.Success;
	}
}
