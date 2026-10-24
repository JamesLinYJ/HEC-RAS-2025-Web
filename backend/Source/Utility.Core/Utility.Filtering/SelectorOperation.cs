using System;
using System.Collections.Generic;
using System.Globalization;

namespace Utility.Filtering;

public sealed class SelectorOperation
{
	public SelectorOperationKind Kind { get; }

	public double Operand { get; }

	public string OperandText { get; }

	public string CanonicalName => Kind switch
	{
		SelectorOperationKind.Set => "set", 
		SelectorOperationKind.Scale => "scale", 
		SelectorOperationKind.Add => "add", 
		_ => Kind.ToString().ToLowerInvariant(), 
	};

	private SelectorOperation(SelectorOperationKind kind, double operand, string operandText)
	{
		Kind = kind;
		Operand = operand;
		OperandText = operandText;
	}

	public static bool IsNumericField(Type type)
	{
		return FilterValues.IsNumericType(type);
	}

	public static bool TryCreate(string name, IReadOnlyList<FilterLiteral> arguments, out SelectorOperation operation, out string error)
	{
		operation = null;
		error = null;
		if (!TryResolveKind(name, out var kind))
		{
			error = "Unknown operation '" + name + "'. Available: set, scale (mult), add.";
			return false;
		}
		if (arguments.Count != 1)
		{
			error = "Operation '" + name + "' takes exactly one numeric argument.";
			return false;
		}
		if (!arguments[0].TryGetNumber(out var number))
		{
			error = $"Operation '{name}' needs a numeric argument, got '{arguments[0]}'.";
			return false;
		}
		operation = new SelectorOperation(kind, number, arguments[0].Text);
		return true;
	}

	public bool TryApply(object currentValue, Type fieldType, out object result, out string error)
	{
		result = null;
		error = null;
		double operandTerm;
		switch (Kind)
		{
		case SelectorOperationKind.Set:
			operandTerm = Operand;
			break;
		case SelectorOperationKind.Scale:
		case SelectorOperationKind.Add:
		{
			if (!FilterValues.TryToDouble(currentValue, out var number))
			{
				error = "has no numeric value to " + CanonicalName + ".";
				return false;
			}
			operandTerm = ((Kind == SelectorOperationKind.Scale) ? (number * Operand) : (number + Operand));
			break;
		}
		default:
			error = "unknown operation.";
			return false;
		}
		return FilterValues.TryCoerce(FilterLiteral.FromNumber(operandTerm, operandTerm.ToString(CultureInfo.InvariantCulture)), fieldType, out result, out error);
	}

	public override string ToString()
	{
		return CanonicalName + "(" + OperandText + ")";
	}

	private static bool TryResolveKind(string name, out SelectorOperationKind kind)
	{
		switch (name.Trim().ToLowerInvariant())
		{
		case "set":
			kind = SelectorOperationKind.Set;
			return true;
		case "scale":
		case "mult":
		case "multiply":
			kind = SelectorOperationKind.Scale;
			return true;
		case "add":
			kind = SelectorOperationKind.Add;
			return true;
		default:
			kind = SelectorOperationKind.Set;
			return false;
		}
	}
}
