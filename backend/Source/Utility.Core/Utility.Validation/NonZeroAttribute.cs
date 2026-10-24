using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Utility.Validation;

public class NonZeroAttribute : LeveledValidationAttribute
{
	private string _errorMessage;

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Warn;

	protected override string DefaultErrorMessage => "Value cannot be 0.";

	public NonZeroAttribute(string errorMessage = null)
	{
		_errorMessage = errorMessage;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		if (localValue is IConvertible convertible)
		{
			try
			{
				if (convertible.ToDouble(CultureInfo.InvariantCulture) == 0.0)
				{
					return new LeveledValidationResult(_errorMessage ?? DefaultErrorMessage);
				}
			}
			catch
			{
				throw new NotImplementedException("Can't use with non number");
			}
		}
		return LeveledValidationResult.Success;
	}
}
