using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Utility.Logging;

public sealed class LogTemplate
{
	private readonly struct Token(string literal, string name, string format, int alignment)
	{
		public readonly string Literal = literal;

		public readonly string Name = name;

		public readonly string Format = format;

		public readonly int Alignment = alignment;
	}

	private const string NullValue = "(null)";

	private const string ErrorValue = "(tostring-failed)";

	private const int MaxCachedTemplates = 4096;

	private static readonly ConcurrentDictionary<string, LogTemplate> _cache = new ConcurrentDictionary<string, LogTemplate>(StringComparer.Ordinal);

	private static int _cacheCount;

	private readonly Token[] _tokens;

	public int HoleCount { get; }

	public string Text { get; }

	public static LogTemplate Empty { get; } = new LogTemplate(string.Empty);

	public LogTemplate(string template)
	{
		Text = template ?? string.Empty;
		_tokens = Parse(Text, out var holeCount);
		HoleCount = holeCount;
	}

	public static LogTemplate Get(string template)
	{
		if (string.IsNullOrEmpty(template))
		{
			return Empty;
		}
		if (_cache.TryGetValue(template, out var localValue))
		{
			return localValue;
		}
		LogTemplate logTemplate = new LogTemplate(template);
		if (Volatile.Read(in _cacheCount) < 4096 && _cache.TryAdd(template, logTemplate))
		{
			Interlocked.Increment(ref _cacheCount);
		}
		return logTemplate;
	}

	public void Render(ref ValueStringBuilder builder, IReadOnlyList<KeyValuePair<string, object>> values, int valueCount)
	{
		int position = 0;
		for (int i = 0; i < _tokens.Length; i++)
		{
			ref Token reference = ref _tokens[i];
			builder.Append(reference.Literal);
			if (reference.Name != null)
			{
				object resolveValue = ResolveValue(values, valueCount, reference.Name, ref position);
				AppendValue(ref builder, resolveValue, reference.Format, reference.Alignment);
			}
		}
	}

	private static object ResolveValue(IReadOnlyList<KeyValuePair<string, object>> values, int count, string name, ref int position)
	{
		if (position < count && values[position].Key == name)
		{
			return values[position++].Value;
		}
		for (int i = 0; i < count; i++)
		{
			if (!(values[i].Key != name))
			{
				position = i + 1;
				return values[i].Value;
			}
		}
		if (position < count)
		{
			return values[position++].Value;
		}
		position++;
		return null;
	}

	public void RenderLiteral(ref ValueStringBuilder builder)
	{
		builder.Append(Text);
	}

	private static Token[] Parse(string format, out int holeCount)
	{
		List<Token> tokens = new List<Token>();
		StringBuilder stringBuilder = new StringBuilder();
		holeCount = 0;
		int intervalIndex = 0;
		while (intervalIndex < format.Length)
		{
			char c = format[intervalIndex];
			if ((c == '{' || c == '}') && intervalIndex + 1 < format.Length && format[intervalIndex + 1] == c)
			{
				stringBuilder.Append(c);
				intervalIndex += 2;
				continue;
			}
			if (c != '{')
			{
				stringBuilder.Append(c);
				intervalIndex++;
				continue;
			}
			int indexOf = format.IndexOf('}', intervalIndex + 1);
			if (indexOf < 0)
			{
				stringBuilder.Append(format, intervalIndex, format.Length - intervalIndex);
				break;
			}
			if (!TryParseHole(format, intervalIndex + 1, indexOf, out var name, out var holeFormat, out var alignment))
			{
				stringBuilder.Append(format, intervalIndex, indexOf - intervalIndex + 1);
				intervalIndex = indexOf + 1;
				continue;
			}
			tokens.Add(new Token(stringBuilder.ToString(), name, holeFormat, alignment));
			stringBuilder.Clear();
			holeCount++;
			intervalIndex = indexOf + 1;
		}
		if (stringBuilder.Length > 0)
		{
			tokens.Add(new Token(stringBuilder.ToString(), null, null, 0));
		}
		return tokens.ToArray();
	}

