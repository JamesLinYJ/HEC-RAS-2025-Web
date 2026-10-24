using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Utility.DataStructures;

namespace Utility.Validation;

public class ForwardedValidationAttribute : LeveledValidationAttribute
{
	private List<ValidationAttribute> _validationAttributes;

	private bool _initialized;

	public Type ForwardClassType { get; }

	public string ForwardPropertyName { get; }

	public override ValidationErrorLevel ErrorLevel
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	protected override string DefaultErrorMessage
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public ForwardedValidationAttribute(Type forwardClassType, string forwardPropertyName)
	{
		ForwardClassType = forwardClassType;
		ForwardPropertyName = forwardPropertyName;
	}

	private PropertyInfo TryGetPropertyInfo()
	{
		return ForwardClassType?.GetProperty(ForwardPropertyName);
	}

	private void Initialize()
	{
		_initialized = true;
		PropertyInfo propertyInfo = TryGetPropertyInfo();
		if (propertyInfo == null)
		{
			throw new InvalidOperationException("Can not find property " + ForwardPropertyName + " on class " + (ForwardClassType?.Name ?? "null"));
		}
		object[] customAttributes = propertyInfo.GetCustomAttributes(inherit: false);
		if (customAttributes == null)
		{
			return;
		}
		_validationAttributes = new List<ValidationAttribute>();
		object[] customAttributesArray = customAttributes;
		for (int i = 0; i < customAttributesArray.Length; i++)
		{
			if (customAttributesArray[i] is ValidationAttribute validationAttribute)
			{
				_validationAttributes.Add(validationAttribute);
			}
		}
	}

	public override LeveledValidationResult ValidateValue(object localValue, ValidationContext context)
	{
		IList<LeveledValidationResult> validateValues = ValidateValues(localValue, context);
		if (validateValues == null || validateValues.Count == 0)
		{
			return LeveledValidationResult.Success;
		}
		if (validateValues.Count == 1)
		{
			return validateValues[0];
		}
		throw new NotSupportedException("Please use ValidateValues, because this ValidationResult returns multiple values.");
	}

	public override IList<LeveledValidationResult> ValidateValues(object localValue, ValidationContext context)
	{
		if (!_initialized)
		{
			Initialize();
		}
		CompactList<LeveledValidationResult> compactList = default;
		bool validateType = ValidateType(localValue, out var res);
		if (!validateType)
		{
			compactList.Add(res);
		}
		if (!validateType)
		{
			return compactList;
		}
		if (_validationAttributes == null || _validationAttributes.Count == 0)
		{
			return compactList;
		}
		for (int i = 0; i < _validationAttributes.Count; i++)
		{
			ValidationAttribute validationAttribute = _validationAttributes[i];
			IList<LeveledValidationResult> validationAttributeList = ValidationHelpers.EvaluateValidationAttribute(localValue, validationAttribute, context);
			if (validationAttributeList != null && validationAttributeList.Count != 0)
			{
				compactList.AddRange(validationAttributeList);
			}
		}
		return compactList;
	}

	private bool ValidateType(object localValue, out LeveledValidationResult res)
	{
		res = LeveledValidationResult.Success;
		if (localValue == null)
		{
			return true;
		}
		PropertyInfo propertyInfo = TryGetPropertyInfo();
		bool validateType = ValidationHelpers.ValidateType((localValue is string textValue) ? textValue : localValue.ToString(), propertyInfo);
		if (!validateType)
		{
			res = new LeveledValidationResult("Not valid " + Types.ToFriendlyName(propertyInfo.PropertyType) + " type.", ValidationErrorLevel.TypeError);
		}
		return validateType;
	}
}
