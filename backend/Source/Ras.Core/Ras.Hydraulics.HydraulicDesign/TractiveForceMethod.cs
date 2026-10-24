using System;
using System.Collections.Generic;
using System.Linq;
using Ras.Layers;
using Utility.ORM;

namespace Ras.Hydraulics.HydraulicDesign;

public class TractiveForceMethod
{
	private double _viscosity;

	private double _weightWater;

	private double _temperature;

	[ComplexMetadata("Left Bank")]
	public TractiveForceMethodPart LeftBank { get; set; }

	[ComplexMetadata("Right Bank")]
	public TractiveForceMethodPart RightBank { get; set; }

	[ComplexMetadata("Bed")]
	public TractiveForceMethodPart Bed { get; set; }

	[Metadata("Depth")]
	public double Depth { get; set; }

	[Metadata("Discharge")]
	public double Discharge { get; set; }

	[Metadata("Slope")]
	public double Slope { get; set; }

	[Metadata("Left Bank Side Slope")]
	public double LeftBankSideSlope { get; set; }

	[Metadata("Right Bank Side Slope")]
	public double RightBankSideSlope { get; set; }

	[Metadata("Temperature")]
	public double Temperature
	{
		get
		{
			return _temperature;
		}
		set
		{
			_temperature = value;
			UpdateViscosity(value);
			UpdateWaterWeight(value);
		}
	}

	[Metadata("Width")]
	public double Width { get; set; }

	[Metadata("Solution Method")]
	public TractiveForceSolutionMethod SolutionMethod { get; set; }

	public void UpdateViscosity(double temp)
	{
		if (temp >= 40.0 && temp < 50.0)
		{
			_viscosity = 1.66E-05 - (temp - 40.0) / 10.0 * 2.4999999999999998E-06;
		}
		else if (temp >= 50.0 && temp < 60.0)
		{
			_viscosity = 1.41E-05 - (temp - 50.0) / 10.0 * 1.9000000000000004E-06;
		}
		else if (temp >= 60.0 && temp < 70.0)
		{
			_viscosity = 1.22E-05 - (temp - 60.0) / 10.0 * 1.6E-06;
		}
		else if (temp >= 70.0 && temp < 80.0)
		{
			_viscosity = 1.06E-05 - (temp - 70.0) / 10.0 * 1.2999999999999994E-06;
		}
		else if (temp >= 80.0 && temp < 100.0)
		{
			_viscosity = 9.3E-06 - (temp - 80.0) / 20.0 * 1.9100000000000003E-06;
		}
		else if (temp >= 100.0 && temp <= 120.0)
		{
			_viscosity = 7.39E-06 - (temp - 100.0) / 20.0 * 1.3000000000000003E-06;
		}
		else
		{
			_viscosity = double.NaN;
		}
	}

	public void UpdateWaterWeight(double temp)
	{
		if (temp >= 40.0 && temp < 50.0)
		{
			_weightWater = 62.43 - (temp - 40.0) / 10.0 * 0.030000000000001137;
		}
		else if (temp >= 50.0 && temp < 60.0)
		{
			_weightWater = 62.4 - (temp - 50.0) / 10.0 * 0.030000000000001137;
		}
		else if (temp >= 60.0 && temp < 70.0)
		{
			_weightWater = 62.37 - (temp - 60.0) / 10.0 * 0.07000000000000028;
		}
		else if (temp >= 70.0 && temp < 80.0)
		{
			_weightWater = 62.3 - (temp - 70.0) / 10.0 * 0.0799999999999983;
		}
		else if (temp >= 80.0 && temp < 100.0)
		{
			_weightWater = 62.22 - (temp - 80.0) / 20.0 * 0.21999999999999886;
		}
		else if (temp >= 100.0 && temp <= 120.0)
		{
			_weightWater = 62.0 - (temp - 100.0) / 20.0 * 0.28000000000000114;
		}
		else
		{
			_weightWater = double.NaN;
		}
	}

	public void ComputeRoughnessAndRatios(out double nl, out double nb, out double nr, out double ratioL, out double ratioR)
	{
		if (LeftBank.Equation == BankEquation.Manning)
		{
			nl = LeftBank.Roughness;
		}
		else
		{
			nl = 0.039 * Math.Pow(LeftBank.Roughness, 1.0 / 6.0);
		}
		if (Bed.Equation == BankEquation.Manning)
		{
			nb = Bed.Roughness;
		}
		else
		{
			nb = 0.039 * Math.Pow(Bed.Roughness, 1.0 / 6.0);
		}
		if (RightBank.Equation == BankEquation.Manning)
		{
			nr = RightBank.Roughness;
		}
		else
		{
			nr = 0.039 * Math.Pow(RightBank.Roughness, 1.0 / 6.0);
		}
		double leftBankSideSlopeTerm = Math.Atan(1.0 / LeftBankSideSlope);
		double rightBankSideSlopeTerm = Math.Atan(1.0 / RightBankSideSlope);
		ratioL = Math.Cos(leftBankSideSlopeTerm) * Math.Pow(1.0 - Math.Pow(Math.Tan(leftBankSideSlopeTerm), 2.0) / Math.Pow(Math.Tan(LeftBank.AngleOfRepose), 2.0), 0.5);
		ratioR = Math.Cos(rightBankSideSlopeTerm) * Math.Pow(1.0 - Math.Pow(Math.Tan(rightBankSideSlopeTerm), 2.0) / Math.Pow(Math.Tan(RightBank.AngleOfRepose), 2.0), 0.5);
	}

