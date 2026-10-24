using System.Xml.Linq;

namespace Ras.MonteCarlo;

public class UniformDistribution : ContinuousDistribution
{
	private double _range;

	public double Min { get; private set; }

	public double Max { get; private set; }

	public static string Name => typeof(UniformDistribution).Name;

	public UniformDistribution(double minimum, double maximum)
	{
		Min = minimum;
		Max = maximum;
		_range = Max - Min;
	}

	public override void LoadDefault()
	{
		Min = 0.8;
		Max = 1.2;
		_range = Max - Min;
	}

	public UniformDistribution()
	{
		Min = 0.0;
		Max = 0.0;
		_range = 0.0;
	}

	public override double Sample(double seed)
	{
		return Min + seed * _range;
	}

	public override XElement ToXElement()
	{
		XElement xElement = new XElement("ContinuousDistribution");
		xElement.Add(new XElement("Min", Min.ToString()));
		xElement.Add(new XElement("Max", Max.ToString()));
		xElement.Add(new XElement("Range", _range.ToString()));
		xElement.Add(new XElement("TruncateMin", TruncateMin.ToString()));
		xElement.Add(new XElement("TruncateMax", TruncateMax.ToString()));
		return xElement;
	}

	public override void LoadData(XElement el)
	{
		Min = (double)el.Element("Min");
		Max = (double)el.Element("Max");
		_range = (double)el.Element("Range");
		TruncateMin = (float)el.Element("TruncateMin");
		TruncateMax = (float)el.Element("TruncateMax");
	}

	public override string ToString()
	{
		return Name + ":\nMin: " + Min + "\nMax: " + Max + "\n";
	}
}
