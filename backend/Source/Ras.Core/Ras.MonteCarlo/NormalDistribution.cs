using System;
using System.Xml.Linq;

namespace Ras.MonteCarlo;

public class NormalDistribution : ContinuousDistribution
{
	private int _sampleSize;

	private const double Epsilon = 1E-15;

	private const double C0 = 2.515517;

	private const double C1 = 0.802853;

	private const double C2 = 0.010328;

	private const double D1 = 1.432788;

	private const double D2 = 0.189269;

	private const double D3 = 0.001308;

	private string _desc;

	public double Mean { get; private set; }

	public double StandardDeviation { get; private set; }

	public static string Name => typeof(NormalDistribution).Name;

	public NormalDistribution(double mean, double standardDeviation, int sampleSize)
	{
		Mean = mean;
		StandardDeviation = standardDeviation;
		_sampleSize = sampleSize;
	}

	public override void LoadDefault()
	{
		Mean = 1.0;
		StandardDeviation = 0.1;
		_sampleSize = 1;
	}

	public NormalDistribution()
	{
		Mean = 0.0;
		StandardDeviation = 0.0;
		_sampleSize = 1;
	}

	public override double Sample(double seed)
	{
		if (seed <= 0.0)
		{
			seed = 1E-15;
		}
		else if (seed >= 1.0)
		{
			seed = 0.999999999999999;
		}
		else if (Math.Abs(seed - 0.5) < 1E-15)
		{
			return Mean;
		}
		double seedTerm;
		double sign;
		if (seed < 0.5)
		{
			seedTerm = seed;
			sign = -1.0;
		}
		else
		{
			seedTerm = 1.0 - seed;
			sign = 1.0;
		}
		double computed = Math.Sqrt(Math.Log(1.0 / (seedTerm * seedTerm)));
		return sign * (computed - (2.515517 + 0.802853 * computed + 0.010328 * Math.Pow(computed, 2.0)) / (1.0 + 1.432788 * computed + 0.189269 * Math.Pow(computed, 2.0) + 0.001308 * Math.Pow(computed, 3.0))) * StandardDeviation + Mean;
	}

	public static double GetZ(double seed)
	{
		double computed = 0.0;
		double unity = 1.0;
		if (seed <= 0.0)
		{
			seed = 1E-15;
		}
		else if (seed >= 1.0)
		{
			seed = 0.999999999999999;
		}
		else if (Math.Abs(seed - 0.5) < 1E-15)
		{
			return computed;
		}
		double seedTerm;
		double sign;
		if (seed < 0.5)
		{
			seedTerm = seed;
			sign = -1.0;
		}
		else
		{
			seedTerm = 1.0 - seed;
			sign = 1.0;
		}
		double computed2 = Math.Sqrt(Math.Log(1.0 / (seedTerm * seedTerm)));
		return sign * (computed2 - (2.515517 + 0.802853 * computed2 + 0.010328 * Math.Pow(computed2, 2.0)) / (1.0 + 1.432788 * computed2 + 0.189269 * Math.Pow(computed2, 2.0) + 0.001308 * Math.Pow(computed2, 3.0))) * unity + computed;
	}

	public override XElement ToXElement()
	{
		XElement xElement = new XElement("ContinuousDistribution");
		xElement.Add(new XElement("Mean", Mean.ToString()));
		xElement.Add(new XElement("StdDev", StandardDeviation.ToString()));
		xElement.Add(new XElement("SampleSize", _sampleSize.ToString()));
		xElement.Add(new XElement("TruncateMin", TruncateMin.ToString()));
		xElement.Add(new XElement("TruncateMax", TruncateMax.ToString()));
		return xElement;
	}

	public override void LoadData(XElement el)
	{
		Mean = (double)el.Element("Mean");
		StandardDeviation = (double)el.Element("StdDev");
		_sampleSize = (int)el.Element("SampleSize");
		TruncateMin = (float)el.Element("TruncateMin");
		TruncateMax = (float)el.Element("TruncateMax");
	}

	public override string ToString()
	{
		return _desc ?? (_desc = Name + ":" + Environment.NewLine + "Mean: " + Mean + Environment.NewLine + "Standard Dev: " + StandardDeviation + Environment.NewLine);
	}
}
