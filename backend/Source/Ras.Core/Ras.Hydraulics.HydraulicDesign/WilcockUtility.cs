using System;
using System.Linq;

namespace Ras.Hydraulics.HydraulicDesign;

public static class WilcockUtility
{
	public const double G = 9.81;

	public const double SpecificGravity = 2.65;

	public const double RHO = 1000.0;

	public static void CdfToPdf(double[] cD, double[] cF, int plen, out double[] Dpdf, out double[] Fpdf)
	{
		Dpdf = new double[plen];
		Fpdf = new double[plen];
		for (int i = 0; i < plen; i++)
		{
			Dpdf[i] = Math.Sqrt(cD[i] * cD[i + 1]);
			Fpdf[i] = cF[i] - cF[i + 1];
		}
	}

	public static void PdfToCdf(double[] Fpdf, int clen, out double[] cF)
	{
		cF = new double[clen];
		cF[0] = 100.0;
		for (int i = 1; i < clen; i++)
		{
			cF[i] = cF[i - 1] - Fpdf[i - 1];
		}
		cF[clen - 1] = 0.0;
	}

	public static void ComputeGsdStats(double[] cD, double[] cF, double[] Dpdf, double[] Fpdf, int plen, int clen, out double Dg, out double Sig, out double D50)
	{
		double computed = 0.0;
		for (int i = 0; i < plen; i++)
		{
			double dpdfTerm = Math.Log(Dpdf[i]) / Math.Log(2.0);
			computed += dpdfTerm * (Fpdf[i] / 100.0);
		}
		Dg = Math.Pow(2.0, computed);
		double computed2 = 0.0;
		for (int j = 0; j < plen; j++)
		{
			double dpdfTerm2 = Math.Log(Dpdf[j]) / Math.Log(2.0);
			computed2 += (dpdfTerm2 - computed) * (dpdfTerm2 - computed) * (Fpdf[j] / 100.0);
		}
		Sig = Math.Sqrt(computed2);
		double[] doubleValues = cD.Select((double x) => Math.Log(x) / Math.Log(2.0)).ToArray();
		int intervalIndex;
		for (intervalIndex = 0; intervalIndex < clen && cF[intervalIndex] > 50.0; intervalIndex++)
		{
		}
		if (intervalIndex >= clen)
		{
			intervalIndex = clen - 1;
		}
		if (Math.Abs(cF[intervalIndex] - 50.0) < double.Epsilon)
		{
			D50 = cD[intervalIndex];
			return;
		}
		double y = (doubleValues[intervalIndex - 1] - doubleValues[intervalIndex]) * ((50.0 - cF[intervalIndex]) / (cF[intervalIndex - 1] - cF[intervalIndex])) + doubleValues[intervalIndex];
		D50 = Math.Pow(2.0, y);
	}

	public static double ComputeFs(double[] cD, double[] cF, int clen)
	{
		double[] doubleValues = cD.Select((double x) => Math.Log(x) / Math.Log(2.0)).ToArray();
		if (doubleValues[0] <= 1.0)
		{
			return cF[0];
		}
		int intervalIndex;
		for (intervalIndex = 0; intervalIndex < clen && doubleValues[intervalIndex] > 1.0; intervalIndex++)
		{
		}
		if (intervalIndex >= clen)
		{
			intervalIndex = clen - 1;
		}
		if (Math.Abs(doubleValues[intervalIndex] - 1.0) < double.Epsilon)
		{
			return cF[intervalIndex];
		}
		double cFTerm = (cF[intervalIndex - 1] - cF[intervalIndex]) * ((1.0 - doubleValues[intervalIndex]) / (doubleValues[intervalIndex - 1] - doubleValues[intervalIndex]));
		return cFTerm + cF[intervalIndex];
	}

	public static void ComputeGsdStats84(double[] cD, double[] cF, int plen, out double Dg, out double Sig, out double D50, out double D84)
	{
		if (cD == null || cF == null)
		{
			throw new ArgumentNullException();
		}
		if (cD.Length != cF.Length)
		{
			throw new ArgumentException("cD and cF must have same length.");
		}
		int cDLength = cD.Length;
		CdfToPdf(cD, cF, plen, out var Dpdf, out var Fpdf);
		if (Dpdf == null || Fpdf == null || Dpdf.Length != Fpdf.Length)
		{
			throw new ArgumentException("CdfToPdf must return aligned D and F.");
		}
		double[] doubleValues = new double[plen];
		double computed = 0.0;
		for (int i = 0; i < plen; i++)
		{
			doubleValues[i] = Math.Log(Dpdf[i], 2.0);
			computed += doubleValues[i] * (Fpdf[i] / 100.0);
		}
		Dg = Math.Pow(2.0, computed);
		double computed2 = 0.0;
		for (int j = 0; j < plen; j++)
		{
			double computed3 = doubleValues[j] - computed;
			computed2 += computed3 * computed3 * (Fpdf[j] / 100.0);
		}
		Sig = Math.Sqrt(computed2);
		double[] doubleValuesArray = new double[cDLength];
		for (int k = 0; k < cDLength; k++)
		{
			doubleValuesArray[k] = Math.Log(cD[k], 2.0);
		}
		D50 = InterpDx(cD, cF, doubleValuesArray, 50.0);
		D84 = InterpDx(cD, cF, doubleValuesArray, 84.0);
	}

	private static double InterpDx(double[] cD, double[] cF, double[] cPsi, double targetPercent)
	{
		int cDLength = cD.Length;
		int i;
		for (i = Math.Min(1, cDLength - 1); i < cDLength && cF[i] > targetPercent; i++)
		{
		}
		if (i < cDLength && Math.Abs(cF[i] - targetPercent) < 1E-12)
		{
			return cD[i];
		}
		int cDLengthTerm = Math.Min(i, cDLength - 1);
		int ordinal = Math.Max(i - 1, 0);
		double targetPercentCFTerm = targetPercent - cF[cDLengthTerm];
		double cFStep = cF[ordinal] - cF[cDLengthTerm];
		double y = (cPsi[ordinal] - cPsi[cDLengthTerm]) * (targetPercentCFTerm / cFStep) + cPsi[cDLengthTerm];
		return Math.Pow(2.0, y);
	}
}
