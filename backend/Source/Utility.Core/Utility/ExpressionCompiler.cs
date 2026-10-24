using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace Utility;

public class ExpressionCompiler<T> where T : IFloatingPointIeee754<T>
{
	private readonly string _text;

	private int _pos;

	private char _ch;

	private readonly Dictionary<string, ParameterExpression> _variables;

	private readonly Dictionary<string, T> _constants;

	private readonly ParameterExpression[] _lambdaParameters;

	public static readonly string[] SupportedFunctions = new string[27]
	{
		"if", "sin", "cos", "tan", "asin", "acos", "atan", "sqrt", "root", "abs",
		"log10", "loge", "ln", "log2", "lerp", "sum", "mean", "median", "min", "max",
		"variance", "stdev", "stderr", "skew", "kurtosis", "deg2rad", "rad2deg"
	};

	public static readonly string[] SupportedConstants = new string[3] { "pi", "e", "tau" };

	public ExpressionCompiler(string stringValue, IEnumerable<string> variableNames, IEnumerable<KeyValuePair<string, T>> constants = null)
		: this(stringValue, variableNames.Select((string n) => Expression.Parameter(typeof(T), n)), constants)
	{
	}

	public ExpressionCompiler(string stringValue, IEnumerable<ParameterExpression> parameters, IEnumerable<KeyValuePair<string, T>> constants = null)
	{
		_text = stringValue;
		_pos = -1;
		NextChar();
		_lambdaParameters = parameters.ToArray();
		_variables = new Dictionary<string, ParameterExpression>(StringComparer.OrdinalIgnoreCase);
		ParameterExpression[] lambdaParameters = _lambdaParameters;
		foreach (ParameterExpression parameterExpression in lambdaParameters)
		{
			_variables[parameterExpression.Name] = parameterExpression;
		}
		_constants = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
		if (constants == null)
		{
			return;
		}
		foreach (KeyValuePair<string, T> constant in constants)
		{
			_constants[constant.Key] = constant.Value;
		}
	}

	private void NextChar()
	{
		_pos++;
		if (_pos < _text.Length)
		{
			_ch = _text[_pos];
		}
		else
		{
			_ch = '\0';
		}
	}

	private void ConsumeWhitespace()
	{
		while (char.IsWhiteSpace(_ch))
		{
			NextChar();
		}
	}

	private bool Eat(char c)
	{
		ConsumeWhitespace();
		if (_ch == c)
		{
			NextChar();
			return true;
		}
		return false;
	}

	private static T ConvertConstant(double computed)
	{
		return T.CreateChecked(computed);
	}

	public Delegate CompileLambda()
	{
		Expression body = ParseExpression();
		if (_pos < _text.Length)
		{
			throw new Exception("Unexpected character: " + _ch);
		}
		return Expression.Lambda(body, _lambdaParameters).Compile();
	}

	private Expression ParseExpression()
	{
		return ParseOr();
	}

	private Expression ParseOr()
	{
		Expression expression = ParseAnd();
		while (true)
		{
			ConsumeWhitespace();
			if (_pos + 2 > _text.Length || string.Compare(_text, _pos, "or", 0, 2, StringComparison.OrdinalIgnoreCase) != 0 || (_pos + 2 < _text.Length && char.IsLetterOrDigit(_text[_pos + 2])))
			{
				break;
			}
			NextChar();
			NextChar();
			Expression left = ParseAnd();
			ConstantExpression constantExpression = Expression.Constant(T.Zero, typeof(T));
			ConstantExpression ifTrue = Expression.Constant(T.One, typeof(T));
			BinaryExpression left2 = Expression.NotEqual(expression, constantExpression);
			BinaryExpression right = Expression.NotEqual(left, constantExpression);
			expression = Expression.Condition(Expression.OrElse(left2, right), ifTrue, constantExpression);
		}
		return expression;
	}

