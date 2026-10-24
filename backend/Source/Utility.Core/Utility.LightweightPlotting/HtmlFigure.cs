using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Utility.LightweightPlotting;

public class HtmlFigure
{
	private interface IHtmlElement
	{
	}

	private class ChartElement : IHtmlElement
	{
		public List<DatasetConfig> Datasets = new List<DatasetConfig>();

		public List<AnnotationConfig> Annotations = new List<AnnotationConfig>();

		public string Title = "";

		public string XTitle = "";

		public string YTitle = "";

		public string Y2Title = "";

		public ScaleType XScaleType;

		public ScaleType YScaleType;

		public ScaleType Y2ScaleType;

		public double? MinX;

		public double? MaxX;

		public double? MinY;

		public double? MaxY;

		public double? MinY2;

		public double? MaxY2;

		public bool ShowGrid = true;

		public bool HasY2;

		public bool HasAnnotations;

		public bool HasErrorBars;

		public bool XReverse;

		public bool YReverse;

		public bool Y2Reverse;

		public bool ShowLegend;

		public string LegendPosition;

		public bool IsBarChart;

		public bool IsPieChart;

		public bool IsStacked;

		public bool IsTimeAxis;

		public double TimeStepSeconds;

		public string TimeJsFormat = "HH:mm";

		public int TimeTargetTicks = 7;

		public double[] TimeTickValues = Array.Empty<double>();

		public string IndexAxis;

		public string[] BarLabels;

		public string[] PieLabels;

		public double PieCutout;

		public double PadLeft;

		public double PadRight;

		public double PadTop;

		public double PadBottom;

		public int GridRow;

		public int GridCol;

		public double ExplicitX;

		public double ExplicitY;

		public double ExplicitW;

		public double ExplicitH;

		public (double[] positions, string[] labels)? CustomTicksX;

		public (double[] positions, string[] labels)? CustomTicksY;

		public ColorBarConfig ColorBar;
	}

	private class DatasetConfig
	{
		public string ChartType;

		public string Data = "[]";

		public string Label;

		public string BorderColor;

		public string BackgroundColor;

		public string[] BackgroundColors;

		public double BorderWidth;

		public double[] BorderDash;

		public string PointStyle;

		public double PointRadius = -1.0;

		public double PointHoverRadius;

		public string PointBorderColor;

		public string PointBackgroundColor;

		public bool ShowLine;

		public bool Fill;

		public string FillTarget;

		public string YAxisID;

		public double? BarPercentage;

		public double? CategoryPercentage;

		public bool IsErrorBar;

		public double[] ErrorLow;

		public double[] ErrorHigh;

		public double ErrorCapWidth;

		public double ErrorThickness;
	}

	private class AnnotationConfig
	{
		public string Type = "line";

		public string ScaleID;

		public double Value;

		public double XValue;

		public double YValue;

		public double XMin;

		public double YMin;

		public double XMax;

		public double YMax;

		public string BorderColor = "#999";

		public double BorderWidth = 1.0;

		public double[] BorderDash;

		public string Content;

		public string FontColor;

		public double FontSize = 10.0;
	}

	private class ColorBarConfig
	{
		public double Min;

		public double Max;

		public string[] Palette = Array.Empty<string>();

		public string Label = "";

		public double Width = 20.0;
	}

	private class TableElement : IHtmlElement
	{
		public string[] Headers = Array.Empty<string>();

		public string[][] Rows = Array.Empty<string[]>();

		public double MaxHeight = 500.0;
	}

	private class SvgElement : IHtmlElement
	{
		public string Markup = "";
	}

	private class TextElement : IHtmlElement
	{
		public string Content = "";

		public string Tag = "p";

		public string FontWeight = "normal";

		public double FontSize = 12.0;
	}

	private readonly int _width;

	private readonly int _height;

	private readonly List<IHtmlElement> _elements = new List<IHtmlElement>();

	private string _pageTitle = "";

	private ChartElement _current;

	private int _gridRows;

	private int _gridCols;

	private int _gridIndex;

	private bool _useGrid;

	private static readonly Lazy<string> ChartJsSource = new Lazy<string>(() =>
	{
		using Stream stream = typeof(HtmlFigure).Assembly.GetManifestResourceStream("chart.umd.min.js") ?? throw new InvalidOperationException("Embedded resource 'chart.umd.min.js' not found.");
		using StreamReader streamReader = new StreamReader(stream);
		return streamReader.ReadToEnd();
	});

	private static readonly Lazy<string> AnnotationPluginSource = new Lazy<string>(() =>
	{
		using Stream stream = typeof(HtmlFigure).Assembly.GetManifestResourceStream("chartjs-plugin-annotation.min.js");
		if (stream != null)
		{
			using StreamReader streamReader = new StreamReader(stream);
			return streamReader.ReadToEnd();
		}
		return (string)null;
	});

	public ScaleType XScale { get; set; }

	public ScaleType YScale { get; set; }

	public ScaleType Y2Scale { get; set; }

	public bool ShowGrid { get; set; } = true;

	public bool XReverse { get; set; }

	public bool YReverse { get; set; }

	public bool Y2Reverse { get; set; }

	public bool ShowTicks { get; set; } = true;

	public double TickLength { get; set; } = 6.0;

	public string PageTitle
	{
		get
		{
			return _pageTitle;
		}
		set
		{
			_pageTitle = value ?? "";
		}
	}

	public int Width => _width;

	public int Height => _height;

	public HtmlFigure(int width = 800, int height = 600)
	{
		_width = width;
		_height = height;
	}

	public void Subplot(int rows, int cols, int index)
	{
		FinalizeCurrentChart();
		_useGrid = true;
		_gridRows = rows;
		_gridCols = cols;
		_gridIndex = index;
	}

	public void SetPlotArea(double x, double y, double width, double height)
	{
		FinalizeCurrentChart();
		EnsureChart();
		_current.ExplicitX = x;
		_current.ExplicitY = y;
		_current.ExplicitW = width;
		_current.ExplicitH = height;
	}

	public void SetMargins(double left, double right, double top, double bottom)
	{
		EnsureChart();
		_current.PadLeft = left;
		_current.PadRight = right;
		_current.PadTop = top;
		_current.PadBottom = bottom;
	}

	public void SetLimits(double minX, double maxX, double minY, double maxY)
	{
		EnsureChart();
		_current.MinX = minX;
		_current.MaxX = maxX;
		_current.MinY = minY;
		_current.MaxY = maxY;
	}

	public void SetLimitsY(double minY, double maxY)
	{
		EnsureChart();
		_current.MinY = minY;
		_current.MaxY = maxY;
	}

	public void SetLimitsX(DateTime[] times)
	{
		EnsureChart();
		double[] source = ((IEnumerable<DateTime>)times).Select((Func<DateTime, double>)((DateTime t) => new DateTimeOffset(t).ToUnixTimeSeconds())).ToArray();
		_current.MinX = source.Min();
		_current.MaxX = source.Max();
		_current.IsTimeAxis = true;
	}

	public void SetLimitsX(double minX, double maxX)
	{
		EnsureChart();
		_current.MinX = minX;
		_current.MaxX = maxX;
	}

	public void SetLimitsY2(double minY, double maxY)
	{
		EnsureChart();
		_current.MinY2 = minY;
		_current.MaxY2 = maxY;
		_current.HasY2 = true;
	}

	public void SetTickLabelsX(double[] positions, string[] labels)
	{
		EnsureChart();
		_current.CustomTicksX = (positions, labels);
	}

	public void SetTickLabelsY(double[] positions, string[] labels)
	{
		EnsureChart();
		_current.CustomTicksY = (positions, labels);
	}

	public void DrawAxes(string xTitle = "", string yTitle = "", string y2Title = "", string title = "")
	{
		EnsureChart();
		_current.XTitle = xTitle;
		_current.YTitle = yTitle;
		_current.Y2Title = y2Title;
		_current.Title = title;
		_current.XScaleType = XScale;
		_current.YScaleType = YScale;
		_current.Y2ScaleType = Y2Scale;
		_current.ShowGrid = ShowGrid;
		_current.XReverse = XReverse;
		_current.YReverse = YReverse;
		_current.Y2Reverse = Y2Reverse;
	}

	public void PlotLine(double[] x, double[] y, string color = "blue", double thickness = 2.0, string style = "", bool useY2 = false, string marker = "", double markerSize = 3.0, double markerOpacity = 1.0)
	{
		EnsureChart();
		string borderColor = ResolveColor(color);
		string opacity = ((!string.IsNullOrEmpty(marker) && markerOpacity < 1.0) ? ApplyOpacity(borderColor, markerOpacity) : borderColor);
		DatasetConfig datasetConfig = new DatasetConfig
		{
			ChartType = "scatter",
			ShowLine = true,
			Data = BuildXYData(x, y),
			BorderColor = borderColor,
			BorderWidth = thickness,
			BorderDash = ParseStyle(style),
			PointStyle = MapMarker(marker),
			PointRadius = (string.IsNullOrEmpty(marker) ? 0.0 : markerSize),
			PointHoverRadius = (string.IsNullOrEmpty(marker) ? 4.0 : (markerSize + 2.0)),
			PointBorderColor = opacity,
			PointBackgroundColor = opacity,
			Fill = false,
			YAxisID = (useY2 ? "y2" : "y")
		};
		_current.Datasets.Add(datasetConfig);
		if (useY2)
		{
			_current.HasY2 = true;
		}
	}

	public void PlotLine(float[] x, float[] y, string color = "blue", double thickness = 2.0, string style = "", bool useY2 = false, string marker = "", double markerSize = 3.0, double markerOpacity = 1.0)
	{
		PlotLine(D(x), D(y), color, thickness, style, useY2, marker, markerSize, markerOpacity);
	}

	public void PlotLine(int[] x, double[] y, string color = "blue", double thickness = 2.0, string style = "", bool useY2 = false, string marker = "", double markerSize = 3.0, double markerOpacity = 1.0)
	{
		PlotLine(D(x), y, color, thickness, style, useY2, marker, markerSize, markerOpacity);
	}

	public void PlotLine(int[] x, int[] y, string color = "blue", double thickness = 2.0, string style = "", bool useY2 = false, string marker = "", double markerSize = 3.0, double markerOpacity = 1.0)
	{
		PlotLine(D(x), D(y), color, thickness, style, useY2, marker, markerSize, markerOpacity);
	}

