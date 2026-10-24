using System;

namespace Utility.Attributes;

public class IdentifierAttribute : Attribute
{
	public string Identifier { get; }

	public IdentifierAttribute(string identifier)
	{
		Identifier = identifier;
	}
}
