using System;
using System.Linq;

namespace Ras.Hydraulics.HydraulicDesign;

public static class WilcockStateCalculator
{
	public static StateResults ComputeState(WilcockMethod.STATE m)
	{
		double computed = 9.81;
		double computed2 = 2.65;
		double[] grainDiameter = m.GrainDiameter;
		double[] firstTransportCDFGrainSize = m.FirstTransportCDFGrainSize;
		double[] secondTransportCDFGrainSize = m.SecondTransportCDFGrainSize;
		double[] transportRates = m.TransportRates;
		double[] slopes = m.Slopes;
		int transportRateLength = transportRates.Length;
		int slopeLength = slopes.Length;
		int index = Array.FindIndex(firstTransportCDFGrainSize, (double v) => v == 0.0);
		index = ((index >= 0) ? (index + 1) : firstTransportCDFGrainSize.Length);
		int plen = index - 1;
		WilcockUtility.CdfToPdf(grainDiameter, firstTransportCDFGrainSize, plen, out var Dpdf, out var Fpdf);
		WilcockUtility.ComputeGsdStats(grainDiameter, firstTransportCDFGrainSize, Dpdf, Fpdf, plen, index, out var Dg, out var Sig, out var D);
		double firstFs = WilcockUtility.ComputeFs(grainDiameter, firstTransportCDFGrainSize, index);
		WilcockUtility.CdfToPdf(grainDiameter, secondTransportCDFGrainSize, plen, out var Dpdf2, out var Fpdf2);
		WilcockUtility.ComputeGsdStats(grainDiameter, secondTransportCDFGrainSize, Dpdf2, Fpdf2, plen, index, out var Dg2, out var Sig2, out var D2);
		double secondFs = WilcockUtility.ComputeFs(grainDiameter, secondTransportCDFGrainSize, index);
		double[] sqT1 = new double[transportRateLength];
		double[] sqT2 = new double[transportRateLength];
		for (int intervalIndex = 0; intervalIndex < transportRateLength; intervalIndex++)
		{
			sqT1[intervalIndex] = transportRates[intervalIndex] / (Math.Sqrt((computed2 - 1.0) * computed) * Math.Pow(Dg / 1000.0, 1.5));
			sqT2[intervalIndex] = sqT1[intervalIndex];
		}
		double[] ustr1 = new double[transportRateLength];
		double[] ustr2 = new double[transportRateLength];
		double[] ds1m = new double[transportRateLength];
		double[] ds150_1 = new double[transportRateLength];
		double[] ds250_2 = new double[transportRateLength];
		double[] ds2m = new double[transportRateLength];
		double[] gamma1 = new double[transportRateLength];
		double[] gamma2 = new double[transportRateLength];
		double[] flowDepth1 = new double[transportRateLength];
		double[] flowDepth2 = new double[transportRateLength];
		double[] roughness1 = new double[transportRateLength];
		double[] roughness2 = new double[transportRateLength];
		double[] flowVelocity1 = new double[transportRateLength];
		double[] flowVelocity2 = new double[transportRateLength];
		double[,] sqW1 = new double[transportRateLength, slopeLength];
		double[,] sqW2 = new double[transportRateLength, slopeLength];
		double slope = slopes[slopeLength - 1];
		ISBTMResults iSBTMResults = null;
		ISBTMResults iSBTMResults2 = null;
		for (int intervalIndex2 = 0; intervalIndex2 < transportRateLength; intervalIndex2++)
		{
			iSBTMResults = WilcockIsbtmCalculator.ComputeIsbtm(new WilcockMethod.ISBTM
			{
				TotalTransportRate = transportRates[intervalIndex2],
				GrainDiameter = m.GrainDiameter,
				TransportCdf = m.FirstTransportCDFGrainSize
			});
			index = Math.Max(index, iSBTMResults.SurfaceCdf.Length);
			ustr1[intervalIndex2] = iSBTMResults.UStar;
			ds1m[intervalIndex2] = iSBTMResults.Dsm;
			gamma1[intervalIndex2] = ds1m[intervalIndex2] / Dg;
			ds150_1[intervalIndex2] = iSBTMResults.Ds50;
			flowDepth1[intervalIndex2] = ustr1[intervalIndex2] * ustr1[intervalIndex2] / (computed * slope);
			roughness1[intervalIndex2] = 2.0 * (ds150_1[intervalIndex2] / 1000.0);
			flowVelocity1[intervalIndex2] = 7.8 * ustr1[intervalIndex2] * Math.Pow(flowDepth1[intervalIndex2] / roughness1[intervalIndex2], 1.0 / 6.0);
			sqW1[intervalIndex2, slopeLength - 1] = flowVelocity1[intervalIndex2] * flowDepth1[intervalIndex2] / (Math.Sqrt((computed2 - 1.0) * computed) * Math.Pow(Dg / 1000.0, 1.5));
			iSBTMResults2 = WilcockIsbtmCalculator.ComputeIsbtm(new WilcockMethod.ISBTM
			{
				TotalTransportRate = transportRates[intervalIndex2] * Math.Pow(Dg2 / Dg, 1.5),
				GrainDiameter = m.GrainDiameter,
				TransportCdf = m.SecondTransportCDFGrainSize
			});
			index = Math.Max(index, iSBTMResults2.SurfaceCdf.Length);
			ustr2[intervalIndex2] = iSBTMResults2.UStar;
			ds2m[intervalIndex2] = iSBTMResults2.Dsm;
			gamma2[intervalIndex2] = ds2m[intervalIndex2] / Dg2;
			ds250_2[intervalIndex2] = iSBTMResults2.Ds50;
			flowDepth2[intervalIndex2] = ustr2[intervalIndex2] * ustr2[intervalIndex2] / (computed * slope);
			roughness2[intervalIndex2] = 2.0 * (ds250_2[intervalIndex2] / 1000.0);
			flowVelocity2[intervalIndex2] = 7.8 * ustr2[intervalIndex2] * Math.Pow(flowDepth2[intervalIndex2] / roughness2[intervalIndex2], 1.0 / 6.0);
			sqW2[intervalIndex2, slopeLength - 1] = flowVelocity2[intervalIndex2] * flowDepth2[intervalIndex2] / (Math.Sqrt((computed2 - 1.0) * computed) * Math.Pow(Dg2 / 1000.0, 1.5));
		}
		double computed3 = 7.8 / (Math.Pow(2.0, 1.0 / 6.0) * Math.Sqrt(computed2 - 1.0) * Math.Pow(computed, 1.6666666666666667) * Math.Pow(Dg / 1000.0, 1.5));
		double computed4 = 7.8 / (Math.Pow(2.0, 1.0 / 6.0) * Math.Sqrt(computed2 - 1.0) * Math.Pow(computed, 1.6666666666666667) * Math.Pow(Dg2 / 1000.0, 1.5));
		for (int intervalIndex3 = 0; intervalIndex3 < slopeLength - 1; intervalIndex3++)
		{
			for (int intervalIndex4 = 0; intervalIndex4 < transportRateLength; intervalIndex4++)
			{
				sqW1[intervalIndex4, intervalIndex3] = computed3 * Math.Pow(ustr1[intervalIndex4], 3.3333333333333335) / (Math.Pow(ds150_1[intervalIndex4] / 1000.0, 1.0 / 6.0) * Math.Pow(slopes[intervalIndex3], 1.1666666666666667));
				sqW2[intervalIndex4, intervalIndex3] = computed4 * Math.Pow(ustr2[intervalIndex4], 3.3333333333333335) / (Math.Pow(ds250_2[intervalIndex4] / 1000.0, 1.0 / 6.0) * Math.Pow(slopes[intervalIndex3], 1.1666666666666667));
			}
		}
		double minSqW = sqW1[0, 0];
		double maxSqW = sqW1[0, 0];
		for (int intervalIndex5 = 0; intervalIndex5 < slopeLength; intervalIndex5++)
		{
			for (int intervalIndex6 = 0; intervalIndex6 < transportRateLength; intervalIndex6++)
			{
				minSqW = Math.Min(minSqW, Math.Min(sqW1[intervalIndex6, intervalIndex5], sqW2[intervalIndex6, intervalIndex5]));
				maxSqW = Math.Max(maxSqW, Math.Max(sqW1[intervalIndex6, intervalIndex5], sqW2[intervalIndex6, intervalIndex5]));
			}
		}
		double[] sqWi = new double[10];
		double[,] sb1 = new double[transportRateLength, 10];
		double[,] sb2 = new double[transportRateLength, 10];
		double ustr12 = ustr1[0];
		double ds150_12 = ds150_1[0];
		for (int intervalIndex7 = 0; intervalIndex7 < transportRateLength; intervalIndex7++)
		{
			if (ustr1[intervalIndex7] < ustr12)
			{
				ustr12 = ustr1[intervalIndex7];
			}
			if (ustr2[intervalIndex7] < ustr12)
			{
				ustr12 = ustr2[intervalIndex7];
			}
			if (ds150_1[intervalIndex7] > ds150_12)
			{
				ds150_12 = ds150_1[intervalIndex7];
			}
			if (ds250_2[intervalIndex7] > ds150_12)
			{
				ds150_12 = ds250_2[intervalIndex7];
			}
		}
		double computed5 = Math.Round(Math.Log10(Math.Min(computed3, computed4) * Math.Pow(ustr12, 3.3333333333333335) / (Math.Pow(ds150_12, 1.0 / 6.0) * Math.Pow(0.03162, 1.1666666666666667)))) + 0.5;
		for (int intervalIndex8 = 0; intervalIndex8 < 10; intervalIndex8++)
		{
			sqWi[intervalIndex8] = Math.Pow(10.0, computed5 + (double)(intervalIndex8 + 1) / 2.0);
		}
		for (int intervalIndex9 = 0; intervalIndex9 < 10; intervalIndex9++)
		{
			for (int intervalIndex10 = 0; intervalIndex10 < transportRateLength; intervalIndex10++)
			{
				sb1[intervalIndex10, intervalIndex9] = Math.Pow(computed3 * Math.Pow(ustr1[intervalIndex10], 3.3333333333333335) / (Math.Pow(ds1m[intervalIndex10] / 1000.0, 1.0 / 6.0) * sqWi[intervalIndex9]), 3510.0 / 4095.0);
				sb2[intervalIndex10, intervalIndex9] = Math.Pow(computed4 * Math.Pow(ustr2[intervalIndex10], 3.3333333333333335) / (Math.Pow(ds2m[intervalIndex10] / 1000.0, 1.0 / 6.0) * sqWi[intervalIndex9]), 3510.0 / 4095.0);
			}
		}
		double[,] qW1 = new double[transportRateLength, slopeLength];
		double[,] qW2 = new double[transportRateLength, slopeLength];
		for (int intervalIndex11 = 0; intervalIndex11 < transportRateLength; intervalIndex11++)
		{
			qW1[intervalIndex11, slopeLength - 1] = flowVelocity1[intervalIndex11] * flowDepth1[intervalIndex11];
			qW2[intervalIndex11, slopeLength - 1] = flowVelocity2[intervalIndex11] * flowDepth2[intervalIndex11];
		}
		computed3 = 7.8 / (Math.Pow(2.0, 1.0 / 6.0) * Math.Pow(9.81, 1.1666666666666667));
		computed4 = 7.8 / (Math.Pow(2.0, 1.0 / 6.0) * Math.Pow(9.81, 1.1666666666666667));
		for (int intervalIndex12 = 0; intervalIndex12 < slopeLength - 1; intervalIndex12++)
		{
			for (int intervalIndex13 = 0; intervalIndex13 < transportRateLength; intervalIndex13++)
			{
				qW1[intervalIndex13, intervalIndex12] = computed3 * Math.Pow(ustr1[intervalIndex13], 3.3333333333333335) / (Math.Pow(ds150_1[intervalIndex13] / 1000.0, 1.0 / 6.0) * Math.Pow(slopes[intervalIndex12], 1.1666666666666667));
				qW2[intervalIndex13, intervalIndex12] = computed4 * Math.Pow(ustr2[intervalIndex13], 3.3333333333333335) / (Math.Pow(ds250_2[intervalIndex13] / 1000.0, 1.0 / 6.0) * Math.Pow(slopes[intervalIndex12], 1.1666666666666667));
			}
		}
		double minQW = qW1[0, 0];
		double maxQW = qW1[0, 0];
		for (int intervalIndex14 = 0; intervalIndex14 < slopeLength; intervalIndex14++)
		{
			for (int intervalIndex15 = 0; intervalIndex15 < transportRateLength; intervalIndex15++)
			{
				minQW = Math.Min(minQW, Math.Min(qW1[intervalIndex15, intervalIndex14], qW2[intervalIndex15, intervalIndex14]));
				maxQW = Math.Max(maxQW, Math.Max(qW1[intervalIndex15, intervalIndex14], qW2[intervalIndex15, intervalIndex14]));
			}
		}
		double[,] sd1 = new double[transportRateLength, 10];
		double[,] sd2 = new double[transportRateLength, 10];
		ustr12 = ustr1.Min();
		ds150_12 = ds150_1[0];
		double computed6 = Math.Round(Math.Log10(((computed3 > computed4) ? computed4 : computed3) * Math.Pow(ustr12, 3.3333333333333335) / (Math.Pow(ds150_12, 1.0 / 6.0) * Math.Pow(0.03162, 1.1666666666666667))), 0);
		for (int intervalIndex16 = 0; intervalIndex16 < 10; intervalIndex16++)
		{
			double computed7 = Math.Pow(10.0, computed6 + (double)(intervalIndex16 + 1) / 2.0);
			for (int intervalIndex17 = 0; intervalIndex17 < transportRateLength; intervalIndex17++)
			{
				sd1[intervalIndex17, intervalIndex16] = Math.Pow(computed3 * Math.Pow(ustr1[intervalIndex17], 3.3333333333333335) / (Math.Pow(ds150_1[intervalIndex17] / 1000.0, 1.0 / 6.0) * computed7), 3510.0 / 4095.0);
				sd2[intervalIndex17, intervalIndex16] = Math.Pow(computed4 * Math.Pow(ustr2[intervalIndex17], 3.3333333333333335) / (Math.Pow(ds250_2[intervalIndex17] / 1000.0, 1.0 / 6.0) * computed7), 3510.0 / 4095.0);
			}
		}
		return new StateResults
		{
			FirstDMin = grainDiameter[index - 1],
			FirstDMax = grainDiameter[0],
			FirstD50 = D,
			FirstDg = Dg,
			FirstSigmaG = Sig,
			FirstFs = firstFs,
			SecondDMin = grainDiameter[index - 1],
			SecondDMax = grainDiameter[0],
			SecondD50 = D2,
			SecondDg = Dg2,
			SecondSigmaG = Sig2,
			SecondFs = secondFs,
			SqT1 = sqT1,
			SqT2 = sqT2,
			Ustr1 = ustr1,
			Ustr2 = ustr2,
			Ds150_1 = ds150_1,
			Ds250_2 = ds250_2,
			Ds1m = ds1m,
			Ds2m = ds2m,
			Gamma1 = gamma1,
			Gamma2 = gamma2,
			FlowDepth1 = flowDepth1,
			FlowDepth2 = flowDepth2,
			Roughness1 = roughness1,
			Roughness2 = roughness2,
			FlowVelocity1 = flowVelocity1,
			FlowVelocity2 = flowVelocity2,
			SqW1 = sqW1,
			SqW2 = sqW2,
			SqWi = sqWi,
			Sb1 = sb1,
			Sb2 = sb2,
			Sd1 = sd1,
			Sd2 = sd2,
			QW1 = qW1,
			QW2 = qW2,
			MinSqW = minSqW,
			MaxSqW = maxSqW,
			MinQW = minQW,
			MaxQW = maxQW
		};
	}
}
