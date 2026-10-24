using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Ras.Engine;
using Utility.Matrices;

namespace Ras.Synthetics.Analysis;

public static class WaveAnalysis
{
	public static void AnalyzePerCellHarmonics(Mesh mesh, IList<MeshTimestep> history, Func<Point, double, double> analyticalStage, double angularFrequency, double spinupSeconds, Action<string, object> log)
	{
		int ordinal = -1;
		for (int i = 0; i < history.Count; i++)
		{
			if (history[i].Time >= spinupSeconds)
			{
				ordinal = i;
				break;
			}
		}
		if (ordinal < 0)
		{
			return;
		}
		int count = history.Count - ordinal;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		double[,] doubleValues2 = new double[count, 3];
		double computed = 0.0;
		double computed2 = 0.0;
		double computed3 = 0.0;
		double computed4 = 0.0;
		for (int j = 0; j < mesh.CellCount; j++)
		{
			Point cellCenter = mesh.CellCenters[j];
			for (int k = 0; k < count; k++)
			{
				int index = ordinal + k;
				double time = history[index].Time;
				doubleValues[k] = history[index].WaterState.CellElevation[j];
				doubleValuesArray[k] = analyticalStage(cellCenter, time - 0.5 * history[index].TimeStep);
				doubleValues2[k, 0] = Math.Cos(angularFrequency * time);
				doubleValues2[k, 1] = Math.Sin(angularFrequency * time);
				doubleValues2[k, 2] = 1.0;
			}
			double[] doubleValues3 = new double[3];
			LeastSquares<double>.Solve(doubleValues2, doubleValues3, doubleValues);
			double computed5 = 0.0;
			for (int l = 0; l < count; l++)
			{
				double computed6 = doubleValues2[l, 0] * doubleValues3[0] + doubleValues2[l, 1] * doubleValues3[1] + doubleValues2[l, 2] * doubleValues3[2] - doubleValues[l];
				computed5 += computed6 * computed6;
			}
			double countTerm = Math.Sqrt(computed5 / (double)count);
			double[] doubleValues4 = new double[3];
			LeastSquares<double>.Solve(doubleValues2, doubleValues4, doubleValuesArray);
			double computed7 = Math.Sqrt(doubleValues3[0] * doubleValues3[0] + doubleValues3[1] * doubleValues3[1]);
			double computed8 = Math.Sqrt(doubleValues4[0] * doubleValues4[0] + doubleValues4[1] * doubleValues4[1]);
			double computed9 = computed7 / computed8 - 1.0;
			double computed10 = Math.Atan2(doubleValues3[1], doubleValues3[0]);
			double computed11 = Math.Atan2(doubleValues4[1], doubleValues4[0]);
			double piTerm = (computed10 - computed11 + Math.PI * 5.0) % (Math.PI * 2.0) - Math.PI;
			double computed12 = doubleValues3[2];
			computed += computed9;
			computed2 += piTerm;
			computed3 += computed12;
			computed4 += countTerm;
		}
		int cellCount = mesh.CellCount;
		log("meanAmpError", computed / (double)cellCount);
		log("meanLagSeconds", computed2 / (double)cellCount / angularFrequency);
		log("meanBias", computed3 / (double)cellCount);
		log("meanNoise", computed4 / (double)cellCount);
	}

