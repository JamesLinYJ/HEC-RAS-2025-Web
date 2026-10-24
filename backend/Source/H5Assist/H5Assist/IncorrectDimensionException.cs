using System;
using System.Runtime.CompilerServices;

namespace H5Assist;

public class IncorrectDimensionException : Exception
{
	public IncorrectDimensionException(string objectName, int dimensionsExpected, [CallerMemberName] string callerName = "")
		: base("ERROR: Incorrect Dimensions in " + objectName + "." + callerName + " (Expected " + dimensionsExpected + ")")
	{
	}
}
