namespace Visual.Mapping.Observables;

public readonly struct SymbologyPropertyLabel(string propertyName, string displayName)
{
	public const string PenColorLabel = "Pen Color";

	public const string PenWidthLabel = "Line Width";

	public const string FillColorLabel = "Fill Color";

	public const string PointSizeLabel = "Point Size";

	public string PropertyName { get; } = propertyName;

	public string DisplayName { get; } = displayName;

	public override string ToString()
	{
		return DisplayName;
	}
}
