using System;
using Geospatial.Vectors;

namespace Geospatial.VelocityFields;

public class VectorRungeKutta : RungeKutta
{
	private IStaticVelocityField _field;

	private float _h;

	public IStaticVelocityField Field => _field;

	public float TimeStep => _h;

	public VectorRungeKutta(RungeKuttaOrder order, IStaticVelocityField field, float timestep)
		: base(order)
	{
		_field = field;
		_h = timestep;
	}

	public static Point Follow(RungeKuttaOrder order, IStaticVelocityField field, Point startPos, float totalTime, float timestep)
	{
		VectorRungeKutta vectorRungeKutta = new VectorRungeKutta(order, field, timestep);
		Point point = startPos;
		int count = (int)(totalTime / timestep);
		for (int i = 0; i < count; i++)
		{
			point = vectorRungeKutta.Step(point);
		}
		return point;
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
		Vector velocity = _field.GetVelocity(cur);
		if (velocity == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		Vector velocity2 = _field.GetVelocity(cur.Move(velocity * 0.5f * h));
		if (velocity2 == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		return cur.Move(velocity2 * h);
	}

	private Point Step_RK4(Point cur, float h)
	{
		Vector velocity = _field.GetVelocity(cur);
		if (velocity == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		Vector velocity2 = _field.GetVelocity(cur.Move(velocity * 0.5f * h));
		if (velocity2 == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		Vector velocity3 = _field.GetVelocity(cur.Move(velocity2 * 0.5f * h));
		if (velocity3 == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		Vector velocity4 = _field.GetVelocity(cur.Move(velocity3 * h));
		if (velocity4 == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		return cur.Move((velocity + velocity2 * 2f + velocity3 * 2f + velocity4) * (h / 6f));
	}

	private Point Step_Euler(Point cur, float h)
	{
		Vector velocity = _field.GetVelocity(cur);
		if (velocity == Vector.NullVelocity)
		{
			return Point.NullPoint;
		}
		return cur.Move(velocity * h);
	}
}
