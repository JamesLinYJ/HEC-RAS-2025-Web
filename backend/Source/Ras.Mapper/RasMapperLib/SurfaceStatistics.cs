using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class SurfaceStatistics
{
	private double _minimum;

	private double _maximum;

	private double _mean;

	private double _standardDeviation;

	private double _area;

	private double[] _quantiles;

	public double Maximum => _maximum;

	public double Minimum => _minimum;

	public double Mean => _mean;

	public double StandardDeviation => _standardDeviation;

	public double Area => _area;

	public IList<double> Quantiles => Array.AsReadOnly(_quantiles);

	public SurfaceStatistics(double maximum, double minimum, double mean, double standardDeviation, double area, double[] quantiles)
	{
		_maximum = maximum;
		_minimum = minimum;
		_mean = mean;
		_standardDeviation = standardDeviation;
		_area = area;
		if (quantiles == null)
		{
			_quantiles = new double[1];
			_quantiles[0] = _minimum;
		}
		else
		{
			_quantiles = new double[checked(quantiles.Count() - 1 + 1)];
			Array.Copy(quantiles, _quantiles, quantiles.Count());
		}
	}

	public static string XMLNodeName()
	{
		return "SurfaceStatistics";
	}

	public void XMLSave(XmlDocument doc, XmlElement sourceElement)
	{
		XmlNode xmlNode = doc.CreateElement(XMLNodeName());
		sourceElement.AppendChild(xmlNode);
		XML.AddTextElementSanitized(doc, xmlNode, "Maximum", _maximum.ToString());
		XML.AddTextElementSanitized(doc, xmlNode, "Minimum", _minimum.ToString());
		XML.AddTextElementSanitized(doc, xmlNode, "Mean", _mean.ToString());
		XML.AddTextElementSanitized(doc, xmlNode, "StandardDeviation", _standardDeviation.ToString());
		XML.AddTextElementSanitized(doc, xmlNode, "Area", _area.ToString());
		XML.AddTextElementSanitized(doc, xmlNode, "Quantiles", Form.ArrayToCSS(_quantiles));
	}

	public static SurfaceStatistics XMLLoad(XmlNode statisticsNode)
	{
		double maximum = XML.DoubleTextElement(statisticsNode, "Maximum");
		double minimum = XML.DoubleTextElement(statisticsNode, "Minimum");
		double mean = XML.DoubleTextElement(statisticsNode, "Mean");
		double standardDeviation = XML.DoubleTextElement(statisticsNode, "StandardDeviation");
		double area = XML.DoubleTextElement(statisticsNode, "Area");
		double[] quantiles = Form.CSSToDoubleArray(XML.StringTextElement(statisticsNode, "Quantiles"));
		return new SurfaceStatistics(maximum, minimum, mean, standardDeviation, area, quantiles);
	}
}
