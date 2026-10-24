using System;

namespace Geospatial.Rendering.Editing;

public class EditOperationInvalidStateException : Exception
{
	public EditOperationInvalidStateException(string message)
		: base(message)
	{
	}
}
