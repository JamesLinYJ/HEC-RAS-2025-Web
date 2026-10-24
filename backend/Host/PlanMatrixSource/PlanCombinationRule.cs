// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
using System;

namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public sealed class PlanCombinationRule
{
	private readonly Func<PlanSettingsView, bool> _isViolated;

	public string Name { get; }

	public string Reason { get; }

	public PlanCombinationRule(string name, string reason, Func<PlanSettingsView, bool> isViolated)
	{
		Name = name;
		Reason = reason;
		_isViolated = isViolated;
	}

	public bool IsViolatedBy(PlanSettingsView settings)
	{
		return _isViolated(settings);
	}
}
