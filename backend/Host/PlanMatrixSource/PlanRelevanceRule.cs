// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
using System;

namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public sealed class PlanRelevanceRule
{
	private readonly Func<PlanSettingsView, bool> _isRelevant;

	public string PropertyName { get; }

	public string Reason { get; }

	public PlanRelevanceRule(string propertyName, string reason, Func<PlanSettingsView, bool> isRelevant)
	{
		PropertyName = propertyName;
		Reason = reason;
		_isRelevant = isRelevant;
	}

	public bool IsRelevant(PlanSettingsView settings)
	{
		return _isRelevant(settings);
	}
}
