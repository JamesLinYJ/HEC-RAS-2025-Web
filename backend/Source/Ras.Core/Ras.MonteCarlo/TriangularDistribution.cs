using System;
using System.Xml.Linq;

namespace Ras.MonteCarlo;

public class TriangularDistribution : ContinuousDistribution
{
	public double Min { get; private set; }

	public double Max { get; private set; }

	public double Mode { get; private set; }

	public static string Name => typeof(TriangularDistribution).Name;

	public TriangularDistribution(double minimum, double maximum, double mostLikelyValue)
	{
		Min = minimum;
		Max = maximum;
		Mode = mostLikelyValue;
	}

	public override void LoadDefault()
	{
		Min = 0.8;
		Mode = 1.0;
		Max = 1.2;
	}

	public TriangularDistribution()
	{
		Min = 0.0;
		Max = 0.0;
		Mode = 0.0;
	}

	public override double Sample(double seed)
	{
		double modeAboveMin = Mode - Min;
		double maxAboveMode = Max - Mode;
		if (seed <= 0.0)
		{
			return Min;
		}
		if (seed < modeAboveMin / (Max - Min))
		{
			return Min + Math.Sqrt(seed * (Max - Min) * modeAboveMin);
		}
		if (seed < 1.0)
		{
			return Max - Math.Sqrt((1.0 - seed) * (Max - Min) * maxAboveMode);
		}
		return Max;
	}

	public override XElement ToXElement()
	{
		XElement xElement = new XElement("ContinuousDistribution");
		xElement.Add(new XElement("Min", Min.ToString()));
		xElement.Add(new XElement("Max", Max.ToString()));
		xElement.Add(new XElement("Peak", Mode.ToString()));
		xElement.Add(new XElement("TruncateMin", TruncateMin.ToString()));
		xElement.Add(new XElement("TruncateMax", TruncateMax.ToString()));
		return xElement;
	}

	public override void LoadData(XElement el)
	{
		Min = (double)el.Element("Min");
		Max = (double)el.Element("Max");
		Mode = (double)el.Element("Peak");
		TruncateMin = (float)el.Element("TruncateMin");
		TruncateMax = (float)el.Element("TruncateMax");
	}

	public override string ToString()
	{
		return Name + ":\nMin: " + Min + "\nMax: " + Max + "\n";
	}
}
