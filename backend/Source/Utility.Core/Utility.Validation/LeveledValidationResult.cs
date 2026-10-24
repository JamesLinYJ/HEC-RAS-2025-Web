using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

public class LeveledValidationResult : ValidationResult, IErrorLevel
{
	private ValidationErrorLevel _errorLevel;

	public ValidationErrorLevel ErrorLevel => _errorLevel;

	public new static LeveledValidationResult Success => null;

	public LeveledValidationResult(ValidationResult validationResult, ValidationErrorLevel errorLevel = ValidationErrorLevel.Error)
		: base(validationResult)
	{
		_errorLevel = errorLevel;
	}

	public LeveledValidationResult(string errorMessage, ValidationErrorLevel errorLevel = ValidationErrorLevel.Error)
		: base(errorMessage)
	{
		_errorLevel = errorLevel;
	}

	public LeveledValidationResult(string errorMessage, IEnumerable<string> memberNames, ValidationErrorLevel errorLevel = ValidationErrorLevel.Error)
		: base(errorMessage, memberNames)
	{
		_errorLevel = errorLevel;
	}
}
