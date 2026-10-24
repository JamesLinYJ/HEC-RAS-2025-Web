using System;
using System.Xml.Linq;

namespace Ras.MonteCarlo;

public class LogNormalDistribution : ContinuousDistribution
{
	public double Mean { get; private set; }

	public double StandardDeviation { get; private set; }

	public static string Name => typeof(LogNormalDistribution).Name;

	public LogNormalDistribution(double mean, double standardDeviation)
	{
		Mean = mean;
		StandardDeviation = standardDeviation;
	}

	public override void LoadDefault()
	{
		Mean = 1.0;
		StandardDeviation = 1.0;
	}

	public LogNormalDistribution()
	{
		Mean = 0.0;
		StandardDeviation = 0.0;
	}

	public override double Sample(double seed)
	{
		double z = NormalDistribution.GetZ(seed);
		return Math.Exp(Mean + StandardDeviation * z);
	}

	public override XElement ToXElement()
	{
		XElement xElement = new XElement("ContinuousDistribution");
		xElement.Add(new XElement("Mean", Mean.ToString()));
		xElement.Add(new XElement("StdDev", StandardDeviation.ToString()));
		xElement.Add(new XElement("TruncateMin", TruncateMin.ToString()));
		xElement.Add(new XElement("TruncateMax", TruncateMax.ToString()));
		return xElement;
	}

	public override void LoadData(XElement el)
	{
		Mean = (double)el.Element("Mean");
		StandardDeviation = (double)el.Element("StdDev");
		TruncateMin = (float)el.Element("TruncateMin");
		TruncateMax = (float)el.Element("TruncateMax");
	}

	public override string ToString()
	{
		return Name + ":\nMean: " + Mean + "\nLog Standard Dev: " + StandardDeviation + "\n";
	}
}
