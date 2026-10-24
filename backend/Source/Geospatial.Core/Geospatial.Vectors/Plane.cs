namespace Geospatial.Vectors;

public struct Plane
{
	public double A = 0.0;

	public double B = 0.0;

	public double C = 0.0;

	public double D = 0.0;

	public double E = 0.0;

	public double F = 0.0;

	public double G = 0.0;

	public double H = 0.0;

	public double dZ_dX => (0.0 - A) / C;

	public double dM_dX => (0.0 - E) / G;

	public Plane(IPoint4D pt0, IPoint4D pt1, IPoint4D pt2)
	{
		IVector4D delta = pt0.To(pt1);
		IVector4D delta2 = pt0.To(pt2);
		SetCoeffientsPtDeltaDelta(pt0, delta, delta2);
	}

	public void SetCoeffientsPtDeltaDelta(IPoint4D pt0, IVector4D delta01, IVector4D delta02)
	{
		if (pt0.HasZ)
		{
			A = delta01.DeltaY * delta02.DeltaZ - delta01.DeltaZ * delta02.DeltaY;
			B = delta01.DeltaZ * delta02.DeltaX - delta01.DeltaX * delta02.DeltaZ;
			C = delta01.DeltaX * delta02.DeltaY - delta01.DeltaY * delta02.DeltaX;
			D = (0.0 - A) * pt0.X - B * pt0.Y - C * pt0.Z;
		}
		if (pt0.HasM)
		{
			E = delta01.DeltaY * delta02.DeltaM - delta01.DeltaM * delta02.DeltaY;
			F = delta01.DeltaM * delta02.DeltaX - delta01.DeltaX * delta02.DeltaM;
			G = C;
			H = (0.0 - E) * pt0.X - F * pt0.Y - G * pt0.M;
		}
	}

	public void SetForFlatBottom(double z, double m)
	{
		A = 0.0;
		B = 0.0;
		C = -1.0;
		D = z;
		E = 0.0;
		F = 0.0;
		G = -1.0;
		H = m;
	}

	public double Z(double X, double y)
	{
		if (C == 0.0)
		{
			return 0.0;
		}
		return (0.0 - D - A * X - B * y) / C;
	}

	public double M(double X, double y)
	{
		if (G == 0.0)
		{
			return 0.0;
		}
		return (0.0 - H - E * X - F * y) / G;
	}

	public bool Equal(Plane plane)
	{
		if (A != plane.A || B != plane.B || C != plane.C || D != plane.D)
		{
			return false;
		}
		if (E != plane.E || F != plane.F || G != plane.G || H != plane.H)
		{
			return false;
		}
		return true;
	}
}
