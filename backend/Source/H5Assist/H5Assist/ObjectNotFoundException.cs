using System;
using System.Runtime.CompilerServices;

namespace H5Assist;

public class ObjectNotFoundException : Exception
{
	public ObjectNotFoundException(string objectName, [CallerMemberName] string callerName = "")
		: base("ERROR: " + objectName + " not found in " + callerName)
	{
	}
}
