using System;

namespace Utility.Filtering;

internal readonly struct FilterToken
{
	private readonly string _source;

	private readonly int _start;

	private readonly int _length;

	private readonly string _override;

	public FilterTokenKind Kind { get; }

	public double Number { get; }

	public int Position { get; }

	public ReadOnlySpan<char> Text
	{
		get
		{
			if (_override == null)
			{
				return _source.AsSpan(_start, _length);
			}
			return _override;
		}
	}

	public static FilterToken Slice(FilterTokenKind kind, string source, int start, int length, int position = -1, double number = 0.0)
	{
		return new FilterToken(kind, source, start, length, (position < 0) ? start : position, number, null);
	}

	public static FilterToken Materialized(FilterTokenKind kind, string stringValue, int position)
	{
		return new FilterToken(kind, null, 0, 0, position, 0.0, stringValue);
	}

	private FilterToken(FilterTokenKind kind, string source, int start, int length, int position, double number, string textOverride)
	{
		Kind = kind;
		_source = source;
		_start = start;
		_length = length;
		Position = position;
		Number = number;
		_override = textOverride;
	}
}
