using Utility.ORM;

namespace Ras.Hydraulics.HydraulicDesign;

public class WilcockMethod
{
	public class SBTM
	{
		[Metadata("Bed Shear Velocity")]
		public double ShearVelocity { get; set; } = 0.2;

		[Metadata("Water Density")]
		public double WaterDensity { get; set; } = 1000.0;

		[Metadata("Sediment Density")]
		public double SedimentDensity { get; set; } = 2650.0;

		[DatasetName("Grain Diameter")]
		public double[] GrainDiameter { get; set; } = new double[18]
		{
			64.0, 45.25, 32.0, 22.63, 16.0, 11.31, 8.0, 5.66, 4.0, 2.83,
			2.0, 1.4, 1.0, 0.71, 0.5, 0.35, 0.25, 0.18
		};

		[DatasetName("Surface CDF Percent Finer")]
		public double[] SurfaceCdf { get; set; } = new double[18]
		{
			100.0, 77.31, 57.01, 37.17, 22.36, 16.15, 13.41, 11.42, 10.5, 9.87,
			9.48, 8.76, 7.59, 5.41, 3.09, 1.47, 0.47, 0.0
		};
	}

	public class ISBTM
	{
		[Metadata("Total Transport Rate")]
		public double TotalTransportRate { get; set; } = 0.000668665639507229;

		[DatasetName("Grain Diameter")]
		public double[] GrainDiameter { get; set; } = new double[18]
		{
			64.0, 45.25, 32.0, 22.63, 16.0, 11.31, 8.0, 5.66, 4.0, 2.83,
			2.0, 1.4, 1.0, 0.71, 0.5, 0.35, 0.25, 0.18
		};

		[DatasetName("Transport CDF Percent Finer")]
		public double[] TransportCdf { get; set; } = new double[18]
		{
			100.0, 89.56, 74.04, 53.96, 36.44, 28.37, 24.55, 21.64, 20.23, 19.22,
			18.57, 17.31, 15.18, 11.05, 6.49, 3.18, 1.04, 0.0
		};
	}

	public class STAB
	{
		[DatasetName("Water Discharges")]
		public double[] WaterDischarges { get; set; } = new double[2] { 80.0, 65.0 };

		[DatasetName("Sediment Supply Rates")]
		public double[] SedimentSupplyRates { get; set; } = new double[2] { 0.008, 0.005 };

		[DatasetName("Transport Grain Sizes")]
		public double[][] TransportGrainSizes { get; set; } = new double[2][]
		{
			new double[18]
			{
				100.0, 89.56, 74.04, 53.96, 36.44, 28.37, 24.55, 21.64, 20.23, 19.22,
				18.57, 17.31, 15.18, 11.05, 6.49, 3.18, 1.04, 0.0
			},
			new double[18]
			{
				100.0, 89.56, 74.04, 53.96, 36.44, 28.37, 24.55, 21.64, 20.23, 19.22,
				18.57, 17.31, 15.18, 11.05, 6.49, 3.18, 1.04, 0.0
			}
		};

		[Metadata("Minimum Bottom Width")]
		public double BMin { get; set; } = 5.0;

		[Metadata("Maximum Bottom Width")]
		public double BMax { get; set; } = 40.0;

		[Metadata("Side Slope Z")]
		public double Z { get; set; } = 3.0;

		[Metadata("Side Slope Roughness")]
		public double Ns { get; set; } = 0.045;

		[Metadata("Specify n")]
		public double Nspec { get; set; } = 0.033;

		[Metadata("Use Threshold Slope")]
		public bool UseThresholdSlope { get; set; }

		[Metadata("Threshold Slope Tau Star C")]
		public double TcTauStarC { get; set; } = 0.03;

		[Metadata("Threshold Slope D50")]
		public double TcD50 { get; set; } = 54.0;

		[Metadata("Threshold Slope D84")]
		public double TcD84 { get; set; } = 99.0;

		[Metadata("Threshold Slope M")]
		public double TcM { get; set; } = 1.0;

		[DatasetName("Grain Diameter")]
		public double[] GrainDiameter { get; set; } = new double[18]
		{
			64.0, 45.25, 32.0, 22.63, 16.0, 11.31, 8.0, 5.66, 4.0, 2.83,
			2.0, 1.4, 1.0, 0.71, 0.5, 0.35, 0.25, 0.18
		};
	}

	public class STATE
	{
		[DatasetName("Grain Diameter")]
		public double[] GrainDiameter { get; set; } = new double[19]
		{
			128.0, 90.0, 64.0, 45.25, 32.0, 22.63, 16.0, 11.31, 8.0, 5.66,
			4.0, 2.83, 2.0, 1.4, 1.0, 0.71, 0.5, 0.35, 0.25
		};

		[DatasetName("FIRST Transport CDF Percent Finer")]
		public double[] FirstTransportCDFGrainSize { get; set; } = new double[17]
		{
			100.0, 100.0, 100.0, 96.22, 90.93, 81.05, 70.88, 64.38, 58.44, 42.0,
			28.0, 18.0, 12.0, 8.0, 4.0, 2.0, 0.0
		};

		[DatasetName("SECOND 2 Transport CDF Percent Finer")]
		public double[] SecondTransportCDFGrainSize { get; set; } = new double[19]
		{
			100.0, 100.0, 100.0, 96.22, 90.93, 81.05, 70.88, 64.38, 58.44, 48.0,
			34.0, 30.0, 27.0, 23.0, 18.0, 12.0, 8.0, 3.0, 0.0
		};

		[DatasetName("STATE qT")]
		public double[] TransportRates { get; set; } = new double[22]
		{
			5E-09, 1E-08, 2E-08, 5E-08, 1E-07, 2E-07, 5E-07, 1E-06, 2E-06, 5E-06,
			1E-05, 2E-05, 5E-05, 0.0001, 0.0002, 0.0005, 0.001, 0.002, 0.005, 0.01,
			0.02, 0.05
		};

		[DatasetName("STATE S")]
		public double[] Slopes { get; set; } = new double[10] { 0.0001, 0.0002, 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1 };
	}

	public SBTM Sbtm { get; set; } = new SBTM();

	public ISBTM Isbtm { get; set; } = new ISBTM();

	public STAB Stab { get; set; } = new STAB();

	public STATE State { get; set; } = new STATE();
}
