namespace Ras.Mapping;

public static class MapTypeDefaults
{
	private static readonly double[] DepthDefaultValues = new double[2] { 0.0, 15.0 };

	private static readonly double[] WSELDefaultValues = new double[2] { 0.0, 100.0 };

	private static readonly double[] FlowDefaultValues = new double[2] { 0.0, 10000.0 };

	private static readonly double[] DepthMMCDefaultValues = new double[4] { 0.0, 2.0, 6.0, 15.0 };

	private static readonly double[] VelocityDefaultValues = new double[7] { 0.0, 2.0, 4.0, 6.0, 8.0, 10.0, 15.0 };

	private static readonly double[] DVDefaultValues = new double[2] { 0.0, 10.0 };

	private static readonly double[] DVSquaredDefaultValues = new double[2] { 0.0, 30.0 };

	private static readonly double[] CourantDefaultValues = new double[9] { 0.0, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 };

	private static readonly double[] ArrivalTimeDefaultValues = new double[6] { 0.0, 1.0, 2.0, 4.0, 6.0, 8.0 };

	private static readonly double[] DurationDefaultValues = new double[6] { 0.0, 1.0, 3.0, 6.0, 12.0, 24.0 };

	private static readonly double[] ShearDefaultValues = new double[6] { 0.0, 0.02, 0.04, 0.06, 0.08, 0.1 };

	private static readonly double[] StreamPowerDefaultValues = new double[6] { 0.0, 2.0, 5.0, 10.0, 25.0, 50.0 };

	private static readonly double[] IceDefaultValues = new double[2] { 0.0, 10.0 };

	private static readonly double[] DepthForecastDefaultValues = new double[4] { 0.0, 2.0, 6.0, 15.0 };

	private static readonly double[] ManningsDefaultValues = new double[7] { 0.02, 0.03, 0.04, 0.06, 0.08, 0.1, 0.2 };
}
