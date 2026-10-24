using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Utility.LightweightPlotting;

public class SvgFigure
{
	private readonly int _width;

	private readonly int _height;

	private readonly StringBuilder _svg = new StringBuilder();

	private readonly StringBuilder _bgSvg = new StringBuilder();

	private readonly StringBuilder _overlaySvg = new StringBuilder();

	private double _padL = 60.0;

	private double _padR = 60.0;

	private double _padT = 60.0;

	private double _padB = 60.0;

	private double _cX;

	private double _cY;

	private double _cW;

	private double _cH;

	private double? _minX;

	private double? _maxX;

	private double? _minY;

	private double? _maxY;

	private double? _minY2;

	private double? _maxY2;

	private int _clipId;

	private (double[] positions, string[] labels)? _customTicksX;

	private (double[] positions, string[] labels)? _customTicksY;

	private double _yTickLabelWidthPx;

	private bool _leftMarginReserved;

	private bool _zoomEnabled;

	private const string ZoomStyle = "<style>path,line,polyline,polygon,circle,rect{vector-effect:non-scaling-stroke;}#svgRoot{cursor:grab;touch-action:none;}#svgRoot.dragging{cursor:grabbing;}</style>";

	private const string ZoomScript = "<script><![CDATA[\r\n(function(){\r\n  var svg = document.getElementById('svgRoot');\r\n  if (!svg || !svg.viewBox || !svg.viewBox.baseVal) return;\r\n  var vb = svg.viewBox.baseVal;\r\n  var initialView = { x: vb.x, y: vb.y, width: vb.width, height: vb.height };\r\n\r\n  function resetView(){\r\n    vb.x = initialView.x;\r\n    vb.y = initialView.y;\r\n    vb.width = initialView.width;\r\n    vb.height = initialView.height;\r\n  }\r\n\r\n  svg.addEventListener('dblclick', function(evt){\r\n    evt.preventDefault();\r\n    resetView();\r\n  });\r\n  svg.addEventListener('keydown', function(evt){\r\n    if (evt.key !== 'Home') return;\r\n    evt.preventDefault();\r\n    resetView();\r\n  });\r\n\r\n  svg.addEventListener('wheel', function(evt){\r\n    evt.preventDefault();\r\n    var screenCtm = svg.getScreenCTM();\r\n    if (!screenCtm) return;\r\n    var pt = svg.createSVGPoint();\r\n    pt.x = evt.clientX; pt.y = evt.clientY;\r\n    var svgPt = pt.matrixTransform(screenCtm.inverse());\r\n    var scale = evt.deltaY < 0 ? 0.9 : 1.1;\r\n    var newWidth = vb.width * scale;\r\n    var newHeight = vb.height * scale;\r\n    var minWidth = svg.width.baseVal.value * 0.02;\r\n    var maxWidth = svg.width.baseVal.value * 40;\r\n    if (newWidth < minWidth || newWidth > maxWidth) return;\r\n    vb.x = svgPt.x - (svgPt.x - vb.x) * scale;\r\n    vb.y = svgPt.y - (svgPt.y - vb.y) * scale;\r\n    vb.width = newWidth;\r\n    vb.height = newHeight;\r\n  }, { passive: false });\r\n\r\n  var dragging = false, lastX = 0, lastY = 0;\r\n  svg.addEventListener('pointerdown', function(evt){\r\n    svg.focus();\r\n    dragging = true;\r\n    lastX = evt.clientX; lastY = evt.clientY;\r\n    svg.classList.add('dragging');\r\n    svg.setPointerCapture(evt.pointerId);\r\n  });\r\n  svg.addEventListener('pointermove', function(evt){\r\n    if (!dragging) return;\r\n    var screenCtm = svg.getScreenCTM();\r\n    if (!screenCtm) return;\r\n    // Screen-pixel delta -> SVG user-unit delta, via the CTM's uniform scale factor.\r\n    var userPerPx = vb.width / svg.getBoundingClientRect().width;\r\n    vb.x -= (evt.clientX - lastX) * userPerPx;\r\n    vb.y -= (evt.clientY - lastY) * userPerPx;\r\n    lastX = evt.clientX; lastY = evt.clientY;\r\n  });\r\n  function endDrag(evt){\r\n    if (!dragging) return;\r\n    dragging = false;\r\n    svg.classList.remove('dragging');\r\n    try { svg.releasePointerCapture(evt.pointerId); } catch (e) {}\r\n  }\r\n  svg.addEventListener('pointerup', endDrag);\r\n  svg.addEventListener('pointercancel', endDrag);\r\n})();\r\n]]></script>";

	private const double TickLabelGap = 5.0;

	private const double TitleAscent = 9.0;

	private const double TitleDescent = 3.0;

	private const double TitleGap = 3.0;

	private const double TitleCanvasPad = 4.0;

	public ScaleType XScale { get; set; }

	public ScaleType YScale { get; set; }

	public ScaleType Y2Scale { get; set; }

	public bool ShowGrid { get; set; } = true;

	public bool XReverse { get; set; }

	public bool YReverse { get; set; }

	public bool Y2Reverse { get; set; }

	public bool ShowTicks { get; set; } = true;

	public double TickLength { get; set; } = 6.0;

	public bool ShowMinorTicks { get; set; }

	public double MinorTickLength { get; set; } = 3.0;

	public bool ShowMinorGrid { get; set; }

	public string BackgroundColor { get; set; } = "white";

	public double PlotLeft => _cX + _padL;

	public double PlotRight => _cX + _cW - _padR;

	public double PlotTop => _cY + _padT;

	public double PlotBottom => _cY + _cH - _padB;

	public double PlotWidth => _cW - _padL - _padR;

	public double PlotHeight => _cH - _padT - _padB;

	public bool HasLimits
	{
		get
		{
			if (_minX.HasValue && _maxX.HasValue && _minY.HasValue)
			{
				return _maxY.HasValue;
			}
			return false;
		}
	}

	public int Width => _width;

	public int Height => _height;

	public SvgFigure(int width = 800, int height = 600)
	{
		_width = width;
		_height = height;
		SetPlotArea(0.0, 0.0, width, height);
	}

	public void Subplot(int rows, int cols, int index)
	{
		int indexOffsetOverCols = (index - 1) / cols;
		int indexColsTerm = (index - 1) % cols;
		_cW = (double)_width / (double)cols;
		_cH = (double)_height / (double)rows;
		_cX = (double)indexColsTerm * _cW;
		_cY = (double)indexOffsetOverCols * _cH;
		ResetLimits();
	}

	public void SetPlotArea(double x, double y, double width, double height)
	{
		_cX = x;
		_cY = y;
		_cW = width;
		_cH = height;
		ResetLimits();
	}

	public void SetMargins(double left, double right, double top, double bottom)
	{
		_padL = left;
		_padR = right;
		_padT = top;
		_padB = bottom;
		_leftMarginReserved = false;
	}

	public void SetLimits(double minX, double maxX, double minY, double maxY)
	{
		_minX = minX;
		_maxX = maxX;
		_minY = minY;
		_maxY = maxY;
		_leftMarginReserved = false;
	}

	public void SetLimits(double[] limits)
	{
		if (limits != null && limits.Length == 4)
		{
			_minX = limits[0];
		}
		_maxX = limits[1];
		_minY = limits[2];
		_maxY = limits[3];
		_leftMarginReserved = false;
	}

	public void SetLimitsY(double minY, double maxY)
	{
		_minY = minY;
		_maxY = maxY;
		_leftMarginReserved = false;
	}

	public void SetLimitsX(DateTime[] times)
	{
		double[] source = ((IEnumerable<DateTime>)times).Select((Func<DateTime, double>)((DateTime t) => new DateTimeOffset(t).ToUnixTimeSeconds())).ToArray();
		_minX = source.Min();
		_maxX = source.Max();
	}

	public void SetLimitsX(double minX, double maxX)
	{
		_minX = minX;
		_maxX = maxX;
	}

	public void SetLimitsY2(double minY, double maxY)
	{
		_minY2 = minY;
		_maxY2 = maxY;
	}

	public void SetAxesEqual()
	{
		if (!HasLimits)
		{
			return;
		}
		double plotWidth = PlotWidth;
		double plotHeight = PlotHeight;
		if (!(plotWidth <= 0.0) && !(plotHeight <= 0.0))
		{
			double maxXMinXTerm = _maxX.Value - _minX.Value;
			double maxYMinYTerm = _maxY.Value - _minY.Value;
			if (!(maxXMinXTerm <= 0.0) && !(maxYMinYTerm <= 0.0))
			{
				double plotWidthPlotHeightTerm = Math.Max(maxXMinXTerm / plotWidth, maxYMinYTerm / plotHeight);
				double plotWidthTerm = plotWidthPlotHeightTerm * plotWidth;
				double plotHeightTerm = plotWidthPlotHeightTerm * plotHeight;
				double minXMaxXTerm = (_minX.Value + _maxX.Value) / 2.0;
				double minYMaxYTerm = (_minY.Value + _maxY.Value) / 2.0;
				_minX = minXMaxXTerm - plotWidthTerm / 2.0;
				_maxX = minXMaxXTerm + plotWidthTerm / 2.0;
				_minY = minYMaxYTerm - plotHeightTerm / 2.0;
				_maxY = minYMaxYTerm + plotHeightTerm / 2.0;
				_leftMarginReserved = false;
			}
		}
	}

	public void SetTickLabelsX(double[] positions, string[] labels)
	{
		_customTicksX = (positions, labels);
	}

	public void SetTickLabelsY(double[] positions, string[] labels)
	{
		_customTicksY = (positions, labels);
		_leftMarginReserved = false;
	}

	private void ResetLimits()
	{
		_minX = (_maxX = (_minY = (_maxY = (_minY2 = (_maxY2 = null)))));
		_customTicksX = (_customTicksY = null);
		_leftMarginReserved = false;
	}

