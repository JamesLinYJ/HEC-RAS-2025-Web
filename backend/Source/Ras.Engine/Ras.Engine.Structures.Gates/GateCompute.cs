using System;

namespace Ras.Engine.Structures.Gates;

public static class GateCompute
{
	private class GateVar
	{
		private double _depthUS;

		private double _depthDS;

		private double _gateRatio;

		private double _qGateType;

		private double _qGateSluice;

		private double _qGateOrifice;

		private double _qWeir;

		private bool _isSluiceTypeFlow;

		private bool _isOrificeTypeFlow;

		private bool _isGateTypeFlow;

		private bool _isWeirTypeFlow;

		public GateVar ComputeGateHydComp(GatePhysProps gate, GateState gateState)
		{
			if (_isWeirTypeFlow)
			{
				ComputeGateHydWeir(gate, gateState);
			}
			if (_isGateTypeFlow)
			{
				ComputeGateHydGate(gate, gateState);
			}
			if (_isWeirTypeFlow && _isGateTypeFlow)
			{
				gateState.Qtotal = Interpolate(1.0, 1.25, _gateRatio, _qWeir, _qGateType);
			}
			else if (_isWeirTypeFlow)
			{
				gateState.Qtotal = _qWeir;
			}
			else
			{
				gateState.Qtotal = _qGateType;
			}
			return this;
		}

		public GateVar ComputeGateHydGate(GatePhysProps gate, GateState gateState)
		{
			if (_isSluiceTypeFlow)
			{
				ComputeGateHydSluice(gate, gateState);
			}
			if (_isOrificeTypeFlow)
			{
				ComputeGateHydOrifice(gate, gateState);
			}
			if (_isGateTypeFlow)
			{
				_qGateType = Interpolate(0.67, 0.8, gateState.Submergence, _qGateSluice, _qGateOrifice);
			}
			else if (_isSluiceTypeFlow)
			{
				_qGateType = _qGateSluice;
			}
			else if (_isOrificeTypeFlow)
			{
				_qGateType = _qGateOrifice;
			}
			return this;
		}

		public GateVar ComputeGateHydOrifice(GatePhysProps gate, GateState gateState)
		{
			double energyGradeDrop = gateState.EGUS - gateState.EGDS;
			double openingArea = gate.Span * gateState.GateOpen;
			if (energyGradeDrop > gate.SmallPositiveNumber)
			{
				_qGateOrifice = gate.CoefOrifice * openingArea * Math.Pow(2.0 * gate.Grav * energyGradeDrop, 0.5);
			}
			return this;
		}

		public GateVar ComputeGateHydSluice(GatePhysProps gate, GateState gateState)
		{
			double depthUS = _depthUS;
			double headExponentPower = 1.0;
			if (gate.IsHeadRefCenter)
			{
				depthUS = _depthUS - 0.5 * gateState.GateOpen;
			}
			else if (gateState.Submergence < 0.67)
			{
				depthUS = _depthUS;
			}
			else
			{
				depthUS = gateState.EGUS - gateState.WSEDS;
				double energyGradeDrop = gateState.EGUS - gateState.EGDS;
				if (energyGradeDrop < 0.1)
				{
					double y = depthUS;
					depthUS = Interpolate(0.1, 0.0, energyGradeDrop, y, energyGradeDrop);
				}
				headExponentPower = Math.Pow(3.0, gate.ExpHead);
				if (gate.Shape == GateType.Sluice)
				{
					_qGateSluice = headExponentPower * Math.Pow(2.0 * gate.Grav, 0.5) * gate.CoefGate * gate.Span * gateState.GateOpen * Math.Pow(depthUS, gate.ExpHead);
				}
				else if (gate.Shape == GateType.Radial)
				{
					_qGateSluice = headExponentPower * Math.Pow(2.0 * gate.Grav, 0.5) * gate.CoefGate * gate.Span * Math.Pow(gate.TrunnionHeight, gate.ExpTrunnion) * Math.Pow(gateState.GateOpen, gate.ExpOpen) * Math.Pow(depthUS, gate.ExpHead);
				}
			}
			return this;
		}

		public GateVar ComputeGateHydWeir(GatePhysProps gate, GateState gateState)
		{
			ComputeGateWeirSubmergenceCoef(gate, gateState);
			_qWeir = gate.CoefWeir * gateState.CoefWeirSubmergence * gateState.CoefOgeeHead * gate.Span * Math.Pow(_depthUS, 1.5);
			return this;
		}

