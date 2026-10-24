using System;
using System.Collections.Generic;

namespace Geospatial.IO.ORM;

[AttributeUsage(AttributeTargets.Property)]
public class WrappedColumnNameAttribute : Attribute
{
	private static readonly char[] CommaSeparator = new char[1] { ',' };

	private static readonly char[] EqualsSeparator = new char[1] { '=' };

	public int Count => ColumnNames.Count;

	public List<string> ColumnNames { get; }

	public List<string> ChildPropertiesOrFields { get; }

	public bool Optional { get; set; }

	public WrappedColumnNameAttribute(string wrapSyntax)
	{
		string[] parts = wrapSyntax.Split(CommaSeparator, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0)
		{
			throw new Exception("wrapSyntax improperly formatted.");
		}
		ColumnNames = new List<string>();
		ChildPropertiesOrFields = new List<string>();
		for (int i = 0; i < parts.Length; i++)
		{
			string[] partsArray = parts[i].Split(EqualsSeparator, StringSplitOptions.RemoveEmptyEntries);
			if (partsArray.Length != 2)
			{
				throw new Exception("wrapSyntax improperly formatted.");
			}
			string part = partsArray[0];
			if (part.StartsWith("$"))
			{
				part = part.Substring(1);
			}
			string part2 = partsArray[1];
			ColumnNames.Add(part2);
			ChildPropertiesOrFields.Add(part);
		}
	}
}