	private Expression ParseAnd()
	{
		Expression expression = ParseComparison();
		while (true)
		{
			ConsumeWhitespace();
			if (_pos + 3 > _text.Length || string.Compare(_text, _pos, "and", 0, 3, StringComparison.OrdinalIgnoreCase) != 0 || (_pos + 3 < _text.Length && char.IsLetterOrDigit(_text[_pos + 3])))
			{
				break;
			}
			NextChar();
			NextChar();
			NextChar();
			Expression left = ParseComparison();
			ConstantExpression constantExpression = Expression.Constant(T.Zero, typeof(T));
			ConstantExpression ifTrue = Expression.Constant(T.One, typeof(T));
			BinaryExpression left2 = Expression.NotEqual(expression, constantExpression);
			BinaryExpression right = Expression.NotEqual(left, constantExpression);
			expression = Expression.Condition(Expression.AndAlso(left2, right), ifTrue, constantExpression);
		}
		return expression;
	}

	private Expression ParseComparison()
	{
		Expression expression = ParseAdditive();
		ConsumeWhitespace();
		if (_pos + 1 < _text.Length)
		{
			switch (_text.Substring(_pos, 2))
			{
			case "==":
			{
				NextChar();
				NextChar();
				Expression right4 = ParseAdditive();
				return Expression.Condition(Expression.Equal(expression, right4), Expression.Constant(T.One, typeof(T)), Expression.Constant(T.Zero, typeof(T)));
			}
			case "!=":
			{
				NextChar();
				NextChar();
				Expression right3 = ParseAdditive();
				return Expression.Condition(Expression.NotEqual(expression, right3), Expression.Constant(T.One, typeof(T)), Expression.Constant(T.Zero, typeof(T)));
			}
			case "<=":
			{
				NextChar();
				NextChar();
				Expression right2 = ParseAdditive();
				return Expression.Condition(Expression.LessThanOrEqual(expression, right2), Expression.Constant(T.One, typeof(T)), Expression.Constant(T.Zero, typeof(T)));
			}
			case ">=":
			{
				NextChar();
				NextChar();
				Expression right = ParseAdditive();
				return Expression.Condition(Expression.GreaterThanOrEqual(expression, right), Expression.Constant(T.One, typeof(T)), Expression.Constant(T.Zero, typeof(T)));
			}
			}
		}
		if (_ch == '<')
		{
			NextChar();
			Expression right5 = ParseAdditive();
			return Expression.Condition(Expression.LessThan(expression, right5), Expression.Constant(T.One, typeof(T)), Expression.Constant(T.Zero, typeof(T)));
		}
		if (_ch == '>')
		{
			NextChar();
			Expression right6 = ParseAdditive();
			return Expression.Condition(Expression.GreaterThan(expression, right6), Expression.Constant(T.One, typeof(T)), Expression.Constant(T.Zero, typeof(T)));
		}
		return expression;
	}

	private Expression ParseAdditive()
	{
		Expression expression = ParseMultiplicative();
		while (true)
		{
			if (Eat('+'))
			{
				Expression right = ParseMultiplicative();
				expression = Expression.Add(expression, right);
				continue;
			}
			if (!Eat('-'))
			{
				break;
			}
			Expression right2 = ParseMultiplicative();
			expression = Expression.Subtract(expression, right2);
		}
		return expression;
	}

	private Expression ParseMultiplicative()
	{
		Expression expression = ParseUnary();
		while (true)
		{
			if (Eat('*'))
			{
				Expression right = ParseUnary();
				expression = Expression.Multiply(expression, right);
				continue;
			}
			if (Eat('/'))
			{
				Expression right2 = ParseUnary();
				expression = Expression.Divide(expression, right2);
				continue;
			}
			if (!Eat('%'))
			{
				break;
			}
			Expression right3 = ParseUnary();
			expression = Expression.Modulo(expression, right3);
		}
		return expression;
	}

	private Expression ParseUnary()
	{
		int ordinal = 1;
		while (_ch == '+' || _ch == '-' || _ch == ' ')
		{
			if (Eat('-'))
			{
				ordinal = -ordinal;
			}
			else
			{
				Eat('+');
			}
		}
		Expression expression = ParseExponent();
		if (ordinal < 0)
		{
			expression = Expression.Negate(expression);
		}
		return expression;
	}

