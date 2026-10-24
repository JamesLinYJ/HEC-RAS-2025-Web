using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering;
using Utility.Validation;

namespace Visual.Mapping.Observables.Symbology;

public class ObservableVectorSymbologyCondition : ObservableValidator, IMutableNotifyDataErrorInfo, INotifyDataErrorInfo
{
	private readonly VectorSymbologyCondition _condition;

	public VectorSymbologyCondition Condition => _condition;

	public string Column
	{
		get
		{
			return _condition.Column;
		}
		set
		{
			SetProperty(_condition.Column, value, _condition, (VectorSymbologyCondition c, string v) =>
			{
				c.Column = v;
			}, "Column");
		}
	}

	public string Operator
	{
		get
		{
			ConditionOperatorMap.Map.TryGetKey(_condition.Operator, out var key);
			return key;
		}
		set
		{
			ConditionOperatorMap.Map.TryGetKey(_condition.Operator, out var key);
			SetProperty(key, value, _condition, (VectorSymbologyCondition c, string v) =>
			{
				if (ConditionOperatorMap.Map.TryGetValue(v, out var localValue))
				{
					c.Operator = localValue;
					return;
				}
				throw new Exception("Could not resolve an operator for string " + v);
			}, "Operator");
		}
	}

	[CustomValidation(typeof(ObservableVectorSymbologyCondition), "ValidateThreshold")]
	public string Threshold
	{
		get
		{
			return _condition.Threshold;
		}
		set
		{
			SetProperty(_condition.Threshold, value, _condition, (VectorSymbologyCondition c, string v) =>
			{
				c.Threshold = v;
			}, "Threshold");
		}
	}

	public string ResultValue
	{
		get
		{
			return _condition.ResultValue;
		}
		set
		{
			SetProperty(_condition.ResultValue, value, _condition, (VectorSymbologyCondition c, string v) =>
			{
				c.ResultValue = v;
			}, "ResultValue");
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Type ColumnType
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<Type>.Default.Equals(field, value))
			{
				OnPropertyChanging("ColumnType");
				field = value;
				OnColumnTypeChanged(value);
				OnPropertyChanged("ColumnType");
			}
		}
	}

	public ObservableVectorSymbologyCondition()
	{
		_condition = new VectorSymbologyCondition();
	}

	public static ValidationResult ValidateThreshold(string stringValue, ValidationContext ctx)
	{
		ObservableVectorSymbologyCondition observableVectorSymbologyCondition = (ObservableVectorSymbologyCondition)ctx.ObjectInstance;
		if (string.IsNullOrEmpty(stringValue))
		{
			return ValidationResult.Success;
		}
		if (observableVectorSymbologyCondition.ColumnType == null || observableVectorSymbologyCondition.ColumnType == typeof(string))
		{
			return ValidationResult.Success;
		}
		if (!double.TryParse(stringValue, out var _))
		{
			return new ValidationResult("Threshold must be a number for this column type.");
		}
		return ValidationResult.Success;
	}

	void IMutableNotifyDataErrorInfo.ValidateProperty(object localValue, string propertyName)
	{
		ValidateProperty(localValue, propertyName);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnColumnTypeChanged(Type localValue)
	{
		ValidateProperty(Threshold, "Threshold");
	}
}