	public void SolveForDepthAndWidth(out double depth, out double width)
	{
		convertValues();
		double ratioLLeftBankTerm = 0.0;
		double bedTerm = 0.0;
		double ratioRRightBankTerm = 0.0;
		double computed = 0.055;
		double computed2 = 0.055;
		double computed3 = 0.055;
		ComputeRoughnessAndRatios(out var nl, out var nb, out var nr, out var ratioL, out var ratioR);
		depth = 0.0;
		width = 0.0;
		int ordinal = 0;
		double computed4;
		double computed5;
		double computed6;
		do
		{
			double findMaxShearBedDepthTerm = 0.0;
			double findMaxShearBankDepthTerm = 0.0;
			double findMaxShearBankDepthTerm2 = 0.0;
			computed4 = computed;
			computed5 = computed2;
			computed6 = computed3;
			if (SolutionMethod == TractiveForceSolutionMethod.UserDefinedMobility)
			{
				computed4 = LeftBank.ShieldsParameter;
				computed5 = Bed.ShieldsParameter;
				computed6 = RightBank.ShieldsParameter;
			}
			if (SolutionMethod == TractiveForceSolutionMethod.Lane)
			{
				ratioLLeftBankTerm = ratioL * LeftBank.D * 0.4 * 12.0;
				bedTerm = Bed.D * 0.4 * 12.0;
				ratioRRightBankTerm = ratioR * RightBank.D * 0.4 * 12.0;
			}
			else
			{
				bedTerm = computed5 * Bed.D * (Bed.SpecificGravity * _weightWater - _weightWater);
				ratioLLeftBankTerm = ratioL * computed4 * LeftBank.D * (LeftBank.SpecificGravity * _weightWater - _weightWater);
				ratioRRightBankTerm = ratioR * computed6 * RightBank.D * (RightBank.SpecificGravity * _weightWater - _weightWater);
			}
			if (depth > 0.0)
			{
				findMaxShearBedDepthTerm = FindMaxShearBed(depth, width, Math.Min(LeftBankSideSlope, RightBankSideSlope));
				findMaxShearBankDepthTerm = FindMaxShearBank(depth, width, LeftBankSideSlope);
				findMaxShearBankDepthTerm2 = FindMaxShearBank(depth, width, RightBankSideSlope);
			}
			else
			{
				findMaxShearBedDepthTerm = 1.0;
				findMaxShearBankDepthTerm = 0.75;
				findMaxShearBankDepthTerm2 = 0.75;
			}
			double weightWaterSlopeTerm = 0.0;
			double weightWaterSlopeTerm2 = 0.0;
			double weightWaterSlopeTerm3 = 0.0;
			weightWaterSlopeTerm = bedTerm / (findMaxShearBedDepthTerm * _weightWater * Slope);
			weightWaterSlopeTerm2 = ratioLLeftBankTerm / (findMaxShearBankDepthTerm * _weightWater * Slope);
			weightWaterSlopeTerm3 = ratioRRightBankTerm / (findMaxShearBankDepthTerm2 * _weightWater * Slope);
			depth = Math.Min(Math.Min(weightWaterSlopeTerm2, weightWaterSlopeTerm3), weightWaterSlopeTerm);
			double depthLeftBankSideSlopeTerm = 0.5 * (depth * depth * LeftBankSideSlope);
			double depthLeftBankSideSlopeTerm2 = Math.Pow(Math.Pow(depth, 2.0) + Math.Pow(depth * LeftBankSideSlope, 2.0), 0.5);
			double x = depthLeftBankSideSlopeTerm / depthLeftBankSideSlopeTerm2;
			double nlSlopeTerm = 1.486 / nl * depthLeftBankSideSlopeTerm * Math.Pow(x, 2.0 / 3.0) * Math.Pow(Slope, 0.5);
			double depthRightBankSideSlopeTerm = 0.5 * depth * depth * RightBankSideSlope;
			double depthRightBankSideSlopeTerm2 = Math.Pow(Math.Pow(depth, 2.0) + Math.Pow(depth * RightBankSideSlope, 2.0), 0.5);
			double x2 = depthRightBankSideSlopeTerm / depthRightBankSideSlopeTerm2;
			double nrX2Term = 1.486 / nr * depthRightBankSideSlopeTerm * Math.Pow(x2, 2.0 / 3.0) * Math.Pow(Slope, 0.5);
			double dischargeTerm = Discharge - nlSlopeTerm - nrX2Term;
			width = dischargeTerm * nb / (1.486 * Math.Pow(depth, 1.6666666666666667) * Math.Pow(Slope, 0.5));
			if (width < 0.0)
			{
				width = 0.0;
			}
			double dStar = Math.Pow((LeftBank.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * LeftBank.D;
			double dStar2 = Math.Pow((Bed.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * Bed.D;
			double dStar3 = Math.Pow((RightBank.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * RightBank.D;
			computed = FindNewShieldsParameter(dStar);
			computed2 = FindNewShieldsParameter(dStar2);
			computed3 = FindNewShieldsParameter(dStar3);
			ordinal++;
			if (SolutionMethod != TractiveForceSolutionMethod.Shields)
			{
				computed = computed4;
				computed2 = computed5;
				computed3 = computed6;
			}
		}
		while (ordinal <= 1 || Math.Abs(computed4 - computed) / computed >= 0.001 || Math.Abs(computed5 - computed2) / computed >= 0.001 || Math.Abs(computed6 - computed3) / computed3 >= 0.001);
		convertValues(convertForCalculations: false);
	}

	public void SolveForDepthAndD(out double depth, out double[] outputD)
	{
		List<double> doubleList = new List<double>();
		convertValues();
		ComputeRoughnessAndRatios(out var nl, out var nb, out var nr, out var ratioL, out var ratioR);
		List<double> doubleListList = new List<double>();
		int ordinal = 0;
		doubleListList.Add(5.0);
		do
		{
			depth = doubleListList[ordinal];
			double depthTimesWidth = depth * Width;
			double nbWidthTerm = 1.486 / nb * depthTimesWidth * Math.Pow(depthTimesWidth / Width, 0.667) * Math.Pow(Slope, 0.5);
			double depthLeftBankSideSlopeTerm = 0.5 * (depth * LeftBankSideSlope + depth);
			double depthLeftBankSideSlopeTerm2 = Math.Pow(Math.Pow(depth, 2.0) + Math.Pow(LeftBankSideSlope * depth, 2.0), 0.5);
			double x = depthLeftBankSideSlopeTerm / depthLeftBankSideSlopeTerm2;
			double nlSlopeTerm = 1.486 / nl * depthLeftBankSideSlopeTerm * Math.Pow(x, 0.667) * Math.Pow(Slope, 0.5);
			double depthRightBankSideSlopeTerm = 0.5 * (depth * RightBankSideSlope + depth);
			double depthRightBankSideSlopeTerm2 = Math.Pow(Math.Pow(depth, 2.0) + Math.Pow(RightBankSideSlope * depth, 2.0), 0.5);
			double x2 = depthRightBankSideSlopeTerm / depthRightBankSideSlopeTerm2;
			double nrX2Term = 1.486 / nr * depthRightBankSideSlopeTerm * Math.Pow(x2, 0.667) * Math.Pow(Slope, 0.5);
			doubleList.Add(nbWidthTerm + nlSlopeTerm + nrX2Term - Discharge);
			if (Math.Abs(doubleList[ordinal]) >= 0.001)
			{
				if (ordinal == 0)
				{
					doubleListList.Add(10.0);
					continue;
				}
				if (doubleListList[ordinal] == doubleListList[ordinal - 1])
				{
					doubleListList.Add(doubleListList[ordinal] + 0.01);
					continue;
				}
				double computed = (doubleList[ordinal] - doubleList[ordinal - 1]) / (doubleListList[ordinal] - doubleListList[ordinal - 1]);
				double computed2 = doubleList[ordinal] - computed * doubleListList[ordinal];
				doubleListList.Add(-1.0 * computed2 / computed);
			}
		}
		while (Math.Abs(doubleList[ordinal++]) >= 0.1);
		CalculateD(ratioL, ratioR, depth, Width, Slope, out outputD);
		convertValues(convertForCalculations: false);
	}

	public void SolveForWidthAndD(out double width, out double[] outputD)
	{
		List<double> doubleList = new List<double>();
		convertValues();
		ComputeRoughnessAndRatios(out var nl, out var nb, out var nr, out var ratioL, out var ratioR);
		List<double> doubleListList = new List<double>();
		int ordinal = 0;
		doubleListList.Add(50.0);
		do
		{
			width = doubleListList[ordinal];
			double depthTimesWidth = Depth * width;
			double width2 = width;
			double x = depthTimesWidth / width2;
			double nbSlopeTerm = 1.486 / nb * depthTimesWidth * Math.Pow(x, 0.667) * Math.Pow(Slope, 0.5);
			double depthLeftBankSideSlopeTerm = 0.5 * (Depth * LeftBankSideSlope + Depth);
			double depthLeftBankSideSlopeTerm2 = Math.Pow(Math.Pow(Depth, 2.0) + Math.Pow(LeftBankSideSlope * Depth, 2.0), 0.5);
			double x2 = depthLeftBankSideSlopeTerm / depthLeftBankSideSlopeTerm2;
			double nlX2Term = 1.486 / nl * depthLeftBankSideSlopeTerm * Math.Pow(x2, 0.667) * Math.Pow(Slope, 0.5);
			double depthRightBankSideSlopeTerm = 0.5 * (Depth * RightBankSideSlope + Depth);
			double depthRightBankSideSlopeTerm2 = Math.Pow(Math.Pow(Depth, 2.0) + Math.Pow(RightBankSideSlope * Depth, 2.0), 0.5);
			double x3 = depthRightBankSideSlopeTerm / depthRightBankSideSlopeTerm2;
			double nrX3Term = 1.486 / nr * depthRightBankSideSlopeTerm * Math.Pow(x3, 0.667) * Math.Pow(Slope, 0.5);
			doubleList.Add(nbSlopeTerm + nlX2Term + nrX3Term - Discharge);
			if (Math.Abs(doubleList[ordinal]) >= 0.001)
			{
				if (ordinal == 0)
				{
					doubleListList.Add(100.0);
					continue;
				}
				double computed = (doubleList[ordinal] - doubleList[ordinal - 1]) / (doubleListList[ordinal] - doubleListList[ordinal - 1]);
				double computed2 = doubleList[ordinal] - computed * doubleListList[ordinal];
				doubleListList.Add(-1.0 * computed2 / computed);
			}
		}
		while (Math.Abs(doubleList[ordinal++]) >= 0.1);
		CalculateD(ratioL, ratioR, Depth, width, Slope, out outputD);
		convertValues(convertForCalculations: false);
	}

	public void SolveForSlopeAndD(out double slope, out double[] outputD)
	{
		new List<double>();
		convertValues();
		ComputeRoughnessAndRatios(out var nl, out var nb, out var nr, out var ratioL, out var ratioR);
		double depthTimesWidth = Depth * Width;
		double width = Width;
		double x = depthTimesWidth / width;
		double nbTerm = 1.486 / nb * depthTimesWidth * Math.Pow(x, 0.667);
		double depthLeftBankSideSlopeTerm = 0.5 * (Depth * LeftBankSideSlope + Depth);
		double depthLeftBankSideSlopeTerm2 = Math.Pow(Math.Pow(Depth, 2.0) + Math.Pow(LeftBankSideSlope * Depth, 2.0), 0.5);
		double x2 = depthLeftBankSideSlopeTerm / depthLeftBankSideSlopeTerm2;
		double nlX2Term = 1.486 / nl * depthLeftBankSideSlopeTerm * Math.Pow(x2, 0.667);
		double depthRightBankSideSlopeTerm = 0.5 * (Depth * RightBankSideSlope + Depth);
		double depthRightBankSideSlopeTerm2 = Math.Pow(Math.Pow(Depth, 2.0) + Math.Pow(RightBankSideSlope * Depth, 2.0), 0.5);
		double x3 = depthRightBankSideSlopeTerm / depthRightBankSideSlopeTerm2;
		double nrX3Term = 1.486 / nr * depthRightBankSideSlopeTerm * Math.Pow(x3, 0.667);
		slope = Math.Pow(Discharge / (nbTerm + nlX2Term + nrX3Term), 2.0);
		CalculateD(ratioL, ratioR, Depth, Width, slope, out outputD);
		convertValues(convertForCalculations: false);
	}

	public void CalculateD(double ratioL, double ratioR, double depth, double width, double slope, out double[] outputD)
	{
		double computed = 0.055;
		double computed2 = 0.055;
		double computed3 = 0.055;
		outputD = new double[3];
		bool condition = false;
		bool condition2 = false;
		bool condition3 = false;
		if (outputD[0] == 0.0 || outputD[0] == double.NaN)
		{
			condition2 = true;
			outputD[0] = 0.1;
		}
		if (outputD[1] == 0.0 || outputD[1] == double.NaN)
		{
			condition = true;
			outputD[1] = 0.1;
		}
		if (outputD[2] == 0.0 || outputD[2] == double.NaN)
		{
			condition3 = true;
			outputD[2] = 0.1;
		}
		computed = 0.055;
		computed2 = 0.055;
		computed3 = 0.055;
		int unity = 0;
		double outputD2;
		double outputD3;
		double outputD4;
		double computed4;
		double computed5;
		double computed6;
		do
		{
			outputD2 = outputD[0];
			outputD3 = outputD[1];
			outputD4 = outputD[2];
			computed4 = computed;
			computed5 = computed2;
			computed6 = computed3;
			if (SolutionMethod == TractiveForceSolutionMethod.UserDefinedMobility)
			{
				computed4 = LeftBank.ShieldsParameter;
				computed5 = Bed.ShieldsParameter;
				computed6 = RightBank.ShieldsParameter;
			}
			if (SolutionMethod != TractiveForceSolutionMethod.Lane)
			{
				_ = LeftBank.SpecificGravity;
				_ = _weightWater;
				_ = _weightWater;
				_ = Bed.SpecificGravity;
				_ = _weightWater;
				_ = _weightWater;
				_ = RightBank.SpecificGravity;
				_ = _weightWater;
				_ = _weightWater;
			}
			double maxShearBed = FindMaxShearBed(depth, width, Math.Min(LeftBankSideSlope, RightBankSideSlope));
			double maxShearBank = FindMaxShearBank(depth, width, LeftBankSideSlope);
			double maxShearBank2 = FindMaxShearBank(depth, width, RightBankSideSlope);
			double depthWidthTerm = depth * width / (width + 2.0 * depth);
			double maxShearBedWeightWaterTerm = maxShearBed * depthWidthTerm * _weightWater * slope;
			double maxShearBankWeightWaterTerm = maxShearBank * depthWidthTerm * _weightWater * slope;
			double maxShearBank2WeightWaterTerm = maxShearBank2 * depthWidthTerm * _weightWater * slope;
			if (SolutionMethod == TractiveForceSolutionMethod.Shields || SolutionMethod == TractiveForceSolutionMethod.UserDefinedMobility)
			{
				if (outputD[0] == double.NaN || condition2)
				{
					outputD[0] = maxShearBankWeightWaterTerm / (ratioL * computed4 * (LeftBank.SpecificGravity * _weightWater - _weightWater));
				}
				if (outputD[1] == double.NaN || condition)
				{
					outputD[1] = maxShearBedWeightWaterTerm / (computed5 * (Bed.SpecificGravity * _weightWater - _weightWater));
				}
				if (outputD[2] == double.NaN || condition3)
				{
					outputD[2] = maxShearBank2WeightWaterTerm / (ratioR * computed6 * (RightBank.SpecificGravity * _weightWater - _weightWater));
				}
			}
			else
			{
				if (outputD[0] == double.NaN || condition2)
				{
					outputD[0] = maxShearBankWeightWaterTerm / (ratioL * 0.4 * 12.0);
				}
				if (outputD[1] == double.NaN || condition)
				{
					outputD[1] = maxShearBedWeightWaterTerm / 4.800000000000001;
				}
				if (outputD[2] == double.NaN || condition3)
				{
					outputD[2] = maxShearBank2WeightWaterTerm / (ratioR * 0.4 * 12.0);
				}
			}
			if (Math.Abs(outputD[0]) < 1E-13)
			{
				outputD[0] = 0.0;
				condition2 = false;
			}
			if (Math.Abs(outputD[1]) < 1E-13)
			{
				outputD[1] = 0.0;
				condition = false;
			}
			if (Math.Abs(outputD[2]) < 1E-13)
			{
				outputD[2] = 0.0;
				condition3 = false;
			}
			double dStar = Math.Pow((LeftBank.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * outputD[0];
			double dStar2 = Math.Pow((Bed.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * outputD[1];
			double dStar3 = Math.Pow((RightBank.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * outputD[2];
			computed = FindNewShieldsParameter(dStar);
			computed2 = FindNewShieldsParameter(dStar2);
			computed3 = FindNewShieldsParameter(dStar3);
			unity++;
			if (unity > 1000)
			{
				computed = (computed4 + computed) / 2.0;
				computed2 = (computed5 + computed2) / 2.0;
				computed3 = (computed6 + computed3) / 2.0;
				unity = 1;
			}
			if (SolutionMethod != TractiveForceSolutionMethod.Shields)
			{
				computed = computed4;
				computed2 = computed5;
				computed3 = computed6;
			}
		}
		while (unity <= 1 || Math.Abs(computed4 - computed) / computed >= 0.001 || Math.Abs(computed5 - computed2) / computed2 >= 0.001 || Math.Abs(computed6 - computed3) / computed3 >= 0.001 || (outputD[0] != 0.0 && outputD2 != 0.0 && Math.Abs(outputD2 - outputD[0]) / outputD[0] >= 0.001) || (outputD[1] != 0.0 && outputD3 != 0.0 && Math.Abs(outputD3 - outputD[1]) / outputD[1] >= 0.001) || (outputD[2] != 0.0 && outputD4 != 0.0 && Math.Abs(outputD4 - outputD[2]) / outputD[2] >= 0.001));
		for (int i = 0; i < 3; i++)
		{
			outputD[i] = 304.8 * outputD[i];
		}
	}

	public void SolveForDepthAndSlope(out double depth, out double slope)
	{
		List<double> doubleList = new List<double>();
		List<double> doubleListList = new List<double>();
		List<double> doubleList2 = new List<double>();
		convertValues();
		double ratioLLeftBankTerm = 0.0;
		double bedTerm = 0.0;
		double ratioRRightBankTerm = 0.0;
		double computed = 0.055;
		double computed2 = 0.055;
		double computed3 = 0.055;
		ComputeRoughnessAndRatios(out var nl, out var nb, out var nr, out var ratioL, out var ratioR);
		doubleList.Add(1.0);
		int unity = -1;
		double computed4;
		double computed5;
		double computed6;
		do
		{
			unity++;
			computed4 = computed;
			computed5 = computed2;
			computed6 = computed3;
			if (SolutionMethod == TractiveForceSolutionMethod.UserDefinedMobility)
			{
				computed4 = LeftBank.ShieldsParameter;
				computed5 = Bed.ShieldsParameter;
				computed6 = RightBank.ShieldsParameter;
			}
			if (SolutionMethod == TractiveForceSolutionMethod.Lane)
			{
				ratioLLeftBankTerm = ratioL * LeftBank.D * 0.4 * 12.0;
				bedTerm = Bed.D * 0.4 * 12.0;
				ratioRRightBankTerm = ratioR * RightBank.D * 0.4 * 12.0;
			}
			else
			{
				ratioLLeftBankTerm = ratioL * computed4 * LeftBank.D * (LeftBank.SpecificGravity * _weightWater - _weightWater);
				bedTerm = computed5 * Bed.D * (Bed.SpecificGravity * _weightWater - _weightWater);
				ratioRRightBankTerm = ratioR * computed6 * RightBank.D * (RightBank.SpecificGravity * _weightWater - _weightWater);
			}
			double findMaxShearBedWidthTerm = 0.0;
			double findMaxShearBankWidthTerm = 0.0;
			double findMaxShearBankWidthTerm2 = 0.0;
			if (doubleList[unity] > 0.0)
			{
				findMaxShearBankWidthTerm = FindMaxShearBank(doubleList[unity], Width, LeftBankSideSlope);
				findMaxShearBedWidthTerm = FindMaxShearBed(doubleList[unity], Width, Math.Min(LeftBankSideSlope, RightBankSideSlope));
				findMaxShearBankWidthTerm2 = FindMaxShearBank(doubleList[unity], Width, RightBankSideSlope);
			}
			else
			{
				findMaxShearBankWidthTerm = 0.75;
				findMaxShearBedWidthTerm = 1.0;
				findMaxShearBankWidthTerm2 = 0.75;
			}
			double leftBankSideSlopeTerm = 0.5 * doubleList[unity] * doubleList[unity] * LeftBankSideSlope;
			double leftBankSideSlopeTerm2 = Math.Pow(Math.Pow(doubleList[unity], 2.0) + Math.Pow(doubleList[unity] * LeftBankSideSlope, 2.0), 0.5);
			double x = leftBankSideSlopeTerm / leftBankSideSlopeTerm2;
			double nlTerm = 1.486 / nl * leftBankSideSlopeTerm * Math.Pow(x, 2.0 / 3.0);
			double rightBankSideSlopeTerm = 0.5 * doubleList[unity] * doubleList[unity] * RightBankSideSlope;
			double rightBankSideSlopeTerm2 = Math.Pow(Math.Pow(doubleList[unity], 2.0) + Math.Pow(doubleList[unity] * RightBankSideSlope, 2.0), 0.5);
			double x2 = rightBankSideSlopeTerm / rightBankSideSlopeTerm2;
			double nrX2Term = 1.486 / nr * rightBankSideSlopeTerm * Math.Pow(x2, 2.0 / 3.0);
			double widthTerm = doubleList[unity] * Width;
			double width = Width;
			double x3 = widthTerm / width;
			double nbX3Term = 1.486 / nb * widthTerm * Math.Pow(x3, 2.0 / 3.0);
			doubleListList.Add(Math.Pow(Discharge / (nlTerm + nrX2Term + nbX3Term), 2.0));
			double weightWaterTerm = ratioLLeftBankTerm / (findMaxShearBankWidthTerm * _weightWater * doubleList[unity]);
			double weightWaterTerm2 = bedTerm / (findMaxShearBedWidthTerm * _weightWater * doubleList[unity]);
			double weightWaterTerm3 = ratioRRightBankTerm / (findMaxShearBankWidthTerm2 * _weightWater * doubleList[unity]);
			doubleList2.Add(Math.Min(Math.Min(weightWaterTerm, weightWaterTerm3), weightWaterTerm2));
			double dStar = Math.Pow((LeftBank.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * LeftBank.D;
			double dStar2 = Math.Pow((Bed.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * Bed.D;
			double dStar3 = Math.Pow((RightBank.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * RightBank.D;
			computed = FindNewShieldsParameter(dStar);
			computed2 = FindNewShieldsParameter(dStar2);
			computed3 = FindNewShieldsParameter(dStar3);
			if (SolutionMethod != TractiveForceSolutionMethod.Shields)
			{
				computed = computed4;
				computed2 = computed5;
				computed3 = computed6;
			}
			if (unity == 0)
			{
				doubleList.Add(doubleList[unity] + 1.0);
				continue;
			}
			if (Math.Log10(doubleListList[unity]) - Math.Log10(doubleListList[unity - 1]) == 0.0)
			{
				break;
			}
			double computed7 = (doubleList2[unity] - doubleListList[unity] - (doubleList2[unity - 1] - doubleListList[unity - 1])) / (Math.Log10(doubleList[unity]) - Math.Log10(doubleList[unity - 1]));
			double computed8 = doubleList2[unity] - doubleListList[unity] - computed7 * Math.Log10(doubleList[unity]);
			if (-1.0 * computed8 / computed7 < -10.0 || -1.0 * computed8 / computed7 > 100.0)
			{
				break;
			}
			doubleList.Add(Math.Pow(10.0, -1.0 * computed8 / computed7));
		}
		while (unity <= 0 || Math.Abs(computed4 - computed) / computed >= 0.001 || Math.Abs(computed5 - computed2) / computed2 >= 0.001 || Math.Abs(computed6 - computed3) / computed3 >= 0.001 || Math.Abs(doubleListList[unity] - doubleList2[unity]) / doubleList2[unity] >= 0.001);
		convertValues(convertForCalculations: false);
		depth = doubleList.Last();
		slope = doubleList2.Last();
	}

	public void SolveForWidthAndSlope(out double width, out double slope)
	{
		List<double> doubleList = new List<double>();
		List<double> doubleListList = new List<double>();
		List<double> doubleList2 = new List<double>();
		convertValues();
		double ratioLLeftBankTerm = 0.0;
		double bedTerm = 0.0;
		double ratioRRightBankTerm = 0.0;
		double computed = 0.055;
		double computed2 = 0.055;
		double computed3 = 0.055;
		ComputeRoughnessAndRatios(out var nl, out var nb, out var nr, out var ratioL, out var ratioR);
		doubleList.Add(10.0);
		int unity = -1;
		double computed4;
		double computed5;
		double computed6;
		do
		{
			unity++;
			computed4 = computed;
			computed5 = computed2;
			computed6 = computed3;
			if (SolutionMethod == TractiveForceSolutionMethod.UserDefinedMobility)
			{
				computed4 = LeftBank.ShieldsParameter;
				computed5 = Bed.ShieldsParameter;
				computed6 = RightBank.ShieldsParameter;
			}
			if (SolutionMethod == TractiveForceSolutionMethod.Lane)
			{
				ratioLLeftBankTerm = ratioL * LeftBank.D * 0.4 * 12.0;
				bedTerm = Bed.D * 0.4 * 12.0;
				ratioRRightBankTerm = ratioR * RightBank.D * 0.4 * 12.0;
			}
			else
			{
				ratioLLeftBankTerm = ratioL * computed4 * LeftBank.D * (LeftBank.SpecificGravity * _weightWater - _weightWater);
				bedTerm = computed5 * Bed.D * (Bed.SpecificGravity * _weightWater - _weightWater);
				ratioRRightBankTerm = ratioR * computed6 * RightBank.D * (RightBank.SpecificGravity * _weightWater - _weightWater);
			}
			double findMaxShearBankDepthTerm = 0.0;
			double findMaxShearBedDepthTerm = 0.0;
			double findMaxShearBankDepthTerm2 = 0.0;
			if (Depth > 0.0)
			{
				findMaxShearBankDepthTerm = FindMaxShearBank(Depth, doubleList[unity], LeftBankSideSlope);
				findMaxShearBedDepthTerm = FindMaxShearBed(Depth, doubleList[unity], Math.Min(LeftBankSideSlope, RightBankSideSlope));
				findMaxShearBankDepthTerm2 = FindMaxShearBank(Depth, doubleList[unity], RightBankSideSlope);
			}
			else
			{
				findMaxShearBankDepthTerm = 0.75;
				findMaxShearBedDepthTerm = 1.0;
				findMaxShearBankDepthTerm2 = 0.75;
			}
			double depthLeftBankSideSlopeTerm = 0.5 * Depth * Depth * LeftBankSideSlope;
			double depthLeftBankSideSlopeTerm2 = Math.Pow(Math.Pow(Depth, 2.0) + Math.Pow(Depth * LeftBankSideSlope, 2.0), 0.5);
			double x = depthLeftBankSideSlopeTerm / depthLeftBankSideSlopeTerm2;
			double nlTerm = 1.486 / nl * depthLeftBankSideSlopeTerm * Math.Pow(x, 2.0 / 3.0);
			double depthRightBankSideSlopeTerm = 0.5 * Depth * Depth * RightBankSideSlope;
			Math.Pow(Math.Pow(Depth, 2.0) + Math.Pow(Depth * RightBankSideSlope, 2.0), 0.5);
			double x2 = depthRightBankSideSlopeTerm / depthLeftBankSideSlopeTerm2;
			double nrX2Term = 1.486 / nr * depthRightBankSideSlopeTerm * Math.Pow(x2, 2.0 / 3.0);
			double depthTerm = Depth * doubleList[unity];
			double computed7 = doubleList[unity];
			double x3 = depthTerm / computed7;
			double nbX3Term = 1.486 / nb * depthTerm * Math.Pow(x3, 2.0 / 3.0);
			doubleListList.Add(Math.Pow(Discharge / (nlTerm + nrX2Term + nbX3Term), 2.0));
			double weightWaterDepthTerm = ratioLLeftBankTerm / (findMaxShearBankDepthTerm * _weightWater * Depth);
			double weightWaterDepthTerm2 = bedTerm / (findMaxShearBedDepthTerm * _weightWater * Depth);
			double weightWaterDepthTerm3 = ratioRRightBankTerm / (findMaxShearBankDepthTerm2 * _weightWater * Depth);
			doubleList2.Add(Math.Min(Math.Min(weightWaterDepthTerm, weightWaterDepthTerm3), weightWaterDepthTerm2));
			double dStar = Math.Pow((LeftBank.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * LeftBank.D;
			double dStar2 = Math.Pow((Bed.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * Bed.D;
			double dStar3 = Math.Pow((RightBank.SpecificGravity - 1.0) * 32.17399978637695 / Math.Pow(_viscosity, 2.0), 0.3333) * RightBank.D;
			computed = FindNewShieldsParameter(dStar);
			computed2 = FindNewShieldsParameter(dStar2);
			computed3 = FindNewShieldsParameter(dStar3);
			if (SolutionMethod != TractiveForceSolutionMethod.Shields)
			{
				computed = computed4;
				computed2 = computed5;
				computed3 = computed6;
			}
			if (unity == 0)
			{
				doubleList.Add(doubleList[unity] + 10.0);
				continue;
			}
			if (Math.Log10(doubleListList[unity]) - Math.Log10(doubleListList[unity - 1]) == 0.0)
			{
				break;
			}
			double computed8 = (doubleList2[unity] - doubleListList[unity] - (doubleList2[unity - 1] - doubleListList[unity - 1])) / (Math.Log10(doubleList[unity]) - Math.Log10(doubleList[unity - 1]));
			double computed9 = doubleList2[unity] - doubleListList[unity] - computed8 * Math.Log10(doubleList[unity]);
			if (-1.0 * computed9 / computed8 < -10.0 || -1.0 * computed9 / computed8 > 100.0)
			{
				break;
			}
			doubleList.Add(Math.Pow(10.0, -1.0 * computed9 / computed8));
		}
		while (unity <= 0 || Math.Abs(computed4 - computed) / computed >= 0.001 || Math.Abs(computed5 - computed2) / computed2 >= 0.001 || Math.Abs(computed6 - computed3) / computed3 >= 0.001 || Math.Abs(doubleListList[unity] - doubleList2[unity]) / doubleList2[unity] >= 0.001);
		convertValues(convertForCalculations: false);
		width = doubleList.Last();
		slope = doubleList2.Last();
	}

	public double FindMaxShearBed(double depth, double width, double sideSlope)
	{
		if (depth == 0.0)
		{
			depth = 0.01;
		}
		double widthOverDepth = width / depth;
		if (sideSlope < 1.0)
		{
			if (widthOverDepth >= 0.0 && widthOverDepth < 1.0)
			{
				return Interp(1.0, 1.0 - widthOverDepth, 0.0, 0.37, 0.05);
			}
			if (widthOverDepth >= 1.0 && widthOverDepth < 2.0)
			{
				return Interp(2.0, 2.0 - widthOverDepth, 1.0, 0.68, 0.37);
			}
			if (widthOverDepth >= 2.0 && widthOverDepth < 3.0)
			{
				return Interp(3.0, 3.0 - widthOverDepth, 2.0, 0.85, 0.68);
			}
			if (widthOverDepth >= 3.0 && widthOverDepth < 4.0)
			{
				return Interp(4.0, 4.0 - widthOverDepth, 3.0, 0.92, 0.85);
			}
			if (widthOverDepth >= 4.0 && widthOverDepth < 10.0)
			{
				return Interp(10.0, 10.0 - widthOverDepth, 4.0, 0.98, 0.92);
			}
			if (widthOverDepth >= 10.0)
			{
				return 0.98;
			}
		}
		if (sideSlope >= 1.0)
		{
			if (widthOverDepth >= 0.0 && widthOverDepth < 0.1)
			{
				return Interp(0.1, 0.1 - widthOverDepth, 0.0, 0.4, 0.0);
			}
			if (widthOverDepth >= 0.1 && widthOverDepth < 0.4)
			{
				return Interp(0.4, 0.4 - widthOverDepth, 0.1, 0.6, 0.4);
			}
			if (widthOverDepth >= 0.4 && widthOverDepth < 1.0)
			{
				return Interp(1.0, 1.0 - widthOverDepth, 0.4, 0.78, 0.6);
			}
			if (widthOverDepth >= 1.0 && widthOverDepth < 2.0)
			{
				return Interp(2.0, 2.0 - widthOverDepth, 1.0, 0.89, 0.78);
			}
			if (widthOverDepth >= 2.0 && widthOverDepth < 3.0)
			{
				return Interp(3.0, 3.0 - widthOverDepth, 2.0, 0.94, 0.89);
			}
			if (widthOverDepth >= 3.0 && widthOverDepth < 4.0)
			{
				return Interp(4.0, 4.0 - widthOverDepth, 3.0, 0.97, 0.94);
			}
			if (widthOverDepth >= 4.0 && widthOverDepth < 10.0)
			{
				return Interp(10.0, 10.0 - widthOverDepth, 4.0, 1.0, 0.97);
			}
			if (widthOverDepth >= 10.0)
			{
				return 1.0;
			}
		}
		throw new Exception("Uncaught error in FindMaxShearBed");
	}

	public double FindMaxShearBank(double depth, double width, double sideSlope)
	{
		if (depth == 0.0)
		{
			depth = 0.01;
		}
		double widthOverDepth = width / depth;
		if (sideSlope < 0.5)
		{
			if (widthOverDepth >= 0.0 && widthOverDepth < 1.0)
			{
				return Interp(1.0, 1.0 - widthOverDepth, 0.0, 0.47, 0.0);
			}
			if (widthOverDepth >= 1.0 && widthOverDepth < 2.0)
			{
				return Interp(2.0, 2.0 - widthOverDepth, 1.0, 0.69, 0.47);
			}
			if (widthOverDepth >= 2.0 && widthOverDepth < 3.0)
			{
				return Interp(3.0, 3.0 - widthOverDepth, 2.0, 0.73, 0.69);
			}
			if (widthOverDepth >= 3.0 && widthOverDepth < 4.0)
			{
				return Interp(4.0, 4.0 - widthOverDepth, 3.0, 0.74, 0.73);
			}
			if (widthOverDepth >= 4.0 && widthOverDepth < 10.0)
			{
				return Interp(10.0, 10.0 - widthOverDepth, 4.0, 0.75, 0.74);
			}
			if (widthOverDepth >= 10.0)
			{
				return 0.75;
			}
		}
		if (sideSlope >= 0.5 && sideSlope < 1.25)
		{
			if (widthOverDepth >= 0.0 && widthOverDepth < 1.0)
			{
				return Interp(1.0, 1.0 - widthOverDepth, 0.0, 0.62, 0.48);
			}
			if (widthOverDepth >= 1.0 && widthOverDepth < 2.0)
			{
				return Interp(2.0, 2.0 - widthOverDepth, 1.0, 0.71, 0.62);
			}
			if (widthOverDepth >= 2.0 && widthOverDepth < 3.0)
			{
				return Interp(3.0, 3.0 - widthOverDepth, 2.0, 0.74, 0.71);
			}
			if (widthOverDepth >= 3.0 && widthOverDepth < 4.0)
			{
				return Interp(4.0, 4.0 - widthOverDepth, 3.0, 0.75, 0.74);
			}
			if (widthOverDepth >= 4.0 && widthOverDepth < 10.0)
			{
				return Interp(10.0, 10.0 - widthOverDepth, 4.0, 0.755, 0.75);
			}
			if (widthOverDepth >= 10.0)
			{
				return 0.755;
			}
		}
		if (sideSlope >= 1.25 && sideSlope < 1.75)
		{
			if (widthOverDepth >= 0.0 && widthOverDepth < 1.0)
			{
				return Interp(1.0, 1.0 - widthOverDepth, 0.0, 0.69, 0.57);
			}
			if (widthOverDepth >= 1.0 && widthOverDepth < 2.0)
			{
				return Interp(2.0, 2.0 - widthOverDepth, 1.0, 0.73, 0.69);
			}
			if (widthOverDepth >= 2.0 && widthOverDepth < 3.0)
			{
				return Interp(3.0, 3.0 - widthOverDepth, 2.0, 0.745, 0.73);
			}
			if (widthOverDepth >= 3.0 && widthOverDepth < 4.0)
			{
				return Interp(4.0, 4.0 - widthOverDepth, 3.0, 0.75, 0.745);
			}
			if (widthOverDepth >= 4.0 && widthOverDepth < 10.0)
			{
				return Interp(10.0, 10.0 - widthOverDepth, 4.0, 0.76, 0.75);
			}
			if (widthOverDepth >= 10.0)
			{
				return 0.76;
			}
		}
		if (sideSlope >= 1.75)
		{
			if (widthOverDepth >= 0.0 && widthOverDepth < 1.0)
			{
				return Interp(1.0, 1.0 - widthOverDepth, 0.0, 0.73, 0.65);
			}
			if (widthOverDepth >= 1.0 && widthOverDepth < 2.0)
			{
				return Interp(2.0, 2.0 - widthOverDepth, 1.0, 0.76, 0.73);
			}
			if (widthOverDepth >= 2.0 && widthOverDepth < 3.0)
			{
				return Interp(3.0, 3.0 - widthOverDepth, 2.0, 0.765, 0.76);
			}
			if (widthOverDepth >= 3.0 && widthOverDepth < 4.0)
			{
				return Interp(4.0, 4.0 - widthOverDepth, 3.0, 0.77, 0.765);
			}
			if (widthOverDepth >= 4.0 && widthOverDepth < 10.0)
			{
				return Interp(10.0, 10.0 - widthOverDepth, 4.0, 0.78, 0.77);
			}
			if (widthOverDepth >= 10.0)
			{
				return 0.78;
			}
		}
		throw new Exception("Uncaught error in FindMaxShearBed");
	}

	public double FindNewShieldsParameter(double dStar)
	{
		if (dStar == 0.0)
		{
			return 0.0;
		}
		if (dStar <= 4.0)
		{
			return 0.24 / dStar;
		}
		if (dStar > 4.0 && dStar <= 10.0)
		{
			return 0.14 * Math.Pow(dStar, -0.64);
		}
		if (dStar > 10.0 && dStar <= 20.0)
		{
			return 0.04 * Math.Pow(dStar, -0.1);
		}
		if (dStar > 20.0 && dStar <= 150.0)
		{
			return 0.013 * Math.Pow(dStar, 0.29);
		}
		return 0.055;
	}

	public double Interp(double upperKnown, double known, double lowerKnown, double upper, double lower, bool isLog = false)
	{
		if (!isLog)
		{
			return lower + (upper - lower) * (known - lowerKnown) / (upperKnown - lowerKnown);
		}
		return Math.Pow(10.0, Math.Log10(lower) + (Math.Log10(upper) - Math.Log10(lower)) * (Math.Log10(known) - Math.Log10(lowerKnown)) / (Math.Log(upperKnown) - Math.Log10(lowerKnown)));
	}

	private void convertValues(bool convertForCalculations = true)
	{
		if (convertForCalculations)
		{
			if (LeftBank.D != double.NaN)
			{
				LeftBank.D *= 0.00328;
			}
			if (Bed.D != double.NaN)
			{
				Bed.D *= 0.00328;
			}
			if (RightBank.D != double.NaN)
			{
				RightBank.D *= 0.00328;
			}
			LeftBank.AngleOfRepose = Math.PI * LeftBank.AngleOfRepose / 180.0;
			Bed.AngleOfRepose = Math.PI * Bed.AngleOfRepose / 180.0;
			RightBank.AngleOfRepose = Math.PI * RightBank.AngleOfRepose / 180.0;
		}
		else
		{
			if (LeftBank.D != double.NaN)
			{
				LeftBank.D /= 0.00328;
			}
			if (Bed.D != double.NaN)
			{
				Bed.D /= 0.00328;
			}
			if (RightBank.D != double.NaN)
			{
				RightBank.D /= 0.00328;
			}
			LeftBank.AngleOfRepose = LeftBank.AngleOfRepose * 180.0 / Math.PI;
			Bed.AngleOfRepose = Bed.AngleOfRepose * 180.0 / Math.PI;
			RightBank.AngleOfRepose = RightBank.AngleOfRepose * 180.0 / Math.PI;
		}
	}
}
