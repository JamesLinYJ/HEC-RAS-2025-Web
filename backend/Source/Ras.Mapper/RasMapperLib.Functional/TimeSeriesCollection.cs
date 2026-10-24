using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Utility.Extensions;

namespace RasMapperLib.Functional;

[ComVisible(false)]
public class TimeSeriesCollection : List<TimeSeries>
{
	public override string ToString()
	{
		return string.Join(",", this.Select([SpecialName] (TimeSeries ts) => ts.Variable)).InParentheses();
	}
}
