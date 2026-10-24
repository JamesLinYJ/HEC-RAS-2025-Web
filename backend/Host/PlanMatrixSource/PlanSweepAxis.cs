// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
using System.Collections.Generic;
using System.Linq;

namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public sealed class PlanSweepAxis
{
	public PlanSweepProperty Property { get; }

	public IReadOnlyList<object> Values { get; }

	public bool IsPinned => Values.Count == 1;

	public PlanSweepAxis(PlanSweepProperty property, IEnumerable<object> values)
	{
		Property = property;
		Values = values.ToList();
	}
}
