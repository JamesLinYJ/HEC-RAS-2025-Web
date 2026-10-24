using System;
using System.IO;
using System.Linq;

namespace Utility.LightweightPlotting;

public static class HtmlFigureTests
{
	public static void RunAll(string outputDir)
	{
		Console.WriteLine("HtmlFigureTests: writing to " + outputDir);
		TestBasicLine(outputDir);
		TestMultiLineWithLegend(outputDir);
		TestScatterPlot(outputDir);
		TestNaNHandling(outputDir);
		TestDualYAxis(outputDir);
		TestLineMarkers(outputDir);
		TestLogScale(outputDir);
		TestBarPlot(outputDir);
		TestGroupedBarPlot(outputDir);
		TestStackedBar(outputDir);
		TestHorizontalBar(outputDir);
		TestHistogram(outputDir);
		TestPieChart(outputDir);
		TestPlotFill(outputDir);
		TestRefLines(outputDir);
		TestErrorBars(outputDir);
		TestAnnotationsArrows(outputDir);
		TestCustomTickLabels(outputDir);
		TestTable(outputDir);
		TestStickyHeaderTable(outputDir);
		TestSubplots(outputDir);
		TestEmbeddedSvg(outputDir);
		TestMixedChartAndTable(outputDir);
		TestShorthandColors(outputDir);
		TestTimeLine(outputDir);
		TestTimeLineMultiSeries(outputDir);
		TestAxisReverse(outputDir);
		TestY2Reverse(outputDir);
		TestSaveAsPng(outputDir);
		TestSaveAsPdf(outputDir);
		TestVelocityArrows(outputDir);
		Console.WriteLine("HtmlFigureTests: all tests complete.");
	}

	public static void TestVelocityArrows(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure(720, 540);
		htmlFigure.PageTitle = "Velocity arrows in HtmlFigure";
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
		string[] palette = lookupTable;
		htmlFigure.PlotVelocityArrows(valueTuples, intValues, doubleValues, doubleValuesArray, 26.0, 1.4, 1.0, 2.0, null, "black", 1.0, 0.3, 8.0, palette, 0.0, 2.0, "Velocity arrows — parabolic channel", "X (m)", "Y (m)", "rgba(220,235,250,0.6)", "#bbb", 0.3, "|v| (m/s)");
		double[] doubleValues2 = new double[count2];
		double[] doubleValues3 = new double[count2];
		for (int m = 0; m < count2; m++)
		{
			doubleValues2[m] = Math.Max(0.0, 1.0 - Math.Pow(((doubleValues3[m] = (double)m * 6.0 / (double)(count2 - 1)) - 3.0) / 3.0, 2.0)) * 2.0;
		}
		htmlFigure.SetLimits(0.0, 6.0, 0.0, 2.2);
		htmlFigure.PlotLine(doubleValues3, doubleValues2, "#2c7bb6");
		htmlFigure.DrawAxes("Y (m)", "vmid (m/s)", "", "Mid-channel velocity profile");
		htmlFigure.Save(Path.Combine(dir, "test_velocity_arrows.html"));
	}

