using System;
using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class AbsoluteBoundsAttribute : LeveledValidationAttribute
{
	protected override string DefaultErrorMessage => $"The current input is not between the absolute bounds of {LowerBound} and {UpperBound}";

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Error;

	public double LowerBound { get; private set; }

	public double UpperBound { get; private set; }

	public AbsoluteBoundsAttribute(double lowerBound, double upperBound, string errorMessage = null)
	{
		LowerBound = lowerBound;
		UpperBound = upperBound;
		ErrorMessage = DefaultErrorMessage;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext validationContext)
	{
		double result = 0.0;
		if (!double.TryParse(localValue.ToString(), out result) || result < LowerBound || result > UpperBound)
		{
			return new LeveledValidationResult(ErrorMessage, ErrorLevel);
		}
		return LeveledValidationResult.Success;
	}
}
