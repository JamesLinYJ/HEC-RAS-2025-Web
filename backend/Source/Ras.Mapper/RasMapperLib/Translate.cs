namespace RasMapperLib;

public class Translate : Transform
{
	private double _dx;

	private double _dy;

	private double _dz;

	private double _dm;

	public double DeltaX => _dx;

	public double DeltaY => _dy;

	public double DeltaZ => _dz;

	public double DeltaM => _dm;

	public Translate(double dx, double dy)
		: this(dx, dy, double.NaN, double.NaN)
	{
	}

	public Translate(double dx, double dy, double dz)
		: this(dx, dy, dz, double.NaN)
	{
	}

	public Translate(double dx, double dy, double dz, double dm)
	{
		_dx = dx;
		_dy = dy;
		_dz = dz;
		_dm = dm;
	}

	public override void TransformPointM(ref PointM PointM)
	{
		if (!double.IsNaN(_dx) && !double.IsNaN(PointM.X))
		{
			PointM.X += _dx;
		}
		if (!double.IsNaN(_dy) && !double.IsNaN(PointM.Y))
		{
			PointM.Y += _dy;
		}
		if (!double.IsNaN(_dz) && !double.IsNaN(PointM.Z))
		{
			PointM.Z += _dz;
		}
		if (!double.IsNaN(_dm) && !double.IsNaN(PointM.M))
		{
			PointM.M += _dm;
		}
	}

	public override Transform InverseTransform()
	{
		return new Translate(0.0 - _dx, 0.0 - _dy, 0.0 - _dz, 0.0 - _dm);
	}
}
