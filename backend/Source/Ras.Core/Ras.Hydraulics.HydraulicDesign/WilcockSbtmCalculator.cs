using System;

namespace Ras.Hydraulics.HydraulicDesign;

public static class WilcockSbtmCalculator
{
	public static SBTMResults ComputeSbtm(WilcockMethod.SBTM m)
	{
		double shearVelocity = m.ShearVelocity;
		double computed = 1000.0;
		double computed2 = 2.65;
		double[] grainDiameter = m.GrainDiameter;
		double[] surfaceCdf = m.SurfaceCdf;
		int index = Array.FindIndex(surfaceCdf, (double fi) => fi == 0.0);
		index = ((index >= 0) ? (index + 1) : surfaceCdf.Length);
		int count = index - 1;
		WilcockUtility.CdfToPdf(grainDiameter, surfaceCdf, count, out var Dpdf, out var Fpdf);
		WilcockUtility.ComputeGsdStats(grainDiameter, surfaceCdf, Dpdf, Fpdf, count, index, out var Dg, out var Sig, out var D);
		double sFs = WilcockUtility.ComputeFs(grainDiameter, surfaceCdf, index);
		double sFsDgTerm = (0.021 + 0.015 * Math.Exp(-0.2 * sFs)) * ((computed2 - 1.0) * computed * 9.81 * (Dg / 1000.0));
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		double[] doubleValues2 = new double[count];
		for (int intervalIndex = 0; intervalIndex < count; intervalIndex++)
		{
			doubleValues[intervalIndex] = Dpdf[intervalIndex] / Dg;
			doubleValuesArray[intervalIndex] = 0.67 / (1.0 + Math.Exp(1.5 - doubleValues[intervalIndex]));
			doubleValues2[intervalIndex] = sFsDgTerm * Math.Pow(doubleValues[intervalIndex], doubleValuesArray[intervalIndex]);
		}
		double[] doubleValues3 = new double[count];
		for (int intervalIndex2 = 0; intervalIndex2 < count; intervalIndex2++)
		{
			doubleValues3[intervalIndex2] = computed * (shearVelocity * shearVelocity) / doubleValues2[intervalIndex2];
		}
		double[] doubleValues4 = new double[count];
		for (int intervalIndex3 = 0; intervalIndex3 < count; intervalIndex3++)
		{
			if (doubleValues3[intervalIndex3] < 1.35)
			{
				doubleValues4[intervalIndex3] = 0.002 * Math.Pow(doubleValues3[intervalIndex3], 7.5);
			}
			else
			{
				doubleValues4[intervalIndex3] = 14.0 * Math.Pow(1.0 - 0.894 / Math.Sqrt(doubleValues3[intervalIndex3]), 4.5);
			}
		}
		double transportRate = 0.0;
		double[] doubleValues5 = new double[count];
		for (int intervalIndex4 = 0; intervalIndex4 < count; intervalIndex4++)
		{
			doubleValues5[intervalIndex4] = doubleValues4[intervalIndex4] * (Fpdf[intervalIndex4] / 100.0 * Math.Pow(shearVelocity, 3.0) / ((computed2 - 1.0) * 9.81));
			transportRate += doubleValues5[intervalIndex4];
		}
		double[] doubleValues6 = new double[count];
		for (int intervalIndex5 = 0; intervalIndex5 < count; intervalIndex5++)
		{
			doubleValues6[intervalIndex5] = 100.0 * (doubleValues5[intervalIndex5] / transportRate);
		}
		WilcockUtility.PdfToCdf(doubleValues6, index, out var cF);
		WilcockUtility.ComputeGsdStats(grainDiameter, cF, Dpdf, doubleValues6, count, index, out var Dg2, out var Sig2, out var D2);
		double bFs = WilcockUtility.ComputeFs(grainDiameter, cF, index);
		return new SBTMResults
		{
			TransportRate = transportRate,
			CumulativeBedloadDistribution = cF,
			SurfaceDMin = grainDiameter[index - 1],
			SurfaceDMax = grainDiameter[0],
			Ds50 = D,
			Dsm = Dg,
			SSig = Sig,
			SFs = sFs,
			BedloadDMin = grainDiameter[index - 1],
			BedloadDMax = grainDiameter[0],
			Db50 = D2,
			Dbm = Dg2,
			BSig = Sig2,
			BFs = bFs
		};
	}
}
