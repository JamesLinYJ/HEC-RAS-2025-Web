using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Utility.LightweightPlotting;
using Utility.Units;

namespace Geospatial.Hydraulics;

public static class HydraulicCurvePlotter
{
	private const int W = 900;

	private const int PH = 260;

	private const int VPH = 520;

	private const double ML = 80.0;

	private const double MR = 30.0;

	private const double MT = 30.0;

	private const double MB = 45.0;

	private static readonly string[] Colors = new string[7] { "#2196F3", "#F44336", "#4CAF50", "#FF9800", "#9C27B0", "#00BCD4", "#795548" };

	private static readonly string[] Dashes = new string[7] { "", "8,4", "4,4", "12,4,4,4", "2,4", "", "" };

	public static string GenerateSingleWsePlot(LDMResult result, int wseIndex)
	{
		ValidateSingleWsePlot(result, wseIndex);
		return GenerateSingleWsePlot(result, wseIndex, result.CellVelocity[wseIndex], result.Flow[wseIndex], boundaryScaled: false);
	}

	public static string GenerateSingleWsePlot(LDMResult result, int wseIndex, float boundaryFlow)
	{
		ValidateSingleWsePlot(result, wseIndex);
		if (!float.IsFinite(boundaryFlow) || boundaryFlow < 0f)
		{
			throw new ArgumentOutOfRangeException("boundaryFlow", "Boundary flow must be a nonnegative inflow magnitude.");
		}
		float[] cellVelocityForFlow = result.GetCellVelocityForFlow(wseIndex, boundaryFlow);
		return GenerateSingleWsePlot(result, wseIndex, cellVelocityForFlow, boundaryFlow, boundaryScaled: true);
	}

	private static void ValidateSingleWsePlot(LDMResult result, int wseIndex)
	{
		ArgumentNullException.ThrowIfNull(result, "result");
		if (!result.HasVelocityDistribution)
		{
			throw new ArgumentException("The result does not contain velocity distributions.", "result");
		}
		if ((uint)wseIndex >= (uint)result.SolvedWseCount)
		{
			throw new ArgumentOutOfRangeException("wseIndex");
		}
	}

