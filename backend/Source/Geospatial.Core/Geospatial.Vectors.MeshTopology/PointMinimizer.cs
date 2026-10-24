using System;

namespace Geospatial.Vectors.MeshTopology;

public class PointMinimizer : FunctionMinimizer
{
	public Func<Point, double> Loss;

	public Point xInitial;

	public double xTolerance = -1.0;

	public double fTolerance = 1E-10;

	public int maxIterations = 100;

	public Extent bounds;

	public int numIterations { get; protected set; }

	public double fOpt { get; private set; }

	public PointMinimizer(Func<Point, double> Loss, Point xInitial, Extent bounds, double absXTolerance = -1.0, double relXTolerance = 0.001)
	{
		this.Loss = Loss;
		this.xInitial = xInitial;
		this.bounds = bounds;
		double widthBoundsTerm = Math.Min(bounds.Width, bounds.Height);
		if (relXTolerance > 0.0)
		{
			xTolerance = relXTolerance * widthBoundsTerm;
		}
		if (absXTolerance > 0.0)
		{
			xTolerance = Math.Min(xTolerance, absXTolerance);
		}
	}

	public bool Minimize(out Point xOpt)
	{
		return MinimizeCyclic(out xOpt);
	}

	public bool MinimizeCyclic(out Point xOpt)
	{
		xOpt = xInitial;
		Vector vector = new Vector(1.0, 0.0);
		Vector vector2 = new Vector(0.0, 1.0);
		numIterations = 0;
		while (numIterations < maxIterations && (false || MinimizeAlongVector(vector, ref xOpt) || MinimizeAlongVector(vector2, ref xOpt)))
		{
			numIterations++;
		}
		if (numIterations == maxIterations)
		{
			stopCondition = StopCondition.StopMaxIterations;
		}
		return xOpt != xInitial;
	}

	public bool MinimizePowell(out Point xOpt)
	{
		xOpt = xInitial;
		Vector vector = new Vector(1.0, 0.0);
		Vector vector2 = new Vector(0.0, 1.0);
		numIterations = 0;
		while (numIterations < maxIterations)
		{
			Point point = xOpt;
			bool minimizeAlongVector = false || MinimizeAlongVector(vector, ref xOpt);
			Point other = xOpt;
			if (!minimizeAlongVector && !MinimizeAlongVector(vector2, ref xOpt))
			{
				break;
			}
			Vector vector3 = (xOpt - point).UnitVector();
			if (other.DistanceTo(point) > xOpt.DistanceTo(other))
			{
				vector = vector3;
			}
			else
			{
				vector2 = vector3;
			}
			numIterations++;
		}
		if (numIterations == maxIterations)
		{
			stopCondition = StopCondition.StopMaxIterations;
		}
		return xOpt != xInitial;
	}

	public bool MinimizeAlongVector(Vector vector, ref Point xOpt)
	{
		Point xFoot = xOpt;
		vector = vector.UnitVector();
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		if (vector.DeltaX > 0.0)
		{
			maxValue = Math.Min(maxValue, (bounds.MaxX - xOpt.X) / vector.DeltaX);
			minValue = Math.Max(minValue, (bounds.MinX - xOpt.X) / vector.DeltaX);
		}
		else if (vector.DeltaX < 0.0)
		{
			maxValue = Math.Min(maxValue, (bounds.MinX - xOpt.X) / vector.DeltaX);
			minValue = Math.Max(minValue, (bounds.MaxX - xOpt.X) / vector.DeltaX);
		}
		if (vector.DeltaY > 0.0)
		{
			maxValue = Math.Min(maxValue, (bounds.MaxY - xOpt.Y) / vector.DeltaY);
			minValue = Math.Max(minValue, (bounds.MinY - xOpt.Y) / vector.DeltaY);
		}
		else if (vector.DeltaY < 0.0)
		{
			maxValue = Math.Min(maxValue, (bounds.MinY - xOpt.Y) / vector.DeltaY);
			minValue = Math.Max(minValue, (bounds.MaxY - xOpt.Y) / vector.DeltaY);
		}
		UnivariateMinimizer univariateMinimizer = new UnivariateMinimizer(VectorLoss, 0.0, minValue, maxValue, xTolerance);
		bool minimize = univariateMinimizer.Minimize(out var xOpt2);
		stopCondition = univariateMinimizer.stopCondition;
		fOpt = univariateMinimizer.fOpt;
		if (minimize)
		{
			xOpt = xFoot + xOpt2 * vector;
		}
		return minimize;
		double VectorLoss(double s)
		{
			return Loss(xFoot + s * vector);
		}
	}
}
