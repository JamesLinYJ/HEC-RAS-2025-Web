using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Utility.Logging;

internal sealed class LogValuesFormatter
{
	private const string NullValue = "(null)";

	private static readonly char[] FormatDelimiters = new char[2] { ',', ':' };

	private readonly string _format;

	private readonly List<string> _valueNames = new List<string>();

	public string OriginalFormat { get; private set; }

	public List<string> ValueNames => _valueNames;

	public LogValuesFormatter(string format)
	{
		if (format == null)
		{
			throw new ArgumentNullException("format");
		}
		OriginalFormat = format;
		Span<char> initialBuffer = stackalloc char[256];
		ValueStringBuilder valueStringBuilder = new ValueStringBuilder(initialBuffer);
		int lengthTerm = 0;
		int length = format.Length;
		while (lengthTerm < length)
		{
			int braceIndex = FindBraceIndex(format, '{', lengthTerm, length);
			if (lengthTerm == 0 && braceIndex == length)
			{
				_format = format;
				return;
			}
			int braceIndex2 = FindBraceIndex(format, '}', braceIndex, length);
			if (braceIndex2 == length)
			{
				valueStringBuilder.Append(format.AsSpan(lengthTerm, length - lengthTerm));
				lengthTerm = length;
				continue;
			}
			int indexOfAny = FindIndexOfAny(format, FormatDelimiters, braceIndex, braceIndex2);
			valueStringBuilder.Append(format.AsSpan(lengthTerm, braceIndex - lengthTerm + 1));
			valueStringBuilder.Append(_valueNames.Count.ToString());
			_valueNames.Add(format.Substring(braceIndex + 1, indexOfAny - braceIndex - 1));
			valueStringBuilder.Append(format.AsSpan(indexOfAny, braceIndex2 - indexOfAny + 1));
			lengthTerm = braceIndex2 + 1;
		}
		_format = valueStringBuilder.ToString();
	}

	private static int FindBraceIndex(string format, char brace, int startIndex, int endIndex)
	{
		int result = endIndex;
		int i = startIndex;
		int ordinal = 0;
		for (; i < endIndex; i++)
		{
			if (ordinal > 0 && format[i] != brace)
			{
				if (ordinal % 2 != 0)
				{
					break;
				}
				ordinal = 0;
				result = endIndex;
			}
			else
			{
				if (format[i] != brace)
				{
					continue;
				}
				if (brace == '}')
				{
					if (ordinal == 0)
					{
						result = i;
					}
				}
				else
				{
					result = i;
				}
				ordinal++;
			}
		}
		return result;
	}

	private static int FindIndexOfAny(string format, char[] chars, int startIndex, int endIndex)
	{
		int indexOfAny = format.IndexOfAny(chars, startIndex, endIndex - startIndex);
		if (indexOfAny != -1)
		{
			return indexOfAny;
		}
		return endIndex;
	}

	public string Format(object[] values)
	{
		object[] objectValues = values;
		if (values != null)
		{
			for (int i = 0; i < values.Length; i++)
			{
				object argument = FormatArgument(values[i]);
				if (argument != values[i])
				{
					objectValues = new object[values.Length];
					Array.Copy(values, objectValues, i);
					objectValues[i++] = argument;
					for (; i < values.Length; i++)
					{
						objectValues[i] = FormatArgument(values[i]);
					}
					break;
				}
			}
		}
		return string.Format(CultureInfo.InvariantCulture, _format, objectValues ?? Array.Empty<object>());
	}

	internal string FormatWithOverwrite(object[] values)
	{
		if (values != null)
		{
			for (int i = 0; i < values.Length; i++)
			{
				values[i] = FormatArgument(values[i]);
			}
		}
		return string.Format(CultureInfo.InvariantCulture, _format, values ?? Array.Empty<object>());
	}

	internal string Format()
	{
		return _format;
	}

	internal string Format(object localValue)
	{
		return string.Format(CultureInfo.InvariantCulture, _format, FormatArgument(localValue));
	}

	internal string Format(object localValue, object localValue2)
	{
		return string.Format(CultureInfo.InvariantCulture, _format, FormatArgument(localValue), FormatArgument(localValue2));
	}

	internal string Format(object localValue, object localValue2, object localValue3)
	{
		return string.Format(CultureInfo.InvariantCulture, _format, FormatArgument(localValue), FormatArgument(localValue2), FormatArgument(localValue3));
	}

	public KeyValuePair<string, object> GetValue(object[] values, int index)
	{
		if (index < 0 || index > _valueNames.Count)
		{
			throw new IndexOutOfRangeException("index");
		}
		if (_valueNames.Count > index)
		{
			return new KeyValuePair<string, object>(_valueNames[index], values[index]);
		}
		return new KeyValuePair<string, object>("{OriginalFormat}", OriginalFormat);
	}

	public IEnumerable<KeyValuePair<string, object>> GetValues(object[] values)
	{
		KeyValuePair<string, object>[] objectForStringArray = new KeyValuePair<string, object>[values.Length + 1];
		for (int i = 0; i != _valueNames.Count; i++)
		{
			objectForStringArray[i] = new KeyValuePair<string, object>(_valueNames[i], values[i]);
		}
		objectForStringArray[^1] = new KeyValuePair<string, object>("{OriginalFormat}", OriginalFormat);
		return objectForStringArray;
	}

	private object FormatArgument(object localValue)
	{
		if (localValue == null)
		{
			return "(null)";
		}
		if (localValue is string)
		{
			return localValue;
		}
		if (localValue is IEnumerable enumerable)
		{
			Span<char> initialBuffer = stackalloc char[256];
			ValueStringBuilder valueStringBuilder = new ValueStringBuilder(initialBuffer);
			bool condition = true;
			foreach (object enumerable2 in enumerable)
			{
				if (!condition)
				{
					valueStringBuilder.Append(", ");
				}
				valueStringBuilder.Append((enumerable2 != null) ? enumerable2.ToString() : "(null)");
				condition = false;
			}
			return valueStringBuilder.ToString();
		}
		return localValue;
	}
}