	private static string GenerateSingleWsePlot(LDMResult result, int wseIndex, float[] cellVelocity, float plottedFlow, bool boundaryScaled)
	{
		int height = 1060;
		SvgFigure svgFigure = new SvgFigure(900, height);
		string unitsSiTerm = ((result.Units == UnitSystem.SI) ? "m" : "ft");
		string path = ((result.Units == UnitSystem.SI) ? "m/s" : "ft/s");
		int cellCount = result.Geometry.CellCount;
		if (cellCount == 0)
		{
			return svgFigure.Render();
		}
		int cellCountOffset = cellCount + 1;
		float[] subArray = result.Geometry.FaceStations[..cellCountOffset];
		float[] subArray2 = result.Geometry.FaceBedElevations[..cellCountOffset];
		float[] cellStations = result.Geometry.GetCellStations();
		float wse = result.Wse[wseIndex];
		float flow = result.Flow[wseIndex];
		float plottedFlowOverFlow = ((flow == 0f) ? 0f : (plottedFlow / flow));
		svgFigure.SetPlotArea(0.0, 0.0, 900.0, 260.0);
		svgFigure.SetMargins(80.0, 30.0, 30.0, 45.0);
		MinAndMax(subArray, out var minimum, out var maximum);
		float subArray2Term = subArray2.Min() - 0.5f;
		float subArray2WseTerm = Math.Max(subArray2.Max(), wse) + 0.5f;
		svgFigure.SetLimits(minimum, maximum, subArray2Term, subArray2WseTerm);
		string title = (boundaryScaled ? $"WSE={wse:F3} Q(boundary)={plottedFlow:F5} Q(LDM)={flow:F5} scale={plottedFlowOverFlow:F3} K={result.Conveyance[wseIndex]:F1}" : $"WSE={wse:F3} Q(LDM)={flow:F3} K={result.Conveyance[wseIndex]:F1}");
		svgFigure.DrawAxes("Distance (" + unitsSiTerm + ")", "Elevation (" + unitsSiTerm + ")", "", title);
		svgFigure.BeginClip();
		svgFigure.PlotLine(subArray, subArray2, "#333", 2.5);
		float[] floatValues = new float[subArray.Length];
		for (int i = 0; i < floatValues.Length; i++)
		{
			floatValues[i] = ((wse > subArray2[i]) ? wse : float.NaN);
		}
		svgFigure.PlotLine(subArray, floatValues, "#2196F3");
		svgFigure.EndClip();
		svgFigure.SetPlotArea(0.0, 260.0, 900.0, 520.0);
		svgFigure.SetMargins(80.0, 30.0, 30.0, 45.0);
		float cellVelocity2Term = 0.01f;
		foreach (float cellVelocity2 in cellVelocity)
		{
			if (cellVelocity2 > cellVelocity2Term)
			{
				cellVelocity2Term = cellVelocity2;
			}
		}
		cellVelocity2Term *= 1.1f;
		MinAndMax(cellStations, out var minimum2, out var maximum2);
		svgFigure.SetLimits(minimum2, maximum2, 0.0, cellVelocity2Term);
		svgFigure.DrawAxes("Distance (" + unitsSiTerm + ")", "Velocity (" + path + ")", "", boundaryScaled ? "Boundary-Scaled LDM Velocity" : null);
		svgFigure.BeginClip();
		svgFigure.PlotLine(cellStations, cellVelocity, "#F44336", 2.5);
		svgFigure.EndClip();
		string boundaryScaledTerm = (boundaryScaled ? "Boundary-scaled LDM" : "LDM");
		svgFigure.DrawLegend(new (string, string, string)[1] { (boundaryScaledTerm, "#F44336", "") }, "top-right", svgFigure.PlotRight - (double)(boundaryScaled ? 190 : 150), svgFigure.PlotTop + 15.0);
		double computed = 830.0;
		svgFigure.DrawText(80.0, computed, "Results Summary", 14.0, "bold");
		List<string> stringList = new List<string> { "Method", "Q", "K" };
		if (boundaryScaled)
		{
			stringList.Add("Velocity scale");
		}
		if (result.HasAlpha)
		{
			stringList.Add("alpha");
		}
		if (result.HasBeta)
		{
			stringList.Add("beta");
		}
		if (result.HasDispersion)
		{
			stringList.Add("Dispersion");
		}
		List<string[]> stringsList = new List<string[]>();
		List<string> stringListList = new List<string>
		{
			"LDM result",
			Fmt(flow),
			Fmt(result.Conveyance[wseIndex])
		};
		if (boundaryScaled)
		{
			stringListList.Add("1");
		}
		if (result.HasAlpha)
		{
			stringListList.Add(Fmt(result.Alpha[wseIndex]));
		}
		if (result.HasBeta)
		{
			stringListList.Add(Fmt(result.Beta[wseIndex]));
		}
		if (result.HasDispersion)
		{
			stringListList.Add(Fmt(result.Dispersion[wseIndex]));
		}
		stringsList.Add(stringListList.ToArray());
		if (boundaryScaled)
		{
			List<string> stringList2 = new List<string>
			{
				"Boundary-scaled",
				Fmt(plottedFlow),
				Fmt(result.Conveyance[wseIndex]),
				Fmt(plottedFlowOverFlow)
			};
			if (result.HasAlpha)
			{
				stringList2.Add("");
			}
			if (result.HasBeta)
			{
				stringList2.Add("");
			}
			if (result.HasDispersion)
			{
				stringList2.Add("");
			}
			stringsList.Add(stringList2.ToArray());
		}
		svgFigure.DrawTable(80.0, computed + 10.0, stringList.ToArray(), stringsList.ToArray());
		return svgFigure.Render();
	}

