using System;
using System.Linq;
using System.Threading.Tasks;
using Utility;

namespace Ras.Hydraulics.HydraulicDesign;

public static class WilcockStabCalculator
{
	public static StabResults ComputeStab(WilcockMethod.STAB m)
	{
		int plen = -1;
		int count = -1;
		double[] waterDischarges = m.WaterDischarges;
		double[] sedimentSupplyRates = m.SedimentSupplyRates;
		double[][] transportGrainSizes = m.TransportGrainSizes;
		double bMin = m.BMin;
		double bMax = m.BMax;
		int transportGrainSizeLength = transportGrainSizes.Length;
		double[] grainDiameter = m.GrainDiameter;
		double[] doubleValues = new double[transportGrainSizeLength];
		double[] dMin = new double[transportGrainSizeLength];
		double[] dMax = new double[transportGrainSizeLength];
		double[] d50 = new double[transportGrainSizeLength];
		double[] dg = new double[transportGrainSizeLength];
		double[] sigmaG = new double[transportGrainSizeLength];
		double[] fs = new double[transportGrainSizeLength];
		double[] doubleValuesArray = new double[transportGrainSizeLength];
		for (int i = 0; i < transportGrainSizeLength; i++)
		{
			double[] transportGrainSize = transportGrainSizes[i];
			bool condition = sedimentSupplyRates[i] <= 0.0 || waterDischarges[i] <= 0.0;
			if (!condition)
			{
				bool condition2 = true;
				for (int j = 0; j < transportGrainSize.Length; j++)
				{
					if (transportGrainSize[j] > 0.0)
					{
						condition2 = false;
						break;
					}
				}
				condition = condition2;
			}
			if (condition)
			{
				dMin[i] = ((grainDiameter.Length != 0) ? grainDiameter[^1] : 1E-12);
				dMax[i] = ((grainDiameter.Length != 0) ? grainDiameter[0] : 1E-12);
				d50[i] = 1E-12;
				dg[i] = 1E-12;
				sigmaG[i] = 1E-12;
				fs[i] = 1E-12;
				doubleValuesArray[i] = 1E-12;
				doubleValues[i] = 1E-12;
				continue;
			}
			count = Array.FindIndex(transportGrainSize, (double v) => v == 0.0);
			count = ((count >= 0) ? (count + 1) : transportGrainSize.Length);
			plen = count - 1;
			WilcockUtility.CdfToPdf(grainDiameter, transportGrainSize, plen, out var Dpdf, out var Fpdf);
			WilcockUtility.ComputeGsdStats(grainDiameter, transportGrainSize, Dpdf, Fpdf, plen, count, out var Dg, out var Sig, out var D);
			double computeFs = WilcockUtility.ComputeFs(grainDiameter, transportGrainSize, count);
			dMin[i] = grainDiameter[count - 1];
			dMax[i] = grainDiameter[0];
			d50[i] = D;
			dg[i] = Dg;
			sigmaG[i] = Sig;
			fs[i] = computeFs;
			doubleValuesArray[i] = Dg;
			doubleValues[i] = waterDischarges[i];
		}
		double[] b = new double[51];
		for (int intervalIndex = 0; intervalIndex < 51; intervalIndex++)
		{
			b[intervalIndex] = bMin + (double)intervalIndex * (bMax - bMin) / 50.0;
		}
		double[][] qt = new double[transportGrainSizeLength][];
		int caseIdx;
		for (caseIdx = 0; caseIdx < transportGrainSizeLength; caseIdx++)
		{
			qt[caseIdx] = b.Select((double bw) => sedimentSupplyRates[caseIdx] / bw).ToArray();
		}
		double[][] uStar = new double[transportGrainSizeLength][];
		double[][] dsm = new double[transportGrainSizeLength][];
		double[][] ds50Array = new double[transportGrainSizeLength][];
		double[][] gamma = new double[transportGrainSizeLength][];
		double[][] nd = new double[transportGrainSizeLength][];
		double[][] tb = new double[transportGrainSizeLength][];
		double[][] doubleValues2 = new double[transportGrainSizeLength][];
		double[][] doubleValues3 = new double[transportGrainSizeLength][];
		double[][] nvp = new double[transportGrainSizeLength][];
		double[][] nc = new double[transportGrainSizeLength][];
		double[][] ds84 = new double[transportGrainSizeLength][];
		double[][] hSpecN = new double[transportGrainSizeLength][];
		double[][] sSpecN = new double[transportGrainSizeLength][];
		double[][] doubleValues4 = new double[transportGrainSizeLength][];
		double[][] relativeDepth = new double[transportGrainSizeLength][];
		double[][] gsdAtMeanWidthD = new double[transportGrainSizeLength][];
		double[][] gsdAtMeanWidthFi = new double[transportGrainSizeLength][];
		for (int intervalIndex2 = 0; intervalIndex2 < transportGrainSizeLength; intervalIndex2++)
		{
			uStar[intervalIndex2] = new double[51];
			dsm[intervalIndex2] = new double[51];
			ds50Array[intervalIndex2] = new double[51];
			gamma[intervalIndex2] = new double[51];
			nd[intervalIndex2] = new double[51];
			tb[intervalIndex2] = new double[51];
			gsdAtMeanWidthD[intervalIndex2] = new double[51];
			gsdAtMeanWidthFi[intervalIndex2] = new double[51];
			doubleValues2[intervalIndex2] = new double[51];
			doubleValues3[intervalIndex2] = new double[51];
			nvp[intervalIndex2] = new double[51];
			nc[intervalIndex2] = new double[51];
			ds84[intervalIndex2] = new double[51];
			hSpecN[intervalIndex2] = new double[51];
			sSpecN[intervalIndex2] = new double[51];
			doubleValues4[intervalIndex2] = new double[51];
			relativeDepth[intervalIndex2] = new double[51];
			gsdAtMeanWidthD[intervalIndex2] = new double[51];
			gsdAtMeanWidthFi[intervalIndex2] = new double[51];
		}
		for (int intervalIndex3 = 0; intervalIndex3 < transportGrainSizeLength; intervalIndex3++)
		{
			bool condition3 = sedimentSupplyRates[intervalIndex3] <= 0.0 || waterDischarges[intervalIndex3] <= 0.0;
			if (!condition3)
			{
				double[] transportGrainSizeArray = transportGrainSizes[intervalIndex3];
				bool condition4 = true;
				for (int intervalIndex4 = 0; intervalIndex4 < transportGrainSizeArray.Length; intervalIndex4++)
				{
					if (transportGrainSizeArray[intervalIndex4] > 0.0)
					{
						condition4 = false;
						break;
					}
				}
				condition3 = condition4;
			}
			for (int intervalIndex5 = 0; intervalIndex5 < 51; intervalIndex5++)
			{
				if (condition3 || qt[intervalIndex3][intervalIndex5] <= 0.0)
				{
					uStar[intervalIndex3][intervalIndex5] = 1E-12;
					dsm[intervalIndex3][intervalIndex5] = 1E-12;
					ds50Array[intervalIndex3][intervalIndex5] = 1E-12;
					ds84[intervalIndex3][intervalIndex5] = 1E-12;
					gamma[intervalIndex3][intervalIndex5] = 1E-12;
					nd[intervalIndex3][intervalIndex5] = 1E-12;
					tb[intervalIndex3][intervalIndex5] = 1E-12;
					gsdAtMeanWidthD[intervalIndex3][intervalIndex5] = 1E-12;
					gsdAtMeanWidthFi[intervalIndex3][intervalIndex5] = 1E-12;
					continue;
				}
				WilcockMethod.ISBTM iSBTM = new WilcockMethod.ISBTM
				{
					TotalTransportRate = qt[intervalIndex3][intervalIndex5],
					GrainDiameter = m.GrainDiameter,
					TransportCdf = transportGrainSizes[intervalIndex3]
				};
				ISBTMResults iSBTMResults = WilcockIsbtmCalculator.ComputeIsbtm(iSBTM);
				uStar[intervalIndex3][intervalIndex5] = iSBTMResults.UStar;
				dsm[intervalIndex3][intervalIndex5] = iSBTMResults.Dsm;
				ds50Array[intervalIndex3][intervalIndex5] = iSBTMResults.Ds50;
				gamma[intervalIndex3][intervalIndex5] = ((doubleValuesArray[intervalIndex3] > 0.0) ? (dsm[intervalIndex3][intervalIndex5] / doubleValuesArray[intervalIndex3]) : 1E-12);
				WilcockUtility.ComputeGsdStats84(iSBTM.GrainDiameter, iSBTMResults.SurfaceCdf, plen, out var _, out var _, out var _, out var D3);
				ds84[intervalIndex3][intervalIndex5] = D3;
				nd[intervalIndex3][intervalIndex5] = 0.013992 * Math.Pow(ds84[intervalIndex3][intervalIndex5], 1.0 / 6.0);
				tb[intervalIndex3][intervalIndex5] = iSBTMResults.UStar * iSBTMResults.UStar * 1000.0;
				if ((double)intervalIndex5 == 25.5)
				{
					for (int intervalIndex6 = 0; intervalIndex6 < count; intervalIndex6++)
					{
						gsdAtMeanWidthD[intervalIndex3][intervalIndex6] = iSBTM.GrainDiameter[intervalIndex6];
						gsdAtMeanWidthFi[intervalIndex3][intervalIndex6] = iSBTMResults.SurfaceCdf[intervalIndex6];
					}
				}
			}
		}
		for (int intervalIndex7 = 0; intervalIndex7 < transportGrainSizeLength; intervalIndex7++)
		{
			bool condition5 = sedimentSupplyRates[intervalIndex7] <= 0.0 || waterDischarges[intervalIndex7] <= 0.0;
			if (!condition5)
			{
				double[] transportGrainSize2 = transportGrainSizes[intervalIndex7];
				bool condition6 = true;
				for (int intervalIndex8 = 0; intervalIndex8 < transportGrainSize2.Length; intervalIndex8++)
				{
					if (transportGrainSize2[intervalIndex8] > 0.0)
					{
						condition6 = false;
						break;
					}
				}
				condition5 = condition6;
			}
			if (condition5)
			{
				for (int intervalIndex9 = 0; intervalIndex9 < 51; intervalIndex9++)
				{
					doubleValues2[intervalIndex7][intervalIndex9] = 1E-12;
					doubleValues3[intervalIndex7][intervalIndex9] = 1E-12;
					nvp[intervalIndex7][intervalIndex9] = 1E-12;
					nc[intervalIndex7][intervalIndex9] = 1E-12;
				}
			}
			else
			{
				(double[], double[], double[], double[], double[], double[], double[], double[], int[], bool[]) tuple = STABCompositeRoughnessIterativeSolver(b, nd[intervalIndex7], tb[intervalIndex7], gamma[intervalIndex7], ds84[intervalIndex7], doubleValues[intervalIndex7], m.Z, m.Ns, m.Nspec);
				doubleValues2[intervalIndex7] = tuple.Item1;
				nvp[intervalIndex7] = tuple.Item2;
				nc[intervalIndex7] = tuple.Item3;
				doubleValues3[intervalIndex7] = tuple.Item4;
				hSpecN[intervalIndex7] = tuple.Item5;
				sSpecN[intervalIndex7] = tuple.Item6;
				doubleValues4[intervalIndex7] = tuple.Item7;
				relativeDepth[intervalIndex7] = tuple.Item8;
			}
		}
		double[] cutey = new double[23];
		double bMean = 0.5 * (bMin + bMax);
		cutey[0] = 1E-08;
		for (int unity = 1; unity < 23; unity++)
		{
			cutey[unity] = cutey[unity - 1] * 1.7926224;
		}
		double[][] tuStar = new double[transportGrainSizeLength][];
		double[][] tDsm = new double[transportGrainSizeLength][];
		double[][] tDs50 = new double[transportGrainSizeLength][];
		double[][] tDs84 = new double[transportGrainSizeLength][];
		double[][] tGamma = new double[transportGrainSizeLength][];
		double[][] tnd = new double[transportGrainSizeLength][];
		double[][] ttb = new double[transportGrainSizeLength][];
		double[][] th = new double[transportGrainSizeLength][];
		double[][] tnvp = new double[transportGrainSizeLength][];
		double[][] tnc = new double[transportGrainSizeLength][];
		double[][] ts = new double[transportGrainSizeLength][];
		double[] qtKgPerHr = new double[23];
		for (int intervalIndex10 = 0; intervalIndex10 < transportGrainSizeLength; intervalIndex10++)
		{
			tuStar[intervalIndex10] = new double[23];
			tDsm[intervalIndex10] = new double[23];
			tDs50[intervalIndex10] = new double[23];
			tDs84[intervalIndex10] = new double[23];
			tGamma[intervalIndex10] = new double[23];
			tnd[intervalIndex10] = new double[23];
			ttb[intervalIndex10] = new double[23];
			th[intervalIndex10] = new double[23];
			tnvp[intervalIndex10] = new double[23];
			tnc[intervalIndex10] = new double[23];
			ts[intervalIndex10] = new double[23];
		}
		for (int intervalIndex11 = 0; intervalIndex11 < transportGrainSizeLength; intervalIndex11++)
		{
			bool condition7 = sedimentSupplyRates[intervalIndex11] <= 0.0 || waterDischarges[intervalIndex11] <= 0.0;
			if (!condition7)
			{
				double[] transportGrainSize3 = transportGrainSizes[intervalIndex11];
				bool condition8 = true;
				for (int intervalIndex12 = 0; intervalIndex12 < transportGrainSize3.Length; intervalIndex12++)
				{
					if (transportGrainSize3[intervalIndex12] > 0.0)
					{
						condition8 = false;
						break;
					}
				}
				condition7 = condition8;
			}
			for (int intervalIndex13 = 0; intervalIndex13 < 23; intervalIndex13++)
			{
				if (condition7)
				{
					tuStar[intervalIndex11][intervalIndex13] = 1E-12;
					tDsm[intervalIndex11][intervalIndex13] = 1E-12;
					tDs50[intervalIndex11][intervalIndex13] = 1E-12;
					tGamma[intervalIndex11][intervalIndex13] = 1E-12;
					tnd[intervalIndex11][intervalIndex13] = 1E-12;
					ttb[intervalIndex11][intervalIndex13] = 1E-12;
					continue;
				}
				WilcockMethod.ISBTM iSBTM2 = new WilcockMethod.ISBTM
				{
					TotalTransportRate = cutey[intervalIndex13],
					GrainDiameter = m.GrainDiameter,
					TransportCdf = transportGrainSizes[intervalIndex11]
				};
				ISBTMResults iSBTMResults2 = WilcockIsbtmCalculator.ComputeIsbtm(iSBTM2);
				tuStar[intervalIndex11][intervalIndex13] = iSBTMResults2.UStar;
				tDsm[intervalIndex11][intervalIndex13] = iSBTMResults2.Dsm;
				tDs50[intervalIndex11][intervalIndex13] = iSBTMResults2.Ds50;
				tGamma[intervalIndex11][intervalIndex13] = ((doubleValuesArray[intervalIndex11] > 0.0) ? (iSBTMResults2.Dsm / doubleValuesArray[intervalIndex11]) : 1E-12);
				WilcockUtility.ComputeGsdStats84(iSBTM2.GrainDiameter, iSBTMResults2.SurfaceCdf, plen, out var _, out var _, out var _, out var D5);
				tDs84[intervalIndex11][intervalIndex13] = D5;
				tnd[intervalIndex11][intervalIndex13] = 0.013992 * Math.Pow(tDs84[intervalIndex11][intervalIndex13], 1.0 / 6.0);
				ttb[intervalIndex11][intervalIndex13] = iSBTMResults2.UStar * iSBTMResults2.UStar * 1000.0;
			}
		}
		for (int intervalIndex14 = 0; intervalIndex14 < transportGrainSizeLength; intervalIndex14++)
		{
			bool condition9 = sedimentSupplyRates[intervalIndex14] <= 0.0 || waterDischarges[intervalIndex14] <= 0.0;
			if (!condition9)
			{
				double[] transportGrainSize4 = transportGrainSizes[intervalIndex14];
				bool condition10 = true;
				for (int intervalIndex15 = 0; intervalIndex15 < transportGrainSize4.Length; intervalIndex15++)
				{
					if (transportGrainSize4[intervalIndex15] > 0.0)
					{
						condition10 = false;
						break;
					}
				}
				condition9 = condition10;
			}
			if (condition9)
			{
				for (int intervalIndex16 = 0; intervalIndex16 < 23; intervalIndex16++)
				{
					th[intervalIndex14][intervalIndex16] = 1E-12;
					ts[intervalIndex14][intervalIndex16] = 1E-12;
					tnvp[intervalIndex14][intervalIndex16] = 1E-12;
					tnc[intervalIndex14][intervalIndex16] = 1E-12;
				}
			}
			else
			{
				double[] a = null;
				Buffers.ArraySetAll(ref a, bMean, tnd[intervalIndex14].Length);
				(double[], double[], double[], double[], double[], double[], double[], double[], int[], bool[]) tuple2 = STABCompositeRoughnessIterativeSolver(a, tnd[intervalIndex14], ttb[intervalIndex14], tGamma[intervalIndex14], tDs84[intervalIndex14], doubleValues[intervalIndex14], m.Z, m.Ns, m.Nspec);
				th[intervalIndex14] = tuple2.Item1;
				tnvp[intervalIndex14] = tuple2.Item2;
				tnc[intervalIndex14] = tuple2.Item3;
				ts[intervalIndex14] = tuple2.Item4;
			}
		}
		for (int intervalIndex17 = 0; intervalIndex17 < 23; intervalIndex17++)
		{
			qtKgPerHr[intervalIndex17] = cutey[intervalIndex17] * bMean * 2650.0 * 3600.0;
		}
		double[][] tcS = new double[transportGrainSizeLength][];
		if (m.UseThresholdSlope)
		{
			double tcTauC = 1.65 * m.TcD50 * m.TcTauStarC;
			double tcND = Math.Pow(m.TcD84 / 1000.0, 1.0 / 6.0) / 22.6;
			for (int caseIdx2 = 0; caseIdx2 < transportGrainSizeLength; caseIdx2++)
			{
				tcS[caseIdx2] = new double[51];
				double Q = doubleValues[caseIdx2];
				System.Threading.Tasks.Parallel.For(0, 51, (int ordinal) =>
				{
					double computed = b[ordinal];
					double computed2 = 1.0;
					double computed3 = 0.0;
					double computed4 = 0.0;
					double tcS2 = 0.001;
					for (int k = 0; k < 100; k++)
					{
						double computed5 = computed2;
						double tcS22 = tcS2;
						double sqrtTcS2 = Math.Sqrt(Math.Max(tcS2, 1E-12));
						double computed6 = Math.Pow(computed4 * Q / sqrtTcS2, 0.6);
						double sqrtTcMPolynomial = Math.Sqrt(1.0 + m.TcM * m.TcM);
						double computed7 = Math.Pow(computed + 2.0 * computed2 * sqrtTcMPolynomial, 0.4);
						double tcMTerm = computed + m.TcM * computed2;
						if (tcMTerm > 0.0 && !double.IsNaN(computed6) && !double.IsNaN(computed7))
						{
							double computed8 = computed6 * computed7 / tcMTerm;
							if (!double.IsNaN(computed8) && !double.IsInfinity(computed8) && computed8 > 0.0)
							{
								computed2 = computed8;
							}
						}
						if (m.TcD84 > 0.0 && computed2 > 0.0)
						{
							double tcD84Term = computed2 * 1000.0 / m.TcD84;
							double d = 6.76 + Math.Pow(tcD84Term, 1.6666666666666667);
							double computed9 = Math.Pow(computed2, 1.0 / 6.0) * Math.Sqrt(d);
							double computed10 = Math.Sqrt(9.81) * 6.5 * tcD84Term;
							if (computed10 > 0.0 && !double.IsNaN(computed9))
							{
								computed3 = computed9 / computed10;
								if (double.IsNaN(computed3) || double.IsInfinity(computed3))
								{
									computed3 = 0.0;
								}
							}
						}
						double sqrtTcMPolynomial2 = Math.Sqrt(1.0 + m.TcM * m.TcM);
						double nsTerm = computed * Math.Pow(Math.Max(computed3, 1E-12), 1.5) + 2.0 * computed2 * sqrtTcMPolynomial2 * Math.Pow(m.Ns, 1.5);
						double computed11 = computed + 2.0 * computed2 * sqrtTcMPolynomial2;
						if (computed11 > 0.0)
						{
							computed4 = Math.Pow(nsTerm / computed11, 2.0 / 3.0);
							if (double.IsNaN(computed4) || double.IsInfinity(computed4))
							{
								computed4 = 0.033;
							}
						}
						if (tcND > 0.0)
						{
							double tcMTerm2 = (computed + m.TcM * computed2) * computed2 / (computed + 2.0 * computed2 * sqrtTcMPolynomial2);
							if (tcMTerm2 > 0.0)
							{
								double tcS23 = tcTauC * Math.Pow(computed4 / tcND, 1.5) / 9810.0 / tcMTerm2;
								if (!double.IsNaN(tcS23) && !double.IsInfinity(tcS23) && tcS23 > 0.0)
								{
									tcS2 = tcS23;
								}
							}
						}
						double computed12 = Math.Abs(computed2 - computed5);
						double tcS2Term = Math.Abs(tcS2 - tcS22);
						if (computed12 < 1E-06 && tcS2Term < 1E-06)
						{
							break;
						}
					}
					tcS[caseIdx2][ordinal] = tcS2;
				});
			}
		}
		else
		{
			for (int intervalIndex18 = 0; intervalIndex18 < transportGrainSizeLength; intervalIndex18++)
			{
				tcS[intervalIndex18] = new double[51];
			}
		}
		return new StabResults
		{
			DMin = dMin,
			DMax = dMax,
			D50 = d50,
			Dg = dg,
			SigmaG = sigmaG,
			Fs = fs,
			B = b,
			QT = qt,
			GSDAtMeanWidthD = gsdAtMeanWidthD,
			GSDAtMeanWidthFi = gsdAtMeanWidthFi,
			UStar = uStar,
			Dsm = dsm,
			Ds50Array = ds50Array,
			Gamma = gamma,
			ND = nd,
			TB = tb,
			H = doubleValues2,
			S = doubleValues3,
			NVP = nvp,
			NC = nc,
			Ds84 = ds84,
			HSpecN = hSpecN,
			SSpecN = sSpecN,
			QTKgPerHr = qtKgPerHr,
			Cutey = cutey,
			BMean = bMean,
			TUStar = tuStar,
			TDsm = tDsm,
			TDs50 = tDs50,
			TGamma = tGamma,
			TND = tnd,
			TTB = ttb,
			TH = th,
			TS = ts,
			TNVP = tnvp,
			TNC = tnc,
			TDs84 = tDs84,
			F = doubleValues4,
			RelativeDepth = relativeDepth,
			TcS = tcS
		};
	}

