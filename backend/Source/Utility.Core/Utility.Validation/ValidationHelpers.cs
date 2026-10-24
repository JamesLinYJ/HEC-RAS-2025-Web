using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Utility.DataStructures;
using Utility.Helpers;

namespace Utility.Validation;

public static class ValidationHelpers
{
	public static IList<LeveledValidationResult> EvaluateValidationAttribute(object localValue, ValidationAttribute validationAttribute, object validationContext)
	{
		if (validationAttribute is LeveledValidationAttribute leveledValidationAttribute)
		{
			return leveledValidationAttribute.ValidateValues(localValue, new ValidationContext(validationContext));
		}
		ValidationResult validationResult = validationAttribute.GetValidationResult(localValue, new ValidationContext(validationContext));
		if (validationResult != null)
		{
			if (validationResult is LeveledValidationResult initialValue)
			{
				return new CompactList<LeveledValidationResult>(initialValue);
			}
			return new CompactList<LeveledValidationResult>(new LeveledValidationResult(validationResult));
		}
		return default(CompactList<LeveledValidationResult>);
	}

	public static bool ValidateType(string stringValue, PropertyInfo propInfo)
	{
		if (propInfo == null)
		{
			return true;
		}
		Type propertyType = propInfo.PropertyType;
		if (propertyType == null)
		{
			return true;
		}
		if (ParseHelpers.TryParseStringAs(stringValue, propertyType) == null)
		{
			return false;
		}
		return true;
	}
}