	public static string GenerateMultiWsePlot(LDMResult result)
	{
		ArgumentNullException.ThrowIfNull(result, "result");
		if (!result.HasVelocityDistribution)
		{
			throw new ArgumentException("The result does not contain velocity distributions.", "result");
		}
		int height = 800;
		SvgFigure svgFigure = new SvgFigure(900, height);
		string unitsSiTerm = ((result.Units == UnitSystem.SI) ? "m" : "ft");
		string path = ((result.Units == UnitSystem.SI) ? "m/s" : "ft/s");
		int solvedWseCount = result.SolvedWseCount;
		if (solvedWseCount == 0)
		{
			return svgFigure.Render();
		}
		int cellCountOffset = result.Geometry.CellCount + 1;
		float[] subArray = result.Geometry.FaceStations[..cellCountOffset];
		float[] subArray2 = result.Geometry.FaceBedElevations[..cellCountOffset];
		float[] cellStations = result.Geometry.GetCellStations();
		svgFigure.SetPlotArea(0.0, 0.0, 900.0, 260.0);
		svgFigure.SetMargins(80.0, 30.0, 30.0, 45.0);
		MinAndMax(subArray, out var minimum, out var maximum);
		float subArray2Term = subArray2.Min() - 0.5f;
		float waterSurfaceTerm = subArray2.Max();
		for (int i = 0; i < solvedWseCount; i++)
		{
			waterSurfaceTerm = Math.Max(waterSurfaceTerm, result.Wse[i]);
		}
		waterSurfaceTerm += 0.5f;
		svgFigure.SetLimits(minimum, maximum, subArray2Term, waterSurfaceTerm);
		svgFigure.DrawAxes("Distance (" + unitsSiTerm + ")", "Elevation (" + unitsSiTerm + ")", "", result.Name + ": Cross-Section");
		svgFigure.BeginClip();
		svgFigure.PlotLine(subArray, subArray2, "#333", 2.5);
		for (int j = 0; j < solvedWseCount; j++)
		{
			float[] floatValues = new float[subArray.Length];
			float wse = result.Wse[j];
			for (int k = 0; k < floatValues.Length; k++)
			{
				floatValues[k] = ((wse > subArray2[k]) ? wse : float.NaN);
			}
			svgFigure.PlotLine(subArray, floatValues, Colors[j % Colors.Length], 1.5);
		}
		svgFigure.EndClip();
		svgFigure.SetPlotArea(0.0, 260.0, 900.0, 520.0);
		svgFigure.SetMargins(80.0, 30.0, 30.0, 45.0);
		MinAndMax(cellStations, out var minimum2, out var maximum2);
		float cellVelocity2Term = 0.01f;
		for (int l = 0; l < solvedWseCount; l++)
		{
			float[] cellVelocity = result.CellVelocity[l];
			foreach (float cellVelocity2 in cellVelocity)
			{
				if (cellVelocity2 > cellVelocity2Term)
				{
					cellVelocity2Term = cellVelocity2;
				}
			}
		}
		cellVelocity2Term *= 1.1f;
		svgFigure.SetLimits(minimum2, maximum2, 0.0, cellVelocity2Term);
		svgFigure.DrawAxes("Distance (" + unitsSiTerm + ")", "Velocity (" + path + ")", "", "LDM Velocity Profiles");
		svgFigure.BeginClip();
		List<(string, string, string)> valueTuples = new List<(string, string, string)>();
		for (int n = 0; n < solvedWseCount; n++)
		{
			string color = Colors[n % Colors.Length];
			svgFigure.PlotLine(cellStations, result.CellVelocity[n], color);
			valueTuples.Add(($"WSE={result.Wse[n]:F3}", color, ""));
		}
		svgFigure.EndClip();
		svgFigure.DrawLegend(valueTuples.ToArray(), "top-right", svgFigure.PlotRight - 160.0, svgFigure.PlotTop + 15.0);
		return svgFigure.Render();
	}