	public static void TestSaveAsPng(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		int count = 100;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * 0.1;
			doubleValuesArray[i] = Math.Sin(doubleValues[i]);
		}
		htmlFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		htmlFigure.PlotLine(doubleValues, doubleValuesArray, "#2196F3");
		htmlFigure.DrawAxes("X", "sin(x)", "", "SaveAsPng: HTML + PNG side-by-side");
		string path = Path.Combine(dir, "test_save_as_png.html");
		htmlFigure.Save(path);
		htmlFigure.SaveAsPng(path);
	}

	public static void TestSaveAsPdf(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		int count = 100;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * 0.1;
			doubleValuesArray[i] = Math.Sin(doubleValues[i]);
		}
		htmlFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		htmlFigure.PlotLine(doubleValues, doubleValuesArray, "#2196F3");
		htmlFigure.DrawAxes("X", "sin(x)", "", "SaveAsPdf: HTML + PDF side-by-side");
		string path = Path.Combine(dir, "test_save_as_pdf.html");
		htmlFigure.Save(path);
		htmlFigure.SaveAsPdf(path);
	}

	public static void TestAxisReverse(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		htmlFigure.PageTitle = "Axis Reverse";
		int count = 60;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * (10.0 / (double)(count - 1));
			doubleValuesArray[i] = Math.Sin(doubleValues[i]);
		}
		string[] coefficients = new string[4] { "Normal", "XReverse", "YReverse", "XReverse + YReverse" };
		bool[] coefficientsArray = new bool[4] { false, true, false, true };
		bool[] coefficients2 = new bool[4] { false, false, true, true };
		for (int j = 0; j < 4; j++)
		{
			htmlFigure.Subplot(2, 2, j + 1);
			htmlFigure.XReverse = coefficientsArray[j];
			htmlFigure.YReverse = coefficients2[j];
			htmlFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
			htmlFigure.PlotLine(doubleValues, doubleValuesArray, "#2196F3");
			htmlFigure.PlotScatter(new double[2] { 0.0, 10.0 }, new double[2] { -1.0, 1.0 }, "#F44336", 5.0);
			htmlFigure.DrawAxes("X", "sin(x)", "", coefficients[j]);
		}
		htmlFigure.Save(Path.Combine(dir, "test_axis_reverse.html"));
	}

	public static void TestY2Reverse(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure(800, 450);
		int count = 80;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		double[] doubleValues2 = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * (10.0 / (double)(count - 1));
			doubleValuesArray[i] = Math.Sin(doubleValues[i]);
			doubleValues2[i] = 50.0 + 30.0 * Math.Cos(doubleValues[i] * 0.5);
		}
		htmlFigure.SetLimits(0.0, 10.0, -1.5, 1.5);
		htmlFigure.SetLimitsY2(0.0, 100.0);
		htmlFigure.Y2Reverse = true;
		htmlFigure.PlotLine(doubleValues, doubleValuesArray, "#2196F3");
		htmlFigure.PlotLine(doubleValues, doubleValues2, "#F44336", 2.0, "--", useY2: true);
		htmlFigure.DrawAxes("Time (s)", "Amplitude (Y)", "Temperature °C (Y2 reversed)", "Y2Reverse: Y normal, Y2 flipped");
		htmlFigure.DrawLegend(new (string, string, string)[2]
		{
			("Amplitude (Y)", "#2196F3", ""),
			("Temperature (Y2)", "#F44336", "--")
		});
		htmlFigure.Save(Path.Combine(dir, "test_y2_reverse.html"));
	}

	public static void TestBasicLine(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		int count = 100;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * 0.1;
			doubleValuesArray[i] = Math.Sin(doubleValues[i]);
		}
		htmlFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		htmlFigure.PlotLine(doubleValues, doubleValuesArray, "b");
		htmlFigure.DrawAxes("X", "sin(x)", "", "Basic Sine Wave");
		htmlFigure.Save(Path.Combine(dir, "test_basic_line.html"));
	}

	public static void TestMultiLineWithLegend(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		int count = 100;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		double[] doubleValues2 = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * 0.1;
			doubleValuesArray[i] = Math.Sin(doubleValues[i]);
			doubleValues2[i] = Math.Cos(doubleValues[i]);
		}
		htmlFigure.SetLimits(0.0, 10.0, -1.2, 1.2);
		htmlFigure.PlotLine(doubleValues, doubleValuesArray, "#2196F3");
		htmlFigure.PlotLine(doubleValues, doubleValues2, "#F44336", 2.0, "--");
		htmlFigure.DrawAxes("X", "Value", "", "Multi Line");
		htmlFigure.DrawLegend(new (string, string, string)[2]
		{
			("sin(x)", "#2196F3", ""),
			("cos(x)", "#F44336", "--")
		});
		htmlFigure.Save(Path.Combine(dir, "test_multi_line_legend.html"));
	}

	public static void TestScatterPlot(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		Random random = new Random(42);
		int count = 50;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = random.NextDouble() * 10.0;
			doubleValuesArray[i] = Math.Sin(doubleValues[i]) + (random.NextDouble() - 0.5) * 0.5;
		}
		htmlFigure.PlotScatter(doubleValues, doubleValuesArray, "r", 4.0, 0.7);
		htmlFigure.DrawAxes("X", "Y", "", "Scatter Plot");
		htmlFigure.DrawLegend(new (string, string, string, string)[1] { ("sin(x) + noise", "r", "", "circle") });
		htmlFigure.Save(Path.Combine(dir, "test_scatter.html"));
	}

	public static void TestNaNHandling(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		double[] x = new double[10] { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0 };
		double[] y = new double[10]
		{
			0.0,
			1.0,
			4.0,
			double.NaN,
			double.NaN,
			3.0,
			2.0,
			5.0,
			6.0,
			7.0
		};
		htmlFigure.PlotLine(x, y, "b");
		htmlFigure.DrawAxes("X", "Y", "", "NaN Gap Handling");
		htmlFigure.Save(Path.Combine(dir, "test_nan_handling.html"));
	}

	public static void TestDualYAxis(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		int count = 50;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		double[] doubleValues2 = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = i;
			doubleValuesArray[i] = Math.Sin((double)i * 0.2) * 100.0;
			doubleValues2[i] = (double)(i * i) * 0.5;
		}
		htmlFigure.SetLimits(0.0, 50.0, -120.0, 120.0);
		htmlFigure.SetLimitsY2(0.0, 1300.0);
		htmlFigure.PlotLine(doubleValues, doubleValuesArray, "b");
		htmlFigure.PlotLine(doubleValues, doubleValues2, "r", 2.0, "", useY2: true);
		htmlFigure.DrawAxes("Index", "Sin Scale", "Quadratic Scale", "Dual Y Axis");
		htmlFigure.DrawLegend(new (string, string, string)[2]
		{
			("sin", "b", ""),
			("quadratic", "r", "")
		});
		htmlFigure.Save(Path.Combine(dir, "test_dual_y_axis.html"));
	}

	public static void TestLineMarkers(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		string[] coefficients = new string[7] { "circle", "square", "triangle", "diamond", "*", "x", "+" };
		double[] coefficientsArray = new double[5] { 1.0, 2.0, 3.0, 4.0, 5.0 };
		for (int i = 0; i < coefficients.Length; i++)
		{
			double[] doubleValues = new double[coefficientsArray.Length];
			for (int j = 0; j < coefficientsArray.Length; j++)
			{
				doubleValues[j] = (double)i + Math.Sin(coefficientsArray[j] + (double)i);
			}
			htmlFigure.PlotLine(coefficientsArray, doubleValues, (new string[7] { "#2196F3", "#F44336", "#4CAF50", "#FF9800", "#9C27B0", "#00BCD4", "#795548" })[i], 2.0, "", useY2: false, coefficients[i], 5.0);
		}
		htmlFigure.DrawAxes("X", "Y", "", "Line Markers");
		htmlFigure.Save(Path.Combine(dir, "test_line_markers.html"));
	}

	public static void TestLogScale(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		double[] doubleValues = new double[50];
		double[] doubleValuesArray = new double[50];
		for (int i = 0; i < 50; i++)
		{
			doubleValues[i] = i + 1;
			doubleValuesArray[i] = Math.Pow(10.0, (double)i * 0.1);
		}
		htmlFigure.YScale = ScaleType.Log10;
		htmlFigure.PlotLine(doubleValues, doubleValuesArray, "m");
		htmlFigure.DrawAxes("X", "Y (log)", "", "Log Scale Y Axis");
		htmlFigure.Save(Path.Combine(dir, "test_log_scale.html"));
	}

	public static void TestBarPlot(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		double[] x = new double[5] { 1.0, 2.0, 3.0, 4.0, 5.0 };
		double[] y = new double[5] { 23.0, 45.0, 12.0, 67.0, 34.0 };
		htmlFigure.PlotBar(x, y, "#4CAF50", 0.7);
		htmlFigure.DrawAxes("Category", "Value", "", "Bar Plot");
		htmlFigure.Save(Path.Combine(dir, "test_bar.html"));
	}

	public static void TestGroupedBarPlot(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		double[] x = new double[4] { 1.0, 2.0, 3.0, 4.0 };
		double[][] series = new double[3][]
		{
			new double[4] { 20.0, 35.0, 30.0, 25.0 },
			new double[4] { 25.0, 32.0, 34.0, 20.0 },
			new double[4] { 15.0, 25.0, 28.0, 30.0 }
		};
		htmlFigure.PlotBarGrouped(x, series, new string[3] { "#2196F3", "#F44336", "#4CAF50" });
		htmlFigure.DrawAxes("Group", "Value", "", "Grouped Bar Plot");
		htmlFigure.DrawLegend(new (string, string, string)[3]
		{
			("A", "#2196F3", ""),
			("B", "#F44336", ""),
			("C", "#4CAF50", "")
		});
		htmlFigure.Save(Path.Combine(dir, "test_grouped_bar.html"));
	}

	public static void TestStackedBar(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		double[] x = new double[5] { 1.0, 2.0, 3.0, 4.0, 5.0 };
		double[][] series = new double[3][]
		{
			new double[5] { 10.0, 20.0, 15.0, 25.0, 30.0 },
			new double[5] { 20.0, 15.0, 25.0, 10.0, 20.0 },
			new double[5] { 15.0, 10.0, 20.0, 15.0, 10.0 }
		};
		htmlFigure.PlotBarStacked(x, series, new string[3] { "#2196F3", "#FF9800", "#4CAF50" });
		htmlFigure.DrawAxes("Category", "Stacked Value", "", "Stacked Bar");
		htmlFigure.DrawLegend(new (string, string, string)[3]
		{
			("Low", "#2196F3", ""),
			("Mid", "#FF9800", ""),
			("High", "#4CAF50", "")
		});
		htmlFigure.Save(Path.Combine(dir, "test_stacked_bar.html"));
	}

	public static void TestHorizontalBar(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		double[] y = new double[6] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 };
		double[] x = new double[6] { 45.0, 72.0, 33.0, 88.0, 56.0, 61.0 };
		htmlFigure.PlotBarH(y, x, "#9C27B0", 0.6);
		htmlFigure.DrawAxes("Value", "Category", "", "Horizontal Bar");
		htmlFigure.Save(Path.Combine(dir, "test_horizontal_bar.html"));
	}

	public static void TestHistogram(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		Random random = new Random(123);
		double[] doubleValues = new double[1000];
		for (int i = 0; i < doubleValues.Length; i++)
		{
			double d = 1.0 - random.NextDouble();
			double nextDouble = random.NextDouble();
			doubleValues[i] = Math.Sqrt(-2.0 * Math.Log(d)) * Math.Cos(Math.PI * 2.0 * nextDouble);
		}
		htmlFigure.PlotHistogram(doubleValues, 30);
		htmlFigure.DrawAxes("Value", "Count", "", "Histogram (Normal Distribution)");
		htmlFigure.Save(Path.Combine(dir, "test_histogram.html"));
	}

	public static void TestPieChart(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		double[] values = new double[5] { 30.0, 20.0, 15.0, 25.0, 10.0 };
		string[] colors = new string[5] { "#2196F3", "#F44336", "#4CAF50", "#FF9800", "#9C27B0" };
		string[] labels = new string[5] { "Water", "Fire", "Earth", "Wind", "Aether" };
		htmlFigure.PlotPie(values, colors, labels);
		htmlFigure.DrawAxes("", "", "", "Pie Chart");
		htmlFigure.Save(Path.Combine(dir, "test_pie.html"));
		HtmlFigure htmlFigure2 = new HtmlFigure();
		htmlFigure2.PlotPie(values, colors, labels, 0.5);
		htmlFigure2.DrawAxes("", "", "", "Donut Chart");
		htmlFigure2.DrawLegend(new (string, string, string)[5]
		{
			("Water", "#2196F3", ""),
			("Fire", "#F44336", ""),
			("Earth", "#4CAF50", ""),
			("Wind", "#FF9800", ""),
			("Aether", "#9C27B0", "")
		});
		htmlFigure2.Save(Path.Combine(dir, "test_donut.html"));
	}

	public static void TestPlotFill(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		int count = 100;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		double[] doubleValues2 = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * 0.1;
			double computed = Math.Sin(doubleValues[i]);
			doubleValuesArray[i] = computed - 0.3;
			doubleValues2[i] = computed + 0.3;
		}
		htmlFigure.PlotFill(doubleValues, doubleValuesArray, doubleValues2, "rgba(33,150,243,0.3)");
		double[] doubleValues3 = new double[count];
		for (int j = 0; j < count; j++)
		{
			doubleValues3[j] = Math.Sin(doubleValues[j]);
		}
		htmlFigure.PlotLine(doubleValues, doubleValues3, "#2196F3");
		htmlFigure.DrawAxes("X", "Y", "", "Filled Area (Confidence Band)");
		htmlFigure.Save(Path.Combine(dir, "test_fill.html"));
	}

	public static void TestRefLines(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		int count = 100;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * 0.1;
			doubleValuesArray[i] = Math.Sin(doubleValues[i]);
		}
		htmlFigure.SetLimits(0.0, 10.0, -1.5, 1.5);
		htmlFigure.PlotLine(doubleValues, doubleValuesArray, "b");
		htmlFigure.HLine(0.0);
		htmlFigure.VLine(Math.PI, "r", 1.5, ":");
		htmlFigure.DrawAxes("X", "sin(x)", "", "Reference Lines");
		htmlFigure.Save(Path.Combine(dir, "test_ref_lines.html"));
	}

	public static void TestErrorBars(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		double[] x = new double[6] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 };
		double[] y = new double[6] { 2.1, 3.5, 2.8, 4.2, 3.9, 5.1 };
		double[] yErr = new double[6] { 0.3, 0.5, 0.2, 0.6, 0.4, 0.3 };
		htmlFigure.PlotErrorBar(x, y, yErr, "k", 5.0);
		htmlFigure.DrawAxes("X", "Y", "", "Error Bars");
		htmlFigure.Save(Path.Combine(dir, "test_error_bars.html"));
	}

	public static void TestAnnotationsArrows(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		int count = 100;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * 0.1;
			doubleValuesArray[i] = Math.Sin(doubleValues[i]);
		}
		htmlFigure.SetLimits(0.0, 10.0, -1.5, 1.5);
		htmlFigure.PlotLine(doubleValues, doubleValuesArray, "b");
		htmlFigure.DrawAnnotation(4.2, 0.35, "Peak", 12.0, "r");
		htmlFigure.DrawArrow(4.0, 0.5, Math.PI / 2.0, 1.0, "r");
		htmlFigure.DrawAxes("X", "sin(x)", "", "Annotations & Arrows");
		htmlFigure.Save(Path.Combine(dir, "test_annotations.html"));
	}

	public static void TestCustomTickLabels(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		double[] lookupTable = new double[6] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 };
		double[] y = new double[6] { 15.0, 30.0, 22.0, 45.0, 38.0, 50.0 };
		htmlFigure.PlotBar(lookupTable, y, "#FF9800", 0.7);
		htmlFigure.SetTickLabelsX(lookupTable, new string[6] { "Jan", "Feb", "Mar", "Apr", "May", "Jun" });
		htmlFigure.DrawAxes("Month", "Sales", "", "Custom Tick Labels");
		htmlFigure.Save(Path.Combine(dir, "test_custom_ticks.html"));
	}

	public static void TestTable(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		htmlFigure.PageTitle = "Table Test";
		string[] headers = new string[3] { "Name", "Score", "Grade" };
		string[][] rows = new string[4][]
		{
			new string[3] { "Alice", "95", "A" },
			new string[3] { "Bob", "82", "B" },
			new string[3] { "Charlie", "91", "A" },
			new string[3] { "Diana", "78", "C" }
		};
		htmlFigure.DrawTable(0.0, 0.0, headers, rows);
		htmlFigure.Save(Path.Combine(dir, "test_table.html"));
	}

	public static void TestStickyHeaderTable(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		htmlFigure.PageTitle = "Sticky Header Table";
		string[] headers = new string[5] { "Row", "X", "Y", "Z", "Value" };
		string[][] strings = new string[200][];
		Random random = new Random(99);
		for (int i = 0; i < 200; i++)
		{
			strings[i] = new string[5]
			{
				$"{i + 1}",
				$"{random.NextDouble():F3}",
				$"{random.NextDouble():F3}",
				$"{random.NextDouble():F3}",
				$"{random.Next(1000)}"
			};
		}
		htmlFigure.DrawTable(0.0, 0.0, headers, strings, 100.0, 20.0, 400.0);
		htmlFigure.Save(Path.Combine(dir, "test_sticky_table.html"));
	}

	public static void TestSubplots(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		htmlFigure.PageTitle = "Subplots";
		int count = 50;
		double[] doubleValues = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * 0.2;
		}
		htmlFigure.Subplot(2, 2, 1);
		double[] doubleValuesArray = new double[count];
		for (int j = 0; j < count; j++)
		{
			doubleValuesArray[j] = Math.Sin(doubleValues[j]);
		}
		htmlFigure.PlotLine(doubleValues, doubleValuesArray, "b");
		htmlFigure.DrawAxes("X", "Y", "", "sin(x)");
		htmlFigure.Subplot(2, 2, 2);
		double[] doubleValues2 = new double[count];
		for (int k = 0; k < count; k++)
		{
			doubleValues2[k] = Math.Cos(doubleValues[k]);
		}
		htmlFigure.PlotLine(doubleValues, doubleValues2, "r");
		htmlFigure.DrawAxes("X", "Y", "", "cos(x)");
		htmlFigure.Subplot(2, 2, 3);
		double[] doubleValues3 = new double[count];
		for (int l = 0; l < count; l++)
		{
			doubleValues3[l] = Math.Tan(doubleValues[l]);
		}
		htmlFigure.SetLimits(0.0, 10.0, -5.0, 5.0);
		htmlFigure.PlotLine(doubleValues, doubleValues3, "g");
		htmlFigure.DrawAxes("X", "Y", "", "tan(x)");
		htmlFigure.Subplot(2, 2, 4);
		double[] doubleValues4 = new double[count];
		for (int m = 0; m < count; m++)
		{
			doubleValues4[m] = doubleValues[m] * doubleValues[m];
		}
		htmlFigure.PlotLine(doubleValues, doubleValues4, "m");
		htmlFigure.DrawAxes("X", "Y", "", "x^2");
		htmlFigure.Save(Path.Combine(dir, "test_subplots.html"));
	}

	public static void TestEmbeddedSvg(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		htmlFigure.PageTitle = "Embedded SVG";
		string svgMarkup = "<svg width='200' height='100' xmlns='http://www.w3.org/2000/svg'><rect width='200' height='100' fill='#e3f2fd' stroke='#1565c0' stroke-width='2'/><text x='100' y='55' text-anchor='middle' font-size='14' fill='#1565c0'>Inline SVG</text></svg>";
		htmlFigure.AddSvg(svgMarkup);
		htmlFigure.Save(Path.Combine(dir, "test_embedded_svg.html"));
	}

	public static void TestMixedChartAndTable(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		htmlFigure.PageTitle = "Mixed Chart & Table";
		double[] coefficients = new double[5] { 1.0, 2.0, 3.0, 4.0, 5.0 };
		double[] coefficientsArray = new double[5] { 23.0, 45.0, 12.0, 67.0, 34.0 };
		htmlFigure.PlotBar(coefficients, coefficientsArray, "#2196F3", 0.7);
		htmlFigure.DrawAxes("Category", "Value", "", "Sales Data");
		string[] headers = new string[3] { "Category", "Value", "% of Total" };
		double sum = coefficientsArray.Sum();
		string[][] strings = new string[coefficients.Length][];
		for (int i = 0; i < coefficients.Length; i++)
		{
			strings[i] = new string[3]
			{
				$"Cat {coefficients[i]:F0}",
				$"{coefficientsArray[i]:F0}",
				$"{coefficientsArray[i] / sum * 100.0:F1}%"
			};
		}
		htmlFigure.DrawTable(0.0, 0.0, headers, strings);
		htmlFigure.Save(Path.Combine(dir, "test_mixed.html"));
	}

	public static void TestShorthandColors(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure();
		string[] coefficients = new string[7] { "r", "g", "b", "c", "m", "y", "k" };
		double[] coefficientsArray = new double[5] { 1.0, 2.0, 3.0, 4.0, 5.0 };
		for (int i = 0; i < coefficients.Length; i++)
		{
			double[] doubleValues = new double[coefficientsArray.Length];
			for (int j = 0; j < coefficientsArray.Length; j++)
			{
				doubleValues[j] = (double)i + Math.Sin(coefficientsArray[j] + (double)i * 0.5);
			}
			htmlFigure.PlotLine(coefficientsArray, doubleValues, coefficients[i]);
		}
		htmlFigure.DrawAxes("X", "Y", "", "Shorthand Colors");
		htmlFigure.Save(Path.Combine(dir, "test_shorthand_colors.html"));
	}

	public static void TestTimeLine(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure(800, 400);
		DateTime start = new DateTime(2026, 4, 3, 20, 10, 0);
		DateTime[] dateTimes = (from i in Enumerable.Range(0, 100)
			select start.AddMinutes(i * 5)).ToArray();
		double[] y = dateTimes.Select((DateTime t, int i) => Math.Sin((double)i * 0.1) * 50.0 + 100.0).ToArray();
		htmlFigure.SetLimitsX(dateTimes);
		htmlFigure.SetLimitsY(0.0, 200.0);
		htmlFigure.PlotTimeLine(dateTimes, y, "navy", 2.5);
		htmlFigure.DrawTimeAxes("Event Log", "Amplitude", 7, "Time Series Test");
		htmlFigure.Save(Path.Combine(dir, "test_timeline.html"));
	}

	public static void TestTimeLineMultiSeries(string dir)
	{
		HtmlFigure htmlFigure = new HtmlFigure(800, 400);
		DateTime start1 = new DateTime(2026, 4, 1, 0, 0, 0);
		DateTime start2 = new DateTime(2026, 4, 5, 0, 0, 0);
		DateTime[] dateTimes = (from i in Enumerable.Range(0, 50)
			select start1.AddHours(i)).ToArray();
		DateTime[] dateTimesArray = (from i in Enumerable.Range(0, 50)
			select start2.AddHours(i)).ToArray();
		double[] y = dateTimes.Select((DateTime _, int i) => Math.Sin((double)i * 0.1) * 50.0 + 100.0).ToArray();
		double[] y2 = dateTimesArray.Select((DateTime _, int i) => Math.Cos((double)i * 0.1) * 30.0 + 80.0).ToArray();
		htmlFigure.SetLimitsY(0.0, 200.0);
		htmlFigure.PlotTimeLine(dateTimes, y);
		htmlFigure.PlotTimeLine(dateTimesArray, y2, "red", 2.0, "--");
		htmlFigure.DrawTimeAxes("Time", "Value", 7, "Multi-Series Auto-Scale");
		htmlFigure.Save(Path.Combine(dir, "test_timeline_multi.html"));
	}
}
