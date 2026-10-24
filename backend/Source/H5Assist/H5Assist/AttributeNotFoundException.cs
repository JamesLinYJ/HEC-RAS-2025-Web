using System;
using System.Runtime.CompilerServices;

namespace H5Assist;

public class AttributeNotFoundException : Exception
{
	public AttributeNotFoundException(string objectName, string attributeName, [CallerMemberName] string callerName = "")
		: base("ERROR: Attribute '" + attributeName + "' not found on group/dataset '" + objectName + "' in " + callerName)
	{
	}
}