	private Expression ParseExponent()
	{
		Expression expression = ParsePrimary();
		if (Eat('^'))
		{
			Expression parseExponent = ParseExponent();
			expression = Expression.Call(GetStaticMethod("PowT"), expression, parseExponent);
		}
		return expression;
	}

	private Expression ParsePrimary()
	{
		ConsumeWhitespace();
		if (Eat('('))
		{
			Expression result = ParseExpression();
			if (!Eat(')'))
			{
				throw new Exception("Missing closing parenthesis");
			}
			return result;
		}
		if (char.IsDigit(_ch) || _ch == '.')
		{
			int pos = _pos;
			while (char.IsDigit(_ch) || _ch == '.')
			{
				NextChar();
			}
			string substring = _text.Substring(pos, _pos - pos);
			if (!double.TryParse(substring, NumberStyles.Any, CultureInfo.InvariantCulture, out var result2))
			{
				throw new Exception("Invalid number: " + substring);
			}
			return Expression.Constant(ConvertConstant(result2), typeof(T));
		}
		if (char.IsLetter(_ch))
		{
			int pos2 = _pos;
			while (char.IsLetterOrDigit(_ch))
			{
				NextChar();
			}
			string toLowerInvariant = _text.Substring(pos2, _pos - pos2).ToLowerInvariant();
			if (Eat('('))
			{
				List<Expression> expressions = new List<Expression>();
				expressions.Add(ParseExpression());
				while (Eat(','))
				{
					expressions.Add(ParseExpression());
				}
				if (!Eat(')'))
				{
					throw new Exception("Missing ')' after function '" + toLowerInvariant + "'");
				}
				if (toLowerInvariant == "if")
				{
					if (expressions.Count != 3)
					{
						throw new Exception("if() requires exactly 3 arguments: if(condition, ifTrue, ifFalse)");
					}
					ConstantExpression right = Expression.Constant(T.Zero, typeof(T));
					return Expression.Condition(Expression.NotEqual(expressions[0], right), expressions[1], expressions[2]);
				}
				MethodInfo staticMethod;
				int unity;
				switch (toLowerInvariant)
				{
				case "sin":
					staticMethod = GetStaticMethod("SinT");
					unity = 1;
					break;
				case "cos":
					staticMethod = GetStaticMethod("CosT");
					unity = 1;
					break;
				case "tan":
					staticMethod = GetStaticMethod("TanT");
					unity = 1;
					break;
				case "asin":
					staticMethod = GetStaticMethod("AsinT");
					unity = 1;
					break;
				case "acos":
					staticMethod = GetStaticMethod("AcosT");
					unity = 1;
					break;
				case "atan":
					staticMethod = GetStaticMethod("AtanT");
					unity = 1;
					break;
				case "pow":
					staticMethod = GetStaticMethod("PowT");
					unity = 2;
					break;
				case "sqrt":
					staticMethod = GetStaticMethod("SqrtT");
					unity = 1;
					break;
				case "root":
					staticMethod = GetStaticMethod("RootT");
					unity = 2;
					break;
				case "abs":
					staticMethod = GetStaticMethod("AbsT");
					unity = 1;
					break;
				case "log10":
					staticMethod = GetStaticMethod("Log10T");
					unity = 1;
					break;
				case "loge":
				case "ln":
					staticMethod = GetStaticMethod("LogET");
					unity = 1;
					break;
				case "log2":
					staticMethod = GetStaticMethod("Log2T");
					unity = 1;
					break;
				case "lerp":
					staticMethod = GetStaticMethod("LERPT");
					unity = 3;
					break;
				case "sum":
					staticMethod = GetStaticMethod("SumArray");
					unity = 1;
					break;
				case "mean":
				case "average":
					staticMethod = GetStaticMethod("MeanArray");
					unity = 1;
					break;
				case "median":
					staticMethod = GetStaticMethod("MedianArray");
					unity = 1;
					break;
				case "min":
					if (expressions.Count == 1)
					{
						staticMethod = GetStaticMethod("MinArray");
						unity = 1;
					}
					else
					{
						staticMethod = GetStaticMethod("MinT");
						unity = 2;
					}
					break;
				case "max":
					if (expressions.Count == 1)
					{
						staticMethod = GetStaticMethod("MaxArray");
						unity = 1;
					}
					else
					{
						staticMethod = GetStaticMethod("MaxT");
						unity = 2;
					}
					break;
				case "variance":
					staticMethod = GetStaticMethod("VarianceArray");
					unity = 1;
					break;
				case "stdev":
				case "stddev":
					staticMethod = GetStaticMethod("StDevArray");
					unity = 1;
					break;
				case "stderr":
					staticMethod = GetStaticMethod("StdErrArray");
					unity = 1;
					break;
				case "skew":
					staticMethod = GetStaticMethod("SkewArray");
					unity = 1;
					break;
				case "kurtosis":
					staticMethod = GetStaticMethod("KurtosisArray");
					unity = 1;
					break;
				case "deg2rad":
					staticMethod = GetStaticMethod("Deg2RadT");
					unity = 1;
					break;
				case "rad2deg":
					staticMethod = GetStaticMethod("Rad2DegT");
					unity = 1;
					break;
				default:
					throw new Exception("Unknown function: " + toLowerInvariant);
				}
				if (expressions.Count != unity)
				{
					throw new Exception("Incorrect argument count for '" + toLowerInvariant + "'");
				}
				return Expression.Call(staticMethod, expressions);
			}
			if (_variables.TryGetValue(toLowerInvariant, out var localValue))
			{
				return localValue;
			}
			if (_constants.TryGetValue(toLowerInvariant, out var localValue2))
			{
				return Expression.Constant(localValue2, typeof(T));
			}
			return toLowerInvariant switch
			{
				"pi" => (Expression)Expression.Constant(ConvertConstant(Math.PI), typeof(T)), 
				"e" => Expression.Constant(ConvertConstant(Math.E), typeof(T)), 
				"tau" => Expression.Constant(ConvertConstant(Math.PI * 2.0), typeof(T)), 
				_ => throw new Exception("Unknown variable or constant: " + toLowerInvariant), 
			};
		}
		throw new Exception("Unexpected character: " + _ch);
	}

