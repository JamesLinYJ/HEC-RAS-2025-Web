namespace Ras.Hydraulics.HydraulicDesign;

public class StateResults
{
	public double FirstDMin { get; set; }

	public double FirstDMax { get; set; }

	public double FirstDbm { get; set; }

	public double FirstDg { get; set; }

	public double FirstSigmaG { get; set; }

	public double FirstD50 { get; set; }

	public double FirstFs { get; set; }

	public double SecondDMin { get; set; }

	public double SecondDMax { get; set; }

	public double SecondDg { get; set; }

	public double SecondDbm { get; set; }

	public double SecondSigmaG { get; set; }

	public double SecondD50 { get; set; }

	public double SecondFs { get; set; }

	public double[] SqT1 { get; set; }

	public double[] SqT2 { get; set; }

	public double[] Ustr1 { get; set; }

	public double[] Ds1m { get; set; }

	public double[] Ds150_1 { get; set; }

	public double[] Gamma1 { get; set; }

	public double[] FlowDepth1 { get; set; }

	public double[] Roughness1 { get; set; }

	public double[] FlowVelocity1 { get; set; }

	public double[] Ustr2 { get; set; }

	public double[] Ds2m { get; set; }

	public double[] Ds250_2 { get; set; }

	public double[] Gamma2 { get; set; }

	public double[] FlowDepth2 { get; set; }

	public double[] Roughness2 { get; set; }

	public double[] FlowVelocity2 { get; set; }

	public double[,] SqW1 { get; set; }

	public double[,] SqW2 { get; set; }

	public double[,] QW1 { get; set; }

	public double[,] QW2 { get; set; }

	public double[,] Sb1 { get; set; }

	public double[,] Sb2 { get; set; }

	public double[,] Sd1 { get; set; }

	public double[,] Sd2 { get; set; }

	public double[] SqWi { get; set; }

	public double[] QWi { get; set; }

	public double MinSqW { get; set; }

	public double MaxSqW { get; set; }

	public double MinQW { get; set; }

	public double MaxQW { get; set; }

	public double[] TransportRates { get; set; }

	public double[] Slopes { get; set; }
}
