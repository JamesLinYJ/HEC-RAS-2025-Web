using System;
using Geospatial.Vectors;

namespace Geospatial.VelocityFields;

public class RasterRungeKutta : RungeKutta
{
	private float _h;

	private Vector[,] _velocities;

	public float TimeStep => _h;

	public RasterRungeKutta(RungeKuttaOrder order, Vector[,] velocities, float timestep)
		: base(order)
	{
		_velocities = velocities;
		_h = timestep;
	}

	private Vector GetVelocity(Point input)
	{
		int inputTerm = (int)input.X;
		int inputTerm2 = (int)input.Y;
		if (inputTerm < 0 || inputTerm >= _velocities.GetLength(1))
		{
			return Vector.NullVelocity;
		}
		if (inputTerm2 < 0 || inputTerm2 >= _velocities.GetLength(0))
		{
			return Vector.NullVelocity;
		}
		return _velocities[inputTerm2, inputTerm];
	}

	public override Point Step(Point cur)
	{
		if (_order == RungeKuttaOrder.RK2)
		{
			return Step_RK2(cur, _h);
		}
		if (_order == RungeKuttaOrder.RK4)
		{
			return Step_RK4(cur, _h);
		}
		if (_order == RungeKuttaOrder.RK1)
		{
			return Step_Euler(cur, _h);
		}
		throw new NotImplementedException();
	}

	private Point Step_RK2(Point cur, float h)
	{
		Vector velocity = GetVelocity(cur);
		if (velocity == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		Vector velocity2 = GetVelocity(cur.Move(velocity * 0.5f * h));
		if (velocity2 == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		return cur.Move(velocity2 * h);
	}

	private Point Step_RK4(Point cur, float h)
	{
		Vector velocity = GetVelocity(cur);
		if (velocity == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		Vector velocity2 = GetVelocity(cur.Move(velocity * 0.5f * h));
		if (velocity2 == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		Vector velocity3 = GetVelocity(cur.Move(velocity2 * 0.5f * h));
		if (velocity3 == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		Vector velocity4 = GetVelocity(cur.Move(velocity3 * h));
		if (velocity4 == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		return cur.Move((velocity + velocity2 * 2f + velocity3 * 2f + velocity4) * (h / 6f));
	}

	private Point Step_Euler(Point cur, float h)
	{
		Vector velocity = GetVelocity(cur);
		if (velocity == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		return cur.Move(velocity * h);
	}
}