	public static string GeneratePropertyCurvesPlot(LDMResult r)
	{
		ArgumentNullException.ThrowIfNull(r, "r");
		bool hasAlphaOrHasBeta = r.HasAlpha || r.HasBeta;
		bool hasDispersion = r.HasDispersion;
		int hasAlphaOrHasBetaHasDispersionTerm = 3 + (hasAlphaOrHasBeta ? 1 : 0) + (hasDispersion ? 1 : 0);
		int stride = 275;
		int height = hasAlphaOrHasBetaHasDispersionTerm * 260 + (hasAlphaOrHasBetaHasDispersionTerm - 1) * 15 + 20;
		SvgFigure fig = new SvgFigure(900, height);
		string unitsSiTerm = ((r.Units == UnitSystem.SI) ? "m" : "ft");
		string unitsSiTerm2 = ((r.Units == UnitSystem.SI) ? "m²" : "ft²");
		string path = ((r.Units == UnitSystem.SI) ? "m³/s" : "ft³/s");
		int solvedWseCount = r.SolvedWseCount;
		if (solvedWseCount == 0)
		{
			return fig.Render();
		}
		float[] subArray = r.Wse[..solvedWseCount];
		float[] subArray2 = r.Area[..solvedWseCount];
		float[] subArray3 = r.WettedPerimeter[..solvedWseCount];
		float[] subArray4 = r.Conveyance[..solvedWseCount];
		float wseMin = subArray[0];
		float wseMax = subArray[^1];
		float wseMaxWseMinTerm = (wseMax - wseMin) * 0.05f;
		wseMin -= wseMaxWseMinTerm;
		wseMax += wseMaxWseMinTerm;
		int pi = 0;
		MinAndMax(subArray2, out var minimum, out var maximum);
		float maximumMinimumTerm = (maximum - minimum) * 0.05f;
		SetupPanel(minimum, maximum + maximumMinimumTerm);
		fig.DrawAxes("Area (" + unitsSiTerm2 + ")", "WSE (" + unitsSiTerm + ")", "", "Area");
		fig.BeginClip();
		fig.PlotLine(subArray2, subArray, "#2196F3", 2.5);
		fig.PlotScatter(subArray2, subArray, "#2196F3", 3.0, 0.6);
		fig.EndClip();
		pi++;
		MinAndMax(subArray3, out var minimum2, out var maximum2);
		float maximum2Minimum2Term = (maximum2 - minimum2) * 0.05f;
		SetupPanel(minimum2, maximum2 + maximum2Minimum2Term);
		fig.DrawAxes("Wetted Perimeter (" + unitsSiTerm + ")", "WSE (" + unitsSiTerm + ")", "", "Wetted Perimeter");
		fig.BeginClip();
		fig.PlotLine(subArray3, subArray, "#4CAF50", 2.5);
		fig.PlotScatter(subArray3, subArray, "#4CAF50", 3.0, 0.6);
		fig.EndClip();
		pi++;
		float subArray4Term = subArray4.Max() * 1.05f;
		SetupPanel(0.0, subArray4Term);
		fig.DrawAxes("Conveyance (" + path + ")", "WSE (" + unitsSiTerm + ")", "", "Conveyance");
		fig.BeginClip();
		fig.PlotLine(subArray4, subArray, "#FF9800", 2.5);
		fig.PlotScatter(subArray4, subArray, "#FF9800", 3.0, 0.6);
		fig.EndClip();
		pi++;
		if (hasAlphaOrHasBeta)
		{
			float[] alpha = (r.HasAlpha ? r.Alpha[..solvedWseCount] : Array.Empty<float>());
			float[] beta = (r.HasBeta ? r.Beta[..solvedWseCount] : Array.Empty<float>());
			float unity = 1f;
			for (int i = 0; i < alpha.Length; i++)
			{
				if (alpha[i] > unity)
				{
					unity = alpha[i];
				}
			}
			for (int j = 0; j < beta.Length; j++)
			{
				if (beta[j] > unity)
				{
					unity = beta[j];
				}
			}
			float computed = unity * 0.05f;
			SetupPanel(0.0, unity + computed);
			fig.DrawAxes("Coefficient", "WSE (" + unitsSiTerm + ")", "", "Velocity Weighting & Momentum Coefficients");
			fig.BeginClip();
			List<(string, string, string)> valueTuples = new List<(string, string, string)>();
			if (r.HasAlpha)
			{
				fig.PlotLine(alpha, subArray, "#9C27B0", 2.5);
				fig.PlotScatter(alpha, subArray, "#9C27B0", 3.0, 0.6);
				valueTuples.Add(("Velocity Weighting", "#9C27B0", ""));
			}
			if (r.HasBeta)
			{
				fig.PlotLine(beta, subArray, "#00BCD4", 2.5, "8,4");
				fig.PlotScatter(beta, subArray, "#00BCD4", 3.0, 0.6);
				valueTuples.Add(("Momentum", "#00BCD4", "8,4"));
			}
			fig.EndClip();
			fig.DrawLegend(valueTuples.ToArray(), "top-right", fig.PlotRight - 180.0, fig.PlotTop + 15.0);
			pi++;
		}
		if (hasDispersion)
		{
			float[] subArray5 = r.Dispersion[..solvedWseCount];
			float computed2 = subArray5.Max() * 1.05f;
			if (computed2 < 0.01f)
			{
				computed2 = 0.01f;
			}
			SetupPanel(0.0, computed2);
			fig.DrawAxes("Dispersion", "WSE (" + unitsSiTerm + ")", "", "Dispersion");
			fig.BeginClip();
			fig.PlotLine(subArray5, subArray, "#F44336", 2.5);
			fig.PlotScatter(subArray5, subArray, "#F44336", 3.0, 0.6);
			fig.EndClip();
			pi++;
		}
		return fig.Render();
		void SetupPanel(double xMin, double xMax)
		{
			int piTimesStride = pi * stride;
			fig.SetPlotArea(0.0, piTimesStride, 900.0, 260.0);
			fig.SetMargins(80.0, 30.0, 30.0, 45.0);
			fig.SetLimits(xMin, xMax, wseMin, wseMax);
		}
	}

