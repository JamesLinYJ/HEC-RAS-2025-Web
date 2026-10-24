using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Utility.LightweightPlotting;

public static class SvgFigureTests
{
	private const string MinorGridMark = "stroke='#eee' stroke-dasharray='4,3'";

	private const string MajorGridMark = "stroke='#ddd'";

	private const string TickMark = "stroke='#333' stroke-width='1'";

	public static void RunAll(string outputDir)
	{
		Console.WriteLine("SvgPlotterTests: writing to " + outputDir);
		TestBasicLine(outputDir);
		TestMultiLineWithLegend(outputDir);
		TestScatterPlot(outputDir);
		TestNaNHandling(outputDir);
		TestSubplots(outputDir);
		TestCustomPanels(outputDir);
		TestTable(outputDir);
		TestDualYAxis(outputDir);
		TestLineMarkers(outputDir);
		TestLogScale(outputDir);
		TestTickStyles(outputDir);
		TestGridLines(outputDir);
		TestMinorTicksLinear(outputDir);
		TestMinorTicksLog(outputDir);
		TestMinorTicksSubDecadeLog(outputDir);
		TestMinorTicksDefaults(outputDir);
		TestMinorTicksDualAxis(outputDir);
		TestMesh(outputDir);
		TestMeshWithEdges(outputDir);
		TestMixedMesh(outputDir);
		TestMeshLabels(outputDir);
		TestBarPlot(outputDir);
		TestGroupedBarPlot(outputDir);
		TestLegendPositions(outputDir);
		TestShorthandColors(outputDir);
		TestPlotFill(outputDir);
		TestRefLines(outputDir);
		TestErrorBars(outputDir);
		TestStackedBar(outputDir);
		TestHorizontalBar(outputDir);
		TestAnnotationsArrows(outputDir);
		TestHistogram(outputDir);
		TestColorBar(outputDir);
		TestCustomTickLabels(outputDir);
		TestPieChart(outputDir);
		TestTimeLine(outputDir);
		TestTimeLineNaN(outputDir);
		TestTimeLineMultiSeries(outputDir);
		TestAxisReverse(outputDir);
		TestY2Reverse(outputDir);
		TestYTitleNoOverlap(outputDir);
		TestZoomInteractions(outputDir);
		TestSaveAsPng(outputDir);
		TestSaveAsPdf(outputDir);
		TestImageBackdrop(outputDir);
		TestVelocityArrowsSwirl(outputDir);
		TestVelocityArrowsMixedFootprint(outputDir);
		TestVelocityArrowsPalette(outputDir);
		TestVelocityArrowsLayering(outputDir);
		Console.WriteLine("SvgPlotterTests: all tests complete.");
	}

	public static void TestZoomInteractions(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		svgFigure.SetLimits(0.0, 10.0, 0.0, 100.0);
		svgFigure.DrawAxes("X", "Y", "", "Zoom: wheel, drag, double-click/Home to reset");
		svgFigure.PlotLine(new int[6] { 0, 2, 4, 6, 8, 10 }, new int[6] { 0, 4, 16, 36, 64, 100 }, "#2196F3");
		svgFigure.EnableZoom();
		string render = svgFigure.Render();
		svgFigure.Save(Path.Combine(dir, "test_zoom.svg"));
		if (!render.Contains("tabindex='0'", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Zoom-enabled SVG should be keyboard focusable");
		}
		if (!render.Contains("addEventListener('dblclick'", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Zoom-enabled SVG should reset on double-click");
		}
		if (!render.Contains("evt.key !== 'Home'", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Zoom-enabled SVG should reset when Home is pressed");
		}
	}

	public static void TestImageBackdrop(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		svgFigure.SetLimits(0.0, 10.0, 0.0, 10.0);
		byte[] imageBytes = BuildGradientPng(16, 16);
		svgFigure.DrawAxes("X", "Y", "", "ImageBackdrop: mesh over embedded PNG");
		svgFigure.BeginClip();
		svgFigure.PlotImage(imageBytes, "image/png", 0.0, 10.0, 0.0, 10.0);
		int count = 9;
		int count2 = 9;
		(double, double)[] valueTuples = new (double, double)[count * count2];
		for (int i = 0; i < count2; i++)
		{
			for (int j = 0; j < count; j++)
			{
				valueTuples[i * count + j] = ((double)j * 10.0 / (double)(count - 1), (double)i * 10.0 / (double)(count2 - 1));
			}
		}
		List<int[]> intValuesList = new List<int[]>();
		for (int k = 0; k < count2 - 1; k++)
		{
			for (int l = 0; l < count - 1; l++)
			{
				intValuesList.Add(new int[4]
				{
					k * count + l,
					k * count + l + 1,
					(k + 1) * count + l + 1,
					(k + 1) * count + l
				});
			}
		}
		svgFigure.PlotMesh(valueTuples, intValuesList, "rgba(0,0,0,0)", "#222", 0.4);
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_image_backdrop.svg"));
	}

	private static byte[] BuildGradientPng(int w, int h)
	{
		int[] intValues = new int[w * h];
		for (int i = 0; i < h; i++)
		{
			for (int j = 0; j < w; j++)
			{
				int ordinal = j * 255 / Math.Max(1, w - 1);
				int ordinal2 = i * 255 / Math.Max(1, h - 1);
				int ordinal3 = 255 - ordinal;
				intValues[i * w + j] = -16777216 | (ordinal << 16) | (ordinal2 << 8) | ordinal3;
			}
		}
		return PngEncoder.EncodeArgb(intValues, w, h);
	}

	public static void TestSaveAsPng(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 10.0, 100);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		svgFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		svgFigure.DrawAxes("X", "sin(x)", "", "SaveAsPng: SVG + PNG side-by-side");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.EndClip();
		string path = Path.Combine(dir, "test_save_as_png.svg");
		svgFigure.Save(path);
		svgFigure.SaveAsPng(path);
	}

	public static void TestSaveAsPdf(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 10.0, 100);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		svgFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		svgFigure.DrawAxes("X", "sin(x)", "", "SaveAsPdf: SVG + PDF side-by-side");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.EndClip();
		string path = Path.Combine(dir, "test_save_as_pdf.svg");
		svgFigure.Save(path);
		svgFigure.SaveAsPdf(path);
	}

	public static void TestAxisReverse(string dir)
	{
		SvgFigure svgFigure = new SvgFigure();
		double[] linspace = Linspace(0.0, 10.0, 60);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		string[] coefficients = new string[4] { "Normal", "XReverse", "YReverse", "XReverse + YReverse" };
		bool[] coefficientsArray = new bool[4] { false, true, false, true };
		bool[] coefficients2 = new bool[4] { false, false, true, true };
		for (int intervalIndex = 0; intervalIndex < 4; intervalIndex++)
		{
			svgFigure.Subplot(2, 2, intervalIndex + 1);
			svgFigure.SetMargins(50.0, 20.0, 30.0, 40.0);
			svgFigure.XReverse = coefficientsArray[intervalIndex];
			svgFigure.YReverse = coefficients2[intervalIndex];
			svgFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
			svgFigure.DrawAxes("X", "sin(x)", "", coefficients[intervalIndex]);
			svgFigure.BeginClip();
			svgFigure.PlotLine(linspace, y, "#2196F3");
			svgFigure.PlotScatter(new double[2] { 0.0, 10.0 }, new double[2] { -1.0, 1.0 }, "#F44336", 5.0);
			svgFigure.EndClip();
			svgFigure.DrawAnnotation(0.5, -1.05, "(0,-1)", 10.0, "#F44336");
			svgFigure.DrawAnnotation(9.5, 1.05, "(10,1)", 10.0, "#F44336", "end");
		}
		svgFigure.Save(Path.Combine(dir, "test_axis_reverse.svg"));
	}

	public static void TestY2Reverse(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 400);
		svgFigure.SetMargins(60.0, 60.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 10.0, 80);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		double[] y2 = linspace.Select((double v) => 50.0 + 30.0 * Math.Cos(v * 0.5)).ToArray();
		svgFigure.SetLimits(0.0, 10.0, -1.5, 1.5);
		svgFigure.SetLimitsY2(0.0, 100.0);
		svgFigure.Y2Reverse = true;
		svgFigure.DrawAxes("Time (s)", "Amplitude (Y)", "Temperature °C (Y2 reversed)", "Y2Reverse: Y normal, Y2 flipped");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.PlotLine(linspace, y2, "#F44336", 2.0, "--", useY2: true);
		svgFigure.EndClip();
		svgFigure.DrawLegend(new (string, string, string)[2]
		{
			("Amplitude (Y)", "#2196F3", ""),
			("Temperature (Y2)", "#F44336", "--")
		});
		svgFigure.Save(Path.Combine(dir, "test_y2_reverse.svg"));
	}

