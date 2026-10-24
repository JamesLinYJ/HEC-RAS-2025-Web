using System;
using System.Linq;

namespace Ras.Hydraulics.HydraulicDesign;

public static class WilcockIsbtmCalculator
{
	private static ISBTMResults CreateSentinelISBTMResults(double[] cDi, double[] cPi)
	{
		return new ISBTMResults
		{
			UStar = 1E-12,
			SurfaceCdf = new double[cPi.Length],
			SurfaceDMin = ((cDi.Length != 0) ? cDi[^1] : 1E-12),
			SurfaceDMax = ((cDi.Length != 0) ? cDi[0] : 1E-12),
			Ds50 = 1E-12,
			Dsm = 1E-12,
			SSig = 1E-12,
			SFs = 1E-12,
			BedloadDMin = ((cDi.Length != 0) ? cDi[^1] : 1E-12),
			BedloadDMax = ((cDi.Length != 0) ? cDi[0] : 1E-12),
			Db50 = 1E-12,
			Dbm = 1E-12,
			BSig = 1E-12,
			BFs = 1E-12,
			MaxIterations = 0,
			Iterations = 0,
			Tolerance = 1E-12,
			Dres = 1E-12,
			MaxFres = 1E-12,
			Fsres = 1E-12,
			Qres = 1E-12,
			Ures = 1E-12
		};
	}