	public void PlotTimeLine(DateTime[] times, double[] y, string color = "blue", double thickness = 2.0, string style = "", string marker = "", double markerSize = 3.0, bool useY2 = false)
	{
		EnsureChart();
		double[] doubleValues = ((IEnumerable<DateTime>)times).Select((Func<DateTime, double>)((DateTime t) => new DateTimeOffset(t).ToUnixTimeSeconds())).ToArray();
		if (!_current.MinX.HasValue || !_current.MaxX.HasValue)
		{
			_current.MinX = doubleValues.Min();
			_current.MaxX = doubleValues.Max();
		}
		else
		{
			_current.MinX = Math.Min(_current.MinX.Value, doubleValues.Min());
			_current.MaxX = Math.Max(_current.MaxX.Value, doubleValues.Max());
		}
		_current.IsTimeAxis = true;
		DatasetConfig datasetConfig = new DatasetConfig
		{
			ChartType = "scatter",
			ShowLine = true,
			Data = BuildXYData(doubleValues, y),
			BorderColor = ResolveColor(color),
			BorderWidth = thickness,
			BorderDash = ParseStyle(style),
			PointStyle = MapMarker(marker),
			PointRadius = (string.IsNullOrEmpty(marker) ? 0.0 : markerSize),
			PointHoverRadius = (string.IsNullOrEmpty(marker) ? 4.0 : (markerSize + 2.0)),
			Fill = false,
			YAxisID = (useY2 ? "y2" : "y")
		};
		_current.Datasets.Add(datasetConfig);
		if (useY2)
		{
			_current.HasY2 = true;
		}
	}

	public void DrawTimeAxes(string xTitle = "", string yTitle = "", int targetTicks = 7, string title = "")
	{
		EnsureChart();
		_current.XTitle = xTitle;
		_current.YTitle = yTitle;
		_current.Title = title;
		_current.IsTimeAxis = true;
		_current.TimeTargetTicks = targetTicks;
		_current.XScaleType = ScaleType.Linear;
		_current.YScaleType = YScale;
		_current.ShowGrid = ShowGrid;
		_current.XReverse = XReverse;
		_current.YReverse = YReverse;
		if (!_current.MinX.HasValue || !_current.MaxX.HasValue)
		{
			return;
		}
		DateTime localDateTime = DateTimeOffset.FromUnixTimeSeconds((long)_current.MinX.Value).LocalDateTime;
		DateTime localDateTime2 = DateTimeOffset.FromUnixTimeSeconds((long)_current.MaxX.Value).LocalDateTime;
		var (timeSpan, csFmt) = GetNiceTimeInterval(localDateTime2 - localDateTime, targetTicks);
		_current.TimeStepSeconds = timeSpan.TotalSeconds;
		_current.TimeJsFormat = CSharpFormatToJsFormat(csFmt);
		DateTime dateTime = SnapToInterval(localDateTime, timeSpan);
		List<double> doubleList = new List<double>();
		while (dateTime <= localDateTime2)
		{
			if (dateTime >= localDateTime)
			{
				doubleList.Add(new DateTimeOffset(dateTime).ToUnixTimeSeconds());
			}
			if (timeSpan.Ticks == 0L)
			{
				break;
			}
			dateTime = dateTime.Add(timeSpan);
		}
		_current.TimeTickValues = doubleList.ToArray();
	}

	private static (TimeSpan Step, string Format) GetNiceTimeInterval(TimeSpan duration, int targetTicks)
	{
		(double, string)[] valueTuples = new (double, string)[22]
		{
			(1.0, "HH:mm:ss"),
			(2.0, "HH:mm:ss"),
			(5.0, "HH:mm:ss"),
			(10.0, "HH:mm:ss"),
			(15.0, "HH:mm:ss"),
			(30.0, "HH:mm:ss"),
			(60.0, "HH:mm"),
			(120.0, "HH:mm"),
			(300.0, "HH:mm"),
			(600.0, "HH:mm"),
			(900.0, "HH:mm"),
			(1800.0, "HH:mm"),
			(3600.0, "HH:mm"),
			(7200.0, "HH:mm"),
			(10800.0, "MMM dd HH:mm"),
			(21600.0, "MMM dd HH:mm"),
			(43200.0, "MMM dd HH:mm"),
			(86400.0, "MMM dd"),
			(172800.0, "MMM dd"),
			(604800.0, "yyyy-MM-dd"),
			(1209600.0, "yyyy-MM-dd"),
			(2419200.0, "yyyy-MM-dd")
		};
		double totalSeconds = duration.TotalSeconds;
		double maxValue = double.MaxValue;
		int intervalIndex = 0;
		for (int i = 0; i < valueTuples.Length; i++)
		{
			double totalSecondsTerm = totalSeconds / valueTuples[i].Item1;
			if (!(totalSecondsTerm < 2.0))
			{
				double maxValue2 = Math.Abs(totalSecondsTerm - (double)targetTicks);
				if (totalSecondsTerm > 12.0)
				{
					maxValue2 += (totalSecondsTerm - 12.0) * 2.0;
				}
				if (totalSecondsTerm < 3.0)
				{
					maxValue2 += (3.0 - totalSecondsTerm) * 2.0;
				}
				if (maxValue2 < maxValue)
				{
					maxValue = maxValue2;
					intervalIndex = i;
				}
			}
		}
		return (Step: TimeSpan.FromSeconds(valueTuples[intervalIndex].Item1), Format: valueTuples[intervalIndex].Item2);
	}

	private static DateTime SnapToInterval(DateTime dt, TimeSpan step)
	{
		if (step.TotalDays >= 1.0)
		{
			return dt.Date;
		}
		if (step.Ticks == 0L)
		{
			return dt;
		}
		return new DateTime(dt.Ticks - dt.Ticks % step.Ticks);
	}

	private static string CSharpFormatToJsFormat(string csFmt)
	{
		return csFmt switch
		{
			"HH:mm:ss" => "HH:mm:ss", 
			"HH:mm" => "HH:mm", 
			"MMM dd HH:mm" => "MMM DD HH:mm", 
			"MMM dd" => "MMM DD", 
			"yyyy-MM-dd" => "YYYY-MM-DD", 
			_ => csFmt, 
		};
	}

	public void PlotScatter(double[] x, double[] y, string color = "red", double radius = 3.0, double opacity = 1.0, bool useY2 = false, string marker = "circle")
	{
		EnsureChart();
		string borderColor = ResolveColor(color);
		DatasetConfig datasetConfig = new DatasetConfig
		{
			ChartType = "scatter",
			ShowLine = false,
			Data = BuildXYData(x, y),
			BorderColor = borderColor,
			BackgroundColor = ApplyOpacity(borderColor, opacity),
			PointStyle = MapMarker(marker),
			PointRadius = radius,
			PointHoverRadius = radius + 2.0,
			YAxisID = (useY2 ? "y2" : "y")
		};
		_current.Datasets.Add(datasetConfig);
		if (useY2)
		{
			_current.HasY2 = true;
		}
	}

	public void PlotScatter(float[] x, float[] y, string color = "red", double radius = 3.0, double opacity = 1.0, bool useY2 = false, string marker = "circle")
	{
		PlotScatter(D(x), D(y), color, radius, opacity, useY2, marker);
	}

	public void PlotScatter(int[] x, double[] y, string color = "red", double radius = 3.0, double opacity = 1.0, bool useY2 = false, string marker = "circle")
	{
		PlotScatter(D(x), y, color, radius, opacity, useY2, marker);
	}

	public void PlotScatter(int[] x, int[] y, string color = "red", double radius = 3.0, double opacity = 1.0, bool useY2 = false, string marker = "circle")
	{
		PlotScatter(D(x), D(y), color, radius, opacity, useY2, marker);
	}

	public void PlotBar(double[] x, double[] y, string color = "#2196F3", double barWidth = 0.8, string edgeColor = "#333", double edgeWidth = 0.5, double opacity = 1.0)
	{
		EnsureChart();
		string color2 = ResolveColor(color);
		_current.IsBarChart = true;
		_current.BarLabels = Array.ConvertAll(x, (double v) => Fmt(v));
		DatasetConfig datasetConfig = new DatasetConfig
		{
			ChartType = "bar",
			Data = BuildValueData(y),
			BackgroundColor = ApplyOpacity(color2, opacity),
			BorderColor = ResolveColor(edgeColor),
			BorderWidth = edgeWidth,
			BarPercentage = barWidth
		};
		_current.Datasets.Add(datasetConfig);
	}

	public void PlotBar(float[] x, float[] y, string color = "#2196F3", double barWidth = 0.8, string edgeColor = "#333", double edgeWidth = 0.5, double opacity = 1.0)
	{
		PlotBar(D(x), D(y), color, barWidth, edgeColor, edgeWidth, opacity);
	}

	public void PlotBar(int[] x, double[] y, string color = "#2196F3", double barWidth = 0.8, string edgeColor = "#333", double edgeWidth = 0.5, double opacity = 1.0)
	{
		PlotBar(D(x), y, color, barWidth, edgeColor, edgeWidth, opacity);
	}

	public void PlotBar(int[] x, int[] y, string color = "#2196F3", double barWidth = 0.8, string edgeColor = "#333", double edgeWidth = 0.5, double opacity = 1.0)
	{
		PlotBar(D(x), D(y), color, barWidth, edgeColor, edgeWidth, opacity);
	}

	public void PlotBarGrouped(double[] x, double[][] series, string[] colors, double groupWidth = 0.8, string edgeColor = "#333", double edgeWidth = 0.5)
	{
		EnsureChart();
		_current.IsBarChart = true;
		_current.BarLabels = Array.ConvertAll(x, (double v) => Fmt(v));
		for (int intervalIndex = 0; intervalIndex < series.Length; intervalIndex++)
		{
			string backgroundColor = ResolveColor(colors[intervalIndex % colors.Length]);
			DatasetConfig datasetConfig = new DatasetConfig
			{
				ChartType = "bar",
				Label = $"Series {intervalIndex + 1}",
				Data = BuildValueData(series[intervalIndex]),
				BackgroundColor = backgroundColor,
				BorderColor = ResolveColor(edgeColor),
				BorderWidth = edgeWidth,
				BarPercentage = groupWidth
			};
			_current.Datasets.Add(datasetConfig);
		}
	}