	public static string GenerateMethodComparisonPlot(LDMResult ldm, DCMResult dcm, SCMResult scm)
	{
		ArgumentNullException.ThrowIfNull(ldm, "ldm");
		ValidateMatchingUnits(ldm.Units, dcm?.Units, "dcm");
		ValidateMatchingUnits(ldm.Units, scm?.Units, "scm");
		bool hasAlpha = ldm.HasAlpha || (dcm?.HasAlpha ?? false);
		bool hasBeta = ldm.HasBeta || (dcm?.HasBeta ?? false);
		bool hasDispersion = ldm.HasDispersion;
		int hasAlphaHasBetaTerm = 1 + (hasAlpha ? 1 : 0) + (hasBeta ? 1 : 0) + (hasDispersion ? 1 : 0);
		int stride = 275;
		int height = hasAlphaHasBetaTerm * 260 + (hasAlphaHasBetaTerm - 1) * 15 + 20;
		SvgFigure fig = new SvgFigure(900, height);
		string unitsLdmTerm = ((ldm.Units == UnitSystem.SI) ? "m" : "ft");
		string path = ((ldm.Units == UnitSystem.SI) ? "m³/s" : "ft³/s");
		if (ldm.SolvedWseCount == 0)
		{
			return fig.Render();
		}
		float wseMin = float.MaxValue;
		float wseMax = float.MinValue;
		SpanWse(ldm.Wse, ldm.SolvedWseCount);
		if (dcm != null)
		{
			SpanWse(dcm.Wse, dcm.SolvedWseCount);
		}
		if (scm != null)
		{
			SpanWse(scm.Wse, scm.SolvedWseCount);
		}
		float wseMaxWseMinTerm = (wseMax - wseMin) * 0.05f;
		wseMin -= wseMaxWseMinTerm;
		wseMax += wseMaxWseMinTerm;
		int pi = 0;
		float curveMax = CurveMax(ldm.SolvedWseCount, ldm.Conveyance);
		if (scm != null)
		{
			curveMax = MathF.Max(curveMax, CurveMax(scm.SolvedWseCount, scm.Conveyance));
		}
		if (dcm != null)
		{
			curveMax = MathF.Max(curveMax, CurveMax(dcm.SolvedWseCount, dcm.Conveyance));
		}
		curveMax *= 1.05f;
		SetupPanel(0.0, curveMax);
		fig.DrawAxes("Conveyance (" + path + ")", "WSE (" + unitsLdmTerm + ")", "", "Conveyance — LDM vs SCM vs DCM");
		fig.BeginClip();
		List<(string, string, string)> valueTuples = new List<(string, string, string)>();
		PlotCurve(ldm.Wse, ldm.SolvedWseCount, ldm.Conveyance, "#F44336", 2.5, "");
		valueTuples.Add(("LDM", "#F44336", ""));
		if (scm != null)
		{
			PlotCurve(scm.Wse, scm.SolvedWseCount, scm.Conveyance, "#2196F3", 2.0, "8,4");
			valueTuples.Add(("SCM", "#2196F3", "8,4"));
		}
		if (dcm != null)
		{
			PlotCurve(dcm.Wse, dcm.SolvedWseCount, dcm.Conveyance, "#4CAF50", 2.0, "4,4");
			valueTuples.Add(("DCM", "#4CAF50", "4,4"));
		}
		fig.EndClip();
		fig.DrawLegend(valueTuples.ToArray(), "top-right", fig.PlotRight - 120.0, fig.PlotTop + 15.0);
		pi++;
		if (hasAlpha)
		{
			float unity = 1f;
			if (ldm.HasAlpha)
			{
				unity = MathF.Max(unity, CurveMax(ldm.SolvedWseCount, ldm.Alpha));
			}
			if (dcm != null && dcm.HasAlpha)
			{
				unity = MathF.Max(unity, CurveMax(dcm.SolvedWseCount, dcm.Alpha));
			}
			SetupPanel(0.0, unity * 1.05f);
			fig.DrawAxes("Alpha", "WSE (" + unitsLdmTerm + ")", "", "Velocity Weighting (Alpha) — LDM vs DCM");
			fig.BeginClip();
			List<(string, string, string)> valueTuplesList = new List<(string, string, string)>();
			if (ldm.HasAlpha)
			{
				PlotCurve(ldm.Wse, ldm.SolvedWseCount, ldm.Alpha, "#F44336", 2.5, "");
				valueTuplesList.Add(("LDM", "#F44336", ""));
			}
			if (dcm != null && dcm.HasAlpha)
			{
				PlotCurve(dcm.Wse, dcm.SolvedWseCount, dcm.Alpha, "#4CAF50", 2.0, "4,4");
				valueTuplesList.Add(("DCM", "#4CAF50", "4,4"));
			}
			fig.EndClip();
			fig.DrawLegend(valueTuplesList.ToArray(), "top-right", fig.PlotRight - 120.0, fig.PlotTop + 15.0);
			pi++;
		}
		if (hasBeta)
		{
			float unity2 = 1f;
			if (ldm.HasBeta)
			{
				unity2 = MathF.Max(unity2, CurveMax(ldm.SolvedWseCount, ldm.Beta));
			}
			if (dcm != null && dcm.HasBeta)
			{
				unity2 = MathF.Max(unity2, CurveMax(dcm.SolvedWseCount, dcm.Beta));
			}
			SetupPanel(0.0, unity2 * 1.05f);
			fig.DrawAxes("Beta", "WSE (" + unitsLdmTerm + ")", "", "Momentum (Beta) — LDM vs DCM");
			fig.BeginClip();
			List<(string, string, string)> valueTuples2 = new List<(string, string, string)>();
			if (ldm.HasBeta)
			{
				PlotCurve(ldm.Wse, ldm.SolvedWseCount, ldm.Beta, "#F44336", 2.5, "");
				valueTuples2.Add(("LDM", "#F44336", ""));
			}
			if (dcm != null && dcm.HasBeta)
			{
				PlotCurve(dcm.Wse, dcm.SolvedWseCount, dcm.Beta, "#4CAF50", 2.0, "4,4");
				valueTuples2.Add(("DCM", "#4CAF50", "4,4"));
			}
			fig.EndClip();
			fig.DrawLegend(valueTuples2.ToArray(), "top-right", fig.PlotRight - 120.0, fig.PlotTop + 15.0);
			pi++;
		}
		if (hasDispersion)
		{
			float curveMaxSolvedWseCountTerm = 0.01f;
			if (ldm.HasDispersion)
			{
				curveMaxSolvedWseCountTerm = MathF.Max(curveMaxSolvedWseCountTerm, CurveMax(ldm.SolvedWseCount, ldm.Dispersion));
			}
			SetupPanel(0.0, curveMaxSolvedWseCountTerm * 1.05f);
			fig.DrawAxes("Dispersion", "WSE (" + unitsLdmTerm + ")", "", "Dispersion — LDM vs DCM");
			fig.BeginClip();
			List<(string, string, string)> valueTuples3 = new List<(string, string, string)>();
			if (ldm.HasDispersion)
			{
				PlotCurve(ldm.Wse, ldm.SolvedWseCount, ldm.Dispersion, "#F44336", 2.5, "");
				valueTuples3.Add(("LDM", "#F44336", ""));
			}
			fig.EndClip();
			fig.DrawLegend(valueTuples3.ToArray(), "top-right", fig.PlotRight - 120.0, fig.PlotTop + 15.0);
			pi++;
		}
		return fig.Render();
		static float CurveMax(int solvedWseCount, float[] curve)
		{
			if (solvedWseCount <= 0)
			{
				return 0f;
			}
			return curve[..solvedWseCount].Max();
		}
		void PlotCurve(float[] wse, int solvedWseCount, float[] curve, string color, double width, string dash)
		{
			fig.PlotLine(curve[..solvedWseCount], wse[..solvedWseCount], color, width, dash);
		}
		void SetupPanel(double xMin, double xMax)
		{
			int piTimesStride = pi * stride;
			fig.SetPlotArea(0.0, piTimesStride, 900.0, 260.0);
			fig.SetMargins(80.0, 30.0, 30.0, 45.0);
			fig.SetLimits(xMin, xMax, wseMin, wseMax);
		}
		void SpanWse(float[] wse, int solvedWseCount)
		{
			if (solvedWseCount != 0)
			{
				wseMin = MathF.Min(wseMin, wse[0]);
				wseMax = MathF.Max(wseMax, wse[solvedWseCount - 1]);
			}
		}
	}

