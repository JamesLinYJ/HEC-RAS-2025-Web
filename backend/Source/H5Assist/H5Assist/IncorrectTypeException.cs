using System;
using System.Runtime.CompilerServices;

namespace H5Assist;

public class IncorrectTypeException : Exception
{
	public IncorrectTypeException(string objectName, Type typeExpected, [CallerMemberName] string callerName = "")
		: base("ERROR: Incorrect Type in " + objectName + "." + callerName + " (Expected " + typeExpected.Name + ")")
	{
	}
}