	public void PlotBarStacked(double[] x, double[][] series, string[] colors, double barWidth = 0.8, string edgeColor = "#333", double edgeWidth = 0.5)
	{
		EnsureChart();
		_current.IsBarChart = true;
		_current.IsStacked = true;
		_current.BarLabels = Array.ConvertAll(x, (double v) => Fmt(v));
		for (int intervalIndex = 0; intervalIndex < series.Length; intervalIndex++)
		{
			string backgroundColor = ResolveColor(colors[intervalIndex % colors.Length]);
			DatasetConfig datasetConfig = new DatasetConfig
			{
				ChartType = "bar",
				Label = $"Series {intervalIndex + 1}",
				Data = BuildValueData(series[intervalIndex]),
				BackgroundColor = backgroundColor,
				BorderColor = ResolveColor(edgeColor),
				BorderWidth = edgeWidth,
				BarPercentage = barWidth
			};
			_current.Datasets.Add(datasetConfig);
		}
	}

	public void PlotBarH(double[] y, double[] x, string color = "#2196F3", double barHeight = 0.8, string edgeColor = "#333", double edgeWidth = 0.5, double opacity = 1.0)
	{
		EnsureChart();
		string color2 = ResolveColor(color);
		_current.IsBarChart = true;
		_current.IndexAxis = "y";
		_current.BarLabels = Array.ConvertAll(y, (double v) => Fmt(v));
		DatasetConfig datasetConfig = new DatasetConfig
		{
			ChartType = "bar",
			Data = BuildValueData(x),
			BackgroundColor = ApplyOpacity(color2, opacity),
			BorderColor = ResolveColor(edgeColor),
			BorderWidth = edgeWidth,
			BarPercentage = barHeight
		};
		_current.Datasets.Add(datasetConfig);
	}

	public void PlotFill(double[] x, double[] y1, double[] y2, string color = "rgba(0,0,255,0.3)", string edgeColor = "", double edgeWidth = 0.0)
	{
		EnsureChart();
		string backgroundColor = ResolveColor(color);
		DatasetConfig datasetConfig = new DatasetConfig
		{
			ChartType = "scatter",
			ShowLine = true,
			Data = BuildXYData(x, y1),
			BorderColor = (string.IsNullOrEmpty(edgeColor) ? "transparent" : ResolveColor(edgeColor)),
			BorderWidth = edgeWidth,
			BackgroundColor = "transparent",
			PointRadius = 0.0,
			Fill = false
		};
		DatasetConfig datasetConfig2 = new DatasetConfig
		{
			ChartType = "scatter",
			ShowLine = true,
			Data = BuildXYData(x, y2),
			BorderColor = (string.IsNullOrEmpty(edgeColor) ? "transparent" : ResolveColor(edgeColor)),
			BorderWidth = edgeWidth,
			BackgroundColor = backgroundColor,
			PointRadius = 0.0,
			Fill = true,
			FillTarget = "-1"
		};
		_current.Datasets.Add(datasetConfig);
		_current.Datasets.Add(datasetConfig2);
	}

	public void PlotFill(double[] x, double[] y, string color = "rgba(0,0,255,0.3)", string edgeColor = "", double edgeWidth = 0.0)
	{
		EnsureChart();
		string backgroundColor = ResolveColor(color);
		DatasetConfig datasetConfig = new DatasetConfig
		{
			ChartType = "scatter",
			ShowLine = true,
			Data = BuildXYData(x, y),
			BorderColor = (string.IsNullOrEmpty(edgeColor) ? backgroundColor : ResolveColor(edgeColor)),
			BorderWidth = ((edgeWidth > 0.0) ? edgeWidth : 1.0),
			BackgroundColor = backgroundColor,
			PointRadius = 0.0,
			Fill = true,
			FillTarget = "origin"
		};
		_current.Datasets.Add(datasetConfig);
	}

	public void PlotHistogram(double[] data, int bins = 20, string color = "#2196F3", string edgeColor = "#333", double edgeWidth = 0.5, double opacity = 1.0)
	{
		if (data.Length == 0)
		{
			return;
		}
		double computed = data[0];
		double computed2 = data[0];
		for (int i = 1; i < data.Length; i++)
		{
			if (data[i] < computed)
			{
				computed = data[i];
			}
			if (data[i] > computed2)
			{
				computed2 = data[i];
			}
		}
		if (computed2 == computed)
		{
			computed2 = computed + 1.0;
		}
		double binsTerm = (computed2 - computed) / (double)bins;
		int[] intValues = new int[bins];
		for (int j = 0; j < data.Length; j++)
		{
			int binsOffset = (int)((data[j] - computed) / binsTerm);
			if (binsOffset >= bins)
			{
				binsOffset = bins - 1;
			}
			if (binsOffset < 0)
			{
				binsOffset = 0;
			}
			intValues[binsOffset]++;
		}
		double[] doubleValues = new double[bins];
		double[] doubleValuesArray = new double[bins];
		for (int k = 0; k < bins; k++)
		{
			doubleValues[k] = computed + ((double)k + 0.5) * binsTerm;
			doubleValuesArray[k] = intValues[k];
		}
		EnsureChart();
		string color2 = ResolveColor(color);
		_current.IsBarChart = true;
		_current.BarLabels = Array.ConvertAll(doubleValues, (double v) => Fmt(v));
		DatasetConfig datasetConfig = new DatasetConfig
		{
			ChartType = "bar",
			Data = BuildValueData(doubleValuesArray),
			BackgroundColor = ApplyOpacity(color2, opacity),
			BorderColor = ResolveColor(edgeColor),
			BorderWidth = edgeWidth,
			BarPercentage = 1.0,
			CategoryPercentage = 1.0
		};
		_current.Datasets.Add(datasetConfig);
	}

	public void PlotPie(double[] values, string[] colors, string[] labels = null, double innerRadius = 0.0, string edgeColor = "#fff", double edgeWidth = 1.0)
	{
		EnsureChart();
		_current.IsPieChart = true;
		_current.PieCutout = innerRadius;
		string[] backgroundColors = Array.ConvertAll(colors, ResolveColor);
		string[] pieLabels = labels ?? Array.ConvertAll(Enumerable.Range(0, values.Length).ToArray(), (int i) => $"Slice {i + 1}");
		DatasetConfig datasetConfig = new DatasetConfig
		{
			ChartType = ((innerRadius > 0.0) ? "doughnut" : "pie"),
			Data = BuildValueData(values),
			BackgroundColors = backgroundColors,
			BorderColor = ResolveColor(edgeColor),
			BorderWidth = edgeWidth
		};
		_current.PieLabels = pieLabels;
		_current.Datasets.Add(datasetConfig);
	}

	public void PlotErrorBar(double[] x, double[] y, double[] yErr, string color = "k", double capWidth = 4.0, double thickness = 1.5)
	{
		PlotErrorBar(x, y, yErr, yErr, color, capWidth, thickness);
	}

	public void PlotErrorBar(double[] x, double[] y, double[] yErrLow, double[] yErrHigh, string color = "k", double capWidth = 4.0, double thickness = 1.5)
	{
		EnsureChart();
		string resolveColor = ResolveColor(color);
		DatasetConfig datasetConfig = new DatasetConfig
		{
			ChartType = "scatter",
			ShowLine = false,
			Data = BuildXYData(x, y),
			BorderColor = resolveColor,
			BackgroundColor = resolveColor,
			PointRadius = 3.0,
			PointHoverRadius = 5.0,
			IsErrorBar = true,
			ErrorLow = yErrLow,
			ErrorHigh = yErrHigh,
			ErrorCapWidth = capWidth,
			ErrorThickness = thickness
		};
		_current.Datasets.Add(datasetConfig);
		_current.HasErrorBars = true;
	}

	public void HLine(double y, string color = "#999", double thickness = 1.0, string style = "--")
	{
		EnsureChart();
		_current.Annotations.Add(new AnnotationConfig
		{
			Type = "line",
			ScaleID = "y",
			Value = y,
			BorderColor = ResolveColor(color),
			BorderWidth = thickness,
			BorderDash = ParseStyle(style)
		});
		_current.HasAnnotations = true;
	}

	public void VLine(double x, string color = "#999", double thickness = 1.0, string style = "--")
	{
		EnsureChart();
		_current.Annotations.Add(new AnnotationConfig
		{
			Type = "line",
			ScaleID = "x",
			Value = x,
			BorderColor = ResolveColor(color),
			BorderWidth = thickness,
			BorderDash = ParseStyle(style)
		});
		_current.HasAnnotations = true;
	}

	public void DrawAnnotation(double x, double y, string content, double fontSize = 10.0, string color = "k", string anchor = "start")
	{
		EnsureChart();
		_current.Annotations.Add(new AnnotationConfig
		{
			Type = "label",
			XValue = x,
			YValue = y,
			Content = content,
			FontSize = fontSize,
			FontColor = ResolveColor(color)
		});
		_current.HasAnnotations = true;
	}

	public void DrawArrow(double x1, double y1, double x2, double y2, string color = "k", double thickness = 1.5)
	{
		EnsureChart();
		_current.Annotations.Add(new AnnotationConfig
		{
			Type = "arrow",
			XMin = x1,
			YMin = y1,
			XMax = x2,
			YMax = y2,
			BorderColor = ResolveColor(color),
			BorderWidth = thickness
		});
		_current.HasAnnotations = true;
	}

	public void DrawLegend((string label, string color, string style)[] items, string position = "top-right", double? x = null, double? y = null)
	{
		EnsureChart();
		for (int i = 0; i < items.Length && i < _current.Datasets.Count; i++)
		{
			_current.Datasets[i].Label = items[i].label;
		}
		_current.LegendPosition = MapLegendPosition(position);
		_current.ShowLegend = true;
	}

	public void DrawLegend((string label, string color, string style, string symbol)[] items, string position = "top-right", double? x = null, double? y = null)
	{
		EnsureChart();
		for (int i = 0; i < items.Length && i < _current.Datasets.Count; i++)
		{
			_current.Datasets[i].Label = items[i].label;
			if (!string.IsNullOrEmpty(items[i].symbol))
			{
				_current.Datasets[i].PointStyle = MapMarker(items[i].symbol);
			}
		}
		_current.LegendPosition = MapLegendPosition(position);
		_current.ShowLegend = true;
	}

	public void DrawColorBar(double min, double max, string[] palette, string label = "", double x = -1.0, double y = -1.0, double width = 20.0, double height = -1.0)
	{
		EnsureChart();
		_current.ColorBar = new ColorBarConfig
		{
			Min = min,
			Max = max,
			Palette = Array.ConvertAll(palette, ResolveColor),
			Label = label,
			Width = width
		};
	}

	public void DrawTable(double x, double y, string[] headers, string[][] rows, double columnWidth = 100.0, double rowHeight = 20.0, double maxHeight = 500.0)
	{
		FinalizeCurrentChart();
		_elements.Add(new TableElement
		{
			Headers = headers,
			Rows = rows,
			MaxHeight = maxHeight
		});
	}

