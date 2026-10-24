using System;
using System.Collections.Generic;
using System.Linq;

namespace Ras.Layers;

public class MetPairedData
{
	private readonly List<float> _xValues;

	private readonly List<float> _yValues;

	public int TotalCount => _xValues.Count;

	public List<float> XValues => _xValues;

	public List<float> YValues => _yValues;

	public string XUnitString { get; }

	public string XDataType { get; }

	public string YUnitString { get; }

	public string YDataType { get; }

	public MetPairedData(IList<float> xvals, IList<float> yvals, string xUnits, string xDataType, string yUnits, string yDataType)
	{
		if (xvals == null || yvals == null)
		{
			throw new ArgumentNullException();
		}
		_xValues = xvals.ToList();
		_yValues = yvals.ToList();
		XUnitString = xUnits;
		XDataType = xDataType;
		YUnitString = yUnits;
		YDataType = yDataType;
	}
}