	public static ISBTMResults ComputeIsbtm(WilcockMethod.ISBTM m)
	{
		double totalTransportRate = m.TotalTransportRate;
		double[] grainDiameter = m.GrainDiameter;
		double[] transportCdf = m.TransportCdf;
		if (totalTransportRate <= 0.0)
		{
			return CreateSentinelISBTMResults(grainDiameter, transportCdf);
		}
		bool condition = true;
		for (int i = 0; i < transportCdf.Length; i++)
		{
			if (transportCdf[i] > 0.0)
			{
				condition = false;
				break;
			}
		}
		if (condition)
		{
			return CreateSentinelISBTMResults(grainDiameter, transportCdf);
		}
		int index = Array.FindIndex(transportCdf, (double fi) => fi == 0.0);
		index = ((index >= 0) ? (index + 1) : transportCdf.Length);
		int count = index - 1;
		WilcockUtility.CdfToPdf(grainDiameter, transportCdf, count, out var Dpdf, out var Fpdf);
		WilcockUtility.ComputeGsdStats(grainDiameter, transportCdf, Dpdf, Fpdf, count, index, out var Dg, out var Sig, out var D);
		double bFs = WilcockUtility.ComputeFs(grainDiameter, transportCdf, index);
		double[] doubleValues = Dpdf.Select((double d) => Math.Log(d) / Math.Log(2.0)).ToArray();
		double[] doubleValuesArray = grainDiameter.Select((double d) => Math.Log(d) / Math.Log(2.0)).ToArray();
		double min = Dg;
		double max = grainDiameter[0];
		bool grainDiameterIndexTerm = grainDiameter[index - 1] < 2.0;
		double computed = 1.0;
		double dg = Dg;
		double computeFs = (grainDiameterIndexTerm ? bFs : 0.0);
		double[] take = Fpdf.Take(count).ToArray();
		double computed2 = -1.0;
		double computed3 = -1.0;
		double[] doubleValues2 = new double[count];
		double[] doubleValues3 = new double[count];
		if (grainDiameterIndexTerm)
		{
			int indexOffset = index - 1;
			while (indexOffset >= 0 && doubleValuesArray[indexOffset] < 1.0)
			{
				indexOffset--;
			}
			if (indexOffset < 1)
			{
				indexOffset = 1;
			}
			int ordinal = indexOffset - 1;
			computed2 = doubleValuesArray[indexOffset];
			computed3 = doubleValuesArray[ordinal];
			for (int indexOffset2 = indexOffset; indexOffset2 < count; indexOffset2++)
			{
				doubleValues2[indexOffset2] = 1.0;
			}
			for (int ordinal2 = ordinal; ordinal2 < count; ordinal2++)
			{
				doubleValues3[ordinal2] = 1.0;
			}
		}
		int maxIterations = 200;
		double tolerance = 5E-05;
		int iterations = 0;
		double scaledTolerance = 2.0 * tolerance;
		double computed4 = Math.Log(2.0);
		double totalTransportRate2 = totalTransportRate;
		double[] doubleValues4 = new double[3]
		{
			1.0,
			Dg,
			grainDiameterIndexTerm ? bFs : 0.0
		};
		double[] doubleValues5 = new double[doubleValues4.Length];
		double[] doubleValues6 = new double[3];
		double[] doubleValues7 = new double[count];
		double[] doubleValues8 = new double[count];
		double[] doubleValues9 = new double[count];
		double[] doubleValues10 = new double[count];
		double[] takeArray = new double[count];
		double maxFres = -1.0;
		double dres = -1.0;
		double fsres = -1.0;
		double qres = -1.0;
		double ures = -1.0;
		while (scaledTolerance >= tolerance && iterations <= maxIterations)
		{
			computed = doubleValues4[0];
			dg = doubleValues4[1];
			computeFs = (grainDiameterIndexTerm ? doubleValues4[2] : 0.0);
			double computeFsTerm = (grainDiameterIndexTerm ? (0.021 + 0.015 * Math.Exp(-0.2 * computeFs)) : 0.036);
			double dgTerm = computeFsTerm * 1.65 * 1000.0 * 9.81 * (dg / 1000.0);
			for (int intervalIndex = 0; intervalIndex < count; intervalIndex++)
			{
				double dpdfOverDg = Dpdf[intervalIndex] / dg;
				double y = 0.67 / (1.0 + Math.Exp(1.5 - dpdfOverDg));
				doubleValues9[intervalIndex] = dgTerm * Math.Pow(dpdfOverDg, y);
			}
			for (int intervalIndex2 = 0; intervalIndex2 < count; intervalIndex2++)
			{
				doubleValues10[intervalIndex2] = 1000.0 * Math.Pow(computed, 2.0) / doubleValues9[intervalIndex2];
			}
			for (int intervalIndex3 = 0; intervalIndex3 < count; intervalIndex3++)
			{
				doubleValues7[intervalIndex3] = ((doubleValues10[intervalIndex3] < 1.35) ? (0.002 * Math.Pow(doubleValues10[intervalIndex3], 7.5)) : (14.0 * Math.Pow(1.0 - 0.894 / Math.Sqrt(doubleValues10[intervalIndex3]), 4.5)));
			}
			double computed5 = 0.0;
			for (int intervalIndex4 = 0; intervalIndex4 < count; intervalIndex4++)
			{
				doubleValues8[intervalIndex4] = Fpdf[intervalIndex4] / 100.0 / doubleValues7[intervalIndex4];
				computed5 += doubleValues8[intervalIndex4];
			}
			for (int intervalIndex5 = 0; intervalIndex5 < count; intervalIndex5++)
			{
				takeArray[intervalIndex5] = 100.0 * (doubleValues8[intervalIndex5] / computed5);
			}
			double computed6 = Math.Pow(computed, 3.0);
			double totalTransportRate22 = computed6 / (16.1865 * computed5);
			doubleValues6[0] = totalTransportRate * 1.65 * 9.81 - computed6 / computed5;
			WilcockUtility.PdfToCdf(takeArray, index, out var cF);
			WilcockUtility.CdfToPdf(grainDiameter, cF, count, out var Dpdf2, out var Fpdf2);
			WilcockUtility.ComputeGsdStats(grainDiameter, cF, Dpdf2, Fpdf2, count, index, out var Dg2, out var _, out var _);
			doubleValues6[1] = dg - Dg2;
			if (grainDiameterIndexTerm)
			{
				double computeFs2 = WilcockUtility.ComputeFs(grainDiameter, cF, index);
				doubleValues6[2] = computeFs - computeFs2;
			}
			double[,] doubleValues11 = new double[3, 3];
			double[] doubleValues12 = new double[count];
			double[] doubleValues13 = new double[count];
			double[] doubleValues14 = new double[count];
			double[] doubleValues15 = new double[count];
			double[] doubleValues16 = new double[count];
			for (int intervalIndex6 = 0; intervalIndex6 < count; intervalIndex6++)
			{
				doubleValues12[intervalIndex6] = ((doubleValues10[intervalIndex6] < 1.35) ? (0.015 * Math.Pow(doubleValues10[intervalIndex6], 6.5)) : (28.161 / Math.Pow(doubleValues10[intervalIndex6], 1.5) * Math.Pow(1.0 - 0.894 / Math.Sqrt(doubleValues10[intervalIndex6]), 3.5)));
			}
			for (int intervalIndex7 = 0; intervalIndex7 < count; intervalIndex7++)
			{
				doubleValues13[intervalIndex7] = (0.0 - doubleValues8[intervalIndex7] / doubleValues7[intervalIndex7]) * doubleValues12[intervalIndex7];
			}
			double computed7 = 0.0;
			double computed8 = 0.0;
			for (int intervalIndex8 = 0; intervalIndex8 < count; intervalIndex8++)
			{
				doubleValues14[intervalIndex8] = doubleValues13[intervalIndex8] * (2.0 * doubleValues10[intervalIndex8] / computed);
				double dpdfOverDg2 = Dpdf[intervalIndex8] / dg;
				double computed9 = 0.67 / (1.0 + Math.Exp(1.5 - dpdfOverDg2));
				doubleValues15[intervalIndex8] = doubleValues13[intervalIndex8] * (0.0 - doubleValues10[intervalIndex8] / dg) * (1.0 - computed9 - Math.Pow(computed9, 2.0) / 0.67 * Math.Log(dpdfOverDg2) * dpdfOverDg2 * Math.Exp(1.5 - dpdfOverDg2));
				computed7 += doubleValues14[intervalIndex8];
				computed8 += doubleValues15[intervalIndex8];
			}
			double computed10 = 0.0;
			double computed11 = 0.0;
			double[] doubleValues17 = new double[count];
			double[] doubleValues18 = new double[count];
			for (int intervalIndex9 = 0; intervalIndex9 < count; intervalIndex9++)
			{
				doubleValues17[intervalIndex9] = 100.0 * (doubleValues14[intervalIndex9] / computed5 - doubleValues8[intervalIndex9] / (computed5 * computed5) * computed7);
				doubleValues18[intervalIndex9] = 100.0 * (doubleValues15[intervalIndex9] / computed5 - doubleValues8[intervalIndex9] / (computed5 * computed5) * computed8);
				computed10 += doubleValues[intervalIndex9] * doubleValues17[intervalIndex9];
				computed11 += doubleValues[intervalIndex9] * doubleValues18[intervalIndex9];
			}
			double computed12 = -3.0 * Math.Pow(computed, 2.0) / computed5 + Math.Pow(computed, 3.0) / (computed5 * computed5) * computed7;
			double computed13 = Math.Pow(computed, 3.0) / (computed5 * computed5) * computed8;
			double dg2Term = (0.0 - computed4 * Dg2 / 100.0) * computed10;
			double dg2Term2 = 1.0 - computed4 * Dg2 / 100.0 * computed11;
			if (grainDiameterIndexTerm)
			{
				double computed14 = 0.0;
				double computed15 = 0.0;
				double[] doubleValues19 = new double[count];
				for (int intervalIndex10 = 0; intervalIndex10 < count; intervalIndex10++)
				{
					doubleValues16[intervalIndex10] = doubleValues13[intervalIndex10] * (doubleValues10[intervalIndex10] / computeFsTerm) * (0.003 * Math.Exp(-0.2 * computeFs));
					computed14 += doubleValues16[intervalIndex10];
				}
				double computed16 = Math.Pow(computed, 3.0) / (computed5 * computed5) * computed14;
				for (int intervalIndex11 = 0; intervalIndex11 < count; intervalIndex11++)
				{
					doubleValues19[intervalIndex11] = 100.0 * (doubleValues16[intervalIndex11] / computed5 - doubleValues8[intervalIndex11] / (computed5 * computed5) * computed14);
					computed15 += doubleValues19[intervalIndex11];
				}
				double dg2Term3 = (0.0 - computed4 * Dg2 / 100.0) * computed15;
				double computed17 = (computed3 - 1.0) / (computed3 - computed2);
				double submergenceRatio = (1.0 - computed2) / (computed3 - computed2);
				double computed18 = 0.0;
				double computed19 = 0.0;
				double computed20 = 0.0;
				for (int intervalIndex12 = 0; intervalIndex12 < count; intervalIndex12++)
				{
					double computed21 = computed17 * doubleValues2[intervalIndex12] + submergenceRatio * doubleValues3[intervalIndex12];
					computed18 -= computed21 * doubleValues17[intervalIndex12];
					computed19 -= computed21 * doubleValues18[intervalIndex12];
					computed20 -= computed21 * doubleValues19[intervalIndex12];
				}
				computed20++;
				doubleValues11[0, 0] = computed12;
				doubleValues11[0, 1] = computed13;
				doubleValues11[0, 2] = computed16;
				doubleValues11[1, 0] = dg2Term;
				doubleValues11[1, 1] = dg2Term2;
				doubleValues11[1, 2] = dg2Term3;
				doubleValues11[2, 0] = computed18;
				doubleValues11[2, 1] = computed19;
				doubleValues11[2, 2] = computed20;
			}
			else
			{
				doubleValues11[0, 0] = computed12;
				doubleValues11[0, 1] = computed13;
				doubleValues11[1, 0] = dg2Term;
				doubleValues11[1, 1] = dg2Term2;
			}
			double[] doubleValues20 = new double[3];
			if (grainDiameterIndexTerm)
			{
				double computed22 = doubleValues11[0, 0] * (doubleValues11[1, 1] * doubleValues11[2, 2] - doubleValues11[1, 2] * doubleValues11[2, 1]) - doubleValues11[0, 1] * (doubleValues11[1, 0] * doubleValues11[2, 2] - doubleValues11[1, 2] * doubleValues11[2, 0]) + doubleValues11[0, 2] * (doubleValues11[1, 0] * doubleValues11[2, 1] - doubleValues11[1, 1] * doubleValues11[2, 0]);
				if (Math.Abs(computed22) < 1E-12)
				{
					throw new InvalidOperationException("Jacobian is singular.");
				}
				doubleValues20[0] = (doubleValues6[0] * (doubleValues11[1, 1] * doubleValues11[2, 2] - doubleValues11[1, 2] * doubleValues11[2, 1]) - doubleValues11[0, 1] * (doubleValues6[1] * doubleValues11[2, 2] - doubleValues11[1, 2] * doubleValues6[2]) + doubleValues11[0, 2] * (doubleValues6[1] * doubleValues11[2, 1] - doubleValues11[1, 1] * doubleValues6[2])) / (0.0 - computed22);
				doubleValues20[1] = (doubleValues11[0, 0] * (doubleValues6[1] * doubleValues11[2, 2] - doubleValues11[1, 2] * doubleValues6[2]) - doubleValues6[0] * (doubleValues11[1, 0] * doubleValues11[2, 2] - doubleValues11[1, 2] * doubleValues11[2, 0]) + doubleValues11[0, 2] * (doubleValues11[1, 0] * doubleValues6[2] - doubleValues6[1] * doubleValues11[2, 0])) / (0.0 - computed22);
				doubleValues20[2] = (doubleValues11[0, 0] * (doubleValues11[1, 1] * doubleValues6[2] - doubleValues6[1] * doubleValues11[2, 1]) - doubleValues11[0, 1] * (doubleValues11[1, 0] * doubleValues6[2] - doubleValues6[1] * doubleValues11[2, 0]) + doubleValues6[0] * (doubleValues11[1, 0] * doubleValues11[2, 1] - doubleValues11[1, 1] * doubleValues11[2, 0])) / (0.0 - computed22);
			}
			else
			{
				double computed23 = doubleValues11[0, 0] * doubleValues11[1, 1] - doubleValues11[0, 1] * doubleValues11[1, 0];
				if (Math.Abs(computed23) < 1E-12)
				{
					throw new InvalidOperationException("Jacobian is singular.");
				}
				doubleValues20[0] = (0.0 - (doubleValues6[0] * doubleValues11[1, 1] - doubleValues6[1] * doubleValues11[0, 1])) / computed23;
				doubleValues20[1] = (0.0 - (doubleValues6[1] * doubleValues11[0, 0] - doubleValues6[0] * doubleValues11[1, 0])) / computed23;
			}
			doubleValues5[0] = Math.Abs(doubleValues4[0] + doubleValues20[0]);
			doubleValues5[1] = Math.Abs(doubleValues4[1] + doubleValues20[1]);
			if (grainDiameterIndexTerm)
			{
				doubleValues5[2] = Math.Abs(doubleValues4[2] + doubleValues20[2]);
			}
			doubleValues5[1] = Math.Clamp(doubleValues5[1], min, max);
			if (grainDiameterIndexTerm)
			{
				doubleValues5[2] = Math.Clamp(doubleValues5[2], 0.0, 100.0);
			}
			maxFres = 0.0;
			for (int intervalIndex13 = 0; intervalIndex13 < count; intervalIndex13++)
			{
				maxFres = Math.Max(maxFres, Math.Abs(takeArray[intervalIndex13] - take[intervalIndex13]));
			}
			ures = Math.Abs(100.0 * ((doubleValues5[0] - doubleValues4[0]) / ((doubleValues5[0] + doubleValues4[0]) / 2.0)));
			dres = Math.Abs(doubleValues6[1]);
			qres = Math.Abs(100.0 * ((totalTransportRate22 - totalTransportRate2) / ((totalTransportRate22 + totalTransportRate2) / 2.0)));
			fsres = (grainDiameterIndexTerm ? Math.Abs(doubleValues6[2]) : 0.0);
			scaledTolerance = Math.Max(Math.Max(maxFres, dres), Math.Max(ures, Math.Max(qres, fsres)));
			doubleValues4[0] = doubleValues5[0];
			doubleValues4[1] = doubleValues5[1];
			if (grainDiameterIndexTerm)
			{
				doubleValues4[2] = doubleValues5[2];
			}
			Array.Copy(takeArray, take, count);
			totalTransportRate2 = totalTransportRate22;
			iterations++;
		}
		double uStar = computed;
		WilcockUtility.PdfToCdf(takeArray, index, out var cF2);
		WilcockUtility.CdfToPdf(grainDiameter, cF2, count, out var Dpdf3, out var Fpdf3);
		WilcockUtility.ComputeGsdStats(grainDiameter, cF2, Dpdf3, Fpdf3, count, index, out var Dg3, out var Sig3, out var D3);
		double sFs = WilcockUtility.ComputeFs(grainDiameter, cF2, index);
		return new ISBTMResults
		{
			UStar = uStar,
			SurfaceCdf = cF2,
			SurfaceDMin = grainDiameter[index - 1],
			SurfaceDMax = grainDiameter[0],
			Ds50 = D3,
			Dsm = Dg3,
			SSig = Sig3,
			SFs = sFs,
			BedloadDMin = grainDiameter[index - 1],
			BedloadDMax = grainDiameter[0],
			Db50 = D,
			Dbm = Dg,
			BSig = Sig,
			BFs = bFs,
			MaxIterations = maxIterations,
			Iterations = iterations,
			Tolerance = tolerance,
			Dres = dres,
			MaxFres = maxFres,
			Fsres = fsres,
			Qres = qres,
			Ures = ures
		};
	}
}
