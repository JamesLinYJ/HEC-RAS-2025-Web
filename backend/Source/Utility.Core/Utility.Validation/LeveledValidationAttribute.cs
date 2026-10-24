using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Utility.DataStructures;

namespace Utility.Validation;

public abstract class LeveledValidationAttribute : ValidationAttribute, IErrorLevel
{
	protected abstract string DefaultErrorMessage { get; }

	public abstract ValidationErrorLevel ErrorLevel { get; }

	public abstract LeveledValidationResult ValidateValue(object localValue, ValidationContext context);

	public virtual IList<LeveledValidationResult> ValidateValues(object localValue, ValidationContext validationContext)
	{
		LeveledValidationResult leveledValidationResult = ValidateValue(localValue, validationContext);
		if (leveledValidationResult == LeveledValidationResult.Success)
		{
			return default(CompactList<LeveledValidationResult>);
		}
		return new CompactList<LeveledValidationResult> { leveledValidationResult };
	}

	protected sealed override ValidationResult IsValid(object value, ValidationContext validationContext)
	{
		return ValidateValue(value, validationContext);
	}
}
