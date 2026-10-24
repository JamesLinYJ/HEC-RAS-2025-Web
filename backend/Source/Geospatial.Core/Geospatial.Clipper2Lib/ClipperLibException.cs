using System;

namespace Geospatial.Clipper2Lib;

public class ClipperLibException : Exception
{
	public ClipperLibException(string description)
		: base(description)
	{
	}
}
