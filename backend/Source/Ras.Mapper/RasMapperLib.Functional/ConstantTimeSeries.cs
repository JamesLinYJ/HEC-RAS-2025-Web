using System;
using System.Runtime.CompilerServices;

namespace RasMapperLib.Functional;

public class ConstantTimeSeries : FunctionalTimeSeries
{
	public ConstantTimeSeries(double constVal)
		: this(constVal, "", "")
	{
	}

	public ConstantTimeSeries(double constVal, string variableName, string units)
		: base([SpecialName] (DateTime dt) => constVal, variableName, units)
	{
	}
}