	public static void AnalyzeSlidingWindowHarmonics(IList<(double Time, double Stage)> samples, double meanStage, double basePeriod, int harmonicCount, Action<string, object> log)
	{
		double omega0 = Math.PI * 2.0 / basePeriod;
		double[] doubleValues = (from k in Enumerable.Range(1, harmonicCount)
			select (double)k * omega0).ToArray();
		int count = samples.Count / 3;
		double[] doubleValuesArray = new double[count];
		double[,] doubleValues2 = new double[count, 2 * doubleValues.Length];
		double[,] doubleValues3 = new double[2, doubleValues.Length];
		double[,] doubleValues4 = new double[2, doubleValues.Length];
		double[] doubleValues5 = new double[2];
		for (int intervalIndex = 0; intervalIndex < 2; intervalIndex++)
		{
			int countDifference = ((intervalIndex != 0) ? (samples.Count - count) : 0);
			int count2 = ((intervalIndex == 0) ? count : samples.Count);
			for (int intervalIndex2 = 0; intervalIndex2 < count2 - countDifference; intervalIndex2++)
			{
				int index = countDifference + intervalIndex2;
				doubleValuesArray[intervalIndex2] = samples[index].Stage - meanStage;
				double time = samples[index].Time;
				for (int intervalIndex3 = 0; intervalIndex3 < doubleValues.Length; intervalIndex3++)
				{
					doubleValues2[intervalIndex2, 2 * intervalIndex3] = Math.Cos(doubleValues[intervalIndex3] * time);
					doubleValues2[intervalIndex2, 2 * intervalIndex3 + 1] = Math.Sin(doubleValues[intervalIndex3] * time);
				}
			}
			double[] doubleValues6 = new double[2 * doubleValues.Length];
			LeastSquares<double>.Solve(doubleValues2, doubleValues6, doubleValuesArray);
			double computed = 0.0;
			for (int intervalIndex4 = 0; intervalIndex4 < count; intervalIndex4++)
			{
				double computed2 = 0.0;
				for (int intervalIndex5 = 0; intervalIndex5 < doubleValues6.Length; intervalIndex5++)
				{
					computed2 += doubleValues2[intervalIndex4, intervalIndex5] * doubleValues6[intervalIndex5];
				}
				double computed3 = computed2 - doubleValuesArray[intervalIndex4];
				computed += computed3 * computed3;
			}
			doubleValues5[intervalIndex] = Math.Sqrt(computed / (double)count);
			for (int intervalIndex6 = 0; intervalIndex6 < doubleValues.Length; intervalIndex6++)
			{
				doubleValues3[intervalIndex, intervalIndex6] = Math.Sqrt(doubleValues6[2 * intervalIndex6] * doubleValues6[2 * intervalIndex6] + doubleValues6[2 * intervalIndex6 + 1] * doubleValues6[2 * intervalIndex6 + 1]);
				doubleValues4[intervalIndex, intervalIndex6] = Math.Atan2(doubleValues6[2 * intervalIndex6 + 1], doubleValues6[2 * intervalIndex6]);
			}
		}
		double timeDifferenceOverBasePeriod = (samples.Last().Time - samples.First().Time) / basePeriod;
		double computed4 = 2.0 / 3.0 * timeDifferenceOverBasePeriod;
		log("nperiod", timeDifferenceOverBasePeriod);
		log("period", basePeriod);
		log("analysisPeriods", computed4);
		double computed5 = 0.0;
		double computed6 = 0.0;
		for (int intervalIndex7 = 0; intervalIndex7 < doubleValues.Length; intervalIndex7++)
		{
			double piSum = doubleValues4[1, intervalIndex7] - doubleValues4[0, intervalIndex7];
			if (piSum < Math.PI)
			{
				piSum += Math.PI * 2.0;
			}
			if (piSum > Math.PI)
			{
				piSum -= Math.PI * 2.0;
			}
			double piSumTerm = piSum / doubleValues[intervalIndex7] / computed4;
			double basePeriodTerm = basePeriod + piSumTerm;
			log($"harmonic{intervalIndex7 + 1}_period0", basePeriodTerm);
			computed5 += doubleValues3[0, intervalIndex7] * doubleValues3[0, intervalIndex7];
			computed6 += doubleValues3[1, intervalIndex7] * doubleValues3[1, intervalIndex7];
		}
		double computed7 = (computed6 / computed5 - 1.0) / computed4;
		log("relLossPerPeriod", computed7);
		log("rmsNonharmonic0", doubleValues5[0]);
		log("rmsNonharmonic1", doubleValues5[1]);
		log("energy0", computed5);
		log("energy1", computed6);
	}