	public static string GenerateLdmComparisonPlot(LDMResult a, LDMResult b, string labelA = "Original", string labelB = "Compact", bool[] keepMask = null)
	{
		ArgumentNullException.ThrowIfNull(a, "a");
		ArgumentNullException.ThrowIfNull(b, "b");
		ValidateMatchingUnits(a.Units, b.Units, "b");
		bool hasAlphaOrHasAlpha = a.HasAlpha || b.HasAlpha;
		bool hasBetaOrHasBeta = a.HasBeta || b.HasBeta;
		bool hasDispersionOrHasDispersion = a.HasDispersion || b.HasDispersion;
		int hasAlphaOrHasAlphaHasBetaOrHasBetaTerm = 3 + (hasAlphaOrHasAlpha ? 1 : 0) + (hasBetaOrHasBeta ? 1 : 0) + (hasDispersionOrHasDispersion ? 1 : 0);
		int stride = 275;
		int height = hasAlphaOrHasAlphaHasBetaOrHasBetaTerm * 260 + (hasAlphaOrHasAlphaHasBetaOrHasBetaTerm - 1) * 15 + 20;
		SvgFigure fig = new SvgFigure(900, height);
		string du = ((a.Units == UnitSystem.SI) ? "m" : "ft");
		string unitsSiTerm = ((a.Units == UnitSystem.SI) ? "m²" : "ft²");
		string path = ((a.Units == UnitSystem.SI) ? "m³/s" : "ft³/s");
		int solvedWseCount = a.SolvedWseCount;
		int solvedWseCount2 = b.SolvedWseCount;
		if (solvedWseCount == 0 || solvedWseCount2 == 0)
		{
			return fig.Render();
		}
		float[] wseA = a.Wse[..solvedWseCount];
		float[] wseB = b.Wse[..solvedWseCount2];
		float wseMin = MathF.Min(wseA[0], wseB[0]);
		float wseMax = MathF.Max(wseA[^1], wseB[^1]);
		float wseMaxWseMinTerm = (wseMax - wseMin) * 0.05f;
		wseMin -= wseMaxWseMinTerm;
		wseMax += wseMaxWseMinTerm;
		int pi = 0;
		float[] subArray = a.Area[..solvedWseCount];
		float[] subArray2 = b.Area[..solvedWseCount2];
		float[] subArray3 = a.WettedPerimeter[..solvedWseCount];
		float[] subArray4 = b.WettedPerimeter[..solvedWseCount2];
		float[] subArray5 = a.Conveyance[..solvedWseCount];
		float[] subArray6 = b.Conveyance[..solvedWseCount2];
		DrawPair(subArray, subArray2, "Area", "Area (" + unitsSiTerm + ")", MathF.Min(subArray.Min(), subArray2.Min()), MathF.Max(subArray.Max(), subArray2.Max()), "bottom-right");
		DrawPair(subArray3, subArray4, "Wetted Perimeter", "Wetted Perimeter (" + du + ")", MathF.Min(subArray3.Min(), subArray4.Min()), MathF.Max(subArray3.Max(), subArray4.Max()), "bottom-right");
		DrawPair(subArray5, subArray6, "Conveyance", "Conveyance (" + path + ")", 0f, MathF.Max(subArray5.Max(), subArray6.Max()), "bottom-right");
		if (hasAlphaOrHasAlpha)
		{
			float[] alpha = (a.HasAlpha ? a.Alpha[..solvedWseCount] : Array.Empty<float>());
			float[] alphaArray = (b.HasAlpha ? b.Alpha[..solvedWseCount2] : Array.Empty<float>());
			float xMaxVal = MathF.Max((alpha.Length != 0) ? alpha.Max() : 1f, (alphaArray.Length != 0) ? alphaArray.Max() : 1f);
			DrawPair(alpha, alphaArray, "Velocity Weighting (Alpha)", "Alpha", 0f, xMaxVal, "top-left");
		}
		if (hasBetaOrHasBeta)
		{
			float[] beta = (a.HasBeta ? a.Beta[..solvedWseCount] : Array.Empty<float>());
			float[] betaArray = (b.HasBeta ? b.Beta[..solvedWseCount2] : Array.Empty<float>());
			float xMaxVal2 = MathF.Max((beta.Length != 0) ? beta.Max() : 1f, (betaArray.Length != 0) ? betaArray.Max() : 1f);
			DrawPair(beta, betaArray, "Momentum (Beta)", "Beta", 0f, xMaxVal2, "top-left");
		}
		if (hasDispersionOrHasDispersion)
		{
			float[] dispersion = (a.HasDispersion ? a.Dispersion[..solvedWseCount] : Array.Empty<float>());
			float[] dispersionArray = (b.HasDispersion ? b.Dispersion[..solvedWseCount2] : Array.Empty<float>());
			float xMaxVal3 = MathF.Max((dispersion.Length != 0) ? dispersion.Max() : 0.01f, (dispersionArray.Length != 0) ? dispersionArray.Max() : 0.01f);
			DrawPair(dispersion, dispersionArray, "Dispersion", "Dispersion", 0f, xMaxVal3);
		}
		return fig.Render();
		void DrawKeepMaskRefs(float xLo, float xHi)
		{
			if (keepMask != null)
			{
				int count = Math.Min(keepMask.Length, wseA.Length);
				float[] x = new float[2] { xLo, xHi };
				float[] floatValues = new float[2];
				for (int i = 0; i < count; i++)
				{
					if (keepMask[i])
					{
						floatValues[0] = wseA[i];
						floatValues[1] = wseA[i];
						fig.PlotLine(x, floatValues, "#F44336", 1.0);
					}
				}
			}
		}
		void DrawPair(float[] xA, float[] xB, string title, string xAxis, float xMinVal, float computed, string legendPos = "top-right")
		{
			float computed2 = (computed - xMinVal) * 0.05f;
			if (computed2 <= 0f)
			{
				computed2 = MathF.Max(0.01f * MathF.Abs(computed), 0.01f);
			}
			float computed3 = computed + computed2;
			SetupPanel(xMinVal, computed3);
			fig.DrawAxes(xAxis, "WSE (" + du + ")", "", title);
			fig.BeginClip();
			DrawKeepMaskRefs(xMinVal, computed3);
			List<(string, string, string)> valueTuples = new List<(string, string, string)>();
			if (xA.Length != 0)
			{
				fig.PlotLine(xA, wseA, "#2196F3", 2.5);
				fig.PlotScatter(xA, wseA, "#2196F3");
				valueTuples.Add(($"{labelA} ({xA.Length})", "#2196F3", ""));
			}
			if (xB.Length != 0)
			{
				fig.PlotLine(xB, wseB, "#F44336", 2.0, "--");
				fig.PlotScatter(xB, wseB, "#F44336");
				valueTuples.Add(($"{labelB} ({xB.Length})", "#F44336", "--"));
			}
			fig.EndClip();
			fig.DrawLegend(valueTuples.ToArray(), legendPos);
			pi++;
		}
		void SetupPanel(double xMin, double xMax)
		{
			int piTimesStride = pi * stride;
			fig.SetPlotArea(0.0, piTimesStride, 900.0, 260.0);
			fig.SetMargins(80.0, 30.0, 30.0, 45.0);
			fig.SetLimits(xMin, xMax, wseMin, wseMax);
		}
	}