	private static bool TryParseHole(string format, int start, int end, out string name, out string holeFormat, out int alignment)
	{
		name = null;
		holeFormat = null;
		alignment = 0;
		if (end <= start)
		{
			return false;
		}
		int end2 = end;
		int indexOf = format.IndexOf(':', start, end - start);
		int indexOf2 = format.IndexOf(',', start, end - start);
		if (indexOf2 >= 0 && (indexOf < 0 || indexOf2 < indexOf))
		{
			end2 = indexOf2;
			int indexOf3 = ((indexOf >= 0) ? indexOf : end);
			if (!int.TryParse(format.AsSpan(indexOf2 + 1, indexOf3 - indexOf2 - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out alignment))
			{
				return false;
			}
		}
		else if (indexOf >= 0)
		{
			end2 = indexOf;
		}
		if (indexOf >= 0 && indexOf + 1 < end)
		{
			holeFormat = format.Substring(indexOf + 1, end - indexOf - 1);
		}
		if (end2 > start && (format[start] == '@' || format[start] == '$'))
		{
			start++;
		}
		if (end2 <= start || !IsValidName(format, start, end2))
		{
			return false;
		}
		name = format.Substring(start, end2 - start);
		return true;
	}

	private static bool IsValidName(string format, int start, int end)
	{
		for (int i = start; i < end; i++)
		{
			char c = format[i];
			if (!char.IsLetterOrDigit(c) && c != '_')
			{
				return false;
			}
		}
		return true;
	}

	private static void AppendValue(ref ValueStringBuilder builder, object localValue, string format, int alignment)
	{
		int length = builder.Length;
		AppendRaw(ref builder, localValue, format);
		if (alignment == 0)
		{
			return;
		}
		int lengthDifference = builder.Length - length;
		int alignmentTerm = Math.Abs(alignment) - lengthDifference;
		if (alignmentTerm > 0)
		{
			if (alignment > 0)
			{
				builder.Insert(length, ' ', alignmentTerm);
			}
			else
			{
				builder.Append(' ', alignmentTerm);
			}
		}
	}

	private static void AppendRaw(ref ValueStringBuilder builder, object localValue, string format)
	{
		if (localValue == null)
		{
			builder.Append("(null)");
			return;
		}
		if (localValue is string s)
		{
			builder.Append(s);
			return;
		}
		try
		{
			if (localValue is ISpanFormattable iSpanFormattable)
			{
				AppendSpanFormattable(ref builder, iSpanFormattable, format);
				return;
			}
			if (localValue is IEnumerable enumerable)
			{
				bool condition = true;
				{
					foreach (object enumerable2 in enumerable)
					{
						if (!condition)
						{
							builder.Append(", ");
						}
						AppendRaw(ref builder, enumerable2, format);
						condition = false;
					}
					return;
				}
			}
			if (format != null && localValue is IFormattable formattable)
			{
				builder.Append(formattable.ToString(format, CultureInfo.InvariantCulture));
			}
			else
			{
				builder.Append(localValue.ToString());
			}
		}
		catch
		{
			builder.Append("(tostring-failed)");
		}
	}

	private static void AppendSpanFormattable(ref ValueStringBuilder builder, ISpanFormattable iSpanFormattable, string format)
	{
		ReadOnlySpan<char> format2 = format;
		int charsWritten;
		while (true)
		{
			Span<char> destination = builder.RawChars.Slice(builder.Length);
			if (iSpanFormattable.TryFormat(destination, out charsWritten, format2, CultureInfo.InvariantCulture))
			{
				break;
			}
			builder.EnsureCapacity(Math.Max(builder.Capacity * 2, builder.Length + 64));
		}
		builder.Length += charsWritten;
	}
}
