using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace Utility.Validation;

public class FilenameCharsAttribute : LeveledValidationAttribute
{
	private static readonly HashSet<char> InvalidChars = new HashSet<char>(Path.GetInvalidFileNameChars());

	protected override string DefaultErrorMessage => "The current input has invalid characters.";

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Error;

	public FilenameCharsAttribute()
	{
		ErrorMessage = DefaultErrorMessage;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		if (localValue is string textValue && textValue != null)
		{
			if (textValue.Length == 0)
			{
				return new LeveledValidationResult("Filename can not be empty.");
			}
			for (int i = 0; i < textValue.Length; i++)
			{
				if (InvalidChars.Contains(textValue[i]))
				{
					return new LeveledValidationResult(DefaultErrorMessage);
				}
			}
		}
		return LeveledValidationResult.Success;
	}
}