	public static void AnalyzeAmplitudeDecayAndPhase(IList<(double Time, double Stage)> computed, IList<(double Time, double Stage)> analytical, double meanStage, double basePeriod, Action<string, object> log, out EnvelopeSeries slidingWindow, out EnvelopeSeries zeroCrossing)
	{
		double scaledPiOverBasePeriod = Math.PI * 2.0 / basePeriod;
		slidingWindow = SlidingWindowEnvelope(computed, meanStage, scaledPiOverBasePeriod, basePeriod);
		LogDecay(log, "sw", slidingWindow.Time, slidingWindow.Amplitude, basePeriod);
		double intercept;
		if (slidingWindow.Phase != null && slidingWindow.Phase.Length >= 2)
		{
			BaseAnalysis.FitLine(slidingWindow.Time.ToList(), slidingWindow.Phase.ToList(), out var slope, out intercept);
			double piSlopeTerm = Math.PI * 2.0 / (scaledPiOverBasePeriod + slope);
			log("periodError_sw_s", piSlopeTerm - basePeriod);
		}
		FitFundamental(computed, 0, computed.Count, meanStage, scaledPiOverBasePeriod, out intercept, out var phase);
		FitFundamental(analytical, 0, analytical.Count, meanStage, scaledPiOverBasePeriod, out intercept, out var phase2);
		log("phaseLag_sw_s", Wrap(phase - phase2) / scaledPiOverBasePeriod);
		zeroCrossing = PeakEnvelope(computed, meanStage);
		LogDecay(log, "zc", zeroCrossing.Time, zeroCrossing.Amplitude, basePeriod);
		List<double> upCrossings = UpCrossings(computed, meanStage);
		List<double> upCrossingsList = UpCrossings(analytical, meanStage);
		if (upCrossings.Count >= 2)
		{
			double upCrossingStepOverCountOffset = (upCrossings[upCrossings.Count - 1] - upCrossings[0]) / (double)(upCrossings.Count - 1);
			log("periodError_zc_s", upCrossingStepOverCountOffset - basePeriod);
		}
		if (upCrossings.Count > 0 && upCrossingsList.Count > 0)
		{
			int count = Math.Min(upCrossings.Count, upCrossingsList.Count);
			double wrapToPeriodSum = 0.0;
			for (int i = 0; i < count; i++)
			{
				wrapToPeriodSum += WrapToPeriod(upCrossings[i] - upCrossingsList[i], basePeriod);
			}
			log("phaseLag_zc_s", wrapToPeriodSum / (double)count);
		}
	}

	private static EnvelopeSeries SlidingWindowEnvelope(IList<(double Time, double Stage)> samples, double meanStage, double omega0, double basePeriod)
	{
		double scaledBasePeriod = 2.0 * basePeriod;
		double scaledBasePeriod2 = 0.25 * basePeriod;
		double time = samples[0].Time;
		double time2 = samples[samples.Count - 1].Time;
		List<double> doubleList = new List<double>();
		List<double> doubleListList = new List<double>();
		List<double> doubleList2 = new List<double>();
		double computed = 0.0;
		double phase2 = 0.0;
		bool condition = true;
		int firstAtOrAfter = 0;
		for (double scaledBasePeriodSum = time + 0.5 * scaledBasePeriod; scaledBasePeriodSum <= time2 - 0.5 * scaledBasePeriod; scaledBasePeriodSum += scaledBasePeriod2)
		{
			int firstAtOrAfter2 = FirstAtOrAfter(samples, scaledBasePeriodSum - 0.5 * scaledBasePeriod, firstAtOrAfter);
			firstAtOrAfter = firstAtOrAfter2;
			int firstAtOrAfter3 = FirstAtOrAfter(samples, scaledBasePeriodSum + 0.5 * scaledBasePeriod, firstAtOrAfter2);
			if (firstAtOrAfter3 - firstAtOrAfter2 < 8)
			{
				continue;
			}
			FitFundamental(samples, firstAtOrAfter2, firstAtOrAfter3, meanStage, omega0, out var amp, out var phase);
			double phaseTerm;
			if (condition)
			{
				phaseTerm = phase;
				condition = false;
			}
			else
			{
				double phaseAbovePhase2;
				for (phaseAbovePhase2 = phase - phase2; phaseAbovePhase2 > Math.PI; phaseAbovePhase2 -= Math.PI * 2.0)
				{
				}
				for (; phaseAbovePhase2 < -Math.PI; phaseAbovePhase2 += Math.PI * 2.0)
				{
				}
				phaseTerm = computed + phaseAbovePhase2;
			}
			phase2 = phase;
			computed = phaseTerm;
			doubleList.Add(scaledBasePeriodSum);
			doubleListList.Add(amp);
			doubleList2.Add(phaseTerm);
		}
		return new EnvelopeSeries(doubleList.ToArray(), doubleListList.ToArray(), doubleList2.ToArray());
	}

	private static EnvelopeSeries PeakEnvelope(IList<(double Time, double Stage)> s, double mean)
	{
		List<double> doubleList = new List<double>();
		List<double> doubleListList = new List<double>();
		for (int i = 1; i < s.Count - 1; i++)
		{
			double stage = s[i - 1].Stage;
			double stage2 = s[i].Stage;
			double stage3 = s[i + 1].Stage;
			if (stage2 > stage && stage2 >= stage3 && stage2 > mean)
			{
				double stageStage2Term = stage - 2.0 * stage2 + stage3;
				double timeDifference = s[i + 1].Time - s[i].Time;
				double stageStage3Term = ((stageStage2Term != 0.0) ? (0.5 * (stage - stage3) / stageStage2Term) : 0.0);
				double timeTerm = s[i].Time + stageStage3Term * timeDifference;
				double stage2StageTerm = stage2 - 0.25 * (stage - stage3) * stageStage3Term - mean;
				if (!(stage2StageTerm <= 0.0))
				{
					doubleList.Add(timeTerm);
					doubleListList.Add(stage2StageTerm);
				}
			}
		}
		return new EnvelopeSeries(doubleList.ToArray(), doubleListList.ToArray(), null);
	}

