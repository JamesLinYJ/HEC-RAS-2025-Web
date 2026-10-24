using System.Numerics;

namespace Utility.NumericalAnalysis;

public readonly record struct EndpointPolynomialFit<T>(T Bulge, T Twist) where T : IFloatingPointIeee754<T>
{
	public T Evaluate(T y0, T y1, T t)
	{
		T y1AboveY0 = y1 - y0;
		return y0 + y1AboveY0 * t + t * (t - T.One) * (Bulge + Twist * (T.CreateChecked(2) * t - T.One));
	}
}
