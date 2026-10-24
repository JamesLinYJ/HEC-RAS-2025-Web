using System;
using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

public class WriteableDirectoryAttribute : LeveledValidationAttribute
{
	protected override string DefaultErrorMessage => "The directory cannot be written to.";

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Error;

	public WriteableDirectoryAttribute()
	{
		ErrorMessage = DefaultErrorMessage;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		if (localValue == null)
		{
			return new LeveledValidationResult("Value cannot be blank");
		}
		return ValidateValue((localValue as string) ?? throw new ArgumentException("Can only validate on strings"));
	}

	public static LeveledValidationResult ValidateValue(string path)
	{
		return LeveledValidationResult.Success;
	}
}