	private static void LogDecay(Action<string, object> log, string suffix, double[] time, double[] amplitude, double basePeriod)
	{
		List<double> doubleList = new List<double>();
		List<double> doubleListList = new List<double>();
		for (int i = 0; i < amplitude.Length; i++)
		{
			if (!(amplitude[i] <= 0.0))
			{
				doubleList.Add(time[i]);
				doubleListList.Add(Math.Log(amplitude[i]));
			}
		}
		if (doubleList.Count >= 2)
		{
			BaseAnalysis.FitLine(doubleList, doubleListList, out var slope, out var intercept);
			double positiveInfinity = ((slope != 0.0) ? (-1.0 / slope) : double.PositiveInfinity);
			log("decay_tau_" + suffix + "_s", positiveInfinity);
			log("decay_ratioPerPeriod_" + suffix, Math.Exp(slope * basePeriod));
			log("decay_fitR2_" + suffix, RSquared(doubleList, doubleListList, slope, intercept));
		}
	}

	private static void FitFundamental(IList<(double Time, double Stage)> samples, int start, int stop, double meanStage, double omega, out double amp, out double phase)
	{
		int count = stop - start;
		double[,] doubleValues = new double[count, 2];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			var (localValue, localValue2) = samples[start + i];
			doubleValuesArray[i] = localValue2 - meanStage;
			doubleValues[i, 0] = Math.Cos(omega * localValue);
			doubleValues[i, 1] = Math.Sin(omega * localValue);
		}
		double[] doubleValues2 = new double[2];
		LeastSquares<double>.Solve(doubleValues, doubleValues2, doubleValuesArray);
		amp = Math.Sqrt(doubleValues2[0] * doubleValues2[0] + doubleValues2[1] * doubleValues2[1]);
		phase = Math.Atan2(doubleValues2[1], doubleValues2[0]);
	}

	private static List<double> UpCrossings(IList<(double Time, double Stage)> s, double mean)
	{
		List<double> doubleList = new List<double>();
		for (int i = 1; i < s.Count; i++)
		{
			double waterSurfaceAboveMean = s[i - 1].Stage - mean;
			double waterSurfaceAboveMean2 = s[i].Stage - mean;
			if (waterSurfaceAboveMean <= 0.0 && waterSurfaceAboveMean2 > 0.0)
			{
				double submergenceRatio = ((waterSurfaceAboveMean == waterSurfaceAboveMean2) ? 0.0 : ((0.0 - waterSurfaceAboveMean) / (waterSurfaceAboveMean2 - waterSurfaceAboveMean)));
				doubleList.Add(s[i - 1].Time + submergenceRatio * (s[i].Time - s[i - 1].Time));
			}
		}
		return doubleList;
	}

	private static int FirstAtOrAfter(IList<(double Time, double Stage)> s, double t, int from)
	{
		int i;
		for (i = from; i < s.Count && s[i].Time < t; i++)
		{
		}
		return i;
	}

	private static double Wrap(double angle)
	{
		double piSum = (angle + Math.PI) % (Math.PI * 2.0);
		if (piSum < 0.0)
		{
			piSum += Math.PI * 2.0;
		}
		return piSum - Math.PI;
	}

	private static double WrapToPeriod(double dt, double period)
	{
		double periodSum = dt % period;
		if (periodSum > 0.5 * period)
		{
			periodSum -= period;
		}
		if (periodSum <= -0.5 * period)
		{
			periodSum += period;
		}
		return periodSum;
	}

	private static double RSquared(List<double> x, List<double> y, double slope, double intercept)
	{
		double average = y.Average();
		double computed = 0.0;
		double computed2 = 0.0;
		for (int i = 0; i < y.Count; i++)
		{
			double slopeInterceptTerm = slope * x[i] + intercept;
			computed += (y[i] - slopeInterceptTerm) * (y[i] - slopeInterceptTerm);
			computed2 += (y[i] - average) * (y[i] - average);
		}
		if (!(computed2 > 0.0))
		{
			return double.NaN;
		}
		return 1.0 - computed / computed2;
	}
}
