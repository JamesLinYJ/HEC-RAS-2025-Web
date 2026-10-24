using System.Diagnostics;
using Utility.Extensions;

namespace Ras.Mapping.ArrivalTime;

[DebuggerDisplay("{ToString}")]
public struct Streak(int startIdx, int stopIdx, int type)
{
	public int StartIndex = startIdx;

	public int StopIndex = stopIdx;

	public int StreakType = type;

	public override string ToString()
	{
		return StartIndex + "->" + StopIndex + ", " + StreakType.ToString().InParentheses();
	}
}