	public void PlotLine(double[] x, double[] y, string color = "blue", double thickness = 2.0, string style = "", bool useY2 = false, string marker = "", double markerSize = 3.0, double markerOpacity = 1.0, string tooltip = null)
	{
		color = ResolveColor(color);
		string dashArray = ResolveLineStyle(style);
		(double, double)[] scaledPoints = GetScaledPoints(x, y, useY2);
		bool notIsNullOrEmpty = !string.IsNullOrEmpty(tooltip);
		if (notIsNullOrEmpty)
		{
			_svg.Append("<g>");
			StringBuilder svg = _svg;
			StringBuilder stringBuilder = svg;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, svg);
			handler.AppendLiteral("<title>");
			handler.AppendFormatted(tooltip);
			handler.AppendLiteral("</title>");
			stringBuilder.Append(ref handler);
		}
		bool condition = false;
		for (int i = 0; i < scaledPoints.Length; i++)
		{
			if (double.IsNaN(scaledPoints[i].Item1) || double.IsNaN(scaledPoints[i].Item2))
			{
				if (condition)
				{
					ClosePoly(color, thickness, dashArray);
					condition = false;
				}
				continue;
			}
			if (!condition)
			{
				_svg.Append("<polyline points='");
				condition = true;
			}
			StringBuilder svg = _svg;
			StringBuilder stringBuilder2 = svg;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, svg);
			handler.AppendFormatted(Fmt(scaledPoints[i].Item1));
			handler.AppendLiteral(",");
			handler.AppendFormatted(Fmt(scaledPoints[i].Item2));
			handler.AppendLiteral(" ");
			stringBuilder2.Append(ref handler);
		}
		if (condition)
		{
			ClosePoly(color, thickness, dashArray);
		}
		if (!string.IsNullOrEmpty(marker))
		{
			(double, double)[] scaledPointsArray = scaledPoints;
			for (int j = 0; j < scaledPointsArray.Length; j++)
			{
				(double, double) tuple = scaledPointsArray[j];
				if (!double.IsNaN(tuple.Item1) && !double.IsNaN(tuple.Item2))
				{
					RenderMarker(tuple.Item1, tuple.Item2, markerSize, color, markerOpacity, marker);
				}
			}
		}
		if (notIsNullOrEmpty)
		{
			_svg.AppendLine("</g>");
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

	public void PlotScatter(double[] x, double[] y, string color = "red", double radius = 3.0, double opacity = 1.0, bool useY2 = false, string marker = "circle", string[] tooltips = null)
	{
		color = ResolveColor(color);
		(double, double)[] scaledPoints = GetScaledPoints(x, y, useY2);
		for (int i = 0; i < scaledPoints.Length; i++)
		{
			(double, double) tuple = scaledPoints[i];
			if (!double.IsNaN(tuple.Item1) && !double.IsNaN(tuple.Item2))
			{
				string tooltip = ((tooltips != null && i < tooltips.Length) ? tooltips[i] : null);
				if (string.IsNullOrEmpty(tooltip))
				{
					RenderMarker(tuple.Item1, tuple.Item2, radius, color, opacity, marker);
					continue;
				}
				_svg.Append("<g>");
				StringBuilder svg = _svg;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, svg);
				handler.AppendLiteral("<title>");
				handler.AppendFormatted(tooltip);
				handler.AppendLiteral("</title>");
				svg.Append(ref handler);
				RenderMarker(tuple.Item1, tuple.Item2, radius, color, opacity, marker);
				_svg.AppendLine("</g>");
			}
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

	public void PlotMesh(IEnumerable<(double x, double y)> vertices, IEnumerable<int[]> elements, string color = "rgba(0,0,255,0.1)", string edgeColor = "black", double edgeWidth = 0.2, bool labelVertices = false, bool labelEdges = false, bool labelElements = false, double labelFontSize = 9.0)
	{
		color = ResolveColor(color);
		edgeColor = ResolveColor(edgeColor);
		(double, double)[] verticesArray = (vertices as (double, double)[]) ?? vertices.ToArray();
		int verticeLength = verticesArray.Length;
		if (verticeLength == 0)
		{
			return;
		}
		ReserveLeftMargin();
		double min = _minX ?? verticesArray.Min(((double x, double y) tuple2) => tuple2.x);
		double max = _maxX ?? verticesArray.Max(((double x, double y) tuple2) => tuple2.x);
		double min2 = _minY ?? verticesArray.Min(((double x, double y) tuple2) => tuple2.y);
		double max2 = _maxY ?? verticesArray.Max(((double x, double y) tuple2) => tuple2.y);
		double plotWidth = PlotWidth;
		double plotHeight = PlotHeight;
		double plotLeft = PlotLeft;
		double plotTop = PlotTop;
		(double, double)[] valueTuples = new (double, double)[verticeLength];
		for (int intervalIndex = 0; intervalIndex < verticeLength; intervalIndex++)
		{
			double map = Map(verticesArray[intervalIndex].Item1, min, max, plotWidth, XScale, XReverse);
			double map2 = Map(verticesArray[intervalIndex].Item2, min2, max2, plotHeight, YScale, YReverse);
			valueTuples[intervalIndex] = (plotLeft + map, plotTop + plotHeight - map2);
		}
		int[][] elementsArray = (elements as int[][]) ?? elements.ToArray();
		_svg.Append("<path d='");
		int[][] elements2 = elementsArray;
		StringBuilder svg;
		StringBuilder.AppendInterpolatedStringHandler handler;
		foreach (int[] element in elements2)
		{
			if (element.Length < 3)
			{
				continue;
			}
			bool condition = true;
			for (int intervalIndex2 = 0; intervalIndex2 < element.Length; intervalIndex2++)
			{
				if (element[intervalIndex2] < 0 || element[intervalIndex2] >= verticeLength)
				{
					condition = false;
					break;
				}
			}
			if (condition)
			{
				(double, double) tuple = valueTuples[element[0]];
				svg = _svg;
				StringBuilder stringBuilder = svg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, svg);
				handler.AppendLiteral("M");
				handler.AppendFormatted(Fmt(tuple.Item1));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(tuple.Item2));
				stringBuilder.Append(ref handler);
				for (int unity = 1; unity < element.Length; unity++)
				{
					tuple = valueTuples[element[unity]];
					svg = _svg;
					StringBuilder stringBuilder2 = svg;
					handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, svg);
					handler.AppendLiteral("L");
					handler.AppendFormatted(Fmt(tuple.Item1));
					handler.AppendLiteral(",");
					handler.AppendFormatted(Fmt(tuple.Item2));
					stringBuilder2.Append(ref handler);
				}
				_svg.Append('Z');
			}
		}
		svg = _svg;
		StringBuilder stringBuilder3 = svg;
		handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, svg);
		handler.AppendLiteral("' fill='");
		handler.AppendFormatted(color);
		handler.AppendLiteral("'");
		stringBuilder3.Append(ref handler);
		if (edgeWidth > 0.0)
		{
			svg = _svg;
			StringBuilder stringBuilder4 = svg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(73, 2, svg);
			handler.AppendLiteral(" stroke='");
			handler.AppendFormatted(edgeColor);
			handler.AppendLiteral("' stroke-width='");
			handler.AppendFormatted(edgeWidth);
			handler.AppendLiteral("' stroke-linejoin='round' stroke-linecap='round'");
			stringBuilder4.Append(ref handler);
		}
		_svg.AppendLine(" />");
		if (labelElements)
		{
			for (int intervalIndex3 = 0; intervalIndex3 < elementsArray.Length; intervalIndex3++)
			{
				int[] elementArray = elementsArray[intervalIndex3];
				if (elementArray.Length < 3)
				{
					continue;
				}
				double computed = 0.0;
				double computed2 = 0.0;
				int unity2 = 0;
				for (int intervalIndex4 = 0; intervalIndex4 < elementArray.Length; intervalIndex4++)
				{
					if (elementArray[intervalIndex4] < 0 || elementArray[intervalIndex4] >= verticeLength)
					{
						unity2 = -1;
						break;
					}
					computed += valueTuples[elementArray[intervalIndex4]].Item1;
					computed2 += valueTuples[elementArray[intervalIndex4]].Item2;
					unity2++;
				}
				if (unity2 >= 3)
				{
					computed /= (double)unity2;
					computed2 /= (double)unity2;
					svg = _svg;
					StringBuilder stringBuilder5 = svg;
					handler = new StringBuilder.AppendInterpolatedStringHandler(69, 4, svg);
					handler.AppendLiteral("<text x='");
					handler.AppendFormatted(Fmt(computed));
					handler.AppendLiteral("' y='");
					handler.AppendFormatted(Fmt(computed2 + labelFontSize * 0.35));
					handler.AppendLiteral("' text-anchor='middle' font-size='");
					handler.AppendFormatted(labelFontSize);
					handler.AppendLiteral("' fill='#333'>");
					handler.AppendFormatted(intervalIndex3);
					handler.AppendLiteral("</text>");
					stringBuilder5.AppendLine(ref handler);
				}
			}
		}
		if (labelEdges)
		{
			HashSet<(int, int)> valueTuplesSet = new HashSet<(int, int)>();
			int ordinal = 0;
			foreach (int[] element2 in elementsArray)
			{
				if (element2.Length < 3)
				{
					continue;
				}
				bool condition2 = true;
				for (int intervalIndex5 = 0; intervalIndex5 < element2.Length; intervalIndex5++)
				{
					if (element2[intervalIndex5] < 0 || element2[intervalIndex5] >= verticeLength)
					{
						condition2 = false;
						break;
					}
				}
				if (!condition2)
				{
					continue;
				}
				for (int intervalIndex6 = 0; intervalIndex6 < element2.Length; intervalIndex6++)
				{
					int element3 = element2[intervalIndex6];
					int element4 = element2[(intervalIndex6 + 1) % element2.Length];
					(int, int) element3Element4Term = ((element3 < element4) ? (element3, element4) : (element4, element3));
					if (valueTuplesSet.Add(element3Element4Term))
					{
						double v = (valueTuples[element3].Item1 + valueTuples[element4].Item1) / 2.0;
						double element3Element4Term2 = (valueTuples[element3].Item2 + valueTuples[element4].Item2) / 2.0;
						svg = _svg;
						StringBuilder stringBuilder6 = svg;
						handler = new StringBuilder.AppendInterpolatedStringHandler(69, 4, svg);
						handler.AppendLiteral("<text x='");
						handler.AppendFormatted(Fmt(v));
						handler.AppendLiteral("' y='");
						handler.AppendFormatted(Fmt(element3Element4Term2 + labelFontSize * 0.35));
						handler.AppendLiteral("' text-anchor='middle' font-size='");
						handler.AppendFormatted(labelFontSize);
						handler.AppendLiteral("' fill='blue'>");
						handler.AppendFormatted(ordinal);
						handler.AppendLiteral("</text>");
						stringBuilder6.AppendLine(ref handler);
						ordinal++;
					}
				}
			}
		}
		if (!labelVertices)
		{
			return;
		}
		HashSet<int> intSet = new HashSet<int>();
		elements2 = elementsArray;
		foreach (int[] element5 in elements2)
		{
			if (element5 == null)
			{
				continue;
			}
			int[] element5Array = element5;
			foreach (int element52 in element5Array)
			{
				if (element52 >= 0 && element52 < verticeLength && intSet.Add(element52))
				{
					svg = _svg;
					StringBuilder stringBuilder7 = svg;
					handler = new StringBuilder.AppendInterpolatedStringHandler(47, 4, svg);
					handler.AppendLiteral("<text x='");
					handler.AppendFormatted(Fmt(valueTuples[element52].Item1 + 4.0));
					handler.AppendLiteral("' y='");
					handler.AppendFormatted(Fmt(valueTuples[element52].Item2 - 4.0));
					handler.AppendLiteral("' font-size='");
					handler.AppendFormatted(labelFontSize);
					handler.AppendLiteral("' fill='red'>");
					handler.AppendFormatted(element52);
					handler.AppendLiteral("</text>");
					stringBuilder7.AppendLine(ref handler);
				}
			}
		}
	}

	public void PlotMeshShaded(IEnumerable<(double x, double y)> vertices, IEnumerable<int[]> elements, double[] elementValues, string[] palette, double? minValue = null, double? maxValue = null, string edgeColor = "black", double edgeWidth = 0.0)
	{
		if (elementValues == null || palette == null || palette.Length == 0)
		{
			return;
		}
		edgeColor = ResolveColor(edgeColor);
		(double, double)[] verticesArray = (vertices as (double, double)[]) ?? vertices.ToArray();
		int verticeLength = verticesArray.Length;
		if (verticeLength == 0)
		{
			return;
		}
		int[][] elementsArray = (elements as int[][]) ?? elements.ToArray();
		if (elementsArray.Length == 0)
		{
			return;
		}
		int paletteLength = palette.Length;
		double minValue2 = minValue ?? double.PositiveInfinity;
		double maxValue2 = maxValue ?? double.NegativeInfinity;
		if (!minValue.HasValue || !maxValue.HasValue)
		{
			int count = Math.Min(elementsArray.Length, elementValues.Length);
			for (int i = 0; i < count; i++)
			{
				double elementValue = elementValues[i];
				if (!double.IsNaN(elementValue))
				{
					if (!minValue.HasValue && elementValue < minValue2)
					{
						minValue2 = elementValue;
					}
					if (!maxValue.HasValue && elementValue > maxValue2)
					{
						maxValue2 = elementValue;
					}
				}
			}
		}
		if (double.IsInfinity(minValue2) || double.IsInfinity(maxValue2))
		{
			return;
		}
		ReserveLeftMargin();
		bool maxValue2MinValue2Term = !(maxValue2 > minValue2);
		double paletteLengthOverMaxValue2AboveMinValue2 = (maxValue2MinValue2Term ? 0.0 : ((double)paletteLength / (maxValue2 - minValue2)));
		double min = _minX ?? verticesArray.Min(((double x, double y) v) => v.x);
		double max = _maxX ?? verticesArray.Max(((double x, double y) v) => v.x);
		double min2 = _minY ?? verticesArray.Min(((double x, double y) v) => v.y);
		double max2 = _maxY ?? verticesArray.Max(((double x, double y) v) => v.y);
		double plotWidth = PlotWidth;
		double plotHeight = PlotHeight;
		double plotLeft = PlotLeft;
		double plotTop = PlotTop;
		(double, double)[] valueTuples = new (double, double)[verticeLength];
		for (int intervalIndex = 0; intervalIndex < verticeLength; intervalIndex++)
		{
			double map = Map(verticesArray[intervalIndex].Item1, min, max, plotWidth, XScale, XReverse);
			double map2 = Map(verticesArray[intervalIndex].Item2, min2, max2, plotHeight, YScale, YReverse);
			valueTuples[intervalIndex] = (plotLeft + map, plotTop + plotHeight - map2);
		}
		StringBuilder[] stringBuilders = new StringBuilder[paletteLength];
		int count2 = Math.Min(elementsArray.Length, elementValues.Length);
		for (int intervalIndex2 = 0; intervalIndex2 < count2; intervalIndex2++)
		{
			int[] element = elementsArray[intervalIndex2];
			double elementValue2 = elementValues[intervalIndex2];
			if (double.IsNaN(elementValue2) || element.Length < 3)
			{
				continue;
			}
			bool condition = true;
			for (int intervalIndex3 = 0; intervalIndex3 < element.Length; intervalIndex3++)
			{
				if (element[intervalIndex3] < 0 || element[intervalIndex3] >= verticeLength)
				{
					condition = false;
					break;
				}
			}
			if (condition)
			{
				int paletteLengthOffset = ((!maxValue2MinValue2Term) ? ((int)((elementValue2 - minValue2) * paletteLengthOverMaxValue2AboveMinValue2)) : 0);
				if (paletteLengthOffset < 0)
				{
					paletteLengthOffset = 0;
				}
				else if (paletteLengthOffset >= paletteLength)
				{
					paletteLengthOffset = paletteLength - 1;
				}
				ref StringBuilder reference = ref stringBuilders[paletteLengthOffset];
				StringBuilder stringBuilder = reference ?? (reference = new StringBuilder());
				(double, double) tuple = valueTuples[element[0]];
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder3 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, stringBuilder2);
				handler.AppendLiteral("M");
				handler.AppendFormatted(Fmt(tuple.Item1));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(tuple.Item2));
				stringBuilder3.Append(ref handler);
				for (int unity = 1; unity < element.Length; unity++)
				{
					tuple = valueTuples[element[unity]];
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder4 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, stringBuilder2);
					handler.AppendLiteral("L");
					handler.AppendFormatted(Fmt(tuple.Item1));
					handler.AppendLiteral(",");
					handler.AppendFormatted(Fmt(tuple.Item2));
					stringBuilder4.Append(ref handler);
				}
				stringBuilder.Append('Z');
			}
		}
		for (int intervalIndex4 = 0; intervalIndex4 < paletteLength; intervalIndex4++)
		{
			StringBuilder stringBuilder5 = stringBuilders[intervalIndex4];
			if (stringBuilder5 != null && stringBuilder5.Length != 0)
			{
				StringBuilder stringBuilder2 = _svg;
				StringBuilder stringBuilder6 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(18, 2, stringBuilder2);
				handler.AppendLiteral("<path d='");
				handler.AppendFormatted(stringBuilder5);
				handler.AppendLiteral("' fill='");
				handler.AppendFormatted(ResolveColor(palette[intervalIndex4]));
				handler.AppendLiteral("'");
				stringBuilder6.Append(ref handler);
				if (edgeWidth > 0.0)
				{
					stringBuilder2 = _svg;
					StringBuilder stringBuilder7 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(73, 2, stringBuilder2);
					handler.AppendLiteral(" stroke='");
					handler.AppendFormatted(edgeColor);
					handler.AppendLiteral("' stroke-width='");
					handler.AppendFormatted(edgeWidth);
					handler.AppendLiteral("' stroke-linejoin='round' stroke-linecap='round'");
					stringBuilder7.Append(ref handler);
				}
				_svg.AppendLine(" />");
			}
		}
	}

	public void PlotVelocityArrows(IEnumerable<(double x, double y)> vertices, IEnumerable<int[]> elements, double[] vx, double[] vy, double maxArrowLengthPx = 30.0, double gridSpacingFactor = 1.5, double userScale = 1.0, double minArrowLengthPx = 2.0, double? magnitudeCap = null, string color = "black", double thickness = 1.0, double arrowheadProportion = 0.3, double arrowheadMaxPx = 8.0, string[] palette = null, double paletteMin = double.NaN, double paletteMax = double.NaN)
	{
		string resolveColor = ResolveColor(color);
		(double, double)[] verticesArray = (vertices as (double, double)[]) ?? vertices.ToArray();
		int[][] elementsArray = (elements as int[][]) ?? elements.ToArray();
		int verticeLength = verticesArray.Length;
		if (verticeLength == 0 || elementsArray.Length == 0 || vx == null || vy == null || vx.Length != elementsArray.Length || vy.Length != elementsArray.Length)
		{
			return;
		}
		ReserveLeftMargin();
		double minX = _minX ?? verticesArray.Min(((double x, double y) tuple) => tuple.x);
		double maxX = _maxX ?? verticesArray.Max(((double x, double y) tuple) => tuple.x);
		double minY = _minY ?? verticesArray.Min(((double x, double y) tuple) => tuple.y);
		double maxY = _maxY ?? verticesArray.Max(((double x, double y) tuple) => tuple.y);
		double pW = PlotWidth;
		double pH = PlotHeight;
		double plotLeft = PlotLeft;
		double plotTop = PlotTop;
		double dataW = maxX - minX;
		double dataH = maxY - minY;
		if (!(dataW > 0.0) || !(dataH > 0.0) || !(pW > 0.0) || !(pH > 0.0))
		{
			return;
		}
		double computed = 0.0;
		for (int intervalIndex = 0; intervalIndex < elementsArray.Length; intervalIndex++)
		{
			double vxVyTerm = Math.Sqrt(vx[intervalIndex] * vx[intervalIndex] + vy[intervalIndex] * vy[intervalIndex]);
			if (vxVyTerm > computed)
			{
				computed = vxVyTerm;
			}
		}
		if (!(computed > 0.0))
		{
			return;
		}
		double magnitudeCapTerm = ((magnitudeCap.HasValue && magnitudeCap.Value > 0.0) ? magnitudeCap.Value : computed);
		bool hasPaletteAndPaletteLengthIsNonZero = palette != null && palette.Length != 0;
		double paletteMin2 = (double.IsNaN(paletteMin) ? 0.0 : paletteMin);
		double paletteMax2 = (double.IsNaN(paletteMax) ? magnitudeCapTerm : paletteMax);
		if (!(paletteMax2 > paletteMin2))
		{
			paletteMax2 = paletteMin2 + 1.0;
		}
		int elementLength = elementsArray.Length;
		double[] doubleValues = new double[elementLength];
		double[] doubleValuesArray = new double[elementLength];
		double[] doubleValues2 = new double[elementLength];
		double[] doubleValues3 = new double[elementLength];
		double minValueAboveMaxValueSum = 0.0;
		double minValue2AboveMaxValue2Sum = 0.0;
		int ordinal = 0;
		for (int intervalIndex2 = 0; intervalIndex2 < elementLength; intervalIndex2++)
		{
			int[] element = elementsArray[intervalIndex2];
			if (element == null || element.Length < 3)
			{
				doubleValues[intervalIndex2] = double.NaN;
				continue;
			}
			double maxValue = double.MaxValue;
			double minValue = double.MinValue;
			double maxValue2 = double.MaxValue;
			double minValue2 = double.MinValue;
			bool condition = true;
			foreach (int element2 in element)
			{
				if (element2 < 0 || element2 >= verticeLength)
				{
					condition = false;
					break;
				}
				double verticesArrayElement2Term = verticesArray[element2].Item1;
				double verticesArrayElement2Term2 = verticesArray[element2].Item2;
				if (verticesArrayElement2Term < maxValue)
				{
					maxValue = verticesArrayElement2Term;
				}
				if (verticesArrayElement2Term > minValue)
				{
					minValue = verticesArrayElement2Term;
				}
				if (verticesArrayElement2Term2 < maxValue2)
				{
					maxValue2 = verticesArrayElement2Term2;
				}
				if (verticesArrayElement2Term2 > minValue2)
				{
					minValue2 = verticesArrayElement2Term2;
				}
			}
			if (!condition)
			{
				doubleValues[intervalIndex2] = double.NaN;
				continue;
			}
			doubleValues[intervalIndex2] = maxValue;
			doubleValuesArray[intervalIndex2] = minValue;
			doubleValues2[intervalIndex2] = maxValue2;
			doubleValues3[intervalIndex2] = minValue2;
			minValueAboveMaxValueSum += minValue - maxValue;
			minValue2AboveMaxValue2Sum += minValue2 - maxValue2;
			ordinal++;
		}
		if (ordinal == 0)
		{
			return;
		}
		double computed2 = minValueAboveMaxValueSum / (double)ordinal;
		double computed3 = minValue2AboveMaxValue2Sum / (double)ordinal;
		double dataWDataHTerm = Math.Max(computed2, computed3) * 2.0;
		if (!(dataWDataHTerm > 0.0))
		{
			dataWDataHTerm = Math.Max(dataW, dataH) / 32.0;
		}
		int dataWTerm = Math.Max(1, Math.Min(512, (int)Math.Ceiling(dataW / dataWDataHTerm)));
		int dataHTerm = Math.Max(1, Math.Min(512, (int)Math.Ceiling(dataH / dataWDataHTerm)));
		double dataWTerm2 = dataW / (double)dataWTerm;
		double dataHTerm2 = dataH / (double)dataHTerm;
		List<int>[] intListArray = new List<int>[dataWTerm * dataHTerm];
		for (int intervalIndex3 = 0; intervalIndex3 < elementLength; intervalIndex3++)
		{
			if (double.IsNaN(doubleValues[intervalIndex3]))
			{
				continue;
			}
			int minXTerm = Math.Max(0, Math.Min(dataWTerm - 1, (int)((doubleValues[intervalIndex3] - minX) / dataWTerm2)));
			int count = Math.Max(0, Math.Min(dataWTerm - 1, (int)((doubleValuesArray[intervalIndex3] - minX) / dataWTerm2)));
			int minYTerm = Math.Max(0, Math.Min(dataHTerm - 1, (int)((doubleValues2[intervalIndex3] - minY) / dataHTerm2)));
			int count2 = Math.Max(0, Math.Min(dataHTerm - 1, (int)((doubleValues3[intervalIndex3] - minY) / dataHTerm2)));
			for (int ordinal2 = minYTerm; ordinal2 <= count2; ordinal2++)
			{
				for (int ordinal3 = minXTerm; ordinal3 <= count; ordinal3++)
				{
					int ordinal4 = ordinal2 * dataWTerm + ordinal3;
					List<int>[] intListArrayList = intListArray;
					int ordinal5 = ordinal4;
					(intListArrayList[ordinal5] ?? (intListArrayList[ordinal5] = new List<int>())).Add(intervalIndex3);
				}
			}
		}
		double maxArrowLengthPxTimesGridSpacingFactor = Math.Max(4.0, maxArrowLengthPx * gridSpacingFactor);
		Dictionary<string, StringBuilder> stringBuilderByString = new Dictionary<string, StringBuilder>();
		Dictionary<string, StringBuilder> stringBuilderByString2 = new Dictionary<string, StringBuilder>();
		double xReversePWTerm = (double)((!XReverse) ? 1 : (-1)) * pW / dataW;
		double yReversePHTerm = (double)(YReverse ? 1 : (-1)) * pH / dataH;
		for (double maxArrowLengthPxTimesGridSpacingFactorSum = maxArrowLengthPxTimesGridSpacingFactor * 0.5; maxArrowLengthPxTimesGridSpacingFactorSum < pH; maxArrowLengthPxTimesGridSpacingFactorSum += maxArrowLengthPxTimesGridSpacingFactor)
		{
			for (double maxArrowLengthPxTimesGridSpacingFactorSum2 = maxArrowLengthPxTimesGridSpacingFactor * 0.5; maxArrowLengthPxTimesGridSpacingFactorSum2 < pW; maxArrowLengthPxTimesGridSpacingFactorSum2 += maxArrowLengthPxTimesGridSpacingFactor)
			{
				double pxToDataX2 = pxToDataX(maxArrowLengthPxTimesGridSpacingFactorSum2);
				double pxToDataY2 = pxToDataY(maxArrowLengthPxTimesGridSpacingFactorSum);
				int pxToDataX2MinXTerm = (int)((pxToDataX2 - minX) / dataWTerm2);
				int pxToDataY2MinYTerm = (int)((pxToDataY2 - minY) / dataHTerm2);
				if (pxToDataX2MinXTerm < 0 || pxToDataX2MinXTerm >= dataWTerm || pxToDataY2MinYTerm < 0 || pxToDataY2MinYTerm >= dataHTerm)
				{
					continue;
				}
				List<int> intListArray2 = intListArray[pxToDataY2MinYTerm * dataWTerm + pxToDataX2MinXTerm];
				if (intListArray2 == null)
				{
					continue;
				}
				int ordinal6 = -1;
				for (int intervalIndex4 = 0; intervalIndex4 < intListArray2.Count; intervalIndex4++)
				{
					int ordinal7 = intListArray2[intervalIndex4];
					if (!(pxToDataX2 < doubleValues[ordinal7]) && !(pxToDataX2 > doubleValuesArray[ordinal7]) && !(pxToDataY2 < doubleValues2[ordinal7]) && !(pxToDataY2 > doubleValues3[ordinal7]) && PointInPolygon(pxToDataX2, pxToDataY2, verticesArray, elementsArray[ordinal7]))
					{
						ordinal6 = ordinal7;
						break;
					}
				}
				if (ordinal6 < 0)
				{
					continue;
				}
				double vx2 = vx[ordinal6];
				double vy2 = vy[ordinal6];
				double vx2Vy2Term = Math.Sqrt(vx2 * vx2 + vy2 * vy2);
				if (!(vx2Vy2Term > 0.0))
				{
					continue;
				}
				double maxArrowLengthPxUserScaleTerm = ((vx2Vy2Term > magnitudeCapTerm) ? magnitudeCapTerm : vx2Vy2Term) / magnitudeCapTerm * maxArrowLengthPx * userScale;
				if (maxArrowLengthPxUserScaleTerm < minArrowLengthPx)
				{
					continue;
				}
				double vx2Term = vx2 * xReversePWTerm;
				double vy2Term = vy2 * yReversePHTerm;
				double computed4 = Math.Sqrt(vx2Term * vx2Term + vy2Term * vy2Term);
				if (!(computed4 > 0.0))
				{
					continue;
				}
				double computed5 = vx2Term / computed4;
				double computed6 = vy2Term / computed4;
				double plotLeftTerm = plotLeft + maxArrowLengthPxTimesGridSpacingFactorSum2;
				double plotTopTerm = plotTop + maxArrowLengthPxTimesGridSpacingFactorSum;
				double computed7 = maxArrowLengthPxUserScaleTerm * 0.5;
				double v = plotLeftTerm - computed5 * computed7;
				double v2 = plotTopTerm - computed6 * computed7;
				double computed8 = plotLeftTerm + computed5 * computed7;
				double computed9 = plotTopTerm + computed6 * computed7;
				double arrowheadMaxPxArrowheadProportionTerm = Math.Min(arrowheadMaxPx, arrowheadProportion * maxArrowLengthPxUserScaleTerm);
				bool condition2 = arrowheadMaxPxArrowheadProportionTerm > 0.0;
				double computed10 = computed8 - computed5 * arrowheadMaxPxArrowheadProportionTerm;
				double computed11 = computed9 - computed6 * arrowheadMaxPxArrowheadProportionTerm;
				double v3 = (condition2 ? computed10 : computed8);
				double v4 = (condition2 ? computed11 : computed9);
				string key = resolveColor;
				if (hasPaletteAndPaletteLengthIsNonZero)
				{
					double submergenceRatio = (vx2Vy2Term - paletteMin2) / (paletteMax2 - paletteMin2);
					if (submergenceRatio < 0.0)
					{
						submergenceRatio = 0.0;
					}
					if (submergenceRatio > 1.0)
					{
						submergenceRatio = 1.0;
					}
					int lengthOffset = (int)(submergenceRatio * (double)palette.Length);
					if (lengthOffset >= palette.Length)
					{
						lengthOffset = palette.Length - 1;
					}
					key = ResolveColor(palette[lengthOffset]);
				}
				if (!stringBuilderByString.TryGetValue(key, out var stringBuilderTerm))
				{
					stringBuilderTerm = (stringBuilderByString[key] = new StringBuilder());
				}
				StringBuilder stringBuilder2 = stringBuilderTerm;
				StringBuilder stringBuilder3 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(4, 4, stringBuilder2);
				handler.AppendLiteral("M");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(v2));
				handler.AppendLiteral("L");
				handler.AppendFormatted(Fmt(v3));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(v4));
				stringBuilder3.Append(ref handler);
				if (condition2)
				{
					double computed12 = 0.0 - computed6;
					double computed13 = computed5;
					double computed14 = arrowheadMaxPxArrowheadProportionTerm * 0.5;
					double v5 = computed10 + computed12 * computed14;
					double v6 = computed11 + computed13 * computed14;
					double v7 = computed10 - computed12 * computed14;
					double v8 = computed11 - computed13 * computed14;
					if (!stringBuilderByString2.TryGetValue(key, out var stringBuilder22))
					{
						stringBuilder22 = (stringBuilderByString2[key] = new StringBuilder());
					}
					stringBuilder2 = stringBuilder22;
					StringBuilder stringBuilder5 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(7, 6, stringBuilder2);
					handler.AppendLiteral("M");
					handler.AppendFormatted(Fmt(computed8));
					handler.AppendLiteral(",");
					handler.AppendFormatted(Fmt(computed9));
					handler.AppendLiteral("L");
					handler.AppendFormatted(Fmt(v5));
					handler.AppendLiteral(",");
					handler.AppendFormatted(Fmt(v6));
					handler.AppendLiteral("L");
					handler.AppendFormatted(Fmt(v7));
					handler.AppendLiteral(",");
					handler.AppendFormatted(Fmt(v8));
					handler.AppendLiteral("Z");
					stringBuilder5.Append(ref handler);
				}
			}
		}
		foreach (KeyValuePair<string, StringBuilder> localValue in stringBuilderByString)
		{
			StringBuilder stringBuilder2 = _svg;
			StringBuilder stringBuilder6 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(74, 3, stringBuilder2);
			handler.AppendLiteral("<path d='");
			handler.AppendFormatted(localValue.Value);
			handler.AppendLiteral("' fill='none' stroke='");
			handler.AppendFormatted(localValue.Key);
			handler.AppendLiteral("' stroke-width='");
			handler.AppendFormatted(Fmt(thickness));
			handler.AppendLiteral("' stroke-linecap='round' />");
			stringBuilder6.AppendLine(ref handler);
		}
		foreach (KeyValuePair<string, StringBuilder> localValue2 in stringBuilderByString2)
		{
			StringBuilder stringBuilder2 = _svg;
			StringBuilder stringBuilder7 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(35, 2, stringBuilder2);
			handler.AppendLiteral("<path d='");
			handler.AppendFormatted(localValue2.Value);
			handler.AppendLiteral("' fill='");
			handler.AppendFormatted(localValue2.Key);
			handler.AppendLiteral("' stroke='none' />");
			stringBuilder7.AppendLine(ref handler);
		}
		double pxToDataX(double offFromLeft)
		{
			if (!XReverse)
			{
				return minX + offFromLeft / pW * dataW;
			}
			return minX + (pW - offFromLeft) / pW * dataW;
		}
		double pxToDataY(double offFromTop)
		{
			double pHAboveOffFromTop = pH - offFromTop;
			if (!YReverse)
			{
				return minY + pHAboveOffFromTop / pH * dataH;
			}
			return minY + (pH - pHAboveOffFromTop) / pH * dataH;
		}
	}

	private static bool PointInPolygon(double px, double py, (double x, double y)[] verts, int[] elem)
	{
		int ordinal = 0;
		int elemLength = elem.Length;
		for (int i = 0; i < elemLength; i++)
		{
			var (localValue, localValue2) = verts[elem[i]];
			var (localValue3, localValue4) = verts[elem[(i + 1) % elemLength]];
			if (localValue2 > py != localValue4 > py)
			{
				double pyTerm = (localValue3 - localValue) * (py - localValue2) / (localValue4 - localValue2) + localValue;
				if (px < pyTerm)
				{
					ordinal++;
				}
			}
		}
		return (ordinal & 1) == 1;
	}

	public void PlotImage(byte[] imageBytes, string mimeType, double xMin, double xMax, double yMin, double yMax, double opacity = 1.0)
	{
		if (imageBytes != null && imageBytes.Length != 0)
		{
			string href = "data:" + mimeType + ";base64," + Convert.ToBase64String(imageBytes);
			EmitImage(href, xMin, xMax, yMin, yMax, opacity);
		}
	}

	public void PlotImage(string href, double xMin, double xMax, double yMin, double yMax, double opacity = 1.0)
	{
		if (!string.IsNullOrEmpty(href))
		{
			EmitImage(EscXml(href), xMin, xMax, yMin, yMax, opacity);
		}
	}

	private void EmitImage(string href, double xMin, double xMax, double yMin, double yMax, double opacity)
	{
		ReserveLeftMargin();
		double min = _minX ?? xMin;
		double max = _maxX ?? xMax;
		double min2 = _minY ?? yMin;
		double max2 = _maxY ?? yMax;
		double plotLeftMapTerm = PlotLeft + Map(xMin, min, max, PlotWidth, XScale, XReverse);
		double plotLeftMapTerm2 = PlotLeft + Map(xMax, min, max, PlotWidth, XScale, XReverse);
		double plotTopPlotHeightTerm = PlotTop + PlotHeight - Map(yMin, min2, max2, PlotHeight, YScale, YReverse);
		double plotTopPlotHeightTerm2 = PlotTop + PlotHeight - Map(yMax, min2, max2, PlotHeight, YScale, YReverse);
		double v = Math.Min(plotLeftMapTerm, plotLeftMapTerm2);
		double v2 = Math.Min(plotTopPlotHeightTerm, plotTopPlotHeightTerm2);
		double computed = Math.Abs(plotLeftMapTerm2 - plotLeftMapTerm);
		double computed2 = Math.Abs(plotTopPlotHeightTerm2 - plotTopPlotHeightTerm);
		if (!(computed <= 0.0) && !(computed2 <= 0.0))
		{
			string message = ((opacity < 1.0) ? $" opacity='{opacity:F2}'" : "");
			StringBuilder svg = _svg;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(96, 6, svg);
			handler.AppendLiteral("<image x='");
			handler.AppendFormatted(Fmt(v));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' width='");
			handler.AppendFormatted(Fmt(computed));
			handler.AppendLiteral("' height='");
			handler.AppendFormatted(Fmt(computed2));
			handler.AppendLiteral("' preserveAspectRatio='none' image-rendering='auto'");
			handler.AppendFormatted(message);
			handler.AppendLiteral(" href='");
			handler.AppendFormatted(href);
			handler.AppendLiteral("' />");
			svg.AppendLine(ref handler);
		}
	}

	public void PlotBar(double[] x, double[] y, string color = "#2196F3", double barWidth = 0.8, string edgeColor = "#333", double edgeWidth = 0.5, double opacity = 1.0)
	{
		color = ResolveColor(color);
		edgeColor = ResolveColor(edgeColor);
		ReserveLeftMargin();
		double min = _minX ?? x.Min();
		double max = _maxX ?? x.Max();
		double minY = _minY ?? y.Min();
		double max2 = _maxY ?? y.Max();
		double plotWidth = PlotWidth;
		double plotHeight = PlotHeight;
		double minY2 = Math.Max(minY, 0.0);
		string message = ((opacity < 1.0) ? $" opacity='{opacity:F2}'" : "");
		string message2 = ((edgeWidth > 0.0) ? $" stroke='{edgeColor}' stroke-width='{edgeWidth}'" : "");
		for (int i = 0; i < x.Length && i < y.Length; i++)
		{
			double plotLeftMapTerm = PlotLeft + Map(x[i] - barWidth / 2.0, min, max, plotWidth, XScale, XReverse);
			double plotLeftMapTerm2 = PlotLeft + Map(x[i] + barWidth / 2.0, min, max, plotWidth, XScale, XReverse);
			double plotTopPlotHeightTerm = PlotTop + plotHeight - Map(y[i], minY, max2, plotHeight, YScale, YReverse);
			double plotTopPlotHeightTerm2 = PlotTop + plotHeight - Map(minY2, minY, max2, plotHeight, YScale, YReverse);
			double v = Math.Min(plotTopPlotHeightTerm, plotTopPlotHeightTerm2);
			double v2 = Math.Abs(plotTopPlotHeightTerm - plotTopPlotHeightTerm2);
			StringBuilder svg = _svg;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(45, 7, svg);
			handler.AppendLiteral("<rect x='");
			handler.AppendFormatted(Fmt(plotLeftMapTerm));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(v));
			handler.AppendLiteral("' width='");
			handler.AppendFormatted(Fmt(plotLeftMapTerm2 - plotLeftMapTerm));
			handler.AppendLiteral("' height='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' fill='");
			handler.AppendFormatted(color);
			handler.AppendLiteral("'");
			handler.AppendFormatted(message2);
			handler.AppendFormatted(message);
			handler.AppendLiteral(" />");
			svg.AppendLine(ref handler);
		}
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
		edgeColor = ResolveColor(edgeColor);
		for (int i = 0; i < colors.Length; i++)
		{
			colors[i] = ResolveColor(colors[i]);
		}
		int seryLength = series.Length;
		if (seryLength == 0)
		{
			return;
		}
		ReserveLeftMargin();
		double groupWidthOverSeryLength = groupWidth / (double)seryLength;
		double min = _minX ?? x.Min();
		double max = _maxX ?? x.Max();
		double minY = _minY ?? series.SelectMany((double[] s) => s).Min();
		double max2 = _maxY ?? series.SelectMany((double[] s) => s).Max();
		double plotWidth = PlotWidth;
		double plotHeight = PlotHeight;
		double minY2 = Math.Max(minY, 0.0);
		string message = ((edgeWidth > 0.0) ? $" stroke='{edgeColor}' stroke-width='{edgeWidth}'" : "");
		for (int intervalIndex = 0; intervalIndex < seryLength; intervalIndex++)
		{
			double groupWidthTerm = (0.0 - groupWidth) / 2.0 + groupWidthOverSeryLength * ((double)intervalIndex + 0.5);
			string color = colors[intervalIndex % colors.Length];
			for (int intervalIndex2 = 0; intervalIndex2 < x.Length && intervalIndex2 < series[intervalIndex].Length; intervalIndex2++)
			{
				double computed = x[intervalIndex2] + groupWidthTerm;
				double plotLeftMapTerm = PlotLeft + Map(computed - groupWidthOverSeryLength / 2.0, min, max, plotWidth, XScale, XReverse);
				double plotLeftMapTerm2 = PlotLeft + Map(computed + groupWidthOverSeryLength / 2.0, min, max, plotWidth, XScale, XReverse);
				double plotTopPlotHeightTerm = PlotTop + plotHeight - Map(series[intervalIndex][intervalIndex2], minY, max2, plotHeight, YScale, YReverse);
				double plotTopPlotHeightTerm2 = PlotTop + plotHeight - Map(minY2, minY, max2, plotHeight, YScale, YReverse);
				double v = Math.Min(plotTopPlotHeightTerm, plotTopPlotHeightTerm2);
				double v2 = Math.Abs(plotTopPlotHeightTerm - plotTopPlotHeightTerm2);
				StringBuilder svg = _svg;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(45, 6, svg);
				handler.AppendLiteral("<rect x='");
				handler.AppendFormatted(Fmt(plotLeftMapTerm));
				handler.AppendLiteral("' y='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' width='");
				handler.AppendFormatted(Fmt(plotLeftMapTerm2 - plotLeftMapTerm));
				handler.AppendLiteral("' height='");
				handler.AppendFormatted(Fmt(v2));
				handler.AppendLiteral("' fill='");
				handler.AppendFormatted(color);
				handler.AppendLiteral("'");
				handler.AppendFormatted(message);
				handler.AppendLiteral(" />");
				svg.AppendLine(ref handler);
			}
		}
	}

	public void PlotFill(double[] x, double[] y1, double[] y2, string color = "rgba(0,0,255,0.3)", string edgeColor = "", double edgeWidth = 0.0)
	{
		color = ResolveColor(color);
		edgeColor = ResolveColor(edgeColor);
		(double, double)[] scaledPoints = GetScaledPoints(x, y1, useY2: false);
		(double, double)[] scaledPoints2 = GetScaledPoints(x, y2, useY2: false);
		_svg.Append("<path d='");
		bool condition = true;
		StringBuilder svg;
		StringBuilder.AppendInterpolatedStringHandler handler;
		for (int i = 0; i < scaledPoints.Length; i++)
		{
			if (!double.IsNaN(scaledPoints[i].Item1) && !double.IsNaN(scaledPoints[i].Item2))
			{
				svg = _svg;
				StringBuilder stringBuilder = svg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(1, 3, svg);
				handler.AppendFormatted(condition ? "M" : "L");
				handler.AppendFormatted(Fmt(scaledPoints[i].Item1));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(scaledPoints[i].Item2));
				stringBuilder.Append(ref handler);
				condition = false;
			}
		}
		for (int lengthOffset = scaledPoints2.Length - 1; lengthOffset >= 0; lengthOffset--)
		{
			if (!double.IsNaN(scaledPoints2[lengthOffset].Item1) && !double.IsNaN(scaledPoints2[lengthOffset].Item2))
			{
				svg = _svg;
				StringBuilder stringBuilder2 = svg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, svg);
				handler.AppendLiteral("L");
				handler.AppendFormatted(Fmt(scaledPoints2[lengthOffset].Item1));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(scaledPoints2[lengthOffset].Item2));
				stringBuilder2.Append(ref handler);
			}
		}
		svg = _svg;
		StringBuilder stringBuilder3 = svg;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, svg);
		handler.AppendLiteral("Z' fill='");
		handler.AppendFormatted(color);
		handler.AppendLiteral("'");
		stringBuilder3.Append(ref handler);
		if (!string.IsNullOrEmpty(edgeColor) && edgeWidth > 0.0)
		{
			svg = _svg;
			StringBuilder stringBuilder4 = svg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(26, 2, svg);
			handler.AppendLiteral(" stroke='");
			handler.AppendFormatted(edgeColor);
			handler.AppendLiteral("' stroke-width='");
			handler.AppendFormatted(edgeWidth);
			handler.AppendLiteral("'");
			stringBuilder4.Append(ref handler);
		}
		_svg.AppendLine(" />");
	}

	public void PlotFill(double[] x, double[] y, string color = "rgba(0,0,255,0.3)", string edgeColor = "", double edgeWidth = 0.0)
	{
		double[] y2 = new double[y.Length];
		PlotFill(x, y, y2, color, edgeColor, edgeWidth);
	}

	public void HLine(double y, string color = "#999", double thickness = 1.0, string style = "--")
	{
		color = ResolveColor(color);
		ReserveLeftMargin();
		string resolveLineStyle = ResolveLineStyle(style);
		double valueOrDefault = _minY.GetValueOrDefault();
		double max = _maxY ?? 1.0;
		double v = PlotTop + PlotHeight - Map(y, valueOrDefault, max, PlotHeight, YScale, YReverse);
		StringBuilder svg = _svg;
		StringBuilder stringBuilder = svg;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(55, 6, svg);
		handler.AppendLiteral("<line x1='");
		handler.AppendFormatted(Fmt(PlotLeft));
		handler.AppendLiteral("' y1='");
		handler.AppendFormatted(Fmt(v));
		handler.AppendLiteral("' x2='");
		handler.AppendFormatted(Fmt(PlotRight));
		handler.AppendLiteral("' y2='");
		handler.AppendFormatted(Fmt(v));
		handler.AppendLiteral("' stroke='");
		handler.AppendFormatted(color);
		handler.AppendLiteral("' stroke-width='");
		handler.AppendFormatted(thickness);
		handler.AppendLiteral("'");
		stringBuilder.Append(ref handler);
		if (!string.IsNullOrEmpty(resolveLineStyle))
		{
			svg = _svg;
			StringBuilder stringBuilder2 = svg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, svg);
			handler.AppendLiteral(" stroke-dasharray='");
			handler.AppendFormatted(resolveLineStyle);
			handler.AppendLiteral("'");
			stringBuilder2.Append(ref handler);
		}
		_svg.AppendLine(" />");
	}

	public void VLine(double x, string color = "#999", double thickness = 1.0, string style = "--")
	{
		color = ResolveColor(color);
		ReserveLeftMargin();
		string resolveLineStyle = ResolveLineStyle(style);
		double v = PlotLeft + Map(x, _minX.GetValueOrDefault(), _maxX ?? 1.0, PlotWidth, XScale, XReverse);
		StringBuilder svg = _svg;
		StringBuilder stringBuilder = svg;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(55, 6, svg);
		handler.AppendLiteral("<line x1='");
		handler.AppendFormatted(Fmt(v));
		handler.AppendLiteral("' y1='");
		handler.AppendFormatted(Fmt(PlotTop));
		handler.AppendLiteral("' x2='");
		handler.AppendFormatted(Fmt(v));
		handler.AppendLiteral("' y2='");
		handler.AppendFormatted(Fmt(PlotBottom));
		handler.AppendLiteral("' stroke='");
		handler.AppendFormatted(color);
		handler.AppendLiteral("' stroke-width='");
		handler.AppendFormatted(thickness);
		handler.AppendLiteral("'");
		stringBuilder.Append(ref handler);
		if (!string.IsNullOrEmpty(resolveLineStyle))
		{
			svg = _svg;
			StringBuilder stringBuilder2 = svg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, svg);
			handler.AppendLiteral(" stroke-dasharray='");
			handler.AppendFormatted(resolveLineStyle);
			handler.AppendLiteral("'");
			stringBuilder2.Append(ref handler);
		}
		_svg.AppendLine(" />");
	}

	public void PlotErrorBar(double[] x, double[] y, double[] yErr, string color = "k", double capWidth = 4.0, double thickness = 1.5)
	{
		PlotErrorBar(x, y, yErr, yErr, color, capWidth, thickness);
	}

	public void PlotErrorBar(double[] x, double[] y, double[] yErrLow, double[] yErrHigh, string color = "k", double capWidth = 4.0, double thickness = 1.5)
	{
		color = ResolveColor(color);
		ReserveLeftMargin();
		double min = _minX ?? x.Min();
		double max = _maxX ?? x.Max();
		double min2 = _minY ?? y.Min();
		double max2 = _maxY ?? y.Max();
		double plotWidth = PlotWidth;
		double plotHeight = PlotHeight;
		string message = $"stroke='{color}' stroke-width='{thickness}'";
		double scaledCapWidth = capWidth / 2.0;
		for (int i = 0; i < x.Length; i++)
		{
			double plotLeftMapTerm = PlotLeft + Map(x[i], min, max, plotWidth, XScale, XReverse);
			_ = PlotTop;
			Map(y[i], min2, max2, plotHeight, YScale, YReverse);
			double v = PlotTop + plotHeight - Map(y[i] - yErrLow[i], min2, max2, plotHeight, YScale, YReverse);
			double v2 = PlotTop + plotHeight - Map(y[i] + yErrHigh[i], min2, max2, plotHeight, YScale, YReverse);
			StringBuilder svg = _svg;
			StringBuilder stringBuilder = svg;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(33, 5, svg);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(plotLeftMapTerm));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(v));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(plotLeftMapTerm));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' ");
			handler.AppendFormatted(message);
			handler.AppendLiteral(" />");
			stringBuilder.AppendLine(ref handler);
			svg = _svg;
			StringBuilder stringBuilder2 = svg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(33, 5, svg);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(plotLeftMapTerm - scaledCapWidth));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(v));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(plotLeftMapTerm + scaledCapWidth));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(v));
			handler.AppendLiteral("' ");
			handler.AppendFormatted(message);
			handler.AppendLiteral(" />");
			stringBuilder2.AppendLine(ref handler);
			svg = _svg;
			StringBuilder stringBuilder3 = svg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(33, 5, svg);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(plotLeftMapTerm - scaledCapWidth));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(plotLeftMapTerm + scaledCapWidth));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' ");
			handler.AppendFormatted(message);
			handler.AppendLiteral(" />");
			stringBuilder3.AppendLine(ref handler);
		}
	}

	public void PlotBarStacked(double[] x, double[][] series, string[] colors, double barWidth = 0.8, string edgeColor = "#333", double edgeWidth = 0.5)
	{
		edgeColor = ResolveColor(edgeColor);
		for (int i = 0; i < colors.Length; i++)
		{
			colors[i] = ResolveColor(colors[i]);
		}
		int seryLength = series.Length;
		if (seryLength == 0)
		{
			return;
		}
		ReserveLeftMargin();
		double min = _minX ?? x.Min();
		double max = _maxX ?? x.Max();
		double valueOrDefault = _minY.GetValueOrDefault();
		double max2 = _maxY ?? 1.0;
		double plotWidth = PlotWidth;
		double plotHeight = PlotHeight;
		string message = ((edgeWidth > 0.0) ? $" stroke='{edgeColor}' stroke-width='{edgeWidth}'" : "");
		double[] doubleValues = new double[x.Length];
		for (int j = 0; j < seryLength; j++)
		{
			string color = colors[j % colors.Length];
			for (int k = 0; k < x.Length && k < series[j].Length; k++)
			{
				double computed = doubleValues[k];
				double seriesTerm = computed + series[j][k];
				double plotLeftMapTerm = PlotLeft + Map(x[k] - barWidth / 2.0, min, max, plotWidth, XScale, XReverse);
				double plotLeftMapTerm2 = PlotLeft + Map(x[k] + barWidth / 2.0, min, max, plotWidth, XScale, XReverse);
				double plotTopPlotHeightTerm = PlotTop + plotHeight - Map(computed, valueOrDefault, max2, plotHeight, YScale, YReverse);
				double plotTopPlotHeightTerm2 = PlotTop + plotHeight - Map(seriesTerm, valueOrDefault, max2, plotHeight, YScale, YReverse);
				double v = Math.Min(plotTopPlotHeightTerm, plotTopPlotHeightTerm2);
				double v2 = Math.Abs(plotTopPlotHeightTerm - plotTopPlotHeightTerm2);
				StringBuilder svg = _svg;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(45, 6, svg);
				handler.AppendLiteral("<rect x='");
				handler.AppendFormatted(Fmt(plotLeftMapTerm));
				handler.AppendLiteral("' y='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' width='");
				handler.AppendFormatted(Fmt(plotLeftMapTerm2 - plotLeftMapTerm));
				handler.AppendLiteral("' height='");
				handler.AppendFormatted(Fmt(v2));
				handler.AppendLiteral("' fill='");
				handler.AppendFormatted(color);
				handler.AppendLiteral("'");
				handler.AppendFormatted(message);
				handler.AppendLiteral(" />");
				svg.AppendLine(ref handler);
				doubleValues[k] = seriesTerm;
			}
		}
	}

	public void PlotBarH(double[] y, double[] x, string color = "#2196F3", double barHeight = 0.8, string edgeColor = "#333", double edgeWidth = 0.5, double opacity = 1.0)
	{
		color = ResolveColor(color);
		edgeColor = ResolveColor(edgeColor);
		ReserveLeftMargin();
		double valueOrDefault = _minX.GetValueOrDefault();
		double max = _maxX ?? x.Max();
		double min = _minY ?? y.Min();
		double max2 = _maxY ?? y.Max();
		double plotWidth = PlotWidth;
		double plotHeight = PlotHeight;
		double valueOrDefault2 = Math.Max(valueOrDefault, 0.0);
		string message = ((opacity < 1.0) ? $" opacity='{opacity:F2}'" : "");
		string message2 = ((edgeWidth > 0.0) ? $" stroke='{edgeColor}' stroke-width='{edgeWidth}'" : "");
		for (int i = 0; i < y.Length && i < x.Length; i++)
		{
			double plotTopPlotHeightTerm = PlotTop + plotHeight - Map(y[i] + barHeight / 2.0, min, max2, plotHeight, YScale, YReverse);
			double plotTopPlotHeightTerm2 = PlotTop + plotHeight - Map(y[i] - barHeight / 2.0, min, max2, plotHeight, YScale, YReverse);
			double plotLeftMapTerm = PlotLeft + Map(x[i], valueOrDefault, max, plotWidth, XScale, XReverse);
			double plotLeftMapTerm2 = PlotLeft + Map(valueOrDefault2, valueOrDefault, max, plotWidth, XScale, XReverse);
			double v = Math.Min(plotLeftMapTerm, plotLeftMapTerm2);
			double v2 = Math.Abs(plotLeftMapTerm - plotLeftMapTerm2);
			StringBuilder svg = _svg;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(45, 7, svg);
			handler.AppendLiteral("<rect x='");
			handler.AppendFormatted(Fmt(v));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
			handler.AppendLiteral("' width='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' height='");
			handler.AppendFormatted(Fmt(plotTopPlotHeightTerm2 - plotTopPlotHeightTerm));
			handler.AppendLiteral("' fill='");
			handler.AppendFormatted(color);
			handler.AppendLiteral("'");
			handler.AppendFormatted(message2);
			handler.AppendFormatted(message);
			handler.AppendLiteral(" />");
			svg.AppendLine(ref handler);
		}
	}

	public void PlotHistogram(double[] data, int bins = 20, string color = "#2196F3", string edgeColor = "k", double edgeWidth = 0.5, double opacity = 1.0)
	{
		double minX = _minX ?? data.Min();
		double maxX = _maxX ?? data.Max();
		if (Math.Abs(maxX - minX) < 1E-15)
		{
			maxX = minX + 1.0;
		}
		double maxXAboveMinXOverBins = (maxX - minX) / (double)bins;
		double[] doubleValues = new double[bins];
		for (int i = 0; i < data.Length; i++)
		{
			int binsOffset = (int)((data[i] - minX) / maxXAboveMinXOverBins);
			if (binsOffset >= bins)
			{
				binsOffset = bins - 1;
			}
			if (binsOffset >= 0 && binsOffset < bins)
			{
				doubleValues[binsOffset]++;
			}
		}
		double[] doubleValuesArray = new double[bins];
		for (int j = 0; j < bins; j++)
		{
			doubleValuesArray[j] = minX + ((double)j + 0.5) * maxXAboveMinXOverBins;
		}
		PlotBar(doubleValuesArray, doubleValues, color, maxXAboveMinXOverBins, edgeColor, edgeWidth, opacity);
	}

	public void DrawAnnotation(double x, double y, string stringValue, double fontSize = 10.0, string color = "k", string anchor = "start")
	{
		color = ResolveColor(color);
		(double, double)[] scaledPoints = GetScaledPoints(new double[1] { x }, new double[1] { y }, useY2: false);
		StringBuilder svg = _svg;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(59, 6, svg);
		handler.AppendLiteral("<text x='");
		handler.AppendFormatted(Fmt(scaledPoints[0].Item1));
		handler.AppendLiteral("' y='");
		handler.AppendFormatted(Fmt(scaledPoints[0].Item2));
		handler.AppendLiteral("' font-size='");
		handler.AppendFormatted(fontSize);
		handler.AppendLiteral("' fill='");
		handler.AppendFormatted(color);
		handler.AppendLiteral("' text-anchor='");
		handler.AppendFormatted(anchor);
		handler.AppendLiteral("'>");
		handler.AppendFormatted(EscXml(stringValue));
		handler.AppendLiteral("</text>");
		svg.AppendLine(ref handler);
	}

	public void DrawArrow(double x1, double y1, double x2, double y2, string color = "k", double thickness = 1.5)
	{
		color = ResolveColor(color);
		string message = $"arrow{_clipId++}";
		StringBuilder svg = _svg;
		StringBuilder stringBuilder = svg;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(137, 2, svg);
		handler.AppendLiteral("<defs><marker id='");
		handler.AppendFormatted(message);
		handler.AppendLiteral("' markerWidth='8' markerHeight='6' refX='8' refY='3' orient='auto'><path d='M0,0 L8,3 L0,6 Z' fill='");
		handler.AppendFormatted(color);
		handler.AppendLiteral("'/></marker></defs>");
		stringBuilder.AppendLine(ref handler);
		(double, double)[] scaledPoints = GetScaledPoints(new double[2] { x1, x2 }, new double[2] { y1, y2 }, useY2: false);
		svg = _svg;
		StringBuilder stringBuilder2 = svg;
		handler = new StringBuilder.AppendInterpolatedStringHandler(78, 7, svg);
		handler.AppendLiteral("<line x1='");
		handler.AppendFormatted(Fmt(scaledPoints[0].Item1));
		handler.AppendLiteral("' y1='");
		handler.AppendFormatted(Fmt(scaledPoints[0].Item2));
		handler.AppendLiteral("' x2='");
		handler.AppendFormatted(Fmt(scaledPoints[1].Item1));
		handler.AppendLiteral("' y2='");
		handler.AppendFormatted(Fmt(scaledPoints[1].Item2));
		handler.AppendLiteral("' stroke='");
		handler.AppendFormatted(color);
		handler.AppendLiteral("' stroke-width='");
		handler.AppendFormatted(thickness);
		handler.AppendLiteral("' marker-end='url(#");
		handler.AppendFormatted(message);
		handler.AppendLiteral(")' />");
		stringBuilder2.AppendLine(ref handler);
	}

	public void PlotPie(double[] values, string[] colors, string[] labels = null, double innerRadius = 0.0, string edgeColor = "#fff", double edgeWidth = 1.0)
	{
		edgeColor = ResolveColor(edgeColor);
		for (int i = 0; i < colors.Length; i++)
		{
			colors[i] = ResolveColor(colors[i]);
		}
		double sum = values.Sum();
		if (sum <= 0.0)
		{
			return;
		}
		double plotLeftPlotRightTerm = (PlotLeft + PlotRight) / 2.0;
		double plotTopPlotBottomTerm = (PlotTop + PlotBottom) / 2.0;
		double plotWidthPlotHeightTerm = Math.Min(PlotWidth, PlotHeight) / 2.0 * 0.9;
		double innerRadiusTerm = plotWidthPlotHeightTerm * Math.Clamp(innerRadius, 0.0, 0.99);
		string message = ((edgeWidth > 0.0) ? $" stroke='{edgeColor}' stroke-width='{edgeWidth}'" : "");
		double scaledPi = -Math.PI / 2.0;
		for (int j = 0; j < values.Length; j++)
		{
			double sumPiTerm = values[j] / sum * 2.0 * Math.PI;
			double scaledPi2 = scaledPi;
			double computed = scaledPi + sumPiTerm;
			int piTerm = ((sumPiTerm > Math.PI) ? 1 : 0);
			double v = plotLeftPlotRightTerm + plotWidthPlotHeightTerm * Math.Cos(scaledPi2);
			double v2 = plotTopPlotBottomTerm + plotWidthPlotHeightTerm * Math.Sin(scaledPi2);
			double v3 = plotLeftPlotRightTerm + plotWidthPlotHeightTerm * Math.Cos(computed);
			double v4 = plotTopPlotBottomTerm + plotWidthPlotHeightTerm * Math.Sin(computed);
			if (innerRadiusTerm > 0.0)
			{
				double v5 = plotLeftPlotRightTerm + innerRadiusTerm * Math.Cos(scaledPi2);
				double v6 = plotTopPlotBottomTerm + innerRadiusTerm * Math.Sin(scaledPi2);
				double v7 = plotLeftPlotRightTerm + innerRadiusTerm * Math.Cos(computed);
				double v8 = plotTopPlotBottomTerm + innerRadiusTerm * Math.Sin(computed);
				StringBuilder svg = _svg;
				StringBuilder stringBuilder = svg;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(51, 18, svg);
				handler.AppendLiteral("<path d='M");
				handler.AppendFormatted(Fmt(v5));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(v6));
				handler.AppendLiteral(" L");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(v2));
				handler.AppendLiteral(" A");
				handler.AppendFormatted(Fmt(plotWidthPlotHeightTerm));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(plotWidthPlotHeightTerm));
				handler.AppendLiteral(" 0 ");
				handler.AppendFormatted(piTerm);
				handler.AppendLiteral(",1 ");
				handler.AppendFormatted(Fmt(v3));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(v4));
				handler.AppendLiteral(" L");
				handler.AppendFormatted(Fmt(v7));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(v8));
				handler.AppendLiteral(" A");
				handler.AppendFormatted(Fmt(innerRadiusTerm));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(innerRadiusTerm));
				handler.AppendLiteral(" 0 ");
				handler.AppendFormatted(piTerm);
				handler.AppendLiteral(",0 ");
				handler.AppendFormatted(Fmt(v5));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(v6));
				handler.AppendLiteral(" Z' fill='");
				handler.AppendFormatted(colors[j % colors.Length]);
				handler.AppendLiteral("'");
				handler.AppendFormatted(message);
				handler.AppendLiteral(" />");
				stringBuilder.AppendLine(ref handler);
			}
			else
			{
				StringBuilder svg = _svg;
				StringBuilder stringBuilder2 = svg;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(38, 11, svg);
				handler.AppendLiteral("<path d='M");
				handler.AppendFormatted(Fmt(plotLeftPlotRightTerm));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(plotTopPlotBottomTerm));
				handler.AppendLiteral(" L");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(v2));
				handler.AppendLiteral(" A");
				handler.AppendFormatted(Fmt(plotWidthPlotHeightTerm));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(plotWidthPlotHeightTerm));
				handler.AppendLiteral(" 0 ");
				handler.AppendFormatted(piTerm);
				handler.AppendLiteral(",1 ");
				handler.AppendFormatted(Fmt(v3));
				handler.AppendLiteral(",");
				handler.AppendFormatted(Fmt(v4));
				handler.AppendLiteral(" Z' fill='");
				handler.AppendFormatted(colors[j % colors.Length]);
				handler.AppendLiteral("'");
				handler.AppendFormatted(message);
				handler.AppendLiteral(" />");
				stringBuilder2.AppendLine(ref handler);
			}
			if (labels != null && j < labels.Length && !string.IsNullOrEmpty(labels[j]))
			{
				double computed2 = scaledPi2 + sumPiTerm / 2.0;
				double computed3 = ((innerRadiusTerm > 0.0) ? ((plotWidthPlotHeightTerm + innerRadiusTerm) / 2.0) : (plotWidthPlotHeightTerm * 0.65));
				double v9 = plotLeftPlotRightTerm + computed3 * Math.Cos(computed2);
				double computed4 = plotTopPlotBottomTerm + computed3 * Math.Sin(computed2);
				StringBuilder svg = _svg;
				StringBuilder stringBuilder3 = svg;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(59, 3, svg);
				handler.AppendLiteral("<text x='");
				handler.AppendFormatted(Fmt(v9));
				handler.AppendLiteral("' y='");
				handler.AppendFormatted(Fmt(computed4 + 4.0));
				handler.AppendLiteral("' text-anchor='middle' font-size='11'>");
				handler.AppendFormatted(EscXml(labels[j]));
				handler.AppendLiteral("</text>");
				stringBuilder3.AppendLine(ref handler);
			}
			scaledPi = computed;
		}
	}

	public void DrawColorBar(double min, double max, string[] palette, string label = "", double x = -1.0, double y = -1.0, double width = 20.0, double height = -1.0)
	{
		double plotRightOffset = ((x < 0.0) ? (PlotRight + 10.0) : x);
		double plotTop = ((y < 0.0) ? PlotTop : y);
		double plotHeight = ((height < 0.0) ? PlotHeight : height);
		double plotHeightOverLength = plotHeight / (double)palette.Length;
		StringBuilder overlaySvg;
		StringBuilder.AppendInterpolatedStringHandler handler;
		for (int i = 0; i < palette.Length; i++)
		{
			double v = plotTop + plotHeight - (double)(i + 1) * plotHeightOverLength;
			overlaySvg = _overlaySvg;
			StringBuilder stringBuilder = overlaySvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(45, 5, overlaySvg);
			handler.AppendLiteral("<rect x='");
			handler.AppendFormatted(Fmt(plotRightOffset));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(v));
			handler.AppendLiteral("' width='");
			handler.AppendFormatted(Fmt(width));
			handler.AppendLiteral("' height='");
			handler.AppendFormatted(Fmt(plotHeightOverLength + 0.5));
			handler.AppendLiteral("' fill='");
			handler.AppendFormatted(ResolveColor(palette[i]));
			handler.AppendLiteral("' />");
			stringBuilder.AppendLine(ref handler);
		}
		overlaySvg = _overlaySvg;
		StringBuilder stringBuilder2 = overlaySvg;
		handler = new StringBuilder.AppendInterpolatedStringHandler(82, 4, overlaySvg);
		handler.AppendLiteral("<rect x='");
		handler.AppendFormatted(Fmt(plotRightOffset));
		handler.AppendLiteral("' y='");
		handler.AppendFormatted(Fmt(plotTop));
		handler.AppendLiteral("' width='");
		handler.AppendFormatted(Fmt(width));
		handler.AppendLiteral("' height='");
		handler.AppendFormatted(Fmt(plotHeight));
		handler.AppendLiteral("' fill='none' stroke='#333' stroke-width='0.5' />");
		stringBuilder2.AppendLine(ref handler);
		DrawColorBarTicks(min, max, plotRightOffset, plotTop, plotHeight, width);
		if (!string.IsNullOrEmpty(label))
		{
			overlaySvg = _overlaySvg;
			StringBuilder stringBuilder3 = overlaySvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(59, 3, overlaySvg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(plotRightOffset + width / 2.0));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(plotTop - 6.0));
			handler.AppendLiteral("' text-anchor='middle' font-size='10'>");
			handler.AppendFormatted(EscXml(label));
			handler.AppendLiteral("</text>");
			stringBuilder3.AppendLine(ref handler);
		}
	}

	private void DrawColorBarTicks(double min, double max, double cbX, double cbY, double cbH, double width)
	{
		double maxAboveMin = max - min;
		double labelX = cbX + width + 5.0;
		double tickX1 = cbX + width;
		double tickX2 = cbX + width + 3.0;
		string pickRangeFormat = PickRangeFormat(min, max);
		double cbYPlusCbH = cbY + cbH;
		Emit(cbYPlusCbH, min.ToString(pickRangeFormat, CultureInfo.InvariantCulture));
		Emit(cbY, max.ToString(pickRangeFormat, CultureInfo.InvariantCulture));
		if (!(maxAboveMin > 0.0))
		{
			return;
		}
		double niceSpacing = NiceSpacing(maxAboveMin, 5);
		double minNiceSpacingTerm = Math.Ceiling(min / niceSpacing) * niceSpacing;
		int count = (int)Math.Round((max - minNiceSpacingTerm) / niceSpacing);
		int tickDecimals = TickDecimals(niceSpacing);
		for (int i = 0; i <= count; i++)
		{
			double niceSpacingTerm = minNiceSpacingTerm + (double)i * niceSpacing;
			if (!(niceSpacingTerm < min) && !(niceSpacingTerm > max + niceSpacing * 1E-09))
			{
				double minTerm = (niceSpacingTerm - min) / maxAboveMin;
				double cbYCbHTerm = cbY + cbH - minTerm * cbH;
				if (!(Math.Abs(cbYCbHTerm - cbYPlusCbH) < 6.0) && !(Math.Abs(cbYCbHTerm - cbY) < 6.0))
				{
					Emit(cbYCbHTerm, niceSpacingTerm.ToString("F" + tickDecimals, CultureInfo.InvariantCulture));
				}
			}
		}
		void Emit(double ty, string lbl)
		{
			StringBuilder overlaySvg = _overlaySvg;
			StringBuilder stringBuilder = overlaySvg;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(65, 4, overlaySvg);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(tickX1));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(ty));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(tickX2));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(ty));
			handler.AppendLiteral("' stroke='#333' stroke-width='0.5' />");
			stringBuilder.AppendLine(ref handler);
			overlaySvg = _overlaySvg;
			StringBuilder stringBuilder2 = overlaySvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(37, 3, overlaySvg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(labelX));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(ty + 3.0));
			handler.AppendLiteral("' font-size='9'>");
			handler.AppendFormatted(lbl);
			handler.AppendLiteral("</text>");
			stringBuilder2.AppendLine(ref handler);
		}
	}

	public void PlotTimeLine(DateTime[] times, double[] y, string color = "blue", double thickness = 2.0, string style = "", string marker = "", double markerSize = 3.0, bool useY2 = false)
	{
		color = ResolveColor(color);
		string dashArray = ResolveLineStyle(style);
		double[] doubleValues = ((IEnumerable<DateTime>)times).Select((Func<DateTime, double>)((DateTime t) => new DateTimeOffset(t).ToUnixTimeSeconds())).ToArray();
		if (!_minX.HasValue || !_maxX.HasValue)
		{
			_minX = doubleValues.Min();
			_maxX = doubleValues.Max();
		}
		else
		{
			_minX = Math.Min(_minX.Value, doubleValues.Min());
			_maxX = Math.Max(_maxX.Value, doubleValues.Max());
		}
		(double, double)[] scaledPoints = GetScaledPoints(doubleValues, y, useY2);
		bool condition = false;
		for (int intervalIndex = 0; intervalIndex < scaledPoints.Length; intervalIndex++)
		{
			if (double.IsNaN(scaledPoints[intervalIndex].Item1) || double.IsNaN(scaledPoints[intervalIndex].Item2))
			{
				if (condition)
				{
					ClosePoly(color, thickness, dashArray);
					condition = false;
				}
				continue;
			}
			if (!condition)
			{
				_svg.Append("<polyline points='");
				condition = true;
			}
			StringBuilder svg = _svg;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, svg);
			handler.AppendFormatted(Fmt(scaledPoints[intervalIndex].Item1));
			handler.AppendLiteral(",");
			handler.AppendFormatted(Fmt(scaledPoints[intervalIndex].Item2));
			handler.AppendLiteral(" ");
			svg.Append(ref handler);
		}
		if (condition)
		{
			ClosePoly(color, thickness, dashArray);
		}
		if (string.IsNullOrEmpty(marker))
		{
			return;
		}
		(double, double)[] scaledPointsArray = scaledPoints;
		for (int intervalIndex2 = 0; intervalIndex2 < scaledPointsArray.Length; intervalIndex2++)
		{
			(double, double) tuple = scaledPointsArray[intervalIndex2];
			if (!double.IsNaN(tuple.Item1) && !double.IsNaN(tuple.Item2))
			{
				RenderMarker(tuple.Item1, tuple.Item2, markerSize, color, 1.0, marker);
			}
		}
	}

	public void DrawTimeAxes(string xTitle = "", string yTitle = "", int targetTicks = 7, string title = "")
	{
		if (!_minX.HasValue || !_maxX.HasValue)
		{
			return;
		}
		_yTickLabelWidthPx = 0.0;
		GrowLeftMarginForYTitle(MaxTimeAxisYLabelWidthPx());
		DateTime localDateTime = DateTimeOffset.FromUnixTimeSeconds((long)_minX.Value).LocalDateTime;
		DateTime localDateTime2 = DateTimeOffset.FromUnixTimeSeconds((long)_maxX.Value).LocalDateTime;
		(TimeSpan Step, string Format) niceTimeInterval = GetNiceTimeInterval(localDateTime2 - localDateTime, targetTicks);
		TimeSpan step = niceTimeInterval.Step;
		string format = niceTimeInterval.Format;
		DateTime dateTime = SnapToInterval(localDateTime, step);
		List<(double, DateTime)> valueTuples = new List<(double, DateTime)>();
		while (dateTime <= localDateTime2)
		{
			if (dateTime >= localDateTime)
			{
				double map = Map(new DateTimeOffset(dateTime).ToUnixTimeSeconds(), _minX.Value, _maxX.Value, PlotWidth, ScaleType.Linear, XReverse);
				valueTuples.Add((PlotLeft + map, dateTime));
			}
			if (step.Ticks == 0L)
			{
				break;
			}
			dateTime = dateTime.Add(step);
		}
		StringBuilder bgSvg;
		StringBuilder.AppendInterpolatedStringHandler handler;
		if (_minY.HasValue && _maxY.HasValue)
		{
			for (int i = 0; i <= 5; i++)
			{
				double v = PlotTop + PlotHeight - (double)i * PlotHeight / 5.0;
				if (ShowGrid)
				{
					bgSvg = _bgSvg;
					StringBuilder stringBuilder = bgSvg;
					handler = new StringBuilder.AppendInterpolatedStringHandler(69, 4, bgSvg);
					handler.AppendLiteral("<line x1='");
					handler.AppendFormatted(Fmt(PlotLeft));
					handler.AppendLiteral("' y1='");
					handler.AppendFormatted(Fmt(v));
					handler.AppendLiteral("' x2='");
					handler.AppendFormatted(Fmt(PlotRight));
					handler.AppendLiteral("' y2='");
					handler.AppendFormatted(Fmt(v));
					handler.AppendLiteral("' stroke='#ddd' stroke-dasharray='4,4' />");
					stringBuilder.AppendLine(ref handler);
				}
			}
		}
		foreach (var localValue in valueTuples)
		{
			double computed = localValue.Item1;
			if (ShowGrid)
			{
				bgSvg = _bgSvg;
				StringBuilder stringBuilder2 = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(69, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(computed));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(PlotTop));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(computed));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(PlotBottom));
				handler.AppendLiteral("' stroke='#ddd' stroke-dasharray='4,4' />");
				stringBuilder2.AppendLine(ref handler);
			}
		}
		bgSvg = _svg;
		StringBuilder stringBuilder3 = bgSvg;
		handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
		handler.AppendLiteral("<rect x='");
		handler.AppendFormatted(Fmt(PlotLeft));
		handler.AppendLiteral("' y='");
		handler.AppendFormatted(Fmt(PlotTop));
		handler.AppendLiteral("' width='");
		handler.AppendFormatted(Fmt(PlotWidth));
		handler.AppendLiteral("' height='");
		handler.AppendFormatted(Fmt(PlotHeight));
		handler.AppendLiteral("' fill='none' stroke='#000' />");
		stringBuilder3.AppendLine(ref handler);
		foreach (var (v2, dateTime2) in valueTuples)
		{
			if (ShowTicks)
			{
				bgSvg = _svg;
				StringBuilder stringBuilder4 = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(46, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(v2));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(PlotBottom));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(v2));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(PlotBottom + TickLength));
				handler.AppendLiteral("' stroke='#000' />");
				stringBuilder4.AppendLine(ref handler);
			}
			double v3 = PlotBottom + 15.0;
			bgSvg = _svg;
			StringBuilder stringBuilder5 = bgSvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(85, 5, bgSvg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(v3));
			handler.AppendLiteral("' text-anchor='start' font-size='10' transform='rotate(35, ");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral(", ");
			handler.AppendFormatted(Fmt(v3));
			handler.AppendLiteral(")'>");
			handler.AppendFormatted(dateTime2.ToString(format));
			handler.AppendLiteral("</text>");
			stringBuilder5.AppendLine(ref handler);
		}
		if (_minY.HasValue && _maxY.HasValue)
		{
			for (int j = 0; j <= 5; j++)
			{
				double minYMaxYTerm = _minY.Value + (double)j * (_maxY.Value - _minY.Value) / 5.0;
				double plotHeightTerm = (double)j * PlotHeight / 5.0;
				if (YReverse)
				{
					plotHeightTerm = PlotHeight - plotHeightTerm;
				}
				double plotTopPlotHeightTerm = PlotTop + PlotHeight - plotHeightTerm;
				if (ShowTicks)
				{
					bgSvg = _svg;
					StringBuilder stringBuilder6 = bgSvg;
					handler = new StringBuilder.AppendInterpolatedStringHandler(46, 4, bgSvg);
					handler.AppendLiteral("<line x1='");
					handler.AppendFormatted(Fmt(PlotLeft - TickLength));
					handler.AppendLiteral("' y1='");
					handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
					handler.AppendLiteral("' x2='");
					handler.AppendFormatted(Fmt(PlotLeft));
					handler.AppendLiteral("' y2='");
					handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
					handler.AppendLiteral("' stroke='#000' />");
					stringBuilder6.AppendLine(ref handler);
				}
				string invariantCultureCultureInfoTerm = minYMaxYTerm.ToString("G3", CultureInfo.InvariantCulture);
				_yTickLabelWidthPx = Math.Max(_yTickLabelWidthPx, EstimateLabelWidth(invariantCultureCultureInfoTerm, 10.0));
				bgSvg = _svg;
				StringBuilder stringBuilder7 = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(56, 3, bgSvg);
				handler.AppendLiteral("<text x='");
				handler.AppendFormatted(Fmt(PlotLeft - 5.0));
				handler.AppendLiteral("' y='");
				handler.AppendFormatted(Fmt(plotTopPlotHeightTerm + 4.0));
				handler.AppendLiteral("' text-anchor='end' font-size='10'>");
				handler.AppendFormatted(invariantCultureCultureInfoTerm);
				handler.AppendLiteral("</text>");
				stringBuilder7.AppendLine(ref handler);
			}
		}
		if (!string.IsNullOrEmpty(xTitle))
		{
			bgSvg = _svg;
			StringBuilder stringBuilder8 = bgSvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(59, 3, bgSvg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt((PlotLeft + PlotRight) / 2.0));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(PlotBottom + _padB - 5.0));
			handler.AppendLiteral("' text-anchor='middle' font-size='12'>");
			handler.AppendFormatted(EscXml(xTitle));
			handler.AppendLiteral("</text>");
			stringBuilder8.AppendLine(ref handler);
		}
		if (!string.IsNullOrEmpty(yTitle))
		{
			double v4 = YTitleX();
			bgSvg = _svg;
			StringBuilder stringBuilder9 = bgSvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(87, 5, bgSvg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(v4));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt((PlotTop + PlotBottom) / 2.0));
			handler.AppendLiteral("' text-anchor='middle' font-size='12' transform='rotate(-90, ");
			handler.AppendFormatted(Fmt(v4));
			handler.AppendLiteral(", ");
			handler.AppendFormatted(Fmt((PlotTop + PlotBottom) / 2.0));
			handler.AppendLiteral(")'>");
			handler.AppendFormatted(EscXml(yTitle));
			handler.AppendLiteral("</text>");
			stringBuilder9.AppendLine(ref handler);
		}
		if (!string.IsNullOrEmpty(title))
		{
			bgSvg = _svg;
			StringBuilder stringBuilder10 = bgSvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(78, 3, bgSvg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt((PlotLeft + PlotRight) / 2.0));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(PlotTop - 8.0));
			handler.AppendLiteral("' text-anchor='middle' font-size='13' font-weight='bold'>");
			handler.AppendFormatted(EscXml(title));
			handler.AppendLiteral("</text>");
			stringBuilder10.AppendLine(ref handler);
		}
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

	public void DrawAxes(string xTitle = "", string yTitle = "", string y2Title = "", string title = "")
	{
		_yTickLabelWidthPx = 0.0;
		GrowLeftMarginForYTitle(MaxYTickLabelWidthPx());
		if (_customTicksX.HasValue)
		{
			DrawCustomTicks(_customTicksX.Value.positions, _customTicksX.Value.labels, isX: true);
		}
		else if (XScale == ScaleType.Linear)
		{
			DrawLinearTicks(_minX.GetValueOrDefault(), _maxX ?? 1.0, isX: true);
		}
		else
		{
			DrawLogTicks(_minX ?? 1.0, _maxX ?? 10.0, isX: true);
		}
		if (_customTicksY.HasValue)
		{
			DrawCustomTicks(_customTicksY.Value.positions, _customTicksY.Value.labels, isX: false);
		}
		else if (YScale == ScaleType.Linear)
		{
			DrawLinearTicks(_minY.GetValueOrDefault(), _maxY ?? 1.0, isX: false);
		}
		else
		{
			DrawLogTicks(_minY ?? 1.0, _maxY ?? 10.0, isX: false);
		}
		if (_minY2.HasValue && _maxY2.HasValue)
		{
			if (Y2Scale == ScaleType.Linear)
			{
				DrawLinearTicks(_minY2.Value, _maxY2.Value, isX: false, isY2: true);
			}
			else
			{
				DrawLogTicks(_minY2.Value, _maxY2.Value, isX: false, isY2: true);
			}
		}
		StringBuilder svg = _svg;
		StringBuilder stringBuilder = svg;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(80, 4, svg);
		handler.AppendLiteral("<rect x='");
		handler.AppendFormatted(Fmt(PlotLeft));
		handler.AppendLiteral("' y='");
		handler.AppendFormatted(Fmt(PlotTop));
		handler.AppendLiteral("' width='");
		handler.AppendFormatted(Fmt(PlotWidth));
		handler.AppendLiteral("' height='");
		handler.AppendFormatted(Fmt(PlotHeight));
		handler.AppendLiteral("' fill='none' stroke='#333' stroke-width='1' />");
		stringBuilder.AppendLine(ref handler);
		double v = (PlotLeft + PlotRight) / 2.0;
		double v2 = (PlotTop + PlotBottom) / 2.0;
		if (!string.IsNullOrEmpty(xTitle))
		{
			svg = _svg;
			StringBuilder stringBuilder2 = svg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(59, 3, svg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(v));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(PlotBottom + 35.0));
			handler.AppendLiteral("' text-anchor='middle' font-size='12'>");
			handler.AppendFormatted(EscXml(xTitle));
			handler.AppendLiteral("</text>");
			stringBuilder2.AppendLine(ref handler);
		}
		if (!string.IsNullOrEmpty(yTitle))
		{
			double v3 = YTitleX();
			svg = _svg;
			StringBuilder stringBuilder3 = svg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(85, 5, svg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(v3));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' text-anchor='middle' font-size='12' transform='rotate(-90,");
			handler.AppendFormatted(Fmt(v3));
			handler.AppendLiteral(",");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral(")'>");
			handler.AppendFormatted(EscXml(yTitle));
			handler.AppendLiteral("</text>");
			stringBuilder3.AppendLine(ref handler);
		}
		if (!string.IsNullOrEmpty(y2Title))
		{
			svg = _svg;
			StringBuilder stringBuilder4 = svg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(84, 5, svg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(PlotRight + 35.0));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' text-anchor='middle' font-size='12' transform='rotate(90,");
			handler.AppendFormatted(Fmt(PlotRight + 35.0));
			handler.AppendLiteral(",");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral(")'>");
			handler.AppendFormatted(EscXml(y2Title));
			handler.AppendLiteral("</text>");
			stringBuilder4.AppendLine(ref handler);
		}
		if (!string.IsNullOrEmpty(title))
		{
			svg = _svg;
			StringBuilder stringBuilder5 = svg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(78, 3, svg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(v));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(PlotTop - 8.0));
			handler.AppendLiteral("' text-anchor='middle' font-size='13' font-weight='bold'>");
			handler.AppendFormatted(EscXml(title));
			handler.AppendLiteral("</text>");
			stringBuilder5.AppendLine(ref handler);
		}
	}

	public void DrawLegend((string label, string color, string style)[] items, string position = "top-right", double? x = null, double? y = null)
	{
		(string, string, string, string)[] items2 = items.Select(((string label, string color, string style) it) =>
		{
			string markerFromStyleSlot = MarkerFromStyleSlot(it.style);
			return (!(markerFromStyleSlot == "line")) ? (label: it.label, color: it.color, "", markerFromStyleSlot) : (label: it.label, color: it.color, it.style, "line");
		}).ToArray();
		DrawLegend(items2, position, x, y);
	}

	private static string MarkerFromStyleSlot(string s)
	{
		if (s != null)
		{
			switch (s.Length)
			{
			case 1:
				switch (s[0])
				{
				case '.':
					return "circle";
				case '*':
				case '+':
				case 'x':
					break;
				default:
					goto end_IL_000f;
				}
				goto IL_00ae;
			case 6:
			{
				char c = s[0];
				if (c != 'c')
				{
					if (c != 's' || !(s == "square"))
					{
						break;
					}
				}
				else if (!(s == "circle"))
				{
					break;
				}
				goto IL_00ae;
			}
			case 8:
				if (!(s == "triangle"))
				{
					break;
				}
				goto IL_00ae;
			case 7:
				{
					if (!(s == "diamond"))
					{
						break;
					}
					goto IL_00ae;
				}
				IL_00ae:
				return s;
				end_IL_000f:
				break;
			}
		}
		return "line";
	}

	public void DrawLegend((string label, string color, string style, string symbol)[] items, string position = "top-right", double? x = null, double? y = null)
	{
		double labelItTerm = ((items.Length != 0) ? ((double)items.Max(((string label, string color, string style, string symbol) it) => it.label.Length) * 6.5) : 0.0);
		double legendW = 30.0 + labelItTerm + 10.0;
		double legendH = items.Length * 18;
		(double x, double y) tuple = ResolveLegendPosition(position, legendW, legendH, 10.0);
		double tupleTerm = tuple.x;
		double tupleTerm2 = tuple.y;
		double computed = x ?? tupleTerm;
		double computed2 = y ?? tupleTerm2;
		for (int intervalIndex = 0; intervalIndex < items.Length; intervalIndex++)
		{
			double computed3 = computed2 + (double)(intervalIndex * 18);
			(string label, string color, string style, string symbol) tuple2 = items[intervalIndex];
			string label = tuple2.label;
			string color = tuple2.color;
			string style = tuple2.style;
			string symbol = tuple2.symbol;
			color = ResolveColor(color);
			string resolveLineStyle = ResolveLineStyle(style);
			StringBuilder overlaySvg;
			StringBuilder.AppendInterpolatedStringHandler handler;
			if (symbol == "line")
			{
				overlaySvg = _overlaySvg;
				StringBuilder stringBuilder = overlaySvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(56, 5, overlaySvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(computed));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(computed3));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(computed + 25.0));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(computed3));
				handler.AppendLiteral("' stroke='");
				handler.AppendFormatted(color);
				handler.AppendLiteral("' stroke-width='2'");
				stringBuilder.Append(ref handler);
				if (!string.IsNullOrEmpty(resolveLineStyle))
				{
					overlaySvg = _overlaySvg;
					StringBuilder stringBuilder2 = overlaySvg;
					handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, overlaySvg);
					handler.AppendLiteral(" stroke-dasharray='");
					handler.AppendFormatted(resolveLineStyle);
					handler.AppendLiteral("'");
					stringBuilder2.Append(ref handler);
				}
				_overlaySvg.AppendLine(" />");
			}
			else
			{
				RenderMarker(computed + 12.5, computed3, 4.0, color, 1.0, symbol, _overlaySvg);
			}
			overlaySvg = _overlaySvg;
			StringBuilder stringBuilder3 = overlaySvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(38, 3, overlaySvg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(computed + 30.0));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(computed3 + 4.0));
			handler.AppendLiteral("' font-size='11'>");
			handler.AppendFormatted(EscXml(label));
			handler.AppendLiteral("</text>");
			stringBuilder3.AppendLine(ref handler);
		}
	}

	public void DrawTable(double x, double y, string[] headers, string[][] rows, double columnWidth = 100.0, double rowHeight = 20.0)
	{
		for (int i = 0; i < headers.Length; i++)
		{
			StringBuilder svg = _svg;
			StringBuilder stringBuilder = svg;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(57, 3, svg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(x + (double)i * columnWidth));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(y + rowHeight));
			handler.AppendLiteral("' font-size='11' font-weight='bold'>");
			handler.AppendFormatted(EscXml(headers[i]));
			handler.AppendLiteral("</text>");
			stringBuilder.AppendLine(ref handler);
		}
		for (int j = 0; j < rows.Length; j++)
		{
			for (int k = 0; k < rows[j].Length; k++)
			{
				StringBuilder svg = _svg;
				StringBuilder stringBuilder2 = svg;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(38, 3, svg);
				handler.AppendLiteral("<text x='");
				handler.AppendFormatted(Fmt(x + (double)k * columnWidth));
				handler.AppendLiteral("' y='");
				handler.AppendFormatted(Fmt(y + (double)(j + 2) * rowHeight));
				handler.AppendLiteral("' font-size='11'>");
				handler.AppendFormatted(EscXml(rows[j][k]));
				handler.AppendLiteral("</text>");
				stringBuilder2.AppendLine(ref handler);
			}
		}
	}

	public void DrawText(double x, double y, string stringValue, double fontSize = 12.0, string fontWeight = "normal", string anchor = "start")
	{
		StringBuilder svg = _svg;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(66, 6, svg);
		handler.AppendLiteral("<text x='");
		handler.AppendFormatted(Fmt(x));
		handler.AppendLiteral("' y='");
		handler.AppendFormatted(Fmt(y));
		handler.AppendLiteral("' font-size='");
		handler.AppendFormatted(fontSize);
		handler.AppendLiteral("' font-weight='");
		handler.AppendFormatted(fontWeight);
		handler.AppendLiteral("' text-anchor='");
		handler.AppendFormatted(anchor);
		handler.AppendLiteral("'>");
		handler.AppendFormatted(EscXml(stringValue));
		handler.AppendLiteral("</text>");
		svg.AppendLine(ref handler);
	}

	public void BeginClip()
	{
		string message = $"clip{_clipId++}";
		StringBuilder svg = _svg;
		StringBuilder stringBuilder = svg;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(76, 5, svg);
		handler.AppendLiteral("<defs><clipPath id='");
		handler.AppendFormatted(message);
		handler.AppendLiteral("'><rect x='");
		handler.AppendFormatted(Fmt(PlotLeft));
		handler.AppendLiteral("' y='");
		handler.AppendFormatted(Fmt(PlotTop));
		handler.AppendLiteral("' width='");
		handler.AppendFormatted(Fmt(PlotWidth));
		handler.AppendLiteral("' height='");
		handler.AppendFormatted(Fmt(PlotHeight));
		handler.AppendLiteral("'/></clipPath></defs>");
		stringBuilder.AppendLine(ref handler);
		svg = _svg;
		StringBuilder stringBuilder2 = svg;
		handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, svg);
		handler.AppendLiteral("<g clip-path='url(#");
		handler.AppendFormatted(message);
		handler.AppendLiteral(")'>");
		stringBuilder2.AppendLine(ref handler);
	}

	public void BeginClipPolygon(IReadOnlyList<double> xData, IReadOnlyList<double> yData)
	{
		if (xData == null || yData == null || xData.Count < 3 || xData.Count != yData.Count)
		{
			BeginClip();
			return;
		}
		double valueOrDefault = _minX.GetValueOrDefault();
		double max = _maxX ?? 1.0;
		double valueOrDefault2 = _minY.GetValueOrDefault();
		double max2 = _maxY ?? 1.0;
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < xData.Count; i++)
		{
			double v = PlotLeft + Map(xData[i], valueOrDefault, max, PlotWidth, XScale, XReverse);
			double v2 = PlotTop + PlotHeight - Map(yData[i], valueOrDefault2, max2, PlotHeight, YScale, YReverse);
			if (i > 0)
			{
				stringBuilder.Append(' ');
			}
			stringBuilder.Append(Fmt(v)).Append(',').Append(Fmt(v2));
		}
		string message = $"clip{_clipId++}";
		StringBuilder svg = _svg;
		StringBuilder stringBuilder2 = svg;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(60, 2, svg);
		handler.AppendLiteral("<defs><clipPath id='");
		handler.AppendFormatted(message);
		handler.AppendLiteral("'><polygon points='");
		handler.AppendFormatted(stringBuilder);
		handler.AppendLiteral("'/></clipPath></defs>");
		stringBuilder2.AppendLine(ref handler);
		svg = _svg;
		StringBuilder stringBuilder3 = svg;
		handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, svg);
		handler.AppendLiteral("<g clip-path='url(#");
		handler.AppendFormatted(message);
		handler.AppendLiteral(")'>");
		stringBuilder3.AppendLine(ref handler);
	}

	public void EndClip()
	{
		_svg.AppendLine("</g>");
	}

	public void EnableZoom()
	{
		_zoomEnabled = true;
	}

	public string Render()
	{
		if (_zoomEnabled)
		{
			return $"<svg id='svgRoot' tabindex='0' viewBox='0 0 {_width} {_height}' width='{_width}' height='{_height}' xmlns='http://www.w3.org/2000/svg' style='background:{BackgroundColor};font-family:Arial,sans-serif'>{"<style>path,line,polyline,polygon,circle,rect{vector-effect:non-scaling-stroke;}#svgRoot{cursor:grab;touch-action:none;}#svgRoot.dragging{cursor:grabbing;}</style>"}{_bgSvg}{_svg}{_overlaySvg}{"<script><![CDATA[\r\n(function(){\r\n  var svg = document.getElementById('svgRoot');\r\n  if (!svg || !svg.viewBox || !svg.viewBox.baseVal) return;\r\n  var vb = svg.viewBox.baseVal;\r\n  var initialView = { x: vb.x, y: vb.y, width: vb.width, height: vb.height };\r\n\r\n  function resetView(){\r\n    vb.x = initialView.x;\r\n    vb.y = initialView.y;\r\n    vb.width = initialView.width;\r\n    vb.height = initialView.height;\r\n  }\r\n\r\n  svg.addEventListener('dblclick', function(evt){\r\n    evt.preventDefault();\r\n    resetView();\r\n  });\r\n  svg.addEventListener('keydown', function(evt){\r\n    if (evt.key !== 'Home') return;\r\n    evt.preventDefault();\r\n    resetView();\r\n  });\r\n\r\n  svg.addEventListener('wheel', function(evt){\r\n    evt.preventDefault();\r\n    var screenCtm = svg.getScreenCTM();\r\n    if (!screenCtm) return;\r\n    var pt = svg.createSVGPoint();\r\n    pt.x = evt.clientX; pt.y = evt.clientY;\r\n    var svgPt = pt.matrixTransform(screenCtm.inverse());\r\n    var scale = evt.deltaY < 0 ? 0.9 : 1.1;\r\n    var newWidth = vb.width * scale;\r\n    var newHeight = vb.height * scale;\r\n    var minWidth = svg.width.baseVal.value * 0.02;\r\n    var maxWidth = svg.width.baseVal.value * 40;\r\n    if (newWidth < minWidth || newWidth > maxWidth) return;\r\n    vb.x = svgPt.x - (svgPt.x - vb.x) * scale;\r\n    vb.y = svgPt.y - (svgPt.y - vb.y) * scale;\r\n    vb.width = newWidth;\r\n    vb.height = newHeight;\r\n  }, { passive: false });\r\n\r\n  var dragging = false, lastX = 0, lastY = 0;\r\n  svg.addEventListener('pointerdown', function(evt){\r\n    svg.focus();\r\n    dragging = true;\r\n    lastX = evt.clientX; lastY = evt.clientY;\r\n    svg.classList.add('dragging');\r\n    svg.setPointerCapture(evt.pointerId);\r\n  });\r\n  svg.addEventListener('pointermove', function(evt){\r\n    if (!dragging) return;\r\n    var screenCtm = svg.getScreenCTM();\r\n    if (!screenCtm) return;\r\n    // Screen-pixel delta -> SVG user-unit delta, via the CTM's uniform scale factor.\r\n    var userPerPx = vb.width / svg.getBoundingClientRect().width;\r\n    vb.x -= (evt.clientX - lastX) * userPerPx;\r\n    vb.y -= (evt.clientY - lastY) * userPerPx;\r\n    lastX = evt.clientX; lastY = evt.clientY;\r\n  });\r\n  function endDrag(evt){\r\n    if (!dragging) return;\r\n    dragging = false;\r\n    svg.classList.remove('dragging');\r\n    try { svg.releasePointerCapture(evt.pointerId); } catch (e) {}\r\n  }\r\n  svg.addEventListener('pointerup', endDrag);\r\n  svg.addEventListener('pointercancel', endDrag);\r\n})();\r\n]]></script>"}</svg>";
		}
		return $"<svg width='{_width}' height='{_height}' xmlns='http://www.w3.org/2000/svg' style='background:{BackgroundColor};font-family:Arial,sans-serif'>{_bgSvg}{_svg}{_overlaySvg}</svg>";
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
		Console.WriteLine("Plot saved: " + filePath);
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
			throw new FileNotFoundException("SaveAsPng: source SVG not found. Call Save() first. Path: " + sourcePath, sourcePath);
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
			throw new FileNotFoundException("SaveAsPdf: source SVG not found. Call Save() first. Path: " + sourcePath, sourcePath);
		}
		string changeExtension = Path.ChangeExtension(sourcePath, ".pdf");
		string directoryName = Path.GetDirectoryName(changeExtension);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		FigureRenderer.TryRenderToPdf(sourcePath, changeExtension, _width, _height);
	}

	private (double x, double y)[] GetScaledPoints(double[] x, double[] y, bool useY2)
	{
		ReserveLeftMargin();
		double minX = _minX ?? x.Where((double v) => !double.IsNaN(v)).DefaultIfEmpty(0.0).Min();
		double maxX = _maxX ?? x.Where((double v) => !double.IsNaN(v)).DefaultIfEmpty(1.0).Max();
		double minY;
		double maxY;
		if (useY2)
		{
			minY = _minY2 ?? y.Where((double v) => !double.IsNaN(v)).DefaultIfEmpty(0.0).Min();
			maxY = _maxY2 ?? y.Where((double v) => !double.IsNaN(v)).DefaultIfEmpty(1.0).Max();
		}
		else
		{
			minY = _minY ?? y.Where((double v) => !double.IsNaN(v)).DefaultIfEmpty(0.0).Min();
			maxY = _maxY ?? y.Where((double v) => !double.IsNaN(v)).DefaultIfEmpty(1.0).Max();
		}
		double pW = PlotWidth;
		double pH = PlotHeight;
		ScaleType yScale = (useY2 ? Y2Scale : YScale);
		bool yReverse = (useY2 ? Y2Reverse : YReverse);
		return x.Zip(y, (double xi, double yi) =>
		{
			if (double.IsNaN(xi) || double.IsNaN(yi))
			{
				return (double.NaN, double.NaN);
			}
			double map = Map(xi, minX, maxX, pW, XScale, XReverse);
			double map2 = Map(yi, minY, maxY, pH, yScale, yReverse);
			return (PlotLeft + map, PlotTop + pH - map2);
		}).ToArray();
	}

	private void RenderMarker(double cx, double cy, double r, string color, double opacity, string marker, StringBuilder target = null)
	{
		StringBuilder stringBuilder = target ?? _svg;
		string message = ((opacity < 1.0) ? $" opacity='{opacity:F2}'" : "");
		switch (marker)
		{
		case "square":
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder14 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(45, 6, stringBuilder2);
			handler.AppendLiteral("<rect x='");
			handler.AppendFormatted(Fmt(cx - r));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(cy - r));
			handler.AppendLiteral("' width='");
			handler.AppendFormatted(Fmt(r * 2.0));
			handler.AppendLiteral("' height='");
			handler.AppendFormatted(Fmt(r * 2.0));
			handler.AppendLiteral("' fill='");
			handler.AppendFormatted(color);
			handler.AppendLiteral("'");
			handler.AppendFormatted(message);
			handler.AppendLiteral(" />");
			stringBuilder14.AppendLine(ref handler);
			break;
		}
		case "diamond":
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder13 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(36, 10, stringBuilder2);
			handler.AppendLiteral("<polygon points='");
			handler.AppendFormatted(Fmt(cx));
			handler.AppendLiteral(",");
			handler.AppendFormatted(Fmt(cy - r));
			handler.AppendLiteral(" ");
			handler.AppendFormatted(Fmt(cx + r));
			handler.AppendLiteral(",");
			handler.AppendFormatted(Fmt(cy));
			handler.AppendLiteral(" ");
			handler.AppendFormatted(Fmt(cx));
			handler.AppendLiteral(",");
			handler.AppendFormatted(Fmt(cy + r));
			handler.AppendLiteral(" ");
			handler.AppendFormatted(Fmt(cx - r));
			handler.AppendLiteral(",");
			handler.AppendFormatted(Fmt(cy));
			handler.AppendLiteral("' fill='");
			handler.AppendFormatted(color);
			handler.AppendLiteral("'");
			handler.AppendFormatted(message);
			handler.AppendLiteral(" />");
			stringBuilder13.AppendLine(ref handler);
			break;
		}
		case "triangle":
		{
			double computed = r * 1.15;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder12 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(34, 8, stringBuilder2);
			handler.AppendLiteral("<polygon points='");
			handler.AppendFormatted(Fmt(cx));
			handler.AppendLiteral(",");
			handler.AppendFormatted(Fmt(cy - computed));
			handler.AppendLiteral(" ");
			handler.AppendFormatted(Fmt(cx + r));
			handler.AppendLiteral(",");
			handler.AppendFormatted(Fmt(cy + computed * 0.6));
			handler.AppendLiteral(" ");
			handler.AppendFormatted(Fmt(cx - r));
			handler.AppendLiteral(",");
			handler.AppendFormatted(Fmt(cy + computed * 0.6));
			handler.AppendLiteral("' fill='");
			handler.AppendFormatted(color);
			handler.AppendLiteral("'");
			handler.AppendFormatted(message);
			handler.AppendLiteral(" />");
			stringBuilder12.AppendLine(ref handler);
			break;
		}
		case "*":
		{
			double computed2 = r * 0.9;
			string colorMessageTerm = "stroke='" + color + "' stroke-width='1.5'" + message;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(33, 5, stringBuilder2);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(cx));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(cy - computed2));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(cx));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(cy + computed2));
			handler.AppendLiteral("' ");
			handler.AppendFormatted(colorMessageTerm);
			handler.AppendLiteral(" />");
			stringBuilder8.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder9 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(33, 5, stringBuilder2);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(cx - computed2));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(cy));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(cx + computed2));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(cy));
			handler.AppendLiteral("' ");
			handler.AppendFormatted(colorMessageTerm);
			handler.AppendLiteral(" />");
			stringBuilder9.AppendLine(ref handler);
			double computed3 = computed2 * 0.707;
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder10 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(33, 5, stringBuilder2);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(cx - computed3));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(cy - computed3));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(cx + computed3));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(cy + computed3));
			handler.AppendLiteral("' ");
			handler.AppendFormatted(colorMessageTerm);
			handler.AppendLiteral(" />");
			stringBuilder10.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder11 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(33, 5, stringBuilder2);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(cx + computed3));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(cy - computed3));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(cx - computed3));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(cy + computed3));
			handler.AppendLiteral("' ");
			handler.AppendFormatted(colorMessageTerm);
			handler.AppendLiteral(" />");
			stringBuilder11.AppendLine(ref handler);
			break;
		}
		case "x":
		{
			double computed4 = r * 0.707;
			string colorMessageTerm2 = "stroke='" + color + "' stroke-width='1.5'" + message;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(33, 5, stringBuilder2);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(cx - computed4));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(cy - computed4));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(cx + computed4));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(cy + computed4));
			handler.AppendLiteral("' ");
			handler.AppendFormatted(colorMessageTerm2);
			handler.AppendLiteral(" />");
			stringBuilder6.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(33, 5, stringBuilder2);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(cx + computed4));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(cy - computed4));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(cx - computed4));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(cy + computed4));
			handler.AppendLiteral("' ");
			handler.AppendFormatted(colorMessageTerm2);
			handler.AppendLiteral(" />");
			stringBuilder7.AppendLine(ref handler);
			break;
		}
		case "+":
		{
			string colorMessageTerm3 = "stroke='" + color + "' stroke-width='1.5'" + message;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(33, 5, stringBuilder2);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(cx));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(cy - r));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(cx));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(cy + r));
			handler.AppendLiteral("' ");
			handler.AppendFormatted(colorMessageTerm3);
			handler.AppendLiteral(" />");
			stringBuilder4.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(33, 5, stringBuilder2);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(cx - r));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(cy));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(cx + r));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(cy));
			handler.AppendLiteral("' ");
			handler.AppendFormatted(colorMessageTerm3);
			handler.AppendLiteral(" />");
			stringBuilder5.AppendLine(ref handler);
			break;
		}
		default:
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(35, 5, stringBuilder2);
			handler.AppendLiteral("<circle cx='");
			handler.AppendFormatted(Fmt(cx));
			handler.AppendLiteral("' cy='");
			handler.AppendFormatted(Fmt(cy));
			handler.AppendLiteral("' r='");
			handler.AppendFormatted(r);
			handler.AppendLiteral("' fill='");
			handler.AppendFormatted(color);
			handler.AppendLiteral("'");
			handler.AppendFormatted(message);
			handler.AppendLiteral(" />");
			stringBuilder3.AppendLine(ref handler);
			break;
		}
		}
	}

	private void ClosePoly(string color, double thickness, string dashArray)
	{
		StringBuilder svg = _svg;
		StringBuilder stringBuilder = svg;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(63, 2, svg);
		handler.AppendLiteral("' fill='none' stroke='");
		handler.AppendFormatted(color);
		handler.AppendLiteral("' stroke-width='");
		handler.AppendFormatted(thickness);
		handler.AppendLiteral("' stroke-linejoin='round'");
		stringBuilder.Append(ref handler);
		if (!string.IsNullOrEmpty(dashArray))
		{
			svg = _svg;
			StringBuilder stringBuilder2 = svg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, svg);
			handler.AppendLiteral(" stroke-dasharray='");
			handler.AppendFormatted(dashArray);
			handler.AppendLiteral("'");
			stringBuilder2.Append(ref handler);
		}
		_svg.AppendLine(" />");
	}

	private static double Map(double min2, double min, double max, double len, ScaleType s, bool reverse = false)
	{
		double min2LenTerm;
		if (s == ScaleType.Log10)
		{
			if (min <= 0.0)
			{
				min = 1E-10;
			}
			if (max <= 0.0)
			{
				max = min * 10.0;
			}
			if (min2 <= 0.0)
			{
				min2 = min;
			}
			double minTerm = Math.Log10(min);
			double maxTerm = Math.Log10(max) - minTerm;
			min2LenTerm = ((Math.Abs(maxTerm) < 1E-15) ? 0.0 : ((Math.Log10(min2) - minTerm) / maxTerm * len));
		}
		else
		{
			double maxAboveMin = max - min;
			min2LenTerm = ((Math.Abs(maxAboveMin) < 1E-15) ? 0.0 : ((min2 - min) / maxAboveMin * len));
		}
		if (!reverse)
		{
			return min2LenTerm;
		}
		return len - min2LenTerm;
	}

	private void DrawCustomTicks(double[] positions, string[] labels, bool isX)
	{
		double valueOrDefault;
		double max;
		if (isX)
		{
			valueOrDefault = _minX.GetValueOrDefault();
			max = _maxX ?? 1.0;
		}
		else
		{
			valueOrDefault = _minY.GetValueOrDefault();
			max = _maxY ?? 1.0;
		}
		double len = (isX ? PlotWidth : PlotHeight);
		ScaleType s = (isX ? XScale : YScale);
		bool reverse = (isX ? XReverse : YReverse);
		for (int i = 0; i < positions.Length && i < labels.Length; i++)
		{
			double map = Map(positions[i], valueOrDefault, max, len, s, reverse);
			StringBuilder bgSvg;
			StringBuilder.AppendInterpolatedStringHandler handler;
			if (isX)
			{
				double v = PlotLeft + map;
				if (ShowGrid)
				{
					bgSvg = _bgSvg;
					StringBuilder stringBuilder = bgSvg;
					handler = new StringBuilder.AppendInterpolatedStringHandler(69, 4, bgSvg);
					handler.AppendLiteral("<line x1='");
					handler.AppendFormatted(Fmt(v));
					handler.AppendLiteral("' y1='");
					handler.AppendFormatted(Fmt(PlotTop));
					handler.AppendLiteral("' x2='");
					handler.AppendFormatted(Fmt(v));
					handler.AppendLiteral("' y2='");
					handler.AppendFormatted(Fmt(PlotBottom));
					handler.AppendLiteral("' stroke='#ddd' stroke-dasharray='4,3' />");
					stringBuilder.AppendLine(ref handler);
				}
				if (ShowTicks)
				{
					bgSvg = _svg;
					StringBuilder stringBuilder2 = bgSvg;
					handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
					handler.AppendLiteral("<line x1='");
					handler.AppendFormatted(Fmt(v));
					handler.AppendLiteral("' y1='");
					handler.AppendFormatted(Fmt(PlotTop));
					handler.AppendLiteral("' x2='");
					handler.AppendFormatted(Fmt(v));
					handler.AppendLiteral("' y2='");
					handler.AppendFormatted(Fmt(PlotTop + TickLength));
					handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
					stringBuilder2.AppendLine(ref handler);
					bgSvg = _svg;
					StringBuilder stringBuilder3 = bgSvg;
					handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
					handler.AppendLiteral("<line x1='");
					handler.AppendFormatted(Fmt(v));
					handler.AppendLiteral("' y1='");
					handler.AppendFormatted(Fmt(PlotBottom - TickLength));
					handler.AppendLiteral("' x2='");
					handler.AppendFormatted(Fmt(v));
					handler.AppendLiteral("' y2='");
					handler.AppendFormatted(Fmt(PlotBottom));
					handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
					stringBuilder3.AppendLine(ref handler);
				}
				bgSvg = _svg;
				StringBuilder stringBuilder4 = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(59, 3, bgSvg);
				handler.AppendLiteral("<text x='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y='");
				handler.AppendFormatted(Fmt(PlotBottom + 15.0));
				handler.AppendLiteral("' text-anchor='middle' font-size='10'>");
				handler.AppendFormatted(EscXml(labels[i]));
				handler.AppendLiteral("</text>");
				stringBuilder4.AppendLine(ref handler);
				continue;
			}
			double plotTopPlotHeightTerm = PlotTop + PlotHeight - map;
			if (ShowGrid)
			{
				bgSvg = _bgSvg;
				StringBuilder stringBuilder5 = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(69, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(PlotLeft));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(PlotRight));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
				handler.AppendLiteral("' stroke='#ddd' stroke-dasharray='4,3' />");
				stringBuilder5.AppendLine(ref handler);
			}
			if (ShowTicks)
			{
				bgSvg = _svg;
				StringBuilder stringBuilder6 = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(PlotLeft));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(PlotLeft + TickLength));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
				handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
				stringBuilder6.AppendLine(ref handler);
				bgSvg = _svg;
				StringBuilder stringBuilder7 = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(PlotRight - TickLength));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(PlotRight));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
				handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
				stringBuilder7.AppendLine(ref handler);
			}
			_yTickLabelWidthPx = Math.Max(_yTickLabelWidthPx, EstimateLabelWidth(labels[i], 10.0));
			bgSvg = _svg;
			StringBuilder stringBuilder8 = bgSvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(56, 3, bgSvg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(PlotLeft - 5.0));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(plotTopPlotHeightTerm + 4.0));
			handler.AppendLiteral("' text-anchor='end' font-size='10'>");
			handler.AppendFormatted(EscXml(labels[i]));
			handler.AppendLiteral("</text>");
			stringBuilder8.AppendLine(ref handler);
		}
	}

	private void DrawLinearTicks(double min, double max, bool isX, bool isY2 = false)
	{
		double niceSpacing = NiceSpacing(max - min, 5);
		double minNiceSpacingTerm = Math.Ceiling(min / niceSpacing) * niceSpacing;
		int count = (int)Math.Round((max - minNiceSpacingTerm) / niceSpacing);
		bool reverse;
		if (isX)
		{
			reverse = XReverse;
		}
		else
		{
			reverse = (isY2 ? Y2Reverse : YReverse);
		}
		int decimals = TickDecimals(niceSpacing);
		for (int i = 0; i <= count; i++)
		{
			double niceSpacingTerm = minNiceSpacingTerm + (double)i * niceSpacing;
			if (!(niceSpacingTerm < min) && !(niceSpacingTerm > max + niceSpacing * 1E-09))
			{
				double len = (isX ? PlotWidth : PlotHeight);
				double offset = Map(niceSpacingTerm, min, max, len, ScaleType.Linear, reverse);
				RenderTick(niceSpacingTerm, offset, isX, isY2, decimals);
			}
		}
		if (!ShowMinorTicks && !ShowMinorGrid)
		{
			return;
		}
		int minorSubdivisions = MinorSubdivisions(niceSpacing);
		double niceSpacingOverMinorSubdivisions = niceSpacing / (double)minorSubdivisions;
		double minTerm = Math.Ceiling(min / niceSpacingOverMinorSubdivisions) * niceSpacingOverMinorSubdivisions;
		int count2 = (int)Math.Round((max - minTerm) / niceSpacingOverMinorSubdivisions);
		double len2 = (isX ? PlotWidth : PlotHeight);
		for (int j = 0; j <= count2; j++)
		{
			double computed = minTerm + (double)j * niceSpacingOverMinorSubdivisions;
			if (!(computed < min) && !(computed > max + niceSpacingOverMinorSubdivisions * 1E-09))
			{
				double computed2 = computed / niceSpacingOverMinorSubdivisions;
				if (!(Math.Abs(computed2 - Math.Round(computed2 / (double)minorSubdivisions) * (double)minorSubdivisions) < 1E-06))
				{
					double offset2 = Map(computed, min, max, len2, ScaleType.Linear, reverse);
					RenderMinorTick(offset2, isX, isY2);
				}
			}
		}
	}

	private static int MinorSubdivisions(double spacing)
	{
		if (!(spacing > 0.0) || double.IsInfinity(spacing))
		{
			return 5;
		}
		double y = Math.Floor(Math.Log10(spacing));
		if ((int)Math.Round(spacing / Math.Pow(10.0, y)) != 2)
		{
			return 5;
		}
		return 4;
	}

	private static int TickDecimals(double spacing)
	{
		if (!(spacing > 0.0) || double.IsInfinity(spacing))
		{
			return 0;
		}
		return Math.Clamp((int)Math.Ceiling(0.0 - Math.Log10(spacing) - 1E-09), 0, 15);
	}

	private void DrawLogTicks(double min, double max, bool isX, bool isY2 = false)
	{
		bool reverse;
		if (isX)
		{
			reverse = XReverse;
		}
		else
		{
			reverse = (isY2 ? Y2Reverse : YReverse);
		}
		double len = (isX ? PlotWidth : PlotHeight);
		int minTerm = (int)Math.Floor(Math.Log10(min));
		int count = (int)Math.Ceiling(Math.Log10(max));
		List<double> doubleList = new List<double>();
		for (int i = minTerm; i <= count; i++)
		{
			double computed = Math.Pow(10.0, i);
			if (computed >= min && computed <= max)
			{
				doubleList.Add(computed);
			}
		}
		if (doubleList.Count < 2)
		{
			doubleList.Clear();
			doubleList.Add(min);
			doubleList.Add(max);
			int[] lookupTable = new int[3] { 1, 2, 5 };
			foreach (int lookupTable2 in lookupTable)
			{
				for (int k = minTerm; k <= count; k++)
				{
					double computed2 = (double)lookupTable2 * Math.Pow(10.0, k);
					if (computed2 > min && computed2 < max)
					{
						doubleList.Add(computed2);
					}
				}
			}
			doubleList.Sort();
		}
		foreach (double computed3 in doubleList)
		{
			double offset = Map(computed3, min, max, len, ScaleType.Log10, reverse);
			RenderTick(computed3, offset, isX, isY2);
		}
		if (!ShowMinorTicks && !ShowMinorGrid)
		{
			return;
		}
		for (int l = minTerm; l <= count; l++)
		{
			for (int m = 2; m <= 9; m++)
			{
				double computed4 = (double)m * Math.Pow(10.0, l);
				if (!(computed4 < min) && !(computed4 > max))
				{
					double offset2 = Map(computed4, min, max, len, ScaleType.Log10, reverse);
					RenderMinorTick(offset2, isX, isY2);
				}
			}
		}
	}

	private static double EstimateLabelWidth(string label, double fontSize)
	{
		return (double)label.Length * fontSize * 0.6;
	}

	private double YTitleX()
	{
		return Math.Max(PlotLeft - 5.0 - _yTickLabelWidthPx - 3.0 - 3.0, _cX + 9.0);
	}

	private void ReserveLeftMargin()
	{
		if (!_leftMarginReserved)
		{
			_leftMarginReserved = true;
			GrowLeftMarginForYTitle(Math.Max(MaxYTickLabelWidthPx(), MaxTimeAxisYLabelWidthPx()));
		}
	}

	private void GrowLeftMarginForYTitle(double labelWidthPx)
	{
		if (!(labelWidthPx <= 0.0))
		{
			double labelWidthPxTerm = 5.0 + labelWidthPx + 3.0 + 3.0 + 9.0 + 4.0;
			double cWPadRTerm = _cW - _padR - 40.0;
			if (labelWidthPxTerm > _padL && _padL < cWPadRTerm)
			{
				_padL = Math.Min(labelWidthPxTerm, cWPadRTerm);
			}
		}
	}

	private double MaxYTickLabelWidthPx()
	{
		double estimateLabelWidthLabelTerm = 0.0;
		if (_customTicksY.HasValue)
		{
			string[] labels = _customTicksY.Value.labels;
			foreach (string label in labels)
			{
				estimateLabelWidthLabelTerm = Math.Max(estimateLabelWidthLabelTerm, EstimateLabelWidth(label, 10.0));
			}
			return estimateLabelWidthLabelTerm;
		}
		if (YScale == ScaleType.Linear)
		{
			double valueOrDefault = _minY.GetValueOrDefault();
			double maxY = _maxY ?? 1.0;
			double niceSpacing = NiceSpacing(maxY - valueOrDefault, 5);
			double valueOrDefaultNiceSpacingTerm = Math.Ceiling(valueOrDefault / niceSpacing) * niceSpacing;
			int count = (int)Math.Round((maxY - valueOrDefaultNiceSpacingTerm) / niceSpacing);
			int tickDecimals = TickDecimals(niceSpacing);
			for (int j = 0; j <= count; j++)
			{
				double niceSpacingTerm = valueOrDefaultNiceSpacingTerm + (double)j * niceSpacing;
				if (!(niceSpacingTerm < valueOrDefault) && !(niceSpacingTerm > maxY + niceSpacing * 1E-09))
				{
					estimateLabelWidthLabelTerm = Math.Max(estimateLabelWidthLabelTerm, EstimateLabelWidth(niceSpacingTerm.ToString("F" + tickDecimals, CultureInfo.InvariantCulture), 10.0));
				}
			}
		}
		else
		{
			double minY = _minY ?? 1.0;
			double maxY2 = _maxY ?? 10.0;
			int minYTerm = (int)Math.Floor(Math.Log10(minY));
			int count2 = (int)Math.Ceiling(Math.Log10(maxY2));
			for (int k = minYTerm; k <= count2; k++)
			{
				double computed = Math.Pow(10.0, k);
				if (computed >= minY && computed <= maxY2)
				{
					estimateLabelWidthLabelTerm = Math.Max(estimateLabelWidthLabelTerm, EstimateLabelWidth(computed.ToString("G4", CultureInfo.InvariantCulture), 10.0));
				}
			}
		}
		return estimateLabelWidthLabelTerm;
	}

	private double MaxTimeAxisYLabelWidthPx()
	{
		if (!_minY.HasValue || !_maxY.HasValue)
		{
			return 0.0;
		}
		double estimateLabelWidthMinYTerm = 0.0;
		for (int i = 0; i <= 5; i++)
		{
			estimateLabelWidthMinYTerm = Math.Max(estimateLabelWidthMinYTerm, EstimateLabelWidth((_minY.Value + (double)i * (_maxY.Value - _minY.Value) / 5.0).ToString("G3", CultureInfo.InvariantCulture), 10.0));
		}
		return estimateLabelWidthMinYTerm;
	}

	private void RenderTick(double computed, double offset, bool isX, bool isY2, int decimals = -1)
	{
		bool showGrid = ShowGrid;
		bool showTicks = ShowTicks;
		double tickLength = TickLength;
		string decimalsInvariantCultureTerm = ((decimals >= 0) ? computed.ToString("F" + decimals, CultureInfo.InvariantCulture) : computed.ToString("G4", CultureInfo.InvariantCulture));
		StringBuilder bgSvg;
		StringBuilder.AppendInterpolatedStringHandler handler;
		if (isX)
		{
			double v = PlotLeft + offset;
			if (showGrid)
			{
				bgSvg = _bgSvg;
				StringBuilder stringBuilder = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(69, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(PlotTop));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(PlotBottom));
				handler.AppendLiteral("' stroke='#ddd' stroke-dasharray='4,3' />");
				stringBuilder.AppendLine(ref handler);
			}
			if (showTicks)
			{
				bgSvg = _svg;
				StringBuilder stringBuilder2 = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(PlotTop));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(PlotTop + tickLength));
				handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
				stringBuilder2.AppendLine(ref handler);
				bgSvg = _svg;
				StringBuilder stringBuilder3 = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(PlotBottom - tickLength));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(PlotBottom));
				handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
				stringBuilder3.AppendLine(ref handler);
			}
			bgSvg = _svg;
			StringBuilder stringBuilder4 = bgSvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(59, 3, bgSvg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(v));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(PlotBottom + 15.0));
			handler.AppendLiteral("' text-anchor='middle' font-size='10'>");
			handler.AppendFormatted(decimalsInvariantCultureTerm);
			handler.AppendLiteral("</text>");
			stringBuilder4.AppendLine(ref handler);
			return;
		}
		double plotTopPlotHeightTerm = PlotTop + PlotHeight - offset;
		if (isY2)
		{
			if (showTicks)
			{
				bgSvg = _svg;
				StringBuilder stringBuilder5 = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(PlotRight - tickLength));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(PlotRight));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
				handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
				stringBuilder5.AppendLine(ref handler);
			}
			bgSvg = _svg;
			StringBuilder stringBuilder6 = bgSvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(70, 3, bgSvg);
			handler.AppendLiteral("<text x='");
			handler.AppendFormatted(Fmt(PlotRight + 5.0));
			handler.AppendLiteral("' y='");
			handler.AppendFormatted(Fmt(plotTopPlotHeightTerm + 4.0));
			handler.AppendLiteral("' text-anchor='start' font-size='10' fill='blue'>");
			handler.AppendFormatted(decimalsInvariantCultureTerm);
			handler.AppendLiteral("</text>");
			stringBuilder6.AppendLine(ref handler);
			return;
		}
		if (showGrid)
		{
			bgSvg = _bgSvg;
			StringBuilder stringBuilder7 = bgSvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(69, 4, bgSvg);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(PlotLeft));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(PlotRight));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
			handler.AppendLiteral("' stroke='#ddd' stroke-dasharray='4,3' />");
			stringBuilder7.AppendLine(ref handler);
		}
		if (showTicks)
		{
			bgSvg = _svg;
			StringBuilder stringBuilder8 = bgSvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(PlotLeft));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(PlotLeft + tickLength));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
			handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
			stringBuilder8.AppendLine(ref handler);
			bgSvg = _svg;
			StringBuilder stringBuilder9 = bgSvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(PlotRight - tickLength));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(PlotRight));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(plotTopPlotHeightTerm));
			handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
			stringBuilder9.AppendLine(ref handler);
		}
		_yTickLabelWidthPx = Math.Max(_yTickLabelWidthPx, EstimateLabelWidth(decimalsInvariantCultureTerm, 10.0));
		bgSvg = _svg;
		StringBuilder stringBuilder10 = bgSvg;
		handler = new StringBuilder.AppendInterpolatedStringHandler(56, 3, bgSvg);
		handler.AppendLiteral("<text x='");
		handler.AppendFormatted(Fmt(PlotLeft - 5.0));
		handler.AppendLiteral("' y='");
		handler.AppendFormatted(Fmt(plotTopPlotHeightTerm + 4.0));
		handler.AppendLiteral("' text-anchor='end' font-size='10'>");
		handler.AppendFormatted(decimalsInvariantCultureTerm);
		handler.AppendLiteral("</text>");
		stringBuilder10.AppendLine(ref handler);
	}

	private void RenderMinorTick(double offset, bool isX, bool isY2)
	{
		bool showMinorGrid = ShowMinorGrid;
		bool showMinorTicks = ShowMinorTicks;
		double minorTickLength = MinorTickLength;
		if (isX)
		{
			double v = PlotLeft + offset;
			if (showMinorGrid)
			{
				StringBuilder bgSvg = _bgSvg;
				StringBuilder stringBuilder = bgSvg;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(69, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(PlotTop));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(PlotBottom));
				handler.AppendLiteral("' stroke='#eee' stroke-dasharray='4,3' />");
				stringBuilder.AppendLine(ref handler);
			}
			if (showMinorTicks)
			{
				StringBuilder bgSvg = _svg;
				StringBuilder stringBuilder2 = bgSvg;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(PlotTop));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(PlotTop + minorTickLength));
				handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
				stringBuilder2.AppendLine(ref handler);
				bgSvg = _svg;
				StringBuilder stringBuilder3 = bgSvg;
				handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(PlotBottom - minorTickLength));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(v));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(PlotBottom));
				handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
				stringBuilder3.AppendLine(ref handler);
			}
			return;
		}
		double v2 = PlotTop + PlotHeight - offset;
		if (isY2)
		{
			if (showMinorTicks)
			{
				StringBuilder bgSvg = _svg;
				StringBuilder stringBuilder4 = bgSvg;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
				handler.AppendLiteral("<line x1='");
				handler.AppendFormatted(Fmt(PlotRight - minorTickLength));
				handler.AppendLiteral("' y1='");
				handler.AppendFormatted(Fmt(v2));
				handler.AppendLiteral("' x2='");
				handler.AppendFormatted(Fmt(PlotRight));
				handler.AppendLiteral("' y2='");
				handler.AppendFormatted(Fmt(v2));
				handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
				stringBuilder4.AppendLine(ref handler);
			}
			return;
		}
		if (showMinorGrid)
		{
			StringBuilder bgSvg = _bgSvg;
			StringBuilder stringBuilder5 = bgSvg;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(69, 4, bgSvg);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(PlotLeft));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(PlotRight));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' stroke='#eee' stroke-dasharray='4,3' />");
			stringBuilder5.AppendLine(ref handler);
		}
		if (showMinorTicks)
		{
			StringBuilder bgSvg = _svg;
			StringBuilder stringBuilder6 = bgSvg;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(PlotLeft));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(PlotLeft + minorTickLength));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
			stringBuilder6.AppendLine(ref handler);
			bgSvg = _svg;
			StringBuilder stringBuilder7 = bgSvg;
			handler = new StringBuilder.AppendInterpolatedStringHandler(63, 4, bgSvg);
			handler.AppendLiteral("<line x1='");
			handler.AppendFormatted(Fmt(PlotRight - minorTickLength));
			handler.AppendLiteral("' y1='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' x2='");
			handler.AppendFormatted(Fmt(PlotRight));
			handler.AppendLiteral("' y2='");
			handler.AppendFormatted(Fmt(v2));
			handler.AppendLiteral("' stroke='#333' stroke-width='1' />");
			stringBuilder7.AppendLine(ref handler);
		}
	}

	private static double NiceSpacing(double r, int t)
	{
		if (r <= 1E-15)
		{
			return 1.0;
		}
		double computed = r / (double)(t - 1);
		double y = Math.Floor(Math.Log10(computed));
		double computed2 = computed / Math.Pow(10.0, y);
		return (double)((computed2 < 1.5) ? 1 : ((computed2 < 3.0) ? 2 : ((computed2 < 7.0) ? 5 : 10))) * Math.Pow(10.0, y);
	}

	private (double x, double y) ResolveLegendPosition(string position, double legendW, double legendH, double pad)
	{
		return position switch
		{
			"top-left" => (x: PlotLeft + pad, y: PlotTop + pad), 
			"top-center" => (x: (PlotLeft + PlotRight) / 2.0 - legendW / 2.0, y: PlotTop + pad), 
			"top-right" => (x: PlotRight - legendW, y: PlotTop + pad), 
			"bottom-left" => (x: PlotLeft + pad, y: PlotBottom - legendH - pad + 18.0), 
			"bottom-center" => (x: (PlotLeft + PlotRight) / 2.0 - legendW / 2.0, y: PlotBottom - legendH - pad + 18.0), 
			"bottom-right" => (x: PlotRight - legendW, y: PlotBottom - legendH - pad + 18.0), 
			"middle-left" => (x: PlotLeft + pad, y: (PlotTop + PlotBottom) / 2.0 - legendH / 2.0 + 9.0), 
			"middle-center" => (x: (PlotLeft + PlotRight) / 2.0 - legendW / 2.0, y: (PlotTop + PlotBottom) / 2.0 - legendH / 2.0 + 9.0), 
			"middle-right" => (x: PlotRight - legendW, y: (PlotTop + PlotBottom) / 2.0 - legendH / 2.0 + 9.0), 
			_ => (x: PlotRight - legendW, y: PlotTop + pad + 18.0), 
		};
	}

	private static double[] D(float[] a)
	{
		return Array.ConvertAll(a, (Converter<float, double>)((float f) => f));
	}

	private static double[] D(int[] a)
	{
		return Array.ConvertAll(a, (Converter<int, double>)((int i) => i));
	}

	public static string EscXml(string s)
	{
		return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
			.Replace("\"", "&quot;")
			.Replace("'", "&apos;");
	}

	private static string ResolveColor(string c)
	{
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

	private static string ResolveLineStyle(string style)
	{
		if (string.IsNullOrEmpty(style))
		{
			return "";
		}
		switch (style)
		{
		case "-":
		case "solid":
			return "";
		case "--":
		case "dashed":
			return "8,4";
		case ":":
		case "dotted":
			return "2,3";
		case "-.":
		case "dash-dot":
			return "8,4,2,4";
		default:
			return style;
		}
	}

	private static string Fmt(double v)
	{
		return v.ToString("G6", CultureInfo.InvariantCulture);
	}

	private static string PickRangeFormat(double min, double max)
	{
		double maxAboveMin = max - min;
		if (!(maxAboveMin > 0.0))
		{
			return "G3";
		}
		double minMaxTerm = Math.Max(Math.Abs(min), Math.Abs(max));
		if (minMaxTerm <= 0.0)
		{
			return "G3";
		}
		int ordinal = (int)Math.Floor(Math.Log10(minMaxTerm));
		int ordinal2 = (int)Math.Floor(Math.Log10(maxAboveMin));
		int ordinal3 = Math.Max(3, ordinal - ordinal2 + 2);
		if (ordinal3 > 12)
		{
			ordinal3 = 12;
		}
		return $"G{ordinal3}";
	}
}
