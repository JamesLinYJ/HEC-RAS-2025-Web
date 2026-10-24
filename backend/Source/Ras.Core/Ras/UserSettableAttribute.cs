using System;

namespace Ras;

[AttributeUsage(AttributeTargets.Property)]
public class UserSettableAttribute : Attribute
{
	public bool CanSet { get; set; }

	public UserSettableAttribute(bool canSet)
	{
		CanSet = canSet;
	}
}