	public static string GenerateFacePropertyComparisonPlot(string title, float[] wseExisting, float[] convExisting, float[] areaExisting, float[] perimExisting, float[] wseLdm, float[] convScm, float[] areaScm, float[] perimScm, float[] convLdm, float[] areaLdm, float[] perimLdm, UnitSystem units)
	{
		int stride = 275;
		int height = 830;
		SvgFigure fig = new SvgFigure(900, height);
		string du = ((units == UnitSystem.SI) ? "m" : "ft");
		string unitsSiTerm = ((units == UnitSystem.SI) ? "m²" : "ft²");
		string path = ((units == UnitSystem.SI) ? "m³/s" : "ft³/s");
		if (wseExisting.Length == 0 && wseLdm.Length == 0)
		{
			return fig.Render();
		}
		float wseMin = float.MaxValue;
		float wseMax = float.MinValue;
		if (wseExisting.Length != 0)
		{
			wseMin = MathF.Min(wseMin, wseExisting[0]);
			wseMax = MathF.Max(wseMax, wseExisting[^1]);
		}
		if (wseLdm.Length != 0)
		{
			wseMin = MathF.Min(wseMin, wseLdm[0]);
			wseMax = MathF.Max(wseMax, wseLdm[^1]);
		}
		float wseMaxWseMinTerm = (wseMax - wseMin) * 0.05f;
		wseMin -= wseMaxWseMinTerm;
		wseMax += wseMaxWseMinTerm;
		int pi = 0;
		string title2 = (string.IsNullOrEmpty(title) ? "Conveyance" : (title + ": Conveyance"));
		DrawTriple(convExisting, convScm, convLdm, title2, "Conveyance (" + path + ")", "bottom-right");
		DrawTriple(areaExisting, areaScm, areaLdm, "Area", "Area (" + unitsSiTerm + ")", "bottom-right");
		DrawTriple(perimExisting, perimScm, perimLdm, "Wetted Perimeter", "Wetted Perimeter (" + du + ")", "bottom-right");
		return fig.Render();
		void DrawTriple(float[] existing, float[] scm, float[] ldm, string title3, string xAxis, string legendPos)
		{
			float computed = 0.01f;
			if (existing.Length != 0)
			{
				computed = MathF.Max(computed, existing.Max());
			}
			if (scm.Length != 0)
			{
				computed = MathF.Max(computed, scm.Max());
			}
			if (ldm.Length != 0)
			{
				computed = MathF.Max(computed, ldm.Max());
			}
			computed *= 1.05f;
			int piTimesStride = pi * stride;
			fig.SetPlotArea(0.0, piTimesStride, 900.0, 260.0);
			fig.SetMargins(80.0, 30.0, 30.0, 45.0);
			fig.SetLimits(0.0, computed, wseMin, wseMax);
			fig.DrawAxes(xAxis, "WSE (" + du + ")", "", title3);
			fig.BeginClip();
			List<(string, string, string)> valueTuples = new List<(string, string, string)>();
			if (existing.Length != 0)
			{
				fig.PlotLine(existing, wseExisting, "#2196F3", 2.5);
				fig.PlotScatter(existing, wseExisting, "#2196F3", 3.0, 0.6);
				valueTuples.Add(($"Existing (tables) ({existing.Length} points)", "#2196F3", ""));
			}
			if (scm.Length != 0)
			{
				fig.PlotLine(scm, wseLdm, "#4CAF50", 2.0, "4,4");
				fig.PlotScatter(scm, wseLdm, "#4CAF50", 3.0, 0.6);
				valueTuples.Add(($"SCM (LDM) ({scm.Length} points)", "#4CAF50", "4,4"));
			}
			if (ldm.Length != 0)
			{
				fig.PlotLine(ldm, wseLdm, "#F44336", 2.0, "8,4");
				fig.PlotScatter(ldm, wseLdm, "#F44336", 3.0, 0.6);
				valueTuples.Add(($"LDM ({ldm.Length} points)", "#F44336", "8,4"));
			}
			fig.EndClip();
			fig.DrawLegend(valueTuples.ToArray(), legendPos);
			pi++;
		}
	}

	public static void SaveSvg(string svg, string filePath)
	{
		File.WriteAllText(filePath, svg);
		Console.WriteLine("Plot saved: " + filePath);
	}

	private static void ValidateMatchingUnits(UnitSystem expected, UnitSystem? actual, string parameterName)
	{
		if (actual.HasValue && actual.Value != expected)
		{
			throw new ArgumentException("All plotted results must use the same unit system.", parameterName);
		}
	}

	private static void MinAndMax(float[] values, out float minimum, out float maximum)
	{
		minimum = values[0];
		maximum = values[0];
		for (int i = 1; i < values.Length; i++)
		{
			if (values[i] < minimum)
			{
				minimum = values[i];
			}
			if (values[i] > maximum)
			{
				maximum = values[i];
			}
		}
	}

	private static string Fmt(float v)
	{
		return v.ToString("F2", CultureInfo.InvariantCulture);
	}
}