	private static MethodInfo GetStaticMethod(string name)
	{
		return typeof(ExpressionCompiler<T>).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
	}

	private static T SinT(T x)
	{
		return T.Sin(x);
	}

	private static T CosT(T x)
	{
		return T.Cos(x);
	}

	private static T TanT(T x)
	{
		return T.Tan(x);
	}

	private static T AsinT(T x)
	{
		return T.Asin(x);
	}

	private static T AcosT(T x)
	{
		return T.Acos(x);
	}

	private static T AtanT(T x)
	{
		return T.Atan(x);
	}

	private static T PowT(T a, T b)
	{
		return T.Pow(a, b);
	}

	private static T SqrtT(T x)
	{
		return T.Sqrt(x);
	}

	private static T RootT(T a, T n)
	{
		T y = T.CreateChecked(1) / n;
		return T.Pow(a, y);
	}

	private static T AbsT(T x)
	{
		return T.Abs(x);
	}

	private static T Log10T(T x)
	{
		return T.Log10(x);
	}

	private static T LogET(T x)
	{
		return T.Log(x);
	}

	private static T Log2T(T x)
	{
		return T.Log2(x);
	}

	private static T LERPT(T a, T b, T x)
	{
		return a + x * (b - a);
	}

	private static T MinT(T a, T b)
	{
		return T.Min(a, b);
	}

	private static T MaxT(T a, T b)
	{
		return T.Max(a, b);
	}

	private static T Deg2RadT(T x)
	{
		T convertConstantOverConvertConstant = ConvertConstant(Math.PI) / ConvertConstant(180.0);
		return x * convertConstantOverConvertConstant;
	}

