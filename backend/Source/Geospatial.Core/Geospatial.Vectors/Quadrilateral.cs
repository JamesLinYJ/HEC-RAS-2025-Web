using System.Numerics;
using Utility;

namespace Geospatial.Vectors;

public struct Quadrilateral(Point topLeft, Point topRight, Point lowerRight, Point lowerLeft)
{
	public Point TopLeft = topLeft;

	public Point TopRight = topRight;

	public Point BottomRight = lowerRight;

	public Point BottomLeft = lowerLeft;

	public void InterpolationWeights<T>(Point p, out T topLeftWeight, out T topRightWeight, out T bottomRightWeight, out T bottomLeftWeight) where T : IBinaryFloatingPointIeee754<T>
	{
		ComputationCoordinates<T>(p, out var cx, out var cy);
		topLeftWeight = WeightedValue(cx, cy, T.One, T.Zero, T.Zero, T.Zero);
		topRightWeight = WeightedValue(cx, cy, T.Zero, T.One, T.Zero, T.Zero);
		bottomRightWeight = WeightedValue(cx, cy, T.Zero, T.Zero, T.One, T.Zero);
		bottomLeftWeight = T.One - topLeftWeight - topRightWeight - bottomRightWeight;
	}

	public void ComputationCoordinates<T>(Point p, out T cx, out T cy) where T : IBinaryFloatingPointIeee754<T>
	{
		T saturating = T.CreateSaturating(1E-06);
		cx = T.Zero;
		cy = T.Zero;
		int ordinal = 0;
		T saturating2 = T.CreateSaturating(p.X);
		T saturating3 = T.CreateSaturating(p.Y);
		T v = T.CreateChecked(TopLeft.X);
		T v2 = T.CreateChecked(TopRight.X);
		T v3 = T.CreateChecked(BottomRight.X);
		T v4 = T.CreateChecked(BottomLeft.X);
		T v5 = T.CreateChecked(TopLeft.Y);
		T v6 = T.CreateChecked(TopRight.Y);
		T v7 = T.CreateChecked(BottomRight.Y);
		T v8 = T.CreateChecked(BottomLeft.Y);
		T x;
		T x2;
		do
		{
			T weightedValue = WeightedValue(cx, cy, v, v2, v3, v4);
			T weightedValue2 = WeightedValue(cx, cy, v5, v6, v7, v8);
			Jacobian(cx, cy, out var JXsee, out var JYsee, out var JXeta, out var JYeta);
			MathHelpers.Solve2x2(JXsee, JXeta, JYsee, JYeta, saturating2 - weightedValue, saturating3 - weightedValue2, out x, out x2);
			cx += x;
			cy += x2;
			ordinal++;
		}
		while (ordinal <= 1000 && (!(T.Abs(x) < saturating) || !(T.Abs(x2) < saturating)));
	}

	public Point GlobalCoordinates<T>(T cx, T cy) where T : IBinaryFloatingPointIeee754<T>
	{
		T weightedValue = WeightedValue(cx, cy, T.CreateChecked(TopLeft.X), T.CreateChecked(TopRight.X), T.CreateChecked(BottomRight.X), T.CreateChecked(BottomLeft.X));
		return new Point(y: double.CreateSaturating(WeightedValue(cx, cy, T.CreateChecked(TopLeft.Y), T.CreateChecked(TopRight.Y), T.CreateChecked(BottomRight.Y), T.CreateChecked(BottomLeft.Y))), x: double.CreateSaturating(weightedValue));
	}

	private T WeightedValue<T>(T cx, T cy, T v0, T v1, T v2, T v3) where T : IBinaryFloatingPointIeee754<T>
	{
		T cxTimesCy = cx * cy;
		T oneCxTerm = (T.One - cx - cy + cxTimesCy) * v0;
		T oneCxTerm2 = (T.One + cx - cy - cxTimesCy) * v1;
		T oneCxTerm3 = (T.One + cx + cy + cxTimesCy) * v2;
		T oneCxTerm4 = (T.One - cx + cy - cxTimesCy) * v3;
		return (oneCxTerm + oneCxTerm2 + oneCxTerm3 + oneCxTerm4) * T.CreateChecked(0.25);
	}

	private void Jacobian<T>(T cx, T cy, out T JXsee, out T JYsee, out T JXeta, out T JYeta) where T : IBinaryFloatingPointIeee754<T>
	{
		T createChecked = T.CreateChecked(0.25);
		T oneCyTerm = (T.One - cy) * createChecked;
		T oneCyTerm2 = (T.One + cy) * createChecked;
		T oneCxTerm = (T.One - cx) * createChecked;
		T oneCxTerm2 = (T.One + cx) * createChecked;
		JXsee = oneCyTerm * T.CreateChecked(0.0 - TopLeft.X + TopRight.X) + oneCyTerm2 * T.CreateChecked(BottomRight.X - BottomLeft.X);
		JYsee = oneCyTerm * T.CreateChecked(0.0 - TopLeft.Y + TopRight.Y) + oneCyTerm2 * T.CreateChecked(BottomRight.Y - BottomLeft.Y);
		JXeta = oneCxTerm * T.CreateChecked(0.0 - TopLeft.X + BottomLeft.X) + oneCxTerm2 * T.CreateChecked(0.0 - TopRight.X + BottomRight.X);
		JYeta = oneCxTerm * T.CreateChecked(0.0 - TopLeft.Y + BottomLeft.Y) + oneCxTerm2 * T.CreateChecked(0.0 - TopRight.Y + BottomRight.Y);
	}

	public override string ToString()
	{
		return "p0:" + TopLeft.ToString() + " p1:" + TopRight.ToString() + " p2:" + BottomRight.ToString() + " p3:" + BottomLeft.ToString();
	}
}