		public GateVar ComputeGateWeirSubmergenceCoef(GatePhysProps gate, GateState gateState)
		{
			gateState.CoefWeirSubmergence = 1.0;
			gateState.CoefOgeeHead = 1.0;
			double submergence = gateState.Submergence;
			double submergenceRatio = (gateState.EGDS - gate.Invert) / (gateState.EGUS - gate.Invert);
			double y = submergenceRatio;
			if (submergenceRatio > 0.95)
			{
				double y2 = submergence;
				submergence = Interpolate(0.95, 1.0, submergenceRatio, y2, y);
				if (submergenceRatio > 0.97)
				{
					double y3 = submergence;
					submergence = Interpolate(0.97, 1.0, submergenceRatio, y3, y);
				}
				if (submergenceRatio > 0.99)
				{
					double y4 = submergence;
					submergence = Interpolate(0.99, 1.0, submergenceRatio, y4, y);
				}
			}
			if (gate.WeirShape == GateWeirType.BroadCrested)
			{
				if (gateState.Submergence < 0.76)
				{
					return this;
				}
				double[] breakpoints = new double[10] { 0.76, 0.84, 0.88, 0.9, 0.92, 0.94, 0.96, 0.98, 0.995, 1.0 };
				double[] coefficients = new double[10] { 1.0, 0.985, 0.96, 0.93, 0.88, 0.82, 0.72, 0.575, 0.35, 0.0 };
				int intervalIndex = 0;
				for (int i = 0; i < breakpoints.Length - 1; i++)
				{
					if (submergence < breakpoints[i])
					{
						intervalIndex = i;
						break;
					}
				}
				double interpolationFraction = (submergence - breakpoints[intervalIndex - 1]) / (breakpoints[intervalIndex] - breakpoints[intervalIndex - 1]);
				gateState.CoefWeirSubmergence = coefficients[intervalIndex - 1] + interpolationFraction * (coefficients[intervalIndex] - coefficients[intervalIndex - 1]);
			}
			if (gate.WeirShape == GateWeirType.Ogee)
			{
				double[] breakpointsArray = new double[8] { 0.0, 0.052, 0.086, 0.23, 0.34, 0.495, 0.705, 0.85 };
				double[] coefficientsArray = new double[8] { 100.0, 60.0, 40.0, 10.0, 6.0, 3.0, 1.0, 0.0 };
				double waterSurfaceDropOverDepthUS = (gateState.WSEUS - gateState.WSEDS) / _depthUS;
				if (submergenceRatio > 0.95)
				{
					waterSurfaceDropOverDepthUS = 1.0 - submergence;
				}
				waterSurfaceDropOverDepthUS = Math.Max(waterSurfaceDropOverDepthUS, gate.SmallPositiveNumber);
				int intervalIndex2 = 0;
				for (int j = 0; j < breakpointsArray.Length - 1; j++)
				{
					if (waterSurfaceDropOverDepthUS < breakpointsArray[j])
					{
						intervalIndex2 = j;
						break;
					}
				}
				waterSurfaceDropOverDepthUS = Math.Min(waterSurfaceDropOverDepthUS, 0.85);
				double interpolationFraction2 = (waterSurfaceDropOverDepthUS - breakpointsArray[intervalIndex2 - 1]) / (breakpointsArray[intervalIndex2] - breakpointsArray[intervalIndex2 - 1]);
				double interpolatedValue = coefficientsArray[intervalIndex2 - 1] - interpolationFraction2 * (coefficientsArray[intervalIndex2 - 1] - coefficientsArray[intervalIndex2]);
				gateState.CoefWeirSubmergence = 0.01 * (100.0 - interpolatedValue);
				if (gate.IsDesignHead)
				{
					double designHeadRatio = _depthUS / gate.DesignHead;
					if (designHeadRatio > 1.6)
					{
						gateState.CoefOgeeHead = 1.0;
					}
					else
					{
						double[] breakpoints2 = new double[8] { 0.0, 0.04, 0.16, 0.31, 0.61, 1.0, 1.35, 1.6 };
						double[] coefficients2 = new double[8] { 0.785, 0.8, 0.84, 0.88, 0.94, 1.0, 1.04, 1.07 };
						intervalIndex2 = 0;
						for (int k = 0; k < breakpoints2.Length - 1; k++)
						{
							if (designHeadRatio < breakpoints2[k])
							{
								intervalIndex2 = k;
								break;
							}
						}
						intervalIndex2 = Math.Max(intervalIndex2, 1);
						gateState.CoefOgeeHead = Interpolate(breakpoints2[intervalIndex2 - 1], breakpoints2[intervalIndex2], designHeadRatio, coefficients2[intervalIndex2 - 1], coefficients2[intervalIndex2]);
					}
				}
			}
			if (gate.WeirShape == GateWeirType.SharpCrested && gateState.WSEDS > gate.Invert)
			{
				gateState.CoefWeirSubmergence = Math.Pow(1.0 - Math.Pow(submergence, 1.5), 0.385);
			}
			return this;
		}

		public GateVar ComputeGateHydBeg(GatePhysProps gate, GateState gateState)
		{
			_isSluiceTypeFlow = false;
			_isOrificeTypeFlow = false;
			_isGateTypeFlow = false;
			_isWeirTypeFlow = false;
			_depthUS = gateState.EGUS - gate.Invert;
			_depthDS = Math.Max(gateState.WSEDS - gate.Invert, 0.0);
			gateState.Submergence = _depthDS / _depthUS;
			if (_depthUS < 1.25 * gateState.GateOpen)
			{
				_isWeirTypeFlow = true;
			}
			if (_depthUS > gateState.GateOpen && gateState.Submergence < 0.8)
			{
				_isSluiceTypeFlow = true;
				_isGateTypeFlow = true;
			}
			if (_depthUS > gateState.GateOpen && gateState.Submergence > 0.67)
			{
				_isOrificeTypeFlow = true;
				_isGateTypeFlow = true;
			}
			_gateRatio = _depthUS / gateState.GateOpen;
			return this;
		}
	}

	public static void ComputeGateHyd(GatePhysProps gate, GateState gateState)
	{
		GateVar gateVar = new GateVar();
		gateVar.ComputeGateHydBeg(gate, gateState);
		gateVar.ComputeGateHydComp(gate, gateState);
	}

	public static double Interpolate(double x1, double x2, double stx, double y1, double y2)
	{
		double result = (y1 + y2) / 2.0;
		if (x1 != x2)
		{
			result = (x2 - stx) * (y2 - y1) / (x1 - x2) + y2;
		}
		return result;
	}

	public static bool IsNear(double doubleValue, double doubleValue2, double acceptableDifference)
	{
		return Math.Abs(doubleValue - doubleValue2) <= acceptableDifference;
	}
}
