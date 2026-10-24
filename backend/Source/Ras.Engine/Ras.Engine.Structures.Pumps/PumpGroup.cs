namespace Ras.Engine.Structures.Pumps;

public class PumpGroup
{
	public double[] HeadCurve { get; set; }

	public double[] FlowCurve { get; set; }

	public Pump[] Pumps { get; set; }
}