	private static T Rad2DegT(T x)
	{
		T convertConstant = ConvertConstant(Math.PI);
		T convertConstantOverConvertConstant = ConvertConstant(180.0) / convertConstant;
		return x * convertConstantOverConvertConstant;
	}

	private static T SumArray(T[] arr)
	{
		T zero = T.Zero;
		for (int i = 0; i < arr.Length; i++)
		{
			zero += arr[i];
		}
		return zero;
	}

	private static T MeanArray(T[] arr)
	{
		T sumArray = SumArray(arr);
		T createChecked = T.CreateChecked(arr.Length);
		return sumArray / createChecked;
	}

	private static T MedianArray(T[] arr)
	{
		if (arr.Length == 0)
		{
			throw new ArgumentException("Cannot compute median of empty array");
		}
		T[] arrArray = new T[arr.Length];
		arr.CopyTo(arrArray, 0);
		Array.Sort(arrArray);
		int scaledLength = arrArray.Length / 2;
		if (arrArray.Length % 2 != 0)
		{
			return arrArray[scaledLength];
		}
		return (arrArray[scaledLength - 1] + arrArray[scaledLength]) / T.CreateChecked(2);
	}

	private static T MinArray(T[] arr)
	{
		if (arr.Length == 0)
		{
			throw new ArgumentException("Cannot compute min of empty array");
		}
		T arr2 = arr[0];
		foreach (T arr3 in arr)
		{
			if (arr3 < arr2)
			{
				arr2 = arr3;
			}
		}
		return arr2;
	}

	private static T MaxArray(T[] arr)
	{
		if (arr.Length == 0)
		{
			throw new ArgumentException("Cannot compute max of empty array");
		}
		T arr2 = arr[0];
		foreach (T arr3 in arr)
		{
			if (arr3 > arr2)
			{
				arr2 = arr3;
			}
		}
		return arr2;
	}

	private static T VarianceArray(T[] arr)
	{
		if (arr.Length == 0)
		{
			throw new ArgumentException("Cannot compute variance of empty array");
		}
		T meanArray = MeanArray(arr);
		T zero = T.Zero;
		for (int i = 0; i < arr.Length; i++)
		{
			T arrMeanArrayTerm = arr[i] - meanArray;
			zero += arrMeanArrayTerm * arrMeanArrayTerm;
		}
		return zero / T.CreateChecked(arr.Length);
	}

	private static T StDevArray(T[] arr)
	{
		return T.Sqrt(VarianceArray(arr));
	}

	private static T StdErrArray(T[] arr)
	{
		return StDevArray(arr) / T.Sqrt(T.CreateChecked(arr.Length));
	}

	private static T SkewArray(T[] arr)
	{
		if (arr.Length == 0)
		{
			throw new ArgumentException("Cannot compute skewness of empty array");
		}
		T meanArray = MeanArray(arr);
		T stDevArray = StDevArray(arr);
		if (stDevArray == T.Zero)
		{
			return T.Zero;
		}
		T zero = T.Zero;
		for (int i = 0; i < arr.Length; i++)
		{
			T x = (arr[i] - meanArray) / stDevArray;
			zero += T.Pow(x, T.CreateChecked(3));
		}
		return zero / T.CreateChecked(arr.Length);
	}

	private static T KurtosisArray(T[] arr)
	{
		if (arr.Length == 0)
		{
			throw new ArgumentException("Cannot compute kurtosis of empty array");
		}
		T meanArray = MeanArray(arr);
		T stDevArray = StDevArray(arr);
		if (stDevArray == T.Zero)
		{
			return T.CreateChecked(-3);
		}
		T zero = T.Zero;
		for (int i = 0; i < arr.Length; i++)
		{
			T x = (arr[i] - meanArray) / stDevArray;
			zero += T.Pow(x, T.CreateChecked(4));
		}
		return zero / T.CreateChecked(arr.Length) - T.CreateChecked(3);
	}
}