	public void DrawText(double x, double y, string content, double fontSize = 12.0, string fontWeight = "normal", string anchor = "start")
	{
		string tag;
		if (fontSize >= 16.0)
		{
			tag = "h2";
		}
		else
		{
			tag = ((fontSize >= 14.0) ? "h3" : "p");
		}
		_elements.Add(new TextElement
		{
			Content = content,
			Tag = tag,
			FontWeight = fontWeight,
			FontSize = fontSize
		});
	}

	public void AddSvg(string svgMarkup)
	{
		FinalizeCurrentChart();
		_elements.Add(new SvgElement
		{
			Markup = svgMarkup
		});
	}

	public void PlotVelocityArrows(IEnumerable<(double x, double y)> vertices, IEnumerable<int[]> elements, double[] vx, double[] vy, double maxArrowLengthPx = 30.0, double gridSpacingFactor = 1.5, double userScale = 1.0, double minArrowLengthPx = 2.0, double? magnitudeCap = null, string color = "black", double thickness = 1.0, double arrowheadProportion = 0.3, double arrowheadMaxPx = 8.0, string[] palette = null, double paletteMin = double.NaN, double paletteMax = double.NaN, string title = "", string xLabel = "X", string yLabel = "Y", string meshFillColor = "rgba(220,235,250,0.6)", string meshEdgeColor = "#bbb", double meshEdgeWidth = 0.3, string colorBarLabel = "|v|")
	{
		FinalizeCurrentChart();
		(double, double)[] verticesArray = (vertices as (double, double)[]) ?? vertices.ToArray();
		int[][] elementsArray = (elements as int[][]) ?? elements.ToArray();
		if (verticesArray.Length == 0 || elementsArray.Length == 0 || vx == null || vy == null || vx.Length != elementsArray.Length || vy.Length != elementsArray.Length)
		{
			return;
		}
		bool hasPaletteAndPaletteLengthIsNonZeroAndHasColorBarLabel = palette != null && palette.Length != 0 && colorBarLabel != null;
		SvgFigure svgFigure = new SvgFigure(_width, _height);
		svgFigure.SetMargins(60.0, hasPaletteAndPaletteLengthIsNonZeroAndHasColorBarLabel ? 90 : 30, 30.0, 45.0);
		double minX = verticesArray.Min(((double x, double y) v) => v.x);
		double maxX = verticesArray.Max(((double x, double y) v) => v.x);
		double minY = verticesArray.Min(((double x, double y) v) => v.y);
		double maxY = verticesArray.Max(((double x, double y) v) => v.y);
		svgFigure.SetLimits(minX, maxX, minY, maxY);
		svgFigure.XScale = XScale;
		svgFigure.YScale = YScale;
		svgFigure.XReverse = XReverse;
		svgFigure.YReverse = YReverse;
		svgFigure.ShowGrid = ShowGrid;
		svgFigure.DrawAxes(xLabel, yLabel, "", title);
		svgFigure.BeginClip();
		svgFigure.PlotMesh(verticesArray, elementsArray, meshFillColor, meshEdgeColor, meshEdgeWidth);
		svgFigure.PlotVelocityArrows(verticesArray, elementsArray, vx, vy, maxArrowLengthPx, gridSpacingFactor, userScale, minArrowLengthPx, magnitudeCap, color, thickness, arrowheadProportion, arrowheadMaxPx, palette, paletteMin, paletteMax);
		svgFigure.EndClip();
		if (hasPaletteAndPaletteLengthIsNonZeroAndHasColorBarLabel)
		{
			double computed = 0.0;
			for (int intervalIndex = 0; intervalIndex < vx.Length; intervalIndex++)
			{
				double vxVyTerm = Math.Sqrt(vx[intervalIndex] * vx[intervalIndex] + vy[intervalIndex] * vy[intervalIndex]);
				if (vxVyTerm > computed)
				{
					computed = vxVyTerm;
				}
			}
			double magnitudeCapTerm = ((magnitudeCap.HasValue && magnitudeCap.Value > 0.0) ? magnitudeCap.Value : computed);
			double min = (double.IsNaN(paletteMin) ? 0.0 : paletteMin);
			double max = (double.IsNaN(paletteMax) ? magnitudeCapTerm : paletteMax);
			svgFigure.DrawColorBar(min, max, palette, colorBarLabel);
		}
		_elements.Add(new SvgElement
		{
			Markup = svgFigure.Render()
		});
	}

	public void BeginClip()
	{
	}

	public void EndClip()
	{
	}

