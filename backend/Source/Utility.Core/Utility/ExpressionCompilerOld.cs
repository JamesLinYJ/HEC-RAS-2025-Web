using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace Utility;

public class ExpressionCompilerOld<T> where T : IFloatingPointIeee754<T>
{
	private readonly string _text;

	private int _pos;

	private char _ch;

	private readonly Dictionary<string, ParameterExpression> _variables;

	private readonly ParameterExpression[] _lambdaParameters;

	public static string[] Methods = new string[11]
	{
		"sin", "cos", "tan", "min", "max", "abs", "log10", "logE", "ln", "log2",
		"lerp"
	};

	public static string[] Constants = new string[3] { "pi", "e", "tau" };

	public ExpressionCompilerOld(string stringValue, IEnumerable<string> variableNames)
	{
		_text = stringValue;
		_pos = -1;
		NextChar();
		List<ParameterExpression> parameterExpressions = new List<ParameterExpression>();
		_variables = new Dictionary<string, ParameterExpression>(StringComparer.OrdinalIgnoreCase);
		foreach (string variableName in variableNames)
		{
			ParameterExpression parameterExpression = Expression.Parameter(typeof(T), variableName);
			_variables[variableName] = parameterExpression;
			parameterExpressions.Add(parameterExpression);
		}
		_lambdaParameters = parameterExpressions.ToArray();
	}

	private void NextChar()
	{
		_pos++;
		_ch = ((_pos < _text.Length) ? _text[_pos] : '\0');
	}

	private bool IsEOF()
	{
		return _pos >= _text.Length;
	}

	private void ConsumeWhitespace()
	{
		while (char.IsWhiteSpace(_ch))
		{
			NextChar();
		}
	}

	private bool Eat(char charToEat)
	{
		ConsumeWhitespace();
		if (_ch == charToEat)
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
		return ParseAdditive();
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
		}
		Expression expression = ParseExponent();
		if (ordinal == -1)
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
			expression = Expression.Call(typeof(ExpressionCompiler<T>).GetMethod("PowT", BindingFlags.Static | BindingFlags.NonPublic), expression, parseExponent);
		}
		return expression;
	}

	private Expression ParsePrimary()
	{
		ConsumeWhitespace();
		int pos = _pos;
		Expression result;
		if (Eat('('))
		{
			result = ParseExpression();
			if (!Eat(')'))
			{
				throw new Exception("Missing closing parenthesis");
			}
		}
		else if (char.IsDigit(_ch) || _ch == '.')
		{
			while (char.IsDigit(_ch) || _ch == '.')
			{
				NextChar();
			}
			string substring = _text.Substring(pos, _pos - pos);
			if (!double.TryParse(substring, NumberStyles.Any, CultureInfo.InvariantCulture, out var result2))
			{
				throw new Exception("Invalid number: " + substring);
			}
			result = Expression.Constant(ConvertConstant(result2), typeof(T));
		}
		else
		{
			if (!char.IsLetter(_ch))
			{
				throw new Exception("Unexpected character: " + _ch);
			}
			while (char.IsLetter(_ch))
			{
				NextChar();
			}
			string toLowerInvariant = _text.Substring(pos, _pos - pos).ToLowerInvariant();
			if (!Eat('('))
			{
				result = ((!_variables.TryGetValue(toLowerInvariant, out var localValue)) ? (toLowerInvariant switch
				{
					"pi" => (Expression)Expression.Constant(ConvertConstant(Math.PI), typeof(T)), 
					"e" => Expression.Constant(ConvertConstant(Math.E), typeof(T)), 
					"tau" => Expression.Constant(ConvertConstant(Math.PI * 2.0), typeof(T)), 
					_ => throw new Exception("Unknown variable or constant: " + toLowerInvariant), 
				}) : localValue);
			}
			else
			{
				List<Expression> expressions = new List<Expression>();
				expressions.Add(ParseExpression());
				while (Eat(','))
				{
					expressions.Add(ParseExpression());
				}
				if (!Eat(')'))
				{
					throw new Exception("Missing closing parenthesis after function argument for '" + toLowerInvariant + "'");
				}
				MethodInfo method;
				switch (toLowerInvariant)
				{
				case "sin":
					method = typeof(ExpressionCompiler<T>).GetMethod("SinT", BindingFlags.Static | BindingFlags.NonPublic);
					break;
				case "cos":
					method = typeof(ExpressionCompiler<T>).GetMethod("CosT", BindingFlags.Static | BindingFlags.NonPublic);
					break;
				case "tan":
					method = typeof(ExpressionCompiler<T>).GetMethod("TanT", BindingFlags.Static | BindingFlags.NonPublic);
					break;
				case "min":
					method = typeof(ExpressionCompiler<T>).GetMethod("MinT", BindingFlags.Static | BindingFlags.NonPublic);
					break;
				case "max":
					method = typeof(ExpressionCompiler<T>).GetMethod("MaxT", BindingFlags.Static | BindingFlags.NonPublic);
					break;
				case "abs":
					method = typeof(ExpressionCompiler<T>).GetMethod("AbsT", BindingFlags.Static | BindingFlags.NonPublic);
					break;
				case "log10":
					method = typeof(ExpressionCompiler<T>).GetMethod("Log10T", BindingFlags.Static | BindingFlags.NonPublic);
					break;
				case "loge":
				case "ln":
					method = typeof(ExpressionCompiler<T>).GetMethod("LogET", BindingFlags.Static | BindingFlags.NonPublic);
					break;
				case "log2":
					method = typeof(ExpressionCompiler<T>).GetMethod("Log2T", BindingFlags.Static | BindingFlags.NonPublic);
					break;
				case "lerp":
					method = typeof(ExpressionCompiler<T>).GetMethod("LERPT", BindingFlags.Static | BindingFlags.NonPublic);
					break;
				default:
					throw new Exception("Unknown function: " + toLowerInvariant);
				}
				MethodInfo method2 = method;
				int unity;
				switch (toLowerInvariant)
				{
				case "sin":
				case "tan":
				case "cos":
					unity = 1;
					break;
				case "min":
				case "max":
					unity = 2;
					break;
				case "abs":
					unity = 1;
					break;
				case "log2":
				case "loge":
				case "log10":
				case "ln":
					unity = 1;
					break;
				case "lerp":
					unity = 3;
					break;
				default:
					throw new Exception("Unknown function: " + toLowerInvariant);
				}
				int ordinal = unity;
				if (expressions.Count != ordinal)
				{
					throw new Exception("Incorrect number of arguments to " + toLowerInvariant);
				}
				result = Expression.Call(method2, expressions);
			}
		}
		return result;
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

	private static T PowT(T a, T b)
	{
		return T.Pow(a, b);
	}

	private static T MinT(T a, T b)
	{
		return T.Min(a, b);
	}

	private static T MaxT(T a, T b)
	{
		return T.Max(a, b);
	}

	private static T AbsT(T a)
	{
		return T.Abs(a);
	}

	private static T Log10T(T a)
	{
		return T.Log10(a);
	}

	private static T LogET(T a)
	{
		return T.Log(a);
	}

	private static T Log2T(T a)
	{
		return T.Log2(a);
	}

	private static T LERPT(T a, T b, T x)
	{
		return a + x * (b - a);
	}
}
