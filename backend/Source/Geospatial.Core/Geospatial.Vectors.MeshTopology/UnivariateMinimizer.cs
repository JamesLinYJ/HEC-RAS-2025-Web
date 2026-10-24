using System;

namespace Geospatial.Vectors.MeshTopology;

public class UnivariateMinimizer : FunctionMinimizer
{
	public Func<double, double> Loss;

	public double xInitial;

	public double xLower;

	public double xUpper;

	public double xTolerance = -1.0;

	public double fTolerance = 1E-10;

	public double xOptLow { get; private set; }

	public double xOptHigh { get; private set; }

	public double fOpt { get; private set; }

	public UnivariateMinimizer(Func<double, double> Loss, double xInitial, double xLower, double xUpper, double absXTolerance = -1.0, double relXTolerance = 0.001)
	{
		this.Loss = Loss;
		this.xInitial = xInitial;
		this.xLower = xLower;
		this.xUpper = xUpper;
		if (relXTolerance > 0.0)
		{
			xTolerance = relXTolerance * (xUpper - xLower);
		}
		else
		{
			xTolerance = xUpper - xLower;
		}
		if (absXTolerance > 0.0)
		{
			xTolerance = Math.Min(xTolerance, absXTolerance);
		}
	}

	public bool Minimize(out double xOpt)
	{
		xOpt = xInitial;
		double xUpperXLowerTerm = 0.01 * (xUpper - xLower);
		double fOpt2 = Loss(xInitial);
		double xInitial2 = xInitial;
		double loss = fOpt2;
		double xInitial2Term;
		double xInitial2Term2 = (xInitial2Term = xInitial2);
		double fOpt3;
		double fOpt4 = (fOpt3 = loss);
		bool condition = false;
		if (xInitial2Term2 != xLower)
		{
			while (true)
			{
				xInitial2Term2 = Math.Max(xLower, xInitial2 - xUpperXLowerTerm);
				fOpt4 = Loss(xInitial2Term2);
				if (!(fOpt4 < loss))
				{
					break;
				}
				xInitial2Term = xInitial2;
				fOpt3 = loss;
				condition = true;
				xInitial2 = xInitial2Term2;
				loss = fOpt4;
				if (xInitial2Term2 == xLower)
				{
					break;
				}
				xUpperXLowerTerm *= 2.0;
			}
		}
		if (xInitial2Term < xInitial && fOpt2 - fOpt3 <= fTolerance)
		{
			xInitial2Term = xInitial;
			fOpt3 = fOpt2;
		}
		if (!condition)
		{
			if (xInitial2Term == xUpper)
			{
				condition = true;
			}
			else
			{
				while (true)
				{
					xInitial2Term = Math.Min(xUpper, xInitial2 + xUpperXLowerTerm);
					fOpt3 = Loss(xInitial2Term);
					if (!(fOpt3 < loss))
					{
						break;
					}
					xInitial2Term2 = xInitial2;
					fOpt4 = loss;
					xInitial2 = xInitial2Term;
					loss = fOpt3;
					if (xInitial2Term == xUpper)
					{
						break;
					}
					xUpperXLowerTerm *= 2.0;
				}
				if (xInitial2Term2 > xInitial && fOpt2 - fOpt4 <= fTolerance)
				{
					xInitial2Term2 = xInitial;
					fOpt4 = fOpt2;
				}
			}
		}
		double computed = xInitial2Term - xInitial2Term2;
		stopCondition = StopCondition.StopXTolerance;
		double fOpt4FOpt5Term = Math.Min(fOpt4, fOpt3);
		double xOpt2;
		double xOpt3;
		double fOpt5;
		double fOpt6;
		if (computed > xTolerance)
		{
			double computed2 = (Math.Sqrt(5.0) - 1.0) / 2.0;
			double computed3 = (3.0 - Math.Sqrt(5.0)) / 2.0;
			int xToleranceTerm = (int)Math.Ceiling(Math.Log(xTolerance / computed) / Math.Log(computed2));
			xOpt2 = xInitial2Term2 + computed3 * computed;
			xOpt3 = xInitial2Term2 + computed2 * computed;
			fOpt5 = Loss(xOpt2);
			fOpt6 = Loss(xOpt3);
			for (int i = 0; i < xToleranceTerm - 1; i++)
			{
				double fOpt4FOpt5Term2 = Math.Max(Math.Max(fOpt4, fOpt5), Math.Max(fOpt6, fOpt3));
				fOpt4FOpt5Term = Math.Min(Math.Min(fOpt4, fOpt5), Math.Min(fOpt6, fOpt3));
				if (fOpt4FOpt5Term2 - fOpt4FOpt5Term <= fTolerance)
				{
					stopCondition = StopCondition.StopFTolerance;
					break;
				}
				double fOpt5AboveFOpt6 = fOpt5 - fOpt6;
				if (fOpt2 - Math.Min(fOpt5, fOpt6) <= fTolerance)
				{
					if (xInitial < xOpt2)
					{
						fOpt5AboveFOpt6 = -1.0;
					}
					else if (xInitial > xOpt3)
					{
						fOpt5AboveFOpt6 = 1.0;
					}
				}
				if (fOpt5AboveFOpt6 < 0.0)
				{
					xInitial2Term = xOpt3;
					fOpt3 = fOpt6;
					xOpt3 = xOpt2;
					fOpt6 = fOpt5;
					computed *= computed2;
					xOpt2 = xInitial2Term2 + computed3 * computed;
					fOpt5 = Loss(xOpt2);
				}
				else
				{
					xInitial2Term2 = xOpt2;
					xOpt2 = xOpt3;
					fOpt4 = fOpt5;
					fOpt5 = fOpt6;
					computed *= computed2;
					xOpt3 = xInitial2Term2 + computed2 * computed;
					fOpt6 = Loss(xOpt3);
				}
			}
			if (stopCondition == StopCondition.StopXTolerance)
			{
				if (fOpt2 - fOpt4FOpt5Term > fTolerance)
				{
					double fOpt5AboveFOpt62 = fOpt5 - fOpt6;
					if (fOpt5AboveFOpt62 < 0.0 - fTolerance)
					{
						xInitial2Term = xOpt3;
						fOpt3 = fOpt6;
					}
					else if (fOpt5AboveFOpt62 > fTolerance)
					{
						xInitial2Term2 = xOpt2;
						fOpt4 = fOpt5;
					}
					else
					{
						stopCondition = StopCondition.StopFTolerance;
					}
				}
				else
				{
					stopCondition = StopCondition.StopFTolerance;
				}
			}
		}
		else
		{
			xOpt2 = xInitial2Term2;
			fOpt5 = fOpt4;
			xOpt3 = xInitial2Term;
			fOpt6 = fOpt3;
		}
		xOptLow = xInitial2Term2;
		xOptHigh = xInitial2Term;
		if (fOpt2 - fOpt4FOpt5Term < fTolerance)
		{
			xOpt = xInitial;
			fOpt = fOpt2;
		}
		else if (xInitial2Term2 == xLower)
		{
			xOpt = xInitial2Term2;
			fOpt = fOpt4;
		}
		else if (xInitial2Term == xUpper)
		{
			xOpt = xInitial2Term;
			fOpt = fOpt3;
		}
		else if (fOpt5 < fOpt6)
		{
			xOpt = xOpt2;
			fOpt = fOpt5;
		}
		else
		{
			xOpt = xOpt3;
			fOpt = fOpt6;
		}
		return xOpt != xInitial;
	}
}
