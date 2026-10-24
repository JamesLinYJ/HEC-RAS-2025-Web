using System;
using Geospatial.Vectors.Meshes;
using Utility.Matrices;

namespace Ras.Engine;

public class TransportControl
{
	public const double DefaultMinimumValue = -1E+20;

	public const double DefaultMaximumValue = 1E+20;

	public const double DefaultRangeClampTolerance = 1E-12;

	public double[] ConstituentMinimumValues;

	public double[] ConstituentMaximumValues;

	public double[] ConstituentRangeClampTolerances;

	public double ThetaImplicit = 1.0;

	public double ImplicitRelaxationFactor = 1.0;

	public AdvectionScheme advectionScheme;

	public FluxLimiter fluxLimiter = FluxLimiter.VanLeer;

	public TVDGradientRatioMethod TVDGradientRatioMethod;

	public HydraulicMeshComputer.MultidimensionalSlopeLimiter multidimensionalSlopeLimiter = HydraulicMeshComputer.MultidimensionalSlopeLimiter.BarthJespersen;

	public FaceNormalGradientMethod faceNormalGradientMethod;

	public SourceTimeIntegration sourceTimeIntegration;

	public bool DeriveVolumeChangeFromFlows = true;

	public double DerivedVolumeClampDepth = 0.001;

	public double MaxCourant = 5.0;

	public double ExplicitStabilitySafety = 0.9;

	public bool ExplicitTrapezoidalSink = true;

	public int MaxSubStepsPerStep = 1000;

	public SolverControl.LoopControl OuterControl = new SolverControl.LoopControl
	{
		IsInner = false,
		IterMax = 4,
		tolMax = 1E-06,
		tolConv = 1E-08,
		tolRMS = 1E-09
	};

	public bool UsePartialGrid;

	public bool FailOnOuterNonConvergence;

	public SolverKind MatrixSolverPrimary;

	public bool ReuseFactorizationAcrossConstituents = true;

	public int numCores = 1;

	public TransportControl()
		: this(1)
	{
	}

	public TransportControl(int constituentCount)
	{
		if (constituentCount <= 0)
		{
			throw new ArgumentOutOfRangeException("constituentCount");
		}
		ConstituentMinimumValues = new double[constituentCount];
		ConstituentMaximumValues = new double[constituentCount];
		ConstituentRangeClampTolerances = new double[constituentCount];
		Array.Fill(ConstituentMinimumValues, -1E+20);
		Array.Fill(ConstituentMaximumValues, 1E+20);
		Array.Fill(ConstituentRangeClampTolerances, 1E-12);
	}

	public void SetConstituentRange(int constituent, double minimum, double maximum)
	{
		if (constituent < 0)
		{
			throw new ArgumentOutOfRangeException("constituent");
		}
		EnsureConstituentRanges(constituent + 1);
		ConstituentMinimumValues[constituent] = minimum;
		ConstituentMaximumValues[constituent] = maximum;
	}

	internal void EnsureConstituentRanges(int constituentCount)
	{
		ArgumentNullException.ThrowIfNull(ConstituentMinimumValues, "ConstituentMinimumValues");
		ArgumentNullException.ThrowIfNull(ConstituentMaximumValues, "ConstituentMaximumValues");
		ArgumentNullException.ThrowIfNull(ConstituentRangeClampTolerances, "ConstituentRangeClampTolerances");
		if (ConstituentMinimumValues.Length < constituentCount)
		{
			int constituentMinimumValueLength = ConstituentMinimumValues.Length;
			Array.Resize(ref ConstituentMinimumValues, constituentCount);
			Array.Fill(ConstituentMinimumValues, -1E+20, constituentMinimumValueLength, constituentCount - constituentMinimumValueLength);
		}
		if (ConstituentMaximumValues.Length < constituentCount)
		{
			int constituentMaximumValueLength = ConstituentMaximumValues.Length;
			Array.Resize(ref ConstituentMaximumValues, constituentCount);
			Array.Fill(ConstituentMaximumValues, 1E+20, constituentMaximumValueLength, constituentCount - constituentMaximumValueLength);
		}
		if (ConstituentRangeClampTolerances.Length < constituentCount)
		{
			int constituentRangeClampToleranceLength = ConstituentRangeClampTolerances.Length;
			Array.Resize(ref ConstituentRangeClampTolerances, constituentCount);
			Array.Fill(ConstituentRangeClampTolerances, 1E-12, constituentRangeClampToleranceLength, constituentCount - constituentRangeClampToleranceLength);
		}
	}

	public static TransportControl Create(AdvectionScheme scheme, FluxLimiter limiter, double theta = 1.0)
	{
		return new TransportControl
		{
			advectionScheme = scheme,
			fluxLimiter = limiter,
			ThetaImplicit = theta
		};
	}
}
