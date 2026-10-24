using System.ComponentModel.DataAnnotations;
using System.IO;

namespace Utility.Validation;

public class ValidDirectoryAttribute : LeveledValidationAttribute
{
	protected override string DefaultErrorMessage => "The current input has invalid characters.";

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Error;

	public bool AllowBlank { get; set; }

	public bool MustBeRooted { get; set; } = true;

	public ValidDirectoryAttribute()
	{
		ErrorMessage = DefaultErrorMessage;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		string textValue = localValue as string;
		if (string.IsNullOrEmpty(textValue) && AllowBlank)
		{
			return LeveledValidationResult.Success;
		}
		return ValidateValue(textValue, AllowBlank, MustBeRooted);
	}

	public static LeveledValidationResult ValidateValue(string path, bool allowBlank = false, bool mustBeRooted = false)
	{
		try
		{
			if (string.IsNullOrEmpty(path) & allowBlank)
			{
				return LeveledValidationResult.Success;
			}
			Path.GetFullPath(path);
			if (mustBeRooted && !Path.IsPathRooted(path))
			{
				return new LeveledValidationResult("Path does not have a valid root.");
			}
		}
		catch
		{
			return new LeveledValidationResult("Invalid Path");
		}
		return LeveledValidationResult.Success;
	}
}
