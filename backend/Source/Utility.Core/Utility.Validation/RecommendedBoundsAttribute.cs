using System;
using System.ComponentModel.DataAnnotations;

namespace Utility.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class RecommendedBoundsAttribute : LeveledValidationAttribute
{
	protected override string DefaultErrorMessage => $"The current input is not between the recommended bounds of {LowerBound} and {UpperBound}";

	public override ValidationErrorLevel ErrorLevel => ValidationErrorLevel.Warn;

	public bool AllowBlank { get; private set; }

	public double LowerBound { get; private set; }

	public double UpperBound { get; private set; }

	public RecommendedBoundsAttribute(double lowerBound, double upperBound, string errorMessage = null, bool allowBlank = false)
	{
		LowerBound = lowerBound;
		UpperBound = upperBound;
		ErrorMessage = ((errorMessage == null) ? DefaultErrorMessage : errorMessage);
		AllowBlank = allowBlank;
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext validationContext)
	{
		string textValue = localValue?.ToString();
		if (AllowBlank && string.IsNullOrEmpty(textValue))
		{
			return LeveledValidationResult.Success;
		}
		double result = 0.0;
		if (!double.TryParse(localValue.ToString(), out result) || result < LowerBound || result > UpperBound)
		{
			return new LeveledValidationResult(ErrorMessage, ErrorLevel);
		}
		return LeveledValidationResult.Success;
	}
}
