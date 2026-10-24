namespace Ras.Engine.Structures.Pumps;

public static class PumpCompute
{
	public static void Init(PumpGroup pumpGroup)
	{
		pumpGroup.HeadCurve = new double[2];
		pumpGroup.FlowCurve = new double[2];
		pumpGroup.HeadCurve[0] = 0.0;
		pumpGroup.HeadCurve[1] = 10.0;
		pumpGroup.FlowCurve[0] = 0.0;
		pumpGroup.FlowCurve[1] = 10.0;
		pumpGroup.Pumps = new Pump[1];
	}
}