	public string Render()
	{
		FinalizeCurrentChart();
		StringBuilder stringBuilder = new StringBuilder(8192);
		bool ofTypeElementsTerm = _elements.OfType<ChartElement>().Any((ChartElement c) => c.HasAnnotations);
		bool anyErrorBars = _elements.OfType<ChartElement>().Any((ChartElement c) => c.HasErrorBars);
		stringBuilder.AppendLine("<!DOCTYPE html>");
		stringBuilder.AppendLine("<html lang='en'>");
		stringBuilder.AppendLine("<head>");
		stringBuilder.AppendLine("<meta charset='utf-8'>");
		stringBuilder.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1'>");
		stringBuilder.Append("<title>").Append(EscHtml((_pageTitle.Length > 0) ? _pageTitle : "Chart")).AppendLine("</title>");
		stringBuilder.Append("<script>").Append(ChartJsSource.Value).AppendLine("</script>");
		if (ofTypeElementsTerm)
		{
			string annotationPluginSourceTerm = AnnotationPluginSource.Value;
			if (annotationPluginSourceTerm != null)
			{
				stringBuilder.Append("<script>").Append(annotationPluginSourceTerm).AppendLine("</script>");
			}
		}
		RenderStyles(stringBuilder);
		stringBuilder.AppendLine("</head>");
		stringBuilder.AppendLine("<body>");
		if (_useGrid)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(100, 2, stringBuilder2);
			handler.AppendLiteral("<div class='chart-grid' style='grid-template-columns:repeat(");
			handler.AppendFormatted(_gridCols);
			handler.AppendLiteral(",1fr);grid-template-rows:repeat(");
			handler.AppendFormatted(_gridRows);
			handler.AppendLiteral(",1fr);'>");
			stringBuilder3.AppendLine(ref handler);
		}
		int ordinal = 0;
		foreach (IHtmlElement element in _elements)
		{
			if (!(element is ChartElement chart))
			{
				if (!(element is TableElement table))
				{
					if (!(element is SvgElement svgElement))
					{
						if (element is TextElement textElement)
						{
							StringBuilder stringBuilder4 = stringBuilder;
							StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(50, 3, stringBuilder4);
							handler.AppendLiteral("<");
							handler.AppendFormatted(textElement.Tag);
							handler.AppendLiteral(" style='font-weight:");
							handler.AppendFormatted(textElement.FontWeight);
							handler.AppendLiteral(";font-size:");
							handler.AppendFormatted(textElement.FontSize);
							handler.AppendLiteral("px;margin:8px 0;'>");
							StringBuilder stringBuilder2 = stringBuilder4.Append(ref handler).Append(EscHtml(textElement.Content));
							StringBuilder stringBuilder5 = stringBuilder2;
							StringBuilder.AppendInterpolatedStringHandler handler2 = new StringBuilder.AppendInterpolatedStringHandler(3, 1, stringBuilder2);
							handler2.AppendLiteral("</");
							handler2.AppendFormatted(textElement.Tag);
							handler2.AppendLiteral(">");
							stringBuilder5.AppendLine(ref handler2);
						}
					}
					else
					{
						stringBuilder.Append("<div class='svg-container'>").Append(svgElement.Markup).AppendLine("</div>");
					}
				}
				else
				{
					RenderTable(stringBuilder, table);
				}
			}
			else
			{
				RenderChart(stringBuilder, chart, ordinal++);
			}
		}
		if (_useGrid)
		{
			stringBuilder.AppendLine("</div>");
		}
		stringBuilder.AppendLine("<script>");
		if (_elements.OfType<ChartElement>().Any((ChartElement c) => c.ShowGrid))
		{
			RenderDashedGridPlugin(stringBuilder);
		}
		RenderZoomPanScript(stringBuilder);
		ordinal = 0;
		foreach (IHtmlElement element2 in _elements)
		{
			if (element2 is ChartElement chart2)
			{
				RenderChartScript(stringBuilder, chart2, ordinal++, anyErrorBars);
			}
		}
		stringBuilder.AppendLine("</script>");
		stringBuilder.AppendLine("</body>");
		stringBuilder.AppendLine("</html>");
		return stringBuilder.ToString();
	}

	public void Save(string filePath)
	{
		filePath = SafePath(filePath);
		string directoryName = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		File.WriteAllText(filePath, Render());
		Console.WriteLine("HTML saved: " + filePath);
	}

	private static string SafePath(string filePath)
	{
		string directoryName = Path.GetDirectoryName(filePath);
		string? fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
		string extension = Path.GetExtension(filePath);
		StringBuilder stringBuilder = new StringBuilder(fileNameWithoutExtension.Length);
		string fileNameWithoutExtension2 = fileNameWithoutExtension;
		foreach (char c in fileNameWithoutExtension2)
		{
			stringBuilder.Append((char.IsLetterOrDigit(c) || c == '-' || c == '_') ? c : '_');
		}
		string stringBuilderExtensionTerm = stringBuilder.ToString() + extension;
		if (!string.IsNullOrEmpty(directoryName))
		{
			return Path.Combine(directoryName, stringBuilderExtensionTerm);
		}
		return stringBuilderExtensionTerm;
	}

	public void SaveAsPng(string sourcePath, int deviceScaleFactor = 8)
	{
		if (!File.Exists(sourcePath))
		{
			throw new FileNotFoundException("SaveAsPng: source HTML not found. Call Save() first. Path: " + sourcePath, sourcePath);
		}
		string changeExtension = Path.ChangeExtension(sourcePath, ".png");
		string directoryName = Path.GetDirectoryName(changeExtension);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		FigureRenderer.TryRenderToPng(sourcePath, changeExtension, _width, _height, deviceScaleFactor);
	}

	public void SaveAsPdf(string sourcePath)
	{
		if (!File.Exists(sourcePath))
		{
			throw new FileNotFoundException("SaveAsPdf: source HTML not found. Call Save() first. Path: " + sourcePath, sourcePath);
		}
		string changeExtension = Path.ChangeExtension(sourcePath, ".pdf");
		string directoryName = Path.GetDirectoryName(changeExtension);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		FigureRenderer.TryRenderToPdf(sourcePath, changeExtension, _width, _height);
	}

	private void RenderStyles(StringBuilder sb)
	{
		sb.AppendLine("<style>");
		sb.AppendLine("*{box-sizing:border-box;margin:0;padding:0;}");
		sb.AppendLine("body{font-family:Arial,Helvetica,sans-serif;background:#fff;padding:20px;}");
		sb.AppendLine(".chart-grid{display:grid;gap:16px;}");
		sb.AppendLine(".chart-container{position:relative;margin:12px 0;}");
		sb.AppendLine(".table-wrapper{max-height:500px;overflow-y:auto;margin:12px 0;border:1px solid #ddd;}");
		sb.AppendLine("table.html-table{border-collapse:collapse;width:100%;}");
		sb.AppendLine(".html-table th{position:sticky;top:0;background:#f5f5f5;z-index:1;font-weight:600;}");
		sb.AppendLine(".html-table th,.html-table td{border:1px solid #ddd;padding:6px 10px;text-align:left;font-size:13px;}");
		sb.AppendLine(".html-table tr:nth-child(even){background:#fafafa;}");
		sb.AppendLine(".svg-container{margin:12px 0;}");
		sb.AppendLine(".colorbar{display:flex;align-items:center;margin:8px 0;}");
		sb.AppendLine(".colorbar-gradient{border:1px solid #ccc;}");
		sb.AppendLine(".colorbar-labels{display:flex;flex-direction:column;justify-content:space-between;margin-left:4px;font-size:10px;}");
		sb.AppendLine("</style>");
	}

	private void RenderChart(StringBuilder sb, ChartElement chart, int idx)
	{
		string message = $"chart_{idx}";
		string textValue = "";
		if (chart.GridRow > 0)
		{
			textValue = $"grid-row:{chart.GridRow};grid-column:{chart.GridCol};";
		}
		else if (chart.ExplicitW > 0.0)
		{
			textValue = $"position:absolute;left:{chart.ExplicitX}px;top:{chart.ExplicitY}px;width:{chart.ExplicitW}px;height:{chart.ExplicitH}px;";
		}
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(38, 1, stringBuilder);
		handler.AppendLiteral("<div class='chart-container' style='");
		handler.AppendFormatted(textValue);
		handler.AppendLiteral("'>");
		stringBuilder2.Append(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(23, 1, stringBuilder);
		handler.AppendLiteral("<canvas id='");
		handler.AppendFormatted(message);
		handler.AppendLiteral("'></canvas>");
		stringBuilder3.Append(ref handler);
		if (chart.ColorBar != null)
		{
			RenderColorBar(sb, chart.ColorBar);
		}
		sb.AppendLine("</div>");
	}

	private void RenderColorBar(StringBuilder sb, ColorBarConfig cb)
	{
		string paletteCbTerm = string.Join(",", cb.Palette.Select((string c, int i) => $"{c} {((cb.Palette.Length > 1) ? ((double)i * 100.0 / (double)(cb.Palette.Length - 1)) : 0.0):F0}%"));
		sb.Append("<div class='colorbar'>");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(104, 2, stringBuilder);
		handler.AppendLiteral("<div class='colorbar-gradient' style='width:");
		handler.AppendFormatted(cb.Width);
		handler.AppendLiteral("px;height:150px;background:linear-gradient(to top,");
		handler.AppendFormatted(paletteCbTerm);
		handler.AppendLiteral(");'></div>");
		stringBuilder2.Append(ref handler);
		sb.Append("<div class='colorbar-labels'>");
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
		handler.AppendLiteral("<span>");
		handler.AppendFormatted(Fmt(cb.Max));
		handler.AppendLiteral("</span>");
		stringBuilder3.Append(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
		handler.AppendLiteral("<span>");
		handler.AppendFormatted(Fmt((cb.Min + cb.Max) / 2.0));
		handler.AppendLiteral("</span>");
		stringBuilder4.Append(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
		handler.AppendLiteral("<span>");
		handler.AppendFormatted(Fmt(cb.Min));
		handler.AppendLiteral("</span>");
		stringBuilder5.Append(ref handler);
		sb.AppendLine("</div></div>");
	}

	private void RenderTable(StringBuilder sb, TableElement table)
	{
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(50, 1, stringBuilder);
		handler.AppendLiteral("<div class='table-wrapper' style='max-height:");
		handler.AppendFormatted(table.MaxHeight);
		handler.AppendLiteral("px;'>");
		stringBuilder2.Append(ref handler);
		sb.AppendLine("<table class='html-table'>");
		sb.Append("<thead><tr>");
		string[] headers = table.Headers;
		foreach (string s in headers)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder);
			handler.AppendLiteral("<th>");
			handler.AppendFormatted(EscHtml(s));
			handler.AppendLiteral("</th>");
			stringBuilder3.Append(ref handler);
		}
		sb.AppendLine("</tr></thead>");
		sb.AppendLine("<tbody>");
		string[][] rows = table.Rows;
		foreach (string[] row in rows)
		{
			sb.Append("<tr>");
			headers = row;
			foreach (string s2 in headers)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder4 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder);
				handler.AppendLiteral("<td>");
				handler.AppendFormatted(EscHtml(s2));
				handler.AppendLiteral("</td>");
				stringBuilder4.Append(ref handler);
			}
			sb.AppendLine("</tr>");
		}
		sb.AppendLine("</tbody></table></div>");
	}

	private void RenderChartScript(StringBuilder sb, ChartElement chart, int idx, bool anyErrorBars)
	{
		string message = $"chart_{idx}";
		string determineChartType = DetermineChartType(chart);
		sb.AppendLine("(function(){");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(38, 1, stringBuilder);
		handler.AppendLiteral("const ctx=document.getElementById('");
		handler.AppendFormatted(message);
		handler.AppendLiteral("');");
		stringBuilder2.AppendLine(ref handler);
		if (chart.HasErrorBars)
		{
			RenderErrorBarPlugin(sb);
		}
		sb.AppendLine("new Chart(ctx,{");
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
		handler.AppendLiteral("type:'");
		handler.AppendFormatted(determineChartType);
		handler.AppendLiteral("',");
		stringBuilder3.Append(ref handler);
		RenderChartData(sb, chart);
		sb.Append(",");
		RenderChartOptions(sb, chart);
		sb.AppendLine("});");
		sb.AppendLine("})();");
	}

	private string DetermineChartType(ChartElement chart)
	{
		if (chart.IsPieChart)
		{
			if (!(chart.PieCutout > 0.0))
			{
				return "pie";
			}
			return "doughnut";
		}
		if (chart.IsBarChart)
		{
			return "bar";
		}
		return "scatter";
	}

	private void RenderChartData(StringBuilder sb, ChartElement chart)
	{
		sb.Append("data:{");
		if (chart.IsBarChart && chart.BarLabels != null)
		{
			sb.Append("labels:");
			sb.Append(JsonSerializer.Serialize(chart.BarLabels));
			sb.Append(",");
		}
		else if (chart.IsPieChart && chart.PieLabels != null)
		{
			sb.Append("labels:");
			sb.Append(JsonSerializer.Serialize(chart.PieLabels));
			sb.Append(",");
		}
		sb.Append("datasets:[");
		for (int i = 0; i < chart.Datasets.Count; i++)
		{
			if (i > 0)
			{
				sb.Append(",");
			}
			RenderDataset(sb, chart.Datasets[i], chart);
		}
		sb.Append("]}");
	}

	private void RenderDataset(StringBuilder sb, DatasetConfig ds, ChartElement chart)
	{
		sb.Append("{");
		if (!chart.IsBarChart && !chart.IsPieChart && ds.ChartType != null)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
			handler.AppendLiteral("type:'");
			handler.AppendFormatted(ds.ChartType);
			handler.AppendLiteral("',");
			stringBuilder2.Append(ref handler);
		}
		if (ds.Label != null)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
			handler.AppendLiteral("label:");
			handler.AppendFormatted(JsonSerializer.Serialize(ds.Label));
			handler.AppendLiteral(",");
			stringBuilder3.Append(ref handler);
		}
		sb.Append("data:");
		sb.Append(ds.Data);
		if (ds.BackgroundColors != null)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder4 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder);
			handler.AppendLiteral(",backgroundColor:");
			handler.AppendFormatted(JsonSerializer.Serialize(ds.BackgroundColors));
			stringBuilder4.Append(ref handler);
		}
		else if (ds.BackgroundColor != null)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder5 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder);
			handler.AppendLiteral(",backgroundColor:'");
			handler.AppendFormatted(ds.BackgroundColor);
			handler.AppendLiteral("'");
			stringBuilder5.Append(ref handler);
		}
		if (ds.BorderColor != null)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder6 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder);
			handler.AppendLiteral(",borderColor:'");
			handler.AppendFormatted(ds.BorderColor);
			handler.AppendLiteral("'");
			stringBuilder6.Append(ref handler);
		}
		if (ds.BorderWidth > 0.0)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder7 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
			handler.AppendLiteral(",borderWidth:");
			handler.AppendFormatted(Fmt(ds.BorderWidth));
			stringBuilder7.Append(ref handler);
		}
		if (ds.BorderDash != null && ds.BorderDash.Length != 0)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder8 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder);
			handler.AppendLiteral(",borderDash:[");
			handler.AppendFormatted(string.Join(",", ds.BorderDash.Select(Fmt)));
			handler.AppendLiteral("]");
			stringBuilder8.Append(ref handler);
		}
		if (!chart.IsBarChart && !chart.IsPieChart)
		{
			if (ds.ShowLine)
			{
				sb.Append(",showLine:true");
			}
			else
			{
				sb.Append(",showLine:false");
			}
			sb.Append(",tension:0");
		}
		if (ds.PointStyle != null && ds.PointStyle != "circle")
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder9 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder);
			handler.AppendLiteral(",pointStyle:'");
			handler.AppendFormatted(ds.PointStyle);
			handler.AppendLiteral("'");
			stringBuilder9.Append(ref handler);
		}
		if (ds.PointRadius >= 0.0)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder10 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
			handler.AppendLiteral(",pointRadius:");
			handler.AppendFormatted(Fmt(ds.PointRadius));
			stringBuilder10.Append(ref handler);
		}
		if (ds.PointHoverRadius > 0.0)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder11 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder);
			handler.AppendLiteral(",pointHoverRadius:");
			handler.AppendFormatted(Fmt(ds.PointHoverRadius));
			stringBuilder11.Append(ref handler);
		}
		if (ds.PointBorderColor != null && ds.PointBorderColor != ds.BorderColor)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder12 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder);
			handler.AppendLiteral(",pointBorderColor:'");
			handler.AppendFormatted(ds.PointBorderColor);
			handler.AppendLiteral("'");
			stringBuilder12.Append(ref handler);
		}
		if (ds.PointBackgroundColor != null && ds.PointBackgroundColor != ds.BorderColor)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder13 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder);
			handler.AppendLiteral(",pointBackgroundColor:'");
			handler.AppendFormatted(ds.PointBackgroundColor);
			handler.AppendLiteral("'");
			stringBuilder13.Append(ref handler);
		}
		if (ds.Fill)
		{
			if (ds.FillTarget != null)
			{
				StringBuilder stringBuilder = sb;
				StringBuilder stringBuilder14 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
				handler.AppendLiteral(",fill:'");
				handler.AppendFormatted(ds.FillTarget);
				handler.AppendLiteral("'");
				stringBuilder14.Append(ref handler);
			}
			else
			{
				sb.Append(",fill:true");
			}
		}
		else if (!chart.IsPieChart)
		{
			sb.Append(",fill:false");
		}
		if (ds.YAxisID != null && ds.YAxisID != "y")
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder15 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder);
			handler.AppendLiteral(",yAxisID:'");
			handler.AppendFormatted(ds.YAxisID);
			handler.AppendLiteral("'");
			stringBuilder15.Append(ref handler);
		}
		if (ds.BarPercentage.HasValue)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder16 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder);
			handler.AppendLiteral(",barPercentage:");
			handler.AppendFormatted(Fmt(ds.BarPercentage.Value));
			stringBuilder16.Append(ref handler);
		}
		if (ds.CategoryPercentage.HasValue)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder17 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder);
			handler.AppendLiteral(",categoryPercentage:");
			handler.AppendFormatted(Fmt(ds.CategoryPercentage.Value));
			stringBuilder17.Append(ref handler);
		}
		if (ds.IsErrorBar)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder18 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder);
			handler.AppendLiteral(",errorLow:");
			handler.AppendFormatted(JsonSerializer.Serialize(ds.ErrorLow));
			stringBuilder18.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder19 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder);
			handler.AppendLiteral(",errorHigh:");
			handler.AppendFormatted(JsonSerializer.Serialize(ds.ErrorHigh));
			stringBuilder19.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder20 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder);
			handler.AppendLiteral(",errorCapWidth:");
			handler.AppendFormatted(Fmt(ds.ErrorCapWidth));
			stringBuilder20.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder21 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder);
			handler.AppendLiteral(",errorThickness:");
			handler.AppendFormatted(Fmt(ds.ErrorThickness));
			stringBuilder21.Append(ref handler);
		}
		sb.Append("}");
	}

	private void RenderChartOptions(StringBuilder sb, ChartElement chart)
	{
		sb.Append("options:{");
		sb.Append("responsive:true,maintainAspectRatio:true,");
		if (chart.IndexAxis != null)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
			handler.AppendLiteral("indexAxis:'");
			handler.AppendFormatted(chart.IndexAxis);
			handler.AppendLiteral("',");
			stringBuilder2.Append(ref handler);
		}
		sb.Append("interaction:{mode:'nearest',intersect:false},");
		if (chart.PadLeft > 0.0 || chart.PadRight > 0.0 || chart.PadTop > 0.0 || chart.PadBottom > 0.0)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(45, 4, stringBuilder);
			handler.AppendLiteral("layout:{padding:{left:");
			handler.AppendFormatted(Fmt(chart.PadLeft));
			handler.AppendLiteral(",right:");
			handler.AppendFormatted(Fmt(chart.PadRight));
			handler.AppendLiteral(",top:");
			handler.AppendFormatted(Fmt(chart.PadTop));
			handler.AppendLiteral(",bottom:");
			handler.AppendFormatted(Fmt(chart.PadBottom));
			handler.AppendLiteral("}},");
			stringBuilder3.Append(ref handler);
		}
		sb.Append("plugins:{");
		if (chart.IsTimeAxis)
		{
			string timeJsFormat = chart.TimeJsFormat;
			sb.Append("tooltip:{enabled:true,callbacks:{label:function(ctx){");
			sb.Append("const d=new Date(ctx.parsed.x*1000);");
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder4 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
			handler.AppendLiteral("const fmt='");
			handler.AppendFormatted(timeJsFormat);
			handler.AppendLiteral("';");
			stringBuilder4.Append(ref handler);
			sb.Append(RenderJsDateFormatter());
			sb.Append("return formatDate(d,fmt)+': '+ctx.parsed.y.toFixed(2);");
			sb.Append("}}}");
		}
		else
		{
			sb.Append("tooltip:{enabled:true}");
		}
		sb.Append(",legend:{");
		if (chart.ShowLegend && chart.Datasets.Any((DatasetConfig d) => d.Label != null))
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder5 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(65, 1, stringBuilder);
			handler.AppendLiteral("display:true,position:'");
			handler.AppendFormatted(chart.LegendPosition ?? "top");
			handler.AppendLiteral("',labels:{color:'#000',usePointStyle:true}");
			stringBuilder5.Append(ref handler);
		}
		else
		{
			sb.Append("display:false");
		}
		sb.Append("}");
		if (!string.IsNullOrEmpty(chart.Title))
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder6 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(69, 1, stringBuilder);
			handler.AppendLiteral(",title:{display:true,text:");
			handler.AppendFormatted(JsonSerializer.Serialize(chart.Title));
			handler.AppendLiteral(",color:'#000',font:{size:14,weight:'bold'}}");
			stringBuilder6.Append(ref handler);
		}
		if (chart.HasAnnotations && chart.Annotations.Count > 0)
		{
			sb.Append(",annotation:{annotations:{");
			for (int intervalIndex = 0; intervalIndex < chart.Annotations.Count; intervalIndex++)
			{
				if (intervalIndex > 0)
				{
					sb.Append(",");
				}
				RenderAnnotation(sb, chart.Annotations[intervalIndex], intervalIndex);
			}
			sb.Append("}}");
		}
		sb.Append("}");
		if (!chart.IsPieChart)
		{
			sb.Append(",scales:{");
			if (chart.IsTimeAxis)
			{
				RenderTimeScale(sb, chart);
			}
			else
			{
				RenderScale(sb, "x", chart.XScaleType, chart.MinX, chart.MaxX, chart.XTitle, chart.ShowGrid, chart.IsBarChart, chart.CustomTicksX, chart.IsStacked, null, chart.XReverse);
			}
			sb.Append(",");
			RenderScale(sb, "y", chart.YScaleType, chart.MinY, chart.MaxY, chart.YTitle, chart.ShowGrid, isCategory: false, chart.CustomTicksY, chart.IsStacked, null, chart.YReverse);
			if (chart.HasY2)
			{
				sb.Append(",");
				RenderScale(sb, "y2", chart.Y2ScaleType, chart.MinY2, chart.MaxY2, chart.Y2Title, showGrid: false, isCategory: false, null, stacked: false, "right", chart.Y2Reverse);
			}
			sb.Append("}");
		}
		if (chart.IsPieChart && chart.PieCutout > 0.0)
		{
			StringBuilder stringBuilder = sb;
			StringBuilder stringBuilder7 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder);
			handler.AppendLiteral(",cutout:'");
			handler.AppendFormatted(chart.PieCutout * 100.0, "F0");
			handler.AppendLiteral("%'");
			stringBuilder7.Append(ref handler);
		}
		sb.Append("}");
	}

	private void RenderScale(StringBuilder sb, string id, ScaleType scaleType, double? min, double? max, string title, bool showGrid, bool isCategory, (double[] positions, string[] labels)? customTicks, bool stacked = false, string position = null, bool reverse = false)
	{
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, stringBuilder);
		handler.AppendFormatted(id);
		handler.AppendLiteral(":{");
		stringBuilder2.Append(ref handler);
		string textValue;
		if (isCategory && id == "x")
		{
			textValue = "category";
		}
		else
		{
			textValue = ((scaleType == ScaleType.Log10) ? "logarithmic" : "linear");
		}
		if (!isCategory || id != "x")
		{
			stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
			handler.AppendLiteral("type:'");
			handler.AppendFormatted(textValue);
			handler.AppendLiteral("',");
			stringBuilder3.Append(ref handler);
		}
		if (position != null)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder4 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder);
			handler.AppendLiteral("position:'");
			handler.AppendFormatted(position);
			handler.AppendLiteral("',");
			stringBuilder4.Append(ref handler);
		}
		if (reverse)
		{
			sb.Append("reverse:true,");
		}
		if (min.HasValue)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder5 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder);
			handler.AppendLiteral("min:");
			handler.AppendFormatted(FmtData(min.Value));
			handler.AppendLiteral(",");
			stringBuilder5.Append(ref handler);
		}
		if (max.HasValue)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder6 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder);
			handler.AppendLiteral("max:");
			handler.AppendFormatted(FmtData(max.Value));
			handler.AppendLiteral(",");
			stringBuilder6.Append(ref handler);
		}
		if (!string.IsNullOrEmpty(title))
		{
			stringBuilder = sb;
			StringBuilder stringBuilder7 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(40, 1, stringBuilder);
			handler.AppendLiteral("title:{display:true,text:");
			handler.AppendFormatted(JsonSerializer.Serialize(title));
			handler.AppendLiteral(",color:'#000'},");
			stringBuilder7.Append(ref handler);
		}
		sb.Append("border:{display:true,color:'#000'},");
		sb.Append("ticks:{color:'#000'");
		if (customTicks.HasValue)
		{
			var (positions, labels) = customTicks.Value;
			sb.Append(",callback:function(value,index,ticks){");
			sb.Append("const map={");
			for (int i = 0; i < positions.Length; i++)
			{
				if (i > 0)
				{
					sb.Append(",");
				}
				stringBuilder = sb;
				StringBuilder stringBuilder8 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(1, 2, stringBuilder);
				handler.AppendFormatted(FmtData(positions[i]));
				handler.AppendLiteral(":");
				handler.AppendFormatted(JsonSerializer.Serialize(labels[i]));
				stringBuilder8.Append(ref handler);
			}
			sb.Append("};return map[value]||value;}");
		}
		sb.Append("},");
		stringBuilder = sb;
		StringBuilder stringBuilder9 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder);
		handler.AppendLiteral("grid:{display:");
		handler.AppendFormatted(showGrid ? "true" : "false");
		stringBuilder9.Append(ref handler);
		if (showGrid)
		{
			sb.Append(",color:'transparent',drawOnChartArea:false");
		}
		if (position == "right")
		{
			sb.Append(",drawOnChartArea:false");
		}
		sb.Append("}");
		if (stacked)
		{
			sb.Append(",stacked:true");
		}
		sb.Append("}");
	}

	private void RenderTimeScale(StringBuilder sb, ChartElement chart)
	{
		sb.Append("x:{type:'linear',");
		if (chart.XReverse)
		{
			sb.Append("reverse:true,");
		}
		StringBuilder stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler;
		if (chart.MinX.HasValue)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder2 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder);
			handler.AppendLiteral("min:");
			handler.AppendFormatted(FmtData(chart.MinX.Value));
			handler.AppendLiteral(",");
			stringBuilder2.Append(ref handler);
		}
		if (chart.MaxX.HasValue)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder);
			handler.AppendLiteral("max:");
			handler.AppendFormatted(FmtData(chart.MaxX.Value));
			handler.AppendLiteral(",");
			stringBuilder3.Append(ref handler);
		}
		if (!string.IsNullOrEmpty(chart.XTitle))
		{
			stringBuilder = sb;
			StringBuilder stringBuilder4 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(40, 1, stringBuilder);
			handler.AppendLiteral("title:{display:true,text:");
			handler.AppendFormatted(JsonSerializer.Serialize(chart.XTitle));
			handler.AppendLiteral(",color:'#000'},");
			stringBuilder4.Append(ref handler);
		}
		sb.Append("border:{display:true,color:'#000'},");
		sb.Append("afterBuildTicks:function(scale){scale.ticks=[");
		for (int i = 0; i < chart.TimeTickValues.Length; i++)
		{
			if (i > 0)
			{
				sb.Append(",");
			}
			stringBuilder = sb;
			StringBuilder stringBuilder5 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
			handler.AppendLiteral("{value:");
			handler.AppendFormatted(FmtData(chart.TimeTickValues[i]));
			handler.AppendLiteral("}");
			stringBuilder5.Append(ref handler);
		}
		sb.Append("];},");
		string timeJsFormat = chart.TimeJsFormat;
		sb.Append("ticks:{color:'#000',maxRotation:45,minRotation:25,autoSkip:false,");
		sb.Append("callback:function(value){");
		sb.Append("const d=new Date(value*1000);");
		stringBuilder = sb;
		StringBuilder stringBuilder6 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
		handler.AppendLiteral("const fmt='");
		handler.AppendFormatted(timeJsFormat);
		handler.AppendLiteral("';");
		stringBuilder6.Append(ref handler);
		sb.Append(RenderJsDateFormatter());
		sb.Append("return formatDate(d,fmt);");
		sb.Append("}},");
		stringBuilder = sb;
		StringBuilder stringBuilder7 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder);
		handler.AppendLiteral("grid:{display:");
		handler.AppendFormatted(chart.ShowGrid ? "true" : "false");
		stringBuilder7.Append(ref handler);
		if (chart.ShowGrid)
		{
			sb.Append(",color:'transparent',drawOnChartArea:false");
		}
		sb.Append("}}");
	}

	private static string RenderJsDateFormatter()
	{
		return "\r\nfunction formatDate(d,f){\r\nconst months=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];\r\nconst pad=(n)=>n<10?'0'+n:''+n;\r\nreturn f.replace('YYYY',d.getFullYear())\r\n.replace('MMM',months[d.getMonth()])\r\n.replace('MM',pad(d.getMonth()+1))\r\n.replace('DD',pad(d.getDate()))\r\n.replace('HH',pad(d.getHours()))\r\n.replace('mm',pad(d.getMinutes()))\r\n.replace('ss',pad(d.getSeconds()));\r\n}";
	}

	private void RenderAnnotation(StringBuilder sb, AnnotationConfig ann, int idx)
	{
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(3, 1, stringBuilder);
		handler.AppendLiteral("a");
		handler.AppendFormatted(idx);
		handler.AppendLiteral(":{");
		stringBuilder2.Append(ref handler);
		if (ann.Type == "line")
		{
			sb.Append("type:'line',");
			stringBuilder = sb;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder);
			handler.AppendLiteral("scaleID:'");
			handler.AppendFormatted(ann.ScaleID);
			handler.AppendLiteral("',");
			stringBuilder3.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder4 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
			handler.AppendLiteral("value:");
			handler.AppendFormatted(FmtData(ann.Value));
			handler.AppendLiteral(",");
			stringBuilder4.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder5 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder);
			handler.AppendLiteral("borderColor:'");
			handler.AppendFormatted(ann.BorderColor);
			handler.AppendLiteral("',");
			stringBuilder5.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder6 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder);
			handler.AppendLiteral("borderWidth:");
			handler.AppendFormatted(Fmt(ann.BorderWidth));
			stringBuilder6.Append(ref handler);
			if (ann.BorderDash != null && ann.BorderDash.Length != 0)
			{
				stringBuilder = sb;
				StringBuilder stringBuilder7 = stringBuilder;
				handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder);
				handler.AppendLiteral(",borderDash:[");
				handler.AppendFormatted(string.Join(",", ann.BorderDash.Select(Fmt)));
				handler.AppendLiteral("]");
				stringBuilder7.Append(ref handler);
			}
		}
		else if (ann.Type == "label")
		{
			sb.Append("type:'label',");
			stringBuilder = sb;
			StringBuilder stringBuilder8 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
			handler.AppendLiteral("xValue:");
			handler.AppendFormatted(FmtData(ann.XValue));
			handler.AppendLiteral(",");
			stringBuilder8.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder9 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
			handler.AppendLiteral("yValue:");
			handler.AppendFormatted(FmtData(ann.YValue));
			handler.AppendLiteral(",");
			stringBuilder9.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder10 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder);
			handler.AppendLiteral("content:");
			handler.AppendFormatted(JsonSerializer.Serialize(ann.Content));
			handler.AppendLiteral(",");
			stringBuilder10.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder11 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder);
			handler.AppendLiteral("color:'");
			handler.AppendFormatted(ann.FontColor);
			handler.AppendLiteral("',");
			stringBuilder11.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder12 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder);
			handler.AppendLiteral("font:{size:");
			handler.AppendFormatted(Fmt(ann.FontSize));
			handler.AppendLiteral("}");
			stringBuilder12.Append(ref handler);
		}
		else if (ann.Type == "arrow")
		{
			sb.Append("type:'line',");
			stringBuilder = sb;
			StringBuilder stringBuilder13 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 2, stringBuilder);
			handler.AppendLiteral("xMin:");
			handler.AppendFormatted(FmtData(ann.XMin));
			handler.AppendLiteral(",yMin:");
			handler.AppendFormatted(FmtData(ann.YMin));
			handler.AppendLiteral(",");
			stringBuilder13.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder14 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 2, stringBuilder);
			handler.AppendLiteral("xMax:");
			handler.AppendFormatted(FmtData(ann.XMax));
			handler.AppendLiteral(",yMax:");
			handler.AppendFormatted(FmtData(ann.YMax));
			handler.AppendLiteral(",");
			stringBuilder14.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder15 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder);
			handler.AppendLiteral("borderColor:'");
			handler.AppendFormatted(ann.BorderColor);
			handler.AppendLiteral("',");
			stringBuilder15.Append(ref handler);
			stringBuilder = sb;
			StringBuilder stringBuilder16 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
			handler.AppendLiteral("borderWidth:");
			handler.AppendFormatted(Fmt(ann.BorderWidth));
			handler.AppendLiteral(",");
			stringBuilder16.Append(ref handler);
			sb.Append("arrowHeads:{end:{display:true,length:10,width:6}}");
		}
		sb.Append("}");
	}

	private void RenderErrorBarPlugin(StringBuilder sb)
	{
		sb.AppendLine("const errorBarPlugin={\r\nid:'errorBars',\r\nafterDatasetsDraw(chart){\r\nconst ctx=chart.ctx;\r\nchart.data.datasets.forEach((ds,i)=>{\r\nif(!ds.errorLow)return;\r\nconst meta=chart.getDatasetMeta(i);\r\nconst capW=ds.errorCapWidth||4;\r\nctx.save();\r\nctx.strokeStyle=ds.borderColor||'#000';\r\nctx.lineWidth=ds.errorThickness||1.5;\r\nmeta.data.forEach((pt,j)=>{\r\nif(!pt||j>=ds.errorLow.length)return;\r\nconst x=pt.x,yc=pt.y;\r\nconst yScale=chart.scales[ds.yAxisID||'y'];\r\nconst yLow=yScale.getPixelForValue(ds.data[j].y-ds.errorLow[j]);\r\nconst yHigh=yScale.getPixelForValue(ds.data[j].y+ds.errorHigh[j]);\r\nctx.beginPath();ctx.moveTo(x,yLow);ctx.lineTo(x,yHigh);ctx.stroke();\r\nctx.beginPath();ctx.moveTo(x-capW,yLow);ctx.lineTo(x+capW,yLow);ctx.stroke();\r\nctx.beginPath();ctx.moveTo(x-capW,yHigh);ctx.lineTo(x+capW,yHigh);ctx.stroke();\r\n});\r\nctx.restore();\r\n});\r\n}};\r\nChart.register(errorBarPlugin);");
	}

	private static void RenderDashedGridPlugin(StringBuilder sb)
	{
		sb.AppendLine("const dashedGridPlugin={\r\nid:'dashedGrid',\r\nbeforeDraw(chart){\r\nconst ctx=chart.ctx;\r\nconst ca=chart.chartArea;\r\nif(!ca)return;\r\nctx.save();\r\nctx.strokeStyle='#ddd';\r\nctx.lineWidth=1;\r\nctx.setLineDash([4,3]);\r\nObject.values(chart.scales).forEach(scale=>{\r\nif(!scale.options.grid||!scale.options.grid.display)return;\r\nscale.ticks.forEach((_,i)=>{\r\nconst pos=scale.getPixelForTick(i);\r\nctx.beginPath();\r\nif(scale.isHorizontal()){\r\nif(pos<ca.left||pos>ca.right)return;\r\nctx.moveTo(pos,ca.top);ctx.lineTo(pos,ca.bottom);\r\n}else{\r\nif(pos<ca.top||pos>ca.bottom)return;\r\nctx.moveTo(ca.left,pos);ctx.lineTo(ca.right,pos);\r\n}\r\nctx.stroke();\r\n});\r\n});\r\nctx.restore();\r\n},\r\nafterDraw(chart){\r\nconst ctx=chart.ctx;\r\nconst ca=chart.chartArea;\r\nif(!ca)return;\r\nctx.save();\r\nctx.strokeStyle='#000';\r\nctx.lineWidth=2;\r\nctx.setLineDash([]);\r\nconst l=Math.round(ca.left),r=Math.round(ca.right);\r\nconst t=Math.round(ca.top),b=Math.round(ca.bottom);\r\nctx.beginPath();\r\nctx.moveTo(l,t);ctx.lineTo(r,t);\r\nctx.lineTo(r,b);ctx.lineTo(l,b);\r\nctx.closePath();ctx.stroke();\r\nctx.restore();\r\n}};\r\nChart.register(dashedGridPlugin);");
	}

	private static void RenderZoomPanScript(StringBuilder sb)
	{
		sb.AppendLine("\r\n(function(){\r\nconst zoomFactor=0.1;\r\nfunction attachZoomPan(chart){\r\nconst canvas=chart.canvas;\r\nlet dragging=false,lastX=0,lastY=0;\r\nconst origLimits={};\r\nObject.keys(chart.scales).forEach(id=>{\r\nconst s=chart.scales[id];\r\norigLimits[id]={min:s.min,max:s.max};\r\n});\r\ncanvas.addEventListener('wheel',function(e){\r\ne.preventDefault();\r\nconst rect=canvas.getBoundingClientRect();\r\nconst ca=chart.chartArea;\r\nconst mx=e.clientX-rect.left,my=e.clientY-rect.top;\r\nif(mx<ca.left||mx>ca.right||my<ca.top||my>ca.bottom)return;\r\nconst dir=e.deltaY<0?1:-1;\r\nObject.keys(chart.scales).forEach(id=>{\r\nconst s=chart.scales[id];\r\nconst range=s.max-s.min;\r\nconst d=range*zoomFactor*dir;\r\nlet frac;\r\nif(s.isHorizontal()){\r\nfrac=(mx-ca.left)/(ca.right-ca.left);\r\n}else{\r\nfrac=(ca.bottom-my)/(ca.bottom-ca.top);\r\n}\r\ns.options.min=s.min+d*frac;\r\ns.options.max=s.max-d*(1-frac);\r\n});\r\nchart.update('none');\r\n},{passive:false});\r\ncanvas.addEventListener('mousedown',function(e){\r\nconst rect=canvas.getBoundingClientRect();\r\nconst ca=chart.chartArea;\r\nconst mx=e.clientX-rect.left,my=e.clientY-rect.top;\r\nif(mx<ca.left||mx>ca.right||my<ca.top||my>ca.bottom)return;\r\ndragging=true;lastX=e.clientX;lastY=e.clientY;\r\ncanvas.style.cursor='grabbing';\r\n});\r\nwindow.addEventListener('mousemove',function(e){\r\nif(!dragging)return;\r\nconst dx=e.clientX-lastX,dy=e.clientY-lastY;\r\nlastX=e.clientX;lastY=e.clientY;\r\nconst ca=chart.chartArea;\r\nObject.keys(chart.scales).forEach(id=>{\r\nconst s=chart.scales[id];\r\nconst range=s.max-s.min;\r\nlet shift;\r\nif(s.isHorizontal()){\r\nshift=-dx/(ca.right-ca.left)*range;\r\n}else{\r\nshift=dy/(ca.bottom-ca.top)*range;\r\n}\r\ns.options.min=s.min+shift;\r\ns.options.max=s.max+shift;\r\n});\r\nchart.update('none');\r\n});\r\nwindow.addEventListener('mouseup',function(){\r\nif(dragging){dragging=false;canvas.style.cursor='';}\r\n});\r\ncanvas.addEventListener('dblclick',function(){\r\nObject.keys(origLimits).forEach(id=>{\r\nconst s=chart.scales[id];\r\nif(s){s.options.min=origLimits[id].min;s.options.max=origLimits[id].max;}\r\n});\r\nchart.update('none');\r\n});\r\n}\r\nconst orig=Chart.register;\r\nconst pending=[];\r\nconst hookPlugin={id:'zoomPanHook',afterInit(chart){attachZoomPan(chart);}};\r\nChart.register(hookPlugin);\r\n})();");
	}

	private static string BuildXYData(double[] x, double[] y)
	{
		StringBuilder stringBuilder = new StringBuilder(x.Length * 20);
		stringBuilder.Append("[");
		int count = Math.Min(x.Length, y.Length);
		for (int i = 0; i < count; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(",");
			}
			if (double.IsNaN(x[i]) || double.IsNaN(y[i]))
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder3 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
				handler.AppendLiteral("{x:");
				handler.AppendFormatted(double.IsNaN(x[i]) ? FmtData(i) : FmtData(x[i]));
				handler.AppendLiteral(",y:null}");
				stringBuilder3.Append(ref handler);
			}
			else
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder4 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 2, stringBuilder2);
				handler.AppendLiteral("{x:");
				handler.AppendFormatted(FmtData(x[i]));
				handler.AppendLiteral(",y:");
				handler.AppendFormatted(FmtData(y[i]));
				handler.AppendLiteral("}");
				stringBuilder4.Append(ref handler);
			}
		}
		stringBuilder.Append("]");
		return stringBuilder.ToString();
	}

	private static string BuildValueData(double[] y)
	{
		StringBuilder stringBuilder = new StringBuilder(y.Length * 10);
		stringBuilder.Append("[");
		for (int i = 0; i < y.Length; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(",");
			}
			if (double.IsNaN(y[i]))
			{
				stringBuilder.Append("null");
			}
			else
			{
				stringBuilder.Append(FmtData(y[i]));
			}
		}
		stringBuilder.Append("]");
		return stringBuilder.ToString();
	}

	private void EnsureChart()
	{
		if (_current == null)
		{
			_current = new ChartElement();
			if (_useGrid)
			{
				int gridRow = (_gridIndex - 1) / _gridCols + 1;
				int gridCol = (_gridIndex - 1) % _gridCols + 1;
				_current.GridRow = gridRow;
				_current.GridCol = gridCol;
			}
		}
	}

	private void FinalizeCurrentChart()
	{
		if (_current != null)
		{
			_elements.Add(_current);
			_current = null;
		}
	}

	private static string ResolveColor(string c)
	{
		if (string.IsNullOrEmpty(c))
		{
			return "#000000";
		}
		switch (c.ToLowerInvariant())
		{
		case "r":
		case "red":
			return "#FF0000";
		case "g":
		case "green":
			return "#00B050";
		case "b":
		case "blue":
			return "#0000FF";
		case "c":
		case "cyan":
			return "#00BFFF";
		case "m":
		case "magenta":
			return "#FF00FF";
		case "y":
		case "yellow":
			return "#FFD700";
		case "k":
		case "black":
			return "#000000";
		case "w":
		case "white":
			return "#FFFFFF";
		case "orange":
			return "#FF8C00";
		case "purple":
			return "#800080";
		case "brown":
			return "#8B4513";
		case "pink":
			return "#FF69B4";
		case "gray":
		case "grey":
			return "#808080";
		case "lightblue":
			return "#ADD8E6";
		case "lightgreen":
			return "#90EE90";
		case "darkred":
			return "#8B0000";
		case "darkblue":
			return "#00008B";
		case "darkgreen":
			return "#006400";
		case "navy":
			return "#000080";
		case "teal":
			return "#008080";
		case "olive":
			return "#808000";
		case "maroon":
			return "#800000";
		case "gold":
			return "#FFD700";
		case "silver":
			return "#C0C0C0";
		case "lime":
			return "#00FF00";
		case "aqua":
			return "#00FFFF";
		case "coral":
			return "#FF7F50";
		case "salmon":
			return "#FA8072";
		case "indigo":
			return "#4B0082";
		case "violet":
			return "#EE82EE";
		case "tan":
			return "#D2B48C";
		case "crimson":
			return "#DC143C";
		default:
			return c;
		}
	}

	private static string MapMarker(string marker)
	{
		if (string.IsNullOrEmpty(marker))
		{
			return "circle";
		}
		return marker switch
		{
			"circle" => "circle", 
			"square" => "rect", 
			"triangle" => "triangle", 
			"diamond" => "rectRot", 
			"*" => "star", 
			"x" => "crossRot", 
			"+" => "cross", 
			_ => marker, 
		};
	}

	private static string MapLegendPosition(string position)
	{
		switch (position)
		{
		case "top-left":
		case "top-right":
		case "top-center":
			return "top";
		case "bottom-left":
		case "bottom-right":
		case "bottom-center":
			return "bottom";
		case "center-left":
		case "middle-left":
			return "left";
		case "center-right":
		case "middle-right":
			return "right";
		case "middle-center":
			return "top";
		default:
			return "top";
		}
	}

	private static double[] ParseStyle(string style)
	{
		if (string.IsNullOrEmpty(style))
		{
			return Array.Empty<double>();
		}
		string textValue;
		switch (style)
		{
		case "-":
		case "solid":
			textValue = "";
			break;
		case "--":
		case "dashed":
			textValue = "8,4";
			break;
		case ":":
		case "dotted":
			textValue = "2,3";
			break;
		case "-.":
		case "dash-dot":
			textValue = "8,4,2,4";
			break;
		default:
			textValue = style;
			break;
		}
		string textValue2 = textValue;
		if (string.IsNullOrEmpty(textValue2))
		{
			return Array.Empty<double>();
		}
		return (from s in textValue2.Split(',')
			select (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)) ? 0.0 : result).ToArray();
	}

	private static string ApplyOpacity(string color, double opacity)
	{
		if (opacity >= 1.0)
		{
			return color;
		}
		if (color.StartsWith("rgba"))
		{
			return color;
		}
		if (color.StartsWith("#") && color.Length == 7)
		{
			int substringColorTerm = Convert.ToInt32(color.Substring(1, 2), 16);
			int substringColorTerm2 = Convert.ToInt32(color.Substring(3, 2), 16);
			int substringColorTerm3 = Convert.ToInt32(color.Substring(5, 2), 16);
			return $"rgba({substringColorTerm},{substringColorTerm2},{substringColorTerm3},{opacity.ToString("F2", CultureInfo.InvariantCulture)})";
		}
		return color;
	}

	private static string Fmt(double v)
	{
		return v.ToString("G6", CultureInfo.InvariantCulture);
	}

	private static string FmtData(double v)
	{
		return v.ToString("G10", CultureInfo.InvariantCulture);
	}

	private static string EscHtml(string s)
	{
		return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
			.Replace("\"", "&quot;");
	}

	private static double[] D(float[] a)
	{
		return Array.ConvertAll(a, (Converter<float, double>)((float f) => f));
	}

	private static double[] D(int[] a)
	{
		return Array.ConvertAll(a, (Converter<int, double>)((int i) => i));
	}
}