	public static void TestYTitleNoOverlap(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 400);
		svgFigure.SetMargins(60.0, 60.0, 40.0, 50.0);
		double[] linspace = Linspace(0.0, 2500.0, 60);
		double[] y = linspace.Select((double v) => 3.5 + Math.Sin(v * 0.01) * 5E-05).ToArray();
		svgFigure.SetLimits(0.0, 2500.0, 3.4999, 3.50005);
		svgFigure.DrawAxes("Time (s)", "WSE (ft)", "", "Y title vs. wide tick labels");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.EndClip();
		string svg = svgFigure.Render();
		svgFigure.Save(Path.Combine(dir, "test_y_title_no_overlap.svg"));
		double defaultIfEmptyYTickLabelsTerm = (from label in YTickLabels(svg)
			select (double)(label.Length * 10) * 0.6).DefaultIfEmpty(0.0).Max();
		double yTickLabelRightXSvgTerm = YTickLabelRightX(svg) - defaultIfEmptyYTickLabelsTerm;
		double rotatedYTitleX = RotatedYTitleX(svg);
		if (rotatedYTitleX - 9.0 < -0.5)
		{
			throw new InvalidOperationException($"Y-axis title runs off the canvas: left edge {rotatedYTitleX - 9.0:F1} < 0");
		}
		if (rotatedYTitleX + 3.0 > yTickLabelRightXSvgTerm + 0.5)
		{
			throw new InvalidOperationException($"Y-axis title overlaps tick labels: right edge {rotatedYTitleX + 3.0:F1} > label left edge {yTickLabelRightXSvgTerm:F1}");
		}
	}

	private static double YTickLabelRightX(string svg)
	{
		string[] parts = svg.Split('\n');
		foreach (string part in parts)
		{
			if (part.Contains("text-anchor='end'") && part.Contains("font-size='10'"))
			{
				return ExtractXAttr(part);
			}
		}
		throw new InvalidOperationException("No primary-Y tick labels found in SVG.");
	}

	private static IEnumerable<string> YTickLabels(string svg)
	{
		string[] parts = svg.Split('\n');
		foreach (string part in parts)
		{
			if (part.Contains("text-anchor='end'") && part.Contains("font-size='10'"))
			{
				int indexOf = part.IndexOf('>');
				int indexOf2 = part.IndexOf("</text>", StringComparison.Ordinal);
				if (indexOf >= 0 && indexOf2 > indexOf)
				{
					yield return part.Substring(indexOf + 1, indexOf2 - indexOf - 1);
				}
			}
		}
	}

	private static double RotatedYTitleX(string svg)
	{
		string[] parts = svg.Split('\n');
		foreach (string part in parts)
		{
			if (part.Contains("rotate(-90"))
			{
				return ExtractXAttr(part);
			}
		}
		throw new InvalidOperationException("No rotated Y-axis title found in SVG.");
	}

	private static double ExtractXAttr(string line)
	{
		int indexOfLineTerm = line.IndexOf("x='", StringComparison.Ordinal) + "x='".Length;
		int indexOf = line.IndexOf('\'', indexOfLineTerm);
		return double.Parse(line.AsSpan(indexOfLineTerm, indexOf - indexOfLineTerm), CultureInfo.InvariantCulture);
	}

	public static void TestBasicLine(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 10.0, 100);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		svgFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		svgFigure.DrawAxes("X", "sin(x)", "", "Basic Sine Wave");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_basic_line.svg"));
	}

	public static void TestMultiLineWithLegend(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 450);
		svgFigure.SetMargins(60.0, 40.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 6.28, 80);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		double[] y2 = linspace.Select((double v) => Math.Cos(v)).ToArray();
		double[] y3 = linspace.Select((double v) => Math.Exp((0.0 - v) * 0.3) * Math.Sin(v * 2.0)).ToArray();
		svgFigure.SetLimits(0.0, 6.28, -1.3, 1.3);
		svgFigure.DrawAxes("Angle (rad)", "Amplitude", "", "Trigonometric Functions");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.PlotLine(linspace, y2, "#F44336", 2.0, "--");
		svgFigure.PlotLine(linspace, y3, "#4CAF50", 2.0, ":");
		svgFigure.EndClip();
		svgFigure.DrawLegend(new (string, string, string)[3]
		{
			("sin(x)", "#2196F3", ""),
			("cos(x)", "#F44336", "--"),
			("damped", "#4CAF50", ":")
		});
		svgFigure.Save(Path.Combine(dir, "test_multi_line.svg"));
	}

	public static void TestScatterPlot(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 450);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		string[] coefficients = new string[7] { "circle", "square", "triangle", "diamond", "*", "x", "+" };
		string[] coefficientsArray = new string[7] { "#2196F3", "#F44336", "#4CAF50", "#FF9800", "#9C27B0", "#00BCD4", "#795548" };
		Random rng = new Random(42);
		svgFigure.SetLimits(0.0, 8.0, 0.0, 10.0);
		svgFigure.DrawAxes("X", "Y", "", "Scatter Markers");
		svgFigure.BeginClip();
		List<(string, string, string, string)> valueTuples = new List<(string, string, string, string)>();
		for (int i = 0; i < coefficients.Length; i++)
		{
			double[] x = (from _ in Enumerable.Range(0, 8)
				select rng.NextDouble() * 7.0 + 0.5).ToArray();
			double[] y = (from _ in Enumerable.Range(0, 8)
				select rng.NextDouble() * 8.0 + 1.0).ToArray();
			svgFigure.PlotScatter(x, y, coefficientsArray[i], 5.0, 0.8, useY2: false, coefficients[i]);
			valueTuples.Add((coefficients[i], coefficientsArray[i], "", coefficients[i]));
		}
		svgFigure.EndClip();
		svgFigure.DrawLegend(valueTuples.ToArray());
		svgFigure.Save(Path.Combine(dir, "test_scatter.svg"));
	}

	public static void TestNaNHandling(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 10.0, 100);
		double[] y = linspace.Select((double v) =>
		{
			if (v > 3.0 && v < 4.0)
			{
				return double.NaN;
			}
			return (v > 6.5 && v < 7.5) ? double.NaN : Math.Sin(v);
		}).ToArray();
		svgFigure.SetLimits(0.0, 10.0, -1.5, 1.5);
		svgFigure.DrawAxes("X", "Y", "", "Line with NaN Gaps");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#FF9800", 2.5);
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_nan_line.svg"));
	}

	public static void TestSubplots(string dir)
	{
		SvgFigure svgFigure = new SvgFigure();
		double[] linspace = Linspace(0.0, 6.28, 60);
		string[] coefficients = new string[4] { "sin(x)", "cos(x)", "sin(2x)", "cos(2x)" };
		string[] coefficientsArray = new string[4] { "#2196F3", "#F44336", "#4CAF50", "#FF9800" };
		Func<double, double>[] funcArray = new Func<double, double>[4]
		{
			(double v) => Math.Sin(v),
			(double v) => Math.Cos(v),
			(double v) => Math.Sin(2.0 * v),
			(double v) => Math.Cos(2.0 * v)
		};
		for (int intervalIndex = 0; intervalIndex < 4; intervalIndex++)
		{
			svgFigure.Subplot(2, 2, intervalIndex + 1);
			svgFigure.SetMargins(50.0, 20.0, 30.0, 40.0);
			double[] y = linspace.Select(funcArray[intervalIndex]).ToArray();
			svgFigure.SetLimits(0.0, 6.28, -1.2, 1.2);
			svgFigure.DrawAxes("x", "y", "", coefficients[intervalIndex]);
			svgFigure.BeginClip();
			svgFigure.PlotLine(linspace, y, coefficientsArray[intervalIndex]);
			svgFigure.EndClip();
		}
		svgFigure.Save(Path.Combine(dir, "test_subplots.svg"));
	}

	public static void TestCustomPanels(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(800, 500);
		double[] linspace = Linspace(0.0, 10.0, 80);
		svgFigure.SetPlotArea(0.0, 0.0, 800.0, 200.0);
		svgFigure.SetMargins(70.0, 30.0, 25.0, 40.0);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		svgFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		svgFigure.DrawAxes("", "sin(x)", "", "Top Panel (shorter)");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.EndClip();
		svgFigure.SetPlotArea(0.0, 200.0, 800.0, 300.0);
		svgFigure.SetMargins(70.0, 30.0, 25.0, 40.0);
		double[] doubleValues = linspace.Select((double v) => v * v).ToArray();
		svgFigure.SetLimits(0.0, 10.0, 0.0, 105.0);
		svgFigure.DrawAxes("X", "x²", "", "Bottom Panel (taller)");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, doubleValues, "#F44336");
		svgFigure.PlotScatter(linspace.Where((double _, int i) => i % 5 == 0).ToArray(), doubleValues.Where((double _, int i) => i % 5 == 0).ToArray(), "#F44336", 3.0, 0.5);
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_custom_panels.svg"));
	}

	public static void TestTable(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 350);
		svgFigure.SetPlotArea(0.0, 0.0, 600.0, 200.0);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 5.0, 50);
		double[] doubleValues = linspace.Select((double v) => Math.Exp(v * 0.4)).ToArray();
		svgFigure.SetLimits(0.0, 5.0, 0.0, 8.0);
		svgFigure.DrawAxes("X", "exp(0.4x)", "", "Growth Curve");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, doubleValues, "#4CAF50");
		svgFigure.EndClip();
		svgFigure.DrawText(60.0, 230.0, "Summary Statistics", 14.0, "bold");
		svgFigure.DrawTable(60.0, 240.0, new string[2] { "Metric", "Value" }, new string[4][]
		{
			new string[2]
			{
				"Min",
				doubleValues.Min().ToString("F3")
			},
			new string[2]
			{
				"Max",
				doubleValues.Max().ToString("F3")
			},
			new string[2]
			{
				"Mean",
				doubleValues.Average().ToString("F3")
			},
			new string[2]
			{
				"Count",
				doubleValues.Length.ToString()
			}
		}, 120.0);
		svgFigure.Save(Path.Combine(dir, "test_table.svg"));
	}

	public static void TestDualYAxis(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 400);
		svgFigure.SetMargins(60.0, 60.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 10.0, 80);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		double[] y2 = linspace.Select((double v) => 50.0 + 30.0 * Math.Cos(v * 0.5)).ToArray();
		svgFigure.SetLimits(0.0, 10.0, -1.5, 1.5);
		svgFigure.SetLimitsY2(0.0, 100.0);
		svgFigure.DrawAxes("Time (s)", "Amplitude", "Temperature (°C)", "Dual Y-Axis");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.PlotLine(linspace, y2, "#F44336", 2.0, "--", useY2: true);
		svgFigure.EndClip();
		svgFigure.DrawLegend(new (string, string, string)[2]
		{
			("Amplitude", "#2196F3", ""),
			("Temperature", "#F44336", "--")
		});
		svgFigure.Save(Path.Combine(dir, "test_dual_y_axis.svg"));
	}

	public static void TestLineMarkers(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 450);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 6.0, 15);
		string[] coefficients = new string[4] { "circle", "square", "triangle", "diamond" };
		string[] coefficientsArray = new string[4] { "#2196F3", "#F44336", "#4CAF50", "#FF9800" };
		svgFigure.SetLimits(0.0, 6.0, -1.5, 1.5);
		svgFigure.DrawAxes("x", "y", "", "Lines with Markers");
		svgFigure.BeginClip();
		List<(string, string, string, string)> valueTuples = new List<(string, string, string, string)>();
		int i;
		for (i = 0; i < coefficients.Length; i++)
		{
			double[] y = linspace.Select((double v) => Math.Sin(v + (double)i * 0.5)).ToArray();
			svgFigure.PlotLine(linspace, y, coefficientsArray[i], 2.0, "", useY2: false, coefficients[i], 4.0);
			valueTuples.Add(($"sin(x+{(double)i * 0.5:F1})", coefficientsArray[i], "", coefficients[i]));
		}
		svgFigure.EndClip();
		svgFigure.DrawLegend(valueTuples.ToArray());
		svgFigure.Save(Path.Combine(dir, "test_line_markers.svg"));
	}

	public static void TestLogScale(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(800, 300);
		double[] linspace = Linspace(1.0, 1000.0, 200);
		svgFigure.Subplot(1, 2, 1);
		svgFigure.SetMargins(60.0, 20.0, 30.0, 40.0);
		svgFigure.XScale = ScaleType.Log10;
		svgFigure.YScale = ScaleType.Log10;
		double[] y = linspace.Select((double v) => 0.5 * Math.Pow(v, 1.5)).ToArray();
		svgFigure.SetLimits(1.0, 1000.0, 1.0, 20000.0);
		svgFigure.DrawAxes("x", "y", "", "Log-Log");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.EndClip();
		svgFigure.Subplot(1, 2, 2);
		svgFigure.SetMargins(60.0, 20.0, 30.0, 40.0);
		svgFigure.XScale = ScaleType.Linear;
		svgFigure.YScale = ScaleType.Log10;
		double[] linspaceArray = Linspace(0.0, 5.0, 100);
		double[] y2 = linspaceArray.Select((double v) => Math.Pow(10.0, v * 0.6)).ToArray();
		svgFigure.SetLimits(0.0, 5.0, 1.0, 1000.0);
		svgFigure.DrawAxes("x", "y", "", "Semi-Log Y");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspaceArray, y2, "#F44336");
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_log_scale.svg"));
	}

	public static void TestTickStyles(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 300);
		double[] linspace = Linspace(0.0, 10.0, 80);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		svgFigure.Subplot(1, 2, 1);
		svgFigure.SetMargins(50.0, 15.0, 30.0, 40.0);
		svgFigure.ShowGrid = false;
		svgFigure.ShowTicks = false;
		svgFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		svgFigure.DrawAxes("x", "y", "", "No Ticks");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.EndClip();
		svgFigure.Subplot(1, 2, 2);
		svgFigure.SetMargins(50.0, 15.0, 30.0, 40.0);
		svgFigure.ShowGrid = false;
		svgFigure.ShowTicks = true;
		svgFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		svgFigure.DrawAxes("x", "y", "", "With Ticks");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_tick_styles.svg"));
	}

	public static void TestGridLines(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 300);
		double[] linspace = Linspace(0.0, 10.0, 80);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		svgFigure.Subplot(1, 2, 1);
		svgFigure.SetMargins(50.0, 15.0, 30.0, 40.0);
		svgFigure.ShowGrid = false;
		svgFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		svgFigure.DrawAxes("x", "y", "", "No Grid");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.EndClip();
		svgFigure.Subplot(1, 2, 2);
		svgFigure.SetMargins(50.0, 15.0, 30.0, 40.0);
		svgFigure.ShowGrid = true;
		svgFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		svgFigure.DrawAxes("x", "y", "", "With Grid");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_grid_lines.svg"));
	}

	public static void TestMinorTicksLinear(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		svgFigure.ShowMinorTicks = true;
		svgFigure.ShowMinorGrid = true;
		svgFigure.SetLimits(0.0, 5.0, 0.0, 5.0);
		svgFigure.DrawAxes("x", "y", "", "Minor Ticks: linear (mantissa 1 -> 5 subdivisions)");
		svgFigure.BeginClip();
		svgFigure.PlotLine(new int[2] { 0, 5 }, new int[2] { 0, 5 }, "#2196F3");
		svgFigure.EndClip();
		string render = svgFigure.Render();
		svgFigure.Save(Path.Combine(dir, "test_minor_ticks_linear.svg"));
		AssertNoInvalidCoords(render, "linear minor ticks");
		int countSubstring = CountSubstring(render, "stroke='#eee' stroke-dasharray='4,3'");
		if (countSubstring != 40)
		{
			throw new InvalidOperationException($"Linear minor grid: expected 40 (20 X + 20 Y, integers skipped), got {countSubstring}");
		}
		if (CountSubstring(render, "stroke='#ddd'") == 0)
		{
			throw new InvalidOperationException("Linear minor ticks erased the major grid lines");
		}
	}

	public static void TestMinorTicksLog(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		svgFigure.YScale = ScaleType.Log10;
		svgFigure.ShowMinorTicks = true;
		svgFigure.ShowMinorGrid = true;
		svgFigure.SetLimits(0.0, 10.0, 1.0, 100.0);
		svgFigure.DrawAxes("x", "y (log)", "", "Minor Ticks: log Y (2..9 per decade)");
		svgFigure.BeginClip();
		svgFigure.PlotLine(new int[2] { 0, 10 }, new int[2] { 1, 100 }, "#F44336");
		svgFigure.EndClip();
		string render = svgFigure.Render();
		svgFigure.Save(Path.Combine(dir, "test_minor_ticks_log.svg"));
		AssertNoInvalidCoords(render, "log minor ticks");
		int countSubstring = CountSubstring(render, "stroke='#eee' stroke-dasharray='4,3'");
		if (countSubstring != 31)
		{
			throw new InvalidOperationException($"Log minor grid: expected 31 (16 log-Y + 15 linear-X), got {countSubstring}");
		}
	}

	public static void TestMinorTicksSubDecadeLog(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		svgFigure.YScale = ScaleType.Log10;
		svgFigure.ShowMinorTicks = true;
		svgFigure.ShowMinorGrid = true;
		svgFigure.SetLimits(0.0, 10.0, 0.002, 0.007);
		svgFigure.DrawAxes("x", "y (log)", "", "Minor Ticks: sub-decade log range");
		svgFigure.BeginClip();
		svgFigure.PlotLine(new int[2] { 0, 10 }, new double[2] { 0.002, 0.007 }, "#4CAF50");
		svgFigure.EndClip();
		string render = svgFigure.Render();
		svgFigure.Save(Path.Combine(dir, "test_minor_ticks_subdecade.svg"));
		AssertNoInvalidCoords(render, "sub-decade log minor ticks");
		int countSubstring = CountSubstring(render, "stroke='#eee' stroke-dasharray='4,3'");
		if (countSubstring != 21)
		{
			throw new InvalidOperationException($"Sub-decade log minor grid: expected 21 (6 log-Y + 15 linear-X), got {countSubstring}");
		}
	}

	public static void TestMinorTicksDefaults(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		svgFigure.SetLimits(0.0, 5.0, 0.0, 5.0);
		svgFigure.DrawAxes("x", "y", "", "Defaults: major ticks on, minor ticks off");
		svgFigure.BeginClip();
		svgFigure.PlotLine(new int[2] { 0, 5 }, new int[2] { 0, 5 }, "#2196F3");
		svgFigure.EndClip();
		string render = svgFigure.Render();
		svgFigure.Save(Path.Combine(dir, "test_minor_ticks_defaults.svg"));
		if (CountTickMarkLines(render) == 0)
		{
			throw new InvalidOperationException("Major tick marks should be on by default (ShowTicks = true)");
		}
		int countSubstring = CountSubstring(render, "stroke='#eee' stroke-dasharray='4,3'");
		if (countSubstring != 0)
		{
			throw new InvalidOperationException($"Minor grid should be off by default, got {countSubstring} lines");
		}
		if (CountSubstring(render, "stroke='#ddd'") == 0)
		{
			throw new InvalidOperationException("Major grid should render by default (ShowGrid = true)");
		}
	}

	public static void TestMinorTicksDualAxis(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 400);
		svgFigure.SetMargins(60.0, 60.0, 30.0, 45.0);
		svgFigure.XReverse = true;
		svgFigure.YReverse = true;
		svgFigure.ShowMinorTicks = true;
		svgFigure.ShowMinorGrid = true;
		double[] linspace = Linspace(0.0, 10.0, 80);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		double[] y2 = linspace.Select((double v) => 50.0 + 30.0 * Math.Cos(v * 0.5)).ToArray();
		svgFigure.SetLimits(0.0, 10.0, -1.5, 1.5);
		svgFigure.SetLimitsY2(0.0, 100.0);
		svgFigure.DrawAxes("Time (s)", "Amplitude", "Temperature (°C)", "Minor Ticks: dual axis, reversed");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "#2196F3");
		svgFigure.PlotLine(linspace, y2, "#F44336", 2.0, "--", useY2: true);
		svgFigure.EndClip();
		string render = svgFigure.Render();
		svgFigure.Save(Path.Combine(dir, "test_minor_ticks_dual_axis.svg"));
		AssertNoInvalidCoords(render, "dual-axis reversed minor ticks");
		if (CountSubstring(render, "stroke='#eee' stroke-dasharray='4,3'") == 0)
		{
			throw new InvalidOperationException("Dual-axis minor grid: expected primary X/Y minor grid lines, got none");
		}
	}

	private static int CountTickMarkLines(string svg)
	{
		int ordinal = 0;
		string[] parts = svg.Split('\n');
		foreach (string part in parts)
		{
			if (part.Contains("<line", StringComparison.Ordinal) && part.Contains("stroke='#333' stroke-width='1'", StringComparison.Ordinal))
			{
				ordinal++;
			}
		}
		return ordinal;
	}

	private static void AssertNoInvalidCoords(string svg, string context)
	{
		if (svg.Contains("NaN") || svg.Contains("Infinity"))
		{
			throw new InvalidOperationException("SVG for " + context + " contains NaN/Infinity coordinates");
		}
	}

	private static int CountSubstring(string s, string sub)
	{
		int ordinal = 0;
		int startIndex = 0;
		while ((startIndex = s.IndexOf(sub, startIndex, StringComparison.Ordinal)) >= 0)
		{
			ordinal++;
			startIndex += sub.Length;
		}
		return ordinal;
	}

	public static void TestMesh(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		int count = 101;
		int count2 = 101;
		(double, double)[] valueTuples = new (double, double)[count * count2];
		double[] doubleValues = new double[count * count2];
		for (int i = 0; i < count2; i++)
		{
			for (int j = 0; j < count; j++)
			{
				double computed = (double)j * 0.1;
				double computed2 = (double)i * 0.1;
				valueTuples[i * count + j] = (computed, computed2);
				doubleValues[i * count + j] = Math.Sin(computed) * Math.Cos(computed2);
			}
		}
		int[][] intValues = new int[(count - 1) * (count2 - 1) * 2][];
		int ordinal = 0;
		for (int k = 0; k < count2 - 1; k++)
		{
			for (int l = 0; l < count - 1; l++)
			{
				int countTerm = k * count + l;
				int countTerm2 = k * count + l + 1;
				int countTerm3 = (k + 1) * count + l;
				int countTerm4 = (k + 1) * count + l + 1;
				intValues[ordinal++] = new int[3] { countTerm, countTerm2, countTerm4 };
				intValues[ordinal++] = new int[3] { countTerm, countTerm4, countTerm3 };
			}
		}
		svgFigure.SetLimits(0.0, 10.0, 0.0, 10.0);
		svgFigure.DrawAxes("X", "Y", "", $"Mesh ({intValues.Length:N0} triangles)");
		svgFigure.BeginClip();
		string[] coefficients = new string[10] { "#313695", "#4575b4", "#74add1", "#abd9e9", "#e0f3f8", "#fee090", "#fdae61", "#f46d43", "#d73027", "#a50026" };
		List<int[]>[] intValuesListArray = new List<int[]>[coefficients.Length];
		for (int m = 0; m < intValuesListArray.Length; m++)
		{
			intValuesListArray[m] = new List<int[]>();
		}
		foreach (int[] intValue in intValues)
		{
			int ordinal2 = Math.Clamp((int)(((doubleValues[intValue[0]] + doubleValues[intValue[1]] + doubleValues[intValue[2]]) / 3.0 + 1.0) / 2.0 * (double)coefficients.Length), 0, coefficients.Length - 1);
			intValuesListArray[ordinal2].Add(intValue);
		}
		for (int intervalIndex = 0; intervalIndex < coefficients.Length; intervalIndex++)
		{
			if (intValuesListArray[intervalIndex].Count > 0)
			{
				svgFigure.PlotMesh(valueTuples, intValuesListArray[intervalIndex], coefficients[intervalIndex], coefficients[intervalIndex], 0.0);
			}
		}
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_mesh.svg"));
	}

	public static void TestMeshWithEdges(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		int count = 31;
		int count2 = 31;
		(double, double)[] valueTuples = new (double, double)[count * count2];
		double[] doubleValues = new double[count * count2];
		for (int i = 0; i < count2; i++)
		{
			for (int j = 0; j < count; j++)
			{
				double computed = (double)j * 0.1;
				double computed2 = (double)i * 0.1;
				valueTuples[i * count + j] = (computed, computed2);
				doubleValues[i * count + j] = Math.Sin(computed * 2.0) * Math.Cos(computed2 * 2.0);
			}
		}
		int[][] intValues = new int[(count - 1) * (count2 - 1) * 2][];
		int ordinal = 0;
		for (int k = 0; k < count2 - 1; k++)
		{
			for (int l = 0; l < count - 1; l++)
			{
				int countTerm = k * count + l;
				int countTerm2 = k * count + l + 1;
				int countTerm3 = (k + 1) * count + l;
				int countTerm4 = (k + 1) * count + l + 1;
				intValues[ordinal++] = new int[3] { countTerm, countTerm2, countTerm4 };
				intValues[ordinal++] = new int[3] { countTerm, countTerm4, countTerm3 };
			}
		}
		svgFigure.SetLimits(0.0, 3.0, 0.0, 3.0);
		svgFigure.DrawAxes("X", "Y", "", $"Mesh with Edges ({intValues.Length:N0} triangles)");
		svgFigure.BeginClip();
		string[] coefficients = new string[10] { "#313695", "#4575b4", "#74add1", "#abd9e9", "#e0f3f8", "#fee090", "#fdae61", "#f46d43", "#d73027", "#a50026" };
		List<int[]>[] intValuesListArray = new List<int[]>[coefficients.Length];
		for (int m = 0; m < intValuesListArray.Length; m++)
		{
			intValuesListArray[m] = new List<int[]>();
		}
		foreach (int[] intValue in intValues)
		{
			int ordinal2 = Math.Clamp((int)(((doubleValues[intValue[0]] + doubleValues[intValue[1]] + doubleValues[intValue[2]]) / 3.0 + 1.0) / 2.0 * (double)coefficients.Length), 0, coefficients.Length - 1);
			intValuesListArray[ordinal2].Add(intValue);
		}
		for (int intervalIndex = 0; intervalIndex < coefficients.Length; intervalIndex++)
		{
			if (intValuesListArray[intervalIndex].Count > 0)
			{
				svgFigure.PlotMesh(valueTuples, intValuesListArray[intervalIndex], coefficients[intervalIndex], "#333", 0.3);
			}
		}
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_mesh_edges.svg"));
	}

	public static void TestMixedMesh(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 500);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		(double, double)[] vertices = new (double, double)[21]
		{
			(0.0, 0.0),
			(1.0, 0.0),
			(2.0, 0.0),
			(3.0, 0.0),
			(4.0, 0.0),
			(0.0, 1.0),
			(1.0, 1.0),
			(2.0, 1.0),
			(3.0, 1.0),
			(4.0, 1.0),
			(0.0, 2.0),
			(1.0, 2.0),
			(2.0, 2.0),
			(3.0, 2.0),
			(4.0, 2.0),
			(0.0, 3.0),
			(1.0, 3.0),
			(2.0, 3.0),
			(3.0, 3.0),
			(4.0, 3.0),
			(2.0, 4.0)
		};
		int[][] elements = new int[5][]
		{
			new int[4] { 0, 1, 6, 5 },
			new int[4] { 1, 2, 7, 6 },
			new int[4] { 5, 6, 11, 10 },
			new int[4] { 8, 9, 14, 13 },
			new int[4] { 3, 4, 9, 8 }
		};
		int[][] elements2 = new int[9][]
		{
			new int[3] { 2, 3, 7 },
			new int[3] { 3, 8, 7 },
			new int[3] { 6, 7, 12 },
			new int[3] { 7, 8, 12 },
			new int[3] { 12, 8, 13 },
			new int[3] { 10, 11, 15 },
			new int[3] { 11, 12, 16 },
			new int[3] { 13, 14, 18 },
			new int[3] { 14, 19, 18 }
		};
		int[][] elements3 = new int[1][] { new int[5] { 15, 16, 17, 18, 20 } };
		svgFigure.SetLimits(-0.5, 4.5, -0.5, 4.5);
		svgFigure.DrawAxes("X", "Y", "", "Mixed Mesh (triangles, quads, pentagon)");
		svgFigure.BeginClip();
		svgFigure.PlotMesh(vertices, elements, "#abd9e9", "#333", 0.5);
		svgFigure.PlotMesh(vertices, elements2, "#fdae61", "#333", 0.5);
		svgFigure.PlotMesh(vertices, elements3, "#d73027", "#333", 0.5);
		svgFigure.EndClip();
		svgFigure.DrawLegend(new (string, string, string, string)[3]
		{
			("Quads", "#abd9e9", "", "square"),
			("Triangles", "#fdae61", "", "triangle"),
			("Pentagon", "#d73027", "", "diamond")
		});
		svgFigure.Save(Path.Combine(dir, "test_mixed_mesh.svg"));
	}

	public static void TestMeshLabels(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 500);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		(double, double)[] vertices = new (double, double)[12]
		{
			(0.0, 0.0),
			(1.0, 0.0),
			(2.0, 0.0),
			(3.0, 0.0),
			(0.0, 1.0),
			(1.0, 1.0),
			(2.0, 1.0),
			(3.0, 1.0),
			(0.0, 2.0),
			(1.0, 2.0),
			(2.0, 2.0),
			(3.0, 2.0)
		};
		int[][] elements = new int[7][]
		{
			new int[4] { 0, 1, 5, 4 },
			new int[3] { 1, 2, 5 },
			new int[3] { 2, 6, 5 },
			new int[4] { 2, 3, 7, 6 },
			new int[4] { 4, 5, 9, 8 },
			new int[4] { 5, 6, 10, 9 },
			new int[4] { 6, 7, 11, 10 }
		};
		svgFigure.SetLimits(-0.5, 3.5, -0.5, 2.5);
		svgFigure.DrawAxes("X", "Y", "", "Mesh Labels: Vertices (red), Edges (blue), Elements (gray)");
		svgFigure.BeginClip();
		svgFigure.PlotMesh(vertices, elements, "#e0f3f8", "#666", 1.0, labelVertices: true, labelEdges: true, labelElements: true, 11.0);
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_mesh_labels.svg"));
	}

	public static void TestVelocityArrowsSwirl(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700);
		svgFigure.SetMargins(60.0, 100.0, 30.0, 45.0);
		svgFigure.SetLimits(-5.0, 5.0, -5.0, 5.0);
		int count = 21;
		int count2 = 21;
		(double, double)[] valueTuples = new (double, double)[count * count2];
		for (int i = 0; i < count2; i++)
		{
			for (int j = 0; j < count; j++)
			{
				valueTuples[i * count + j] = (-5.0 + (double)j * 10.0 / (double)(count - 1), -5.0 + (double)i * 10.0 / (double)(count2 - 1));
			}
		}
		int count3 = (count - 1) * (count2 - 1);
		int[][] intValues = new int[count3][];
		double[] doubleValues = new double[count3];
		double[] doubleValuesArray = new double[count3];
		int ordinal = 0;
		for (int k = 0; k < count2 - 1; k++)
		{
			for (int l = 0; l < count - 1; l++)
			{
				intValues[ordinal] = new int[4]
				{
					k * count + l,
					k * count + l + 1,
					(k + 1) * count + l + 1,
					(k + 1) * count + l
				};
				double computed = (valueTuples[intValues[ordinal][0]].Item1 + valueTuples[intValues[ordinal][2]].Item1) / 2.0;
				double computed2 = (valueTuples[intValues[ordinal][0]].Item2 + valueTuples[intValues[ordinal][2]].Item2) / 2.0;
				doubleValues[ordinal] = 0.0 - computed2;
				doubleValuesArray[ordinal] = computed;
				ordinal++;
			}
		}
		svgFigure.DrawAxes("X", "Y", "", "Velocity Arrows: Swirl (vx = -y, vy = x)");
		svgFigure.BeginClip();
		svgFigure.PlotMesh(valueTuples, intValues, "rgba(220,235,250,0.6)", "#bbb", 0.3);
		svgFigure.PlotVelocityArrows(valueTuples, intValues, doubleValues, doubleValuesArray, 28.0, 1.5, 1.0, 2.0, null, "#1f3a8a", 1.2);
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_velocity_arrows_swirl.svg"));
	}

	public static void TestVelocityArrowsMixedFootprint(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		svgFigure.SetLimits(-0.5, 4.5, -0.5, 4.5);
		(double, double)[] vertices = new (double, double)[19]
		{
			(0.0, 0.0),
			(1.0, 0.0),
			(2.0, 0.0),
			(3.0, 0.0),
			(4.0, 0.0),
			(0.0, 1.0),
			(1.0, 1.0),
			(2.0, 1.0),
			(3.0, 1.0),
			(4.0, 1.0),
			(0.0, 2.0),
			(1.0, 2.0),
			(2.0, 2.0),
			(0.0, 3.0),
			(1.0, 3.0),
			(2.0, 3.0),
			(0.0, 4.0),
			(1.0, 4.0),
			(2.0, 4.0)
		};
		int[][] intValues = new int[11][]
		{
			new int[4] { 0, 1, 6, 5 },
			new int[4] { 1, 2, 7, 6 },
			new int[4] { 2, 3, 8, 7 },
			new int[4] { 3, 4, 9, 8 },
			new int[4] { 5, 6, 11, 10 },
			new int[4] { 6, 7, 12, 11 },
			new int[4] { 10, 11, 14, 13 },
			new int[4] { 11, 12, 15, 14 },
			new int[4] { 13, 14, 17, 16 },
			new int[4] { 14, 15, 18, 17 },
			new int[3] { 5, 6, 7 }
		};
		Random random = new Random(42);
		double[] doubleValues = new double[intValues.Length];
		double[] doubleValuesArray = new double[intValues.Length];
		for (int i = 0; i < intValues.Length; i++)
		{
			double nextDoubleRandomTerm = random.NextDouble() * 2.0 * Math.PI;
			double nextDoubleRandomTerm2 = 0.3 + random.NextDouble() * 0.9;
			doubleValues[i] = nextDoubleRandomTerm2 * Math.Cos(nextDoubleRandomTerm);
			doubleValuesArray[i] = nextDoubleRandomTerm2 * Math.Sin(nextDoubleRandomTerm);
		}
		svgFigure.DrawAxes("X", "Y", "", "Velocity Arrows: L-shaped footprint");
		svgFigure.BeginClip();
		svgFigure.PlotMesh(vertices, intValues, "#eaf6ff", "#666", 0.6);
		svgFigure.PlotVelocityArrows(vertices, intValues, doubleValues, doubleValuesArray, 25.0, 1.4, 1.0, 2.0, null, "#7a1a1a");
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_velocity_arrows_mixed_footprint.svg"));
	}

	public static void TestVelocityArrowsPalette(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(750);
		svgFigure.SetMargins(60.0, 100.0, 30.0, 45.0);
		svgFigure.SetLimits(0.0, 10.0, 0.0, 6.0);
		int count = 31;
		int count2 = 19;
		(double, double)[] valueTuples = new (double, double)[count * count2];
		for (int i = 0; i < count2; i++)
		{
			for (int j = 0; j < count; j++)
			{
				valueTuples[i * count + j] = ((double)j * 10.0 / (double)(count - 1), (double)i * 6.0 / (double)(count2 - 1));
			}
		}
		int count3 = (count - 1) * (count2 - 1);
		int[][] intValues = new int[count3][];
		double[] doubleValues = new double[count3];
		double[] doubleValuesArray = new double[count3];
		int ordinal = 0;
		for (int k = 0; k < count2 - 1; k++)
		{
			for (int l = 0; l < count - 1; l++)
			{
				intValues[ordinal] = new int[4]
				{
					k * count + l,
					k * count + l + 1,
					(k + 1) * count + l + 1,
					(k + 1) * count + l
				};
				double a = (valueTuples[intValues[ordinal][0]].Item1 + valueTuples[intValues[ordinal][2]].Item1) / 2.0;
				double computed = (valueTuples[intValues[ordinal][0]].Item2 + valueTuples[intValues[ordinal][2]].Item2) / 2.0;
				double computed2 = 1.0 - Math.Pow((computed - 3.0) / 3.0, 2.0);
				doubleValues[ordinal] = Math.Max(0.0, computed2) * 2.0;
				doubleValuesArray[ordinal] = 0.1 * Math.Sin(a);
				ordinal++;
			}
		}
		string[] lookupTable = new string[5] { "#2c7bb6", "#abd9e9", "#ffffbf", "#fdae61", "#d7191c" };
		svgFigure.DrawAxes("X (m)", "Y (m)", "", "Velocity Arrows: palette by |v|");
		svgFigure.BeginClip();
		svgFigure.PlotMesh(valueTuples, intValues, "rgba(0,0,0,0)", "#ddd");
		string[] palette = lookupTable;
		svgFigure.PlotVelocityArrows(valueTuples, intValues, doubleValues, doubleValuesArray, 26.0, 1.4, 1.0, 2.0, null, "black", 1.2, 0.3, 8.0, palette, 0.0, 2.0);
		svgFigure.EndClip();
		svgFigure.DrawColorBar(0.0, 2.0, lookupTable, "|v| (m/s)");
		svgFigure.Save(Path.Combine(dir, "test_velocity_arrows_palette.svg"));
	}

	public static void TestVelocityArrowsLayering(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(500, 400);
		svgFigure.SetMargins(50.0, 90.0, 30.0, 40.0);
		svgFigure.SetLimits(0.0, 4.0, 0.0, 4.0);
		(double, double)[] vertices = new (double, double)[25]
		{
			(0.0, 0.0),
			(1.0, 0.0),
			(2.0, 0.0),
			(3.0, 0.0),
			(4.0, 0.0),
			(0.0, 1.0),
			(1.0, 1.0),
			(2.0, 1.0),
			(3.0, 1.0),
			(4.0, 1.0),
			(0.0, 2.0),
			(1.0, 2.0),
			(2.0, 2.0),
			(3.0, 2.0),
			(4.0, 2.0),
			(0.0, 3.0),
			(1.0, 3.0),
			(2.0, 3.0),
			(3.0, 3.0),
			(4.0, 3.0),
			(0.0, 4.0),
			(1.0, 4.0),
			(2.0, 4.0),
			(3.0, 4.0),
			(4.0, 4.0)
		};
		List<int[]> intValuesList = new List<int[]>();
		for (int i = 0; i < 4; i++)
		{
			for (int j = 0; j < 4; j++)
			{
				intValuesList.Add(new int[4]
				{
					i * 5 + j,
					i * 5 + j + 1,
					(i + 1) * 5 + j + 1,
					(i + 1) * 5 + j
				});
			}
		}
		double[] doubleValues = new double[intValuesList.Count];
		double[] doubleValuesArray = new double[intValuesList.Count];
		for (int k = 0; k < intValuesList.Count; k++)
		{
			doubleValues[k] = 1.0;
			doubleValuesArray[k] = 0.0;
		}
		string[] lookupTable = new string[2] { "#2c7bb6", "#d7191c" };
		svgFigure.DrawAxes("X", "Y", "", "Layering check");
		svgFigure.PlotMesh(vertices, intValuesList, "#eef", "#999", 0.5);
		string[] palette = lookupTable;
		svgFigure.PlotVelocityArrows(vertices, intValuesList, doubleValues, doubleValuesArray, 24.0, 1.5, 1.0, 2.0, null, "black", 1.0, 0.3, 8.0, palette, 0.0, 1.0);
		svgFigure.DrawColorBar(0.0, 1.0, lookupTable, "|v|");
		string render = svgFigure.Render();
		int indexOf = render.IndexOf("#eef", StringComparison.Ordinal);
		int indexOf2 = render.IndexOf("stroke-linecap='round'", StringComparison.Ordinal);
		int indexOf3 = render.IndexOf("|v|", StringComparison.Ordinal);
		if (indexOf < 0 || indexOf2 < 0 || indexOf3 < 0 || indexOf >= indexOf2 || indexOf2 >= indexOf3)
		{
			throw new InvalidOperationException($"Layering order broken: mesh@{indexOf} arrow@{indexOf2} colorbar@{indexOf3}");
		}
		svgFigure.Save(Path.Combine(dir, "test_velocity_arrows_layering.svg"));
	}

	public static void TestBarPlot(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] x = new double[6] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 };
		double[] y = new double[6] { 12.0, 25.0, 18.0, 30.0, 22.0, 15.0 };
		svgFigure.SetLimits(0.2, 6.8, 0.0, 35.0);
		svgFigure.DrawAxes("Month", "Sales", "", "Monthly Sales");
		svgFigure.BeginClip();
		svgFigure.PlotBar(x, y, "#4CAF50", 0.7);
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_bar.svg"));
	}

	public static void TestGroupedBarPlot(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] x = new double[5] { 1.0, 2.0, 3.0, 4.0, 5.0 };
		double[][] series = new double[3][]
		{
			new double[5] { 20.0, 35.0, 30.0, 25.0, 40.0 },
			new double[5] { 25.0, 32.0, 34.0, 20.0, 28.0 },
			new double[5] { 15.0, 28.0, 22.0, 30.0, 35.0 }
		};
		string[] colors = new string[3] { "#2196F3", "#F44336", "#4CAF50" };
		svgFigure.SetLimits(0.2, 5.8, 0.0, 45.0);
		svgFigure.DrawAxes("Category", "Value", "", "Grouped Bar Chart");
		svgFigure.BeginClip();
		svgFigure.PlotBarGrouped(x, series, colors, 0.75);
		svgFigure.EndClip();
		svgFigure.DrawLegend(new (string, string, string, string)[3]
		{
			("Series A", "#2196F3", "", "square"),
			("Series B", "#F44336", "", "square"),
			("Series C", "#4CAF50", "", "square")
		});
		svgFigure.Save(Path.Combine(dir, "test_bar_grouped.svg"));
	}

	public static void TestLegendPositions(string dir)
	{
		string[] coefficients = new string[6] { "top-left", "top-center", "top-right", "bottom-left", "bottom-center", "bottom-right" };
		SvgFigure svgFigure = new SvgFigure(900);
		double[] linspace = Linspace(0.0, 10.0, 60);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		double[] y2 = linspace.Select((double v) => Math.Cos(v)).ToArray();
		for (int intervalIndex = 0; intervalIndex < 6; intervalIndex++)
		{
			svgFigure.Subplot(2, 3, intervalIndex + 1);
			svgFigure.SetMargins(45.0, 15.0, 30.0, 40.0);
			svgFigure.SetLimits(0.0, 10.0, -1.5, 1.5);
			svgFigure.DrawAxes("", "", "", coefficients[intervalIndex]);
			svgFigure.BeginClip();
			svgFigure.PlotLine(linspace, y, "#2196F3");
			svgFigure.PlotLine(linspace, y2, "#F44336", 2.0, "--");
			svgFigure.EndClip();
			svgFigure.DrawLegend(new (string, string, string)[2]
			{
				("sin(x)", "#2196F3", ""),
				("cos(x)", "#F44336", "--")
			}, coefficients[intervalIndex]);
		}
		svgFigure.Save(Path.Combine(dir, "test_legend_positions.svg"));
	}

	public static void TestShorthandColors(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 6.0, 50);
		string[] coefficients = new string[7] { "r", "g", "b", "k", "m", "c", "y" };
		string[] coefficientsArray = new string[7] { "r = red", "g = green", "b = blue", "k = black", "m = magenta", "c = cyan", "y = yellow" };
		svgFigure.SetLimits(0.0, 6.0, -2.0, 2.0);
		svgFigure.DrawAxes("x", "y", "", "Matlab-style Color Shortcuts");
		svgFigure.BeginClip();
		List<(string, string, string, string)> valueTuples = new List<(string, string, string, string)>();
		int i;
		for (i = 0; i < coefficients.Length; i++)
		{
			double[] y = linspace.Select((double v) => Math.Sin(v + (double)i * 0.4) * (1.0 - (double)i * 0.08)).ToArray();
			svgFigure.PlotLine(linspace, y, coefficients[i]);
			valueTuples.Add((coefficientsArray[i], coefficients[i], "", "line"));
		}
		svgFigure.EndClip();
		svgFigure.DrawLegend(valueTuples.ToArray(), "top-left");
		svgFigure.Save(Path.Combine(dir, "test_shorthand_colors.svg"));
	}

	public static void TestPlotFill(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 10.0, 80);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		double[] doubleValues = linspace.Select((double v) => Math.Sin(v) + 0.3).ToArray();
		double[] doubleValuesArray = linspace.Select((double v) => Math.Sin(v) - 0.3).ToArray();
		svgFigure.SetLimits(0.0, 10.0, -1.8, 1.8);
		svgFigure.DrawAxes("x", "y", "", "Filled Area & Confidence Band");
		svgFigure.BeginClip();
		svgFigure.PlotFill(linspace, y, "rgba(76,175,80,0.3)");
		svgFigure.PlotFill(linspace, doubleValues, doubleValuesArray, "rgba(33,150,243,0.3)");
		svgFigure.PlotLine(linspace, y, "#4CAF50");
		svgFigure.PlotLine(linspace, doubleValues, "#2196F3", 1.0, ":");
		svgFigure.PlotLine(linspace, doubleValuesArray, "#2196F3", 1.0, ":");
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_fill.svg"));
	}

	public static void TestRefLines(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 10.0, 80);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		svgFigure.SetLimits(0.0, 10.0, -1.5, 1.5);
		svgFigure.DrawAxes("x", "y", "", "Reference Lines");
		svgFigure.BeginClip();
		svgFigure.HLine(0.0, "k", 1.0, "");
		svgFigure.VLine(Math.PI, "r", 1.5);
		svgFigure.VLine(Math.PI * 2.0, "r", 1.5);
		svgFigure.PlotLine(linspace, y, "b");
		svgFigure.EndClip();
		svgFigure.DrawAnnotation(Math.PI, 1.3, "x = π", 10.0, "r");
		svgFigure.Save(Path.Combine(dir, "test_reflines.svg"));
	}

	public static void TestErrorBars(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] x = new double[8] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0 };
		double[] y = new double[8] { 3.0, 5.0, 4.0, 7.0, 6.0, 8.0, 7.5, 9.0 };
		double[] yErr = new double[8] { 0.5, 0.8, 0.4, 1.0, 0.6, 0.9, 0.7, 0.5 };
		svgFigure.SetLimits(0.0, 9.0, 0.0, 12.0);
		svgFigure.DrawAxes("Sample", "Value", "", "Error Bars");
		svgFigure.BeginClip();
		svgFigure.PlotErrorBar(x, y, yErr, "k", 6.0);
		svgFigure.PlotScatter(x, y, "b", 4.0);
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_errorbars.svg"));
	}

	public static void TestStackedBar(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] x = new double[5] { 1.0, 2.0, 3.0, 4.0, 5.0 };
		double[][] series = new double[3][]
		{
			new double[5] { 10.0, 15.0, 12.0, 18.0, 14.0 },
			new double[5] { 8.0, 10.0, 14.0, 9.0, 12.0 },
			new double[5] { 5.0, 7.0, 6.0, 8.0, 10.0 }
		};
		string[] colors = new string[3] { "#2196F3", "#F44336", "#4CAF50" };
		svgFigure.SetLimits(0.2, 5.8, 0.0, 40.0);
		svgFigure.DrawAxes("Category", "Value", "", "Stacked Bar Chart");
		svgFigure.BeginClip();
		svgFigure.PlotBarStacked(x, series, colors, 0.7);
		svgFigure.EndClip();
		svgFigure.DrawLegend(new (string, string, string, string)[3]
		{
			("A", "#2196F3", "", "square"),
			("B", "#F44336", "", "square"),
			("C", "#4CAF50", "", "square")
		}, "top-left");
		svgFigure.Save(Path.Combine(dir, "test_stacked_bar.svg"));
	}

	public static void TestHorizontalBar(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] y = new double[6] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 };
		double[] x = new double[6] { 42.0, 35.0, 28.0, 22.0, 18.0, 12.0 };
		svgFigure.SetLimits(0.0, 50.0, 0.2, 6.8);
		svgFigure.DrawAxes("Count", "Category", "", "Horizontal Bar Chart");
		svgFigure.BeginClip();
		svgFigure.PlotBarH(y, x, "#FF9800", 0.7);
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_bar_h.svg"));
	}

	public static void TestAnnotationsArrows(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] linspace = Linspace(0.0, 10.0, 100);
		double[] y = linspace.Select((double v) => Math.Sin(v)).ToArray();
		svgFigure.SetLimits(0.0, 10.0, -1.5, 1.5);
		svgFigure.DrawAxes("x", "y", "", "Annotations & Arrows");
		svgFigure.BeginClip();
		svgFigure.PlotLine(linspace, y, "b");
		svgFigure.DrawArrow(3.0, 1.3, Math.PI / 2.0, 1.0, "r");
		svgFigure.DrawAnnotation(3.1, 1.35, "peak", 11.0, "r");
		svgFigure.DrawArrow(8.0, -1.3, 4.71238898038469, -1.0, "r");
		svgFigure.DrawAnnotation(8.1, -1.35, "trough", 11.0, "r");
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_annotations.svg"));
	}

	public static void TestHistogram(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		Random random = new Random(123);
		double[] doubleValues = new double[1000];
		for (int i = 0; i < doubleValues.Length; i++)
		{
			double d = 1.0 - random.NextDouble();
			double nextDouble = random.NextDouble();
			doubleValues[i] = Math.Sqrt(-2.0 * Math.Log(d)) * Math.Cos(Math.PI * 2.0 * nextDouble);
		}
		svgFigure.SetLimits(-4.0, 4.0, 0.0, 120.0);
		svgFigure.DrawAxes("Value", "Count", "", "Histogram (Normal Distribution)");
		svgFigure.BeginClip();
		svgFigure.PlotHistogram(doubleValues, 30, "#9C27B0", "#333");
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_histogram.svg"));
	}

	public static void TestColorBar(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(750, 500);
		svgFigure.SetMargins(60.0, 70.0, 30.0, 45.0);
		int count = 51;
		int count2 = 51;
		(double, double)[] valueTuples = new (double, double)[count * count2];
		double[] doubleValues = new double[count * count2];
		for (int i = 0; i < count2; i++)
		{
			for (int j = 0; j < count; j++)
			{
				double computed = (double)j * 0.2;
				double computed2 = (double)i * 0.2;
				valueTuples[i * count + j] = (computed, computed2);
				doubleValues[i * count + j] = Math.Sin(computed) * Math.Cos(computed2);
			}
		}
		int[][] intValues = new int[(count - 1) * (count2 - 1) * 2][];
		int ordinal = 0;
		for (int k = 0; k < count2 - 1; k++)
		{
			for (int l = 0; l < count - 1; l++)
			{
				int countTerm = k * count + l;
				int countTerm2 = k * count + l + 1;
				int countTerm3 = (k + 1) * count + l;
				int countTerm4 = (k + 1) * count + l + 1;
				intValues[ordinal++] = new int[3] { countTerm, countTerm2, countTerm4 };
				intValues[ordinal++] = new int[3] { countTerm, countTerm4, countTerm3 };
			}
		}
		string[] palette = new string[10] { "#313695", "#4575b4", "#74add1", "#abd9e9", "#e0f3f8", "#fee090", "#fdae61", "#f46d43", "#d73027", "#a50026" };
		svgFigure.SetLimits(0.0, 10.0, 0.0, 10.0);
		svgFigure.DrawAxes("X", "Y", "", "Mesh with Color Bar");
		svgFigure.BeginClip();
		double[] doubleValuesArray = new double[intValues.Length];
		for (int m = 0; m < intValues.Length; m++)
		{
			int[] intValue = intValues[m];
			doubleValuesArray[m] = (doubleValues[intValue[0]] + doubleValues[intValue[1]] + doubleValues[intValue[2]]) / 3.0;
		}
		svgFigure.PlotMeshShaded(valueTuples, intValues, doubleValuesArray, palette, -1.0, 1.0);
		svgFigure.EndClip();
		svgFigure.DrawColorBar(-1.0, 1.0, palette, "z = sin(x)cos(y)");
		svgFigure.Save(Path.Combine(dir, "test_colorbar.svg"));
	}

	public static void TestCustomTickLabels(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(600, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 45.0);
		double[] lookupTable = new double[6] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 };
		double[] y = new double[6] { 18.0, 25.0, 30.0, 22.0, 35.0, 28.0 };
		svgFigure.SetLimits(0.2, 6.8, 0.0, 40.0);
		svgFigure.SetTickLabelsX(lookupTable, new string[6] { "Jan", "Feb", "Mar", "Apr", "May", "Jun" });
		svgFigure.DrawAxes("Month", "Sales", "", "Custom Tick Labels");
		svgFigure.BeginClip();
		svgFigure.PlotBar(lookupTable, y, "#4CAF50", 0.6);
		svgFigure.EndClip();
		svgFigure.Save(Path.Combine(dir, "test_custom_ticks.svg"));
	}

	public static void TestPieChart(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(700, 350);
		string[] colors = new string[5] { "#2196F3", "#F44336", "#4CAF50", "#FF9800", "#9C27B0" };
		string[] labels = new string[5] { "A (30%)", "B (25%)", "C (20%)", "D (15%)", "E (10%)" };
		double[] values = new double[5] { 30.0, 25.0, 20.0, 15.0, 10.0 };
		svgFigure.SetPlotArea(0.0, 0.0, 350.0, 350.0);
		svgFigure.SetMargins(20.0, 20.0, 30.0, 20.0);
		svgFigure.PlotPie(values, colors, labels, 0.0, "#fff", 2.0);
		svgFigure.DrawText(175.0, 20.0, "Pie Chart", 14.0, "bold", "middle");
		svgFigure.SetPlotArea(350.0, 0.0, 350.0, 350.0);
		svgFigure.SetMargins(20.0, 20.0, 30.0, 20.0);
		svgFigure.PlotPie(values, colors, labels, 0.5, "#fff", 2.0);
		svgFigure.DrawText(525.0, 20.0, "Donut Chart", 14.0, "bold", "middle");
		svgFigure.Save(Path.Combine(dir, "test_pie.svg"));
	}

	public static void TestTimeLine(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(800, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 60.0);
		DateTime start = new DateTime(2026, 4, 3, 20, 10, 0);
		DateTime[] dateTimes = (from i in Enumerable.Range(0, 100)
			select start.AddMinutes(i * 5)).ToArray();
		double[] y = dateTimes.Select((DateTime t, int i) => Math.Sin((double)i * 0.1) * 50.0 + 100.0).ToArray();
		svgFigure.Subplot(1, 1, 1);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 60.0);
		svgFigure.SetLimitsX(dateTimes);
		svgFigure.SetLimitsY(0.0, 200.0);
		svgFigure.DrawTimeAxes("Event Log", "Amplitude", 7, "Time Series Test");
		svgFigure.PlotTimeLine(dateTimes, y, "navy", 2.5);
		svgFigure.Save(Path.Combine(dir, "test_timeline.svg"));
	}

	public static void TestTimeLineNaN(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(800, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 60.0);
		DateTime start = new DateTime(2026, 4, 3, 20, 10, 0);
		DateTime[] dateTimes = (from i in Enumerable.Range(0, 20)
			select start.AddMinutes(i * 5)).ToArray();
		double[] y = (from i in Enumerable.Range(0, 20)
			select (i < 8 || i > 11) ? (Math.Sin((double)i * 0.3) * 50.0 + 100.0) : double.NaN).ToArray();
		svgFigure.SetLimitsX(dateTimes);
		svgFigure.SetLimitsY(0.0, 200.0);
		svgFigure.DrawTimeAxes("Time", "Value", 7, "TimeLine NaN Gaps");
		svgFigure.PlotTimeLine(dateTimes, y, "navy");
		svgFigure.Save(Path.Combine(dir, "test_timeline_nan.svg"));
	}

	public static void TestTimeLineMultiSeries(string dir)
	{
		SvgFigure svgFigure = new SvgFigure(800, 400);
		svgFigure.SetMargins(60.0, 30.0, 30.0, 60.0);
		DateTime start1 = new DateTime(2026, 4, 1, 0, 0, 0);
		DateTime start2 = new DateTime(2026, 4, 5, 0, 0, 0);
		DateTime[] dateTimes = (from i in Enumerable.Range(0, 50)
			select start1.AddHours(i)).ToArray();
		DateTime[] dateTimesArray = (from i in Enumerable.Range(0, 50)
			select start2.AddHours(i)).ToArray();
		double[] y = dateTimes.Select((DateTime _, int i) => Math.Sin((double)i * 0.1) * 50.0 + 100.0).ToArray();
		double[] y2 = dateTimesArray.Select((DateTime _, int i) => Math.Cos((double)i * 0.1) * 30.0 + 80.0).ToArray();
		svgFigure.SetLimitsY(0.0, 200.0);
		svgFigure.PlotTimeLine(dateTimes, y);
		svgFigure.PlotTimeLine(dateTimesArray, y2, "red", 2.0, "--");
		svgFigure.DrawTimeAxes("Time", "Value", 7, "Multi-Series Auto-Scale");
		svgFigure.Save(Path.Combine(dir, "test_timeline_multi.svg"));
	}

	private static double[] Linspace(double start, double end, int count)
	{
		double[] doubleValues = new double[count];
		double endAboveStartOverCountOffset = (end - start) / (double)(count - 1);
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = start + (double)i * endAboveStartOverCountOffset;
		}
		return doubleValues;
	}
}