	public static (double[] h, double[] nVP, double[] nc, double[] S, double[] hNspec, double[] Snspec, double[] f, double[] hOverDs84, int[] iters, bool[] converged) STABCompositeRoughnessIterativeSolver(double[] b, double[] nd, double[] tb, double[] gamma, double[] ds84, double q, double z, double ns, double nspec, int maxIterations = 100, double maxChange = 0.001)
	{
		if (b == null || nd == null || tb == null || gamma == null || ds84 == null)
		{
			throw new ArgumentNullException("Input arrays cannot be null.");
		}
		int count = b.Length;
		if (nd.Length != count || tb.Length != count || gamma.Length != count || ds84.Length != count)
		{
			throw new ArgumentException("All input arrays must have the same length.");
		}
		double[] F = new double[count];
		double[] G = new double[count];
		double[] H = new double[count];
		double[] I = new double[count];
		double[] J = new double[count];
		double[] K = new double[count];
		double[] AS = new double[count];
		double[] AU = new double[count];
		int[] iters = new int[count];
		bool[] converged = new bool[count];
		double sqrt_g = Math.Sqrt(9.81);
		double rootZ = Math.Sqrt(1.0 + z * z);
		System.Threading.Tasks.Parallel.For(0, count, (int i) =>
		{
			double computed = b[i];
			double nd2 = nd[i];
			double tb2 = tb[i];
			double ds842 = ds84[i];
			double computed2 = 1.0;
			double computed3 = 0.0;
			double computed4 = 0.0;
			double computed5 = 0.001;
			int j;
			for (j = 0; j < maxIterations; j++)
			{
				double computed6 = Math.Max(computed2, 1E-12);
				double ds8422 = Math.Max(ds842, 1E-12);
				double nd22 = Math.Max(nd2, 1E-12);
				double computedComputed6Term = computed + 2.0 * computed6 * rootZ;
				double computed7 = computed + z * computed6;
				double computed8 = computed6 * 1000.0 / ds8422;
				double computed9 = Math.Sqrt(6.76 + Math.Pow(computed8, 1.6666666666666667));
				double sqrt_gTerm = 6.5 * sqrt_g * Math.Max(computed8, 1E-12);
				double computed10 = Math.Pow(computed6, 1.0 / 6.0) * computed9 / sqrt_gTerm;
				if (double.IsNaN(computed10) || double.IsInfinity(computed10))
				{
					computed10 = computed3;
				}
				double rootZNsTerm = computed * Math.Pow(Math.Max(computed10, 1E-12), 1.5) + 2.0 * computed6 * rootZ * Math.Pow(Math.Max(ns, 1E-12), 1.5);
				double computed11 = Math.Max(computedComputed6Term, 1E-12);
				double computed12 = Math.Pow(rootZNsTerm / computed11, 2.0 / 3.0);
				if (double.IsNaN(computed12) || double.IsInfinity(computed12))
				{
					computed12 = computed4;
				}
				double computed13 = (computed + z * computed6) * computed6 / Math.Max(computedComputed6Term, 1E-12);
				double tb2Term = tb2 * Math.Pow(Math.Max(computed12, 1E-12) / nd22, 1.5) / 9810.0 / Math.Max(computed13, 1E-12);
				double isNaNIsInfinityTerm = ((double.IsNaN(tb2Term) || double.IsInfinity(tb2Term)) ? 0.001 : tb2Term);
				double computed14 = Math.Sqrt(Math.Max(isNaNIsInfinityTerm, 1E-12));
				double computed15 = Math.Pow(Math.Max(computed12, 1E-12) * q / computed14, 0.6);
				double computed16 = Math.Pow(Math.Max(computedComputed6Term, 1E-12), 0.4);
				double computed17 = Math.Max(computed7, 1E-12);
				double computed18 = computed15 * computed16 / computed17;
				double isNaNIsInfinityTerm2 = ((double.IsNaN(computed18) || double.IsInfinity(computed18)) ? 1.0 : computed18);
				double computed19 = Math.Abs(isNaNIsInfinityTerm2 - computed2);
				double computed20 = Math.Abs(computed10 - computed3);
				double computed21 = Math.Abs(computed12 - computed4);
				double computed22 = Math.Max(val2: Math.Max(computed21, Math.Abs(isNaNIsInfinityTerm - computed5)), val1: Math.Max(computed19, computed20));
				computed2 = isNaNIsInfinityTerm2;
				computed3 = computed10;
				computed4 = computed12;
				computed5 = isNaNIsInfinityTerm;
				if (computed22 <= maxChange)
				{
					converged[i] = true;
					break;
				}
			}
			F[i] = computed2;
			G[i] = computed3;
			H[i] = computed4;
			I[i] = computed5;
			iters[i] = j + 1;
			double unity = 1.0;
			double computed23 = 0.001;
			for (int k = 0; k < maxIterations; k++)
			{
				double computed24 = Math.Max(unity, 1E-12);
				double rootZTerm = computed + 2.0 * computed24 * rootZ;
				double computed25 = computed + z * computed24;
				double computed26 = (computed + z * computed24) * computed24 / Math.Max(rootZTerm, 1E-12);
				double tb2NspecTerm = tb2 * Math.Pow(Math.Max(nspec, 1E-12) / nd2, 1.5) / 9810.0 / Math.Max(computed26, 1E-12);
				double computed27 = ((double.IsNaN(tb2NspecTerm) || double.IsInfinity(tb2NspecTerm)) ? 0.001 : tb2NspecTerm);
				double nspecTerm = Math.Pow(Math.Max(nspec, 1E-12) * q / Math.Sqrt(Math.Max(computed27, 1E-12)), 0.6);
				double computed28 = Math.Pow(Math.Max(rootZTerm, 1E-12), 0.4);
				double computed29 = nspecTerm * computed28 / Math.Max(computed25, 1E-12);
				double computed30 = ((double.IsNaN(computed29) || double.IsInfinity(computed29)) ? 1.0 : computed29);
				computed30 = 0.85 * computed30 + 0.15000000000000002 * unity;
				computed27 = 0.85 * computed27 + 0.15000000000000002 * computed23;
				if (Math.Max(Math.Abs(computed30 - unity), Math.Abs(computed27 - computed23)) <= maxChange)
				{
					unity = computed30;
					computed23 = computed27;
					break;
				}
				unity = computed30;
				computed23 = computed27;
			}
			J[i] = unity;
			K[i] = Math.Max(computed23, 1E-12);
			AS[i] = q / (b[i] * Math.Sqrt(9.81) * Math.Pow(F[i], 1.5));
			AU[i] = F[i] / ds84[i] * 1000.0;
		});
		return (h: F, nVP: G, nc: H, S: I, hNspec: J, Snspec: K, f: AS, hOverDs84: AU, iters: iters, converged: converged);
	}
}
