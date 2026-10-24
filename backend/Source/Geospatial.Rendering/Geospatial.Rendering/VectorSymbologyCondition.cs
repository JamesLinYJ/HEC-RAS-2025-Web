namespace Geospatial.Rendering;

public class VectorSymbologyCondition
{
	public string Column { get; set; } = "";

	public ConditionOperator Operator { get; set; }

	public string Threshold { get; set; } = "";

	public string ResultValue { get; set; } = "";
}
