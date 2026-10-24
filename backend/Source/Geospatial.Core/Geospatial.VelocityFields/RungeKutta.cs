using Geospatial.Vectors;

namespace Geospatial.VelocityFields;

public abstract class RungeKutta
{
	protected RungeKuttaOrder _order;

	public RungeKuttaOrder Order => _order;

	public abstract Point Step(Point cur);

	protected RungeKutta(RungeKuttaOrder order)
	{
		_order = order;
	}
}
