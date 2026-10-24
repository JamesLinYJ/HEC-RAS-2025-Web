namespace Ras.Synthetics.SyntheticParams;

public class ColoradoApportionmentParams : CrossSectionApportionmentParams
{
	public static readonly float[] ColoradoStations = new float[23]
	{
		7.519852f, 9.977234f, 14.032539f, 16.98206f, 20.000526f, 22.996452f, 26.00363f, 28.973335f, 32.00711f, 34.995525f,
		38.00273f, 40.99117f, 43.991047f, 46.991066f, 49.998066f, 52.95288f, 55.99789f, 58.986523f, 61.98996f, 64.96681f,
		67.993744f, 70.977936f, 73.58107f
	};

	public static readonly float[] ColoradoBedElevations = new float[23]
	{
		3.597178f, 1.200242f, 1.355066f, 1.294446f, 1.203319f, 1.099629f, 0.995939f, 0.901533f, 0.904603f, 0.797819f,
		0.703413f, 0.602817f, 0.561017f, 0.56254f, 0.404697f, 0.342783f, 0.341211f, 0.300958f, 0.200363f, -0.00235f,
		0.203409f, 1.102335f, 3.598023f
	};

	public ColoradoApportionmentParams()
		: base(ColoradoStations, ColoradoBedElevations)
	{
	}

	public new static ColoradoApportionmentParams Default()
	{
		return new ColoradoApportionmentParams
		{
			CellsTall = 22,
			CellsWide = 30,
			CellSize = 3.0,
			Slope = 0.0005,
			NValue = 0.03f,
			UpstreamFlow = 50.0,
			DownstreamStage = 1.5,
			RampSeconds = 500.0,
			SolveDt = 1.0,
			SolveDuration = 3000.0
		};
	}
}
