namespace RasMapperLib;

public struct PlaneM
{
	public double A;

	public double B;

	public double C;

	public double D;

	public double E;

	public double F;

	public double G;

	public double H;

	public void SetCoeffientsPtPtPt(PointM pt0, PointM pt1, PointM pt2)
	{
		PointM delta = pt0.Delta(pt1);
		PointM delta2 = pt0.Delta(pt2);
		SetCoeffientsPtDeltaDelta(pt0, delta, delta2);
	}

	public void SetCoeffientsPtDeltaDelta(PointM pt0, PointM delta01, PointM delta02)
	{
		A = delta01.Y * delta02.Z - delta01.Z * delta02.Y;
		B = delta01.Z * delta02.X - delta01.X * delta02.Z;
		C = delta01.X * delta02.Y - delta01.Y * delta02.X;
		D = (0.0 - A) * pt0.X - B * pt0.Y - C * pt0.Z;
		if (!double.IsNaN(pt0.M))
		{
			E = delta01.Y * delta02.M - delta01.M * delta02.Y;
			F = delta01.M * delta02.X - delta01.X * delta02.M;
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

	public double dZ_dX()
	{
		return (0.0 - A) / C;
	}

	public double dM_dX()
	{
		return (0.0 - E) / G;
	}

	public bool Equal(PlaneM plane)
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
