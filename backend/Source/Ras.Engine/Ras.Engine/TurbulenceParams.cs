using System;

namespace Ras.Engine;

public struct TurbulenceParams
{
	public bool Enabled = true;

	public TurbulenceModel Model = TurbulenceModel.Smagorinsky;

	public double BaseEddyViscosity = 0.0;

	public double LongMixCoeff = 0.0;

	public double TransMixCoeff = 0.0;

	public double HorizMixCoeff = 0.0;

	public bool UseCrossDiffusion = false;

	public double CMu = 0.09;

	public double C1Eps = 1.44;

	public double C2Eps = 1.92;

	public double SigmaK = 1.0;

	public double SigmaEps = 1.3;

	public const double Kappa = 0.41;

	public double AlphaLength = 41.0 / 600.0;

	public double CD = 0.16431676725154984;

	public bool UseExplicitTransport = false;

	public int OuterIterations = 2;

	public double MinTurbulentEnergy = 1E-10;

	public double MinDissipation = 1E-12;

	public double MaxLengthScaleRatio = 1.0;

	public double MaxEddyViscosity = 10.0;

	public bool IsActive
	{
		get
		{
			if (Enabled)
			{
				if (Model == TurbulenceModel.Smagorinsky && BaseEddyViscosity == 0.0 && LongMixCoeff == 0.0 && TransMixCoeff == 0.0)
				{
					return HorizMixCoeff != 0.0;
				}
				return true;
			}
			return false;
		}
	}

	public double BedProductionK => 1.0 / AlphaLength;

	public double BedProductionEps => C2Eps * Math.Sqrt(CMu) / (AlphaLength * AlphaLength);

	public TurbulenceParams()
	{
	}

	public double EquilibriumK(double uStar)
	{
		return uStar * uStar / Math.Sqrt(CMu);
	}

	public double EquilibriumEps(double uStar, double depth)
	{
		return uStar * uStar * uStar / (AlphaLength * depth);
	}
}
