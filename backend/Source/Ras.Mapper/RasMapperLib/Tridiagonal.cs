using System.Linq;

namespace RasMapperLib;

public class Tridiagonal
{
	public static bool SolveSingle(float[] a, float[] b, float[] c, float[] r, ref float[] u, int n = -1)
	{
		if ((a == null) | (b == null) | (c == null) | (r == null))
		{
			return false;
		}
		if (n == -1)
		{
			n = a.Count();
		}
		if (n == 0 || b.Count() < n || c.Count() < n || r.Count() < n)
		{
			return false;
		}
		if ((double)b[0] == 0.0)
		{
			return false;
		}
		checked
		{
			if (u == null || u.Count() < n)
			{
				u = new float[n - 1 + 1];
			}
			float[] floatValues = new float[n - 1 + 1];
			float computed = b[0];
			u[0] = r[0] / computed;
			int count = n - 1;
			for (int i = 1; i <= count; i++)
			{
				floatValues[i] = c[i - 1] / computed;
				computed = b[i] - a[i] * floatValues[i];
				if ((double)computed == 0.0)
				{
					return false;
				}
				u[i] = (r[i] - a[i] * u[i - 1]) / computed;
			}
			for (int j = n - 2; j >= 0; j += -1)
			{
				u[j] -= floatValues[j + 1] * u[j + 1];
			}
			return true;
		}
	}

	public static bool SolveDouble(double[] a, double[] b, double[] c, double[] r, ref double[] u, int n = -1)
	{
		if ((a == null) | (b == null) | (c == null) | (r == null))
		{
			return false;
		}
		if (n == -1)
		{
			n = a.Count();
		}
		if (n == 0 || b.Count() < n || c.Count() < n || r.Count() < n)
		{
			return false;
		}
		if (b[0] == 0.0)
		{
			return false;
		}
		checked
		{
			if (u == null || u.Count() < n)
			{
				u = new double[n - 1 + 1];
			}
			double[] doubleValues = new double[n - 1 + 1];
			double computed = b[0];
			u[0] = r[0] / computed;
			int count = n - 1;
			for (int i = 1; i <= count; i++)
			{
				doubleValues[i] = c[i - 1] / computed;
				computed = b[i] - a[i] * doubleValues[i];
				if (computed == 0.0)
				{
					return false;
				}
				u[i] = (r[i] - a[i] * u[i - 1]) / computed;
			}
			for (int j = n - 2; j >= 0; j += -1)
			{
				u[j] -= doubleValues[j + 1] * u[j + 1];
			}
			return true;
		}
	}
}
