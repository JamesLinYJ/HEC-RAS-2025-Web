using System;
using System.Numerics;

namespace Geospatial.Vectors;

public struct Vector : IVector, IVector3D, IVector4D, IAdditiveIdentity<Vector, Vector>, IAdditionOperators<Vector, Vector, Vector>, IMultiplyOperators<Vector, double, Vector>
{
	public static readonly Vector NullVelocity = new Vector(double.MaxValue, double.MaxValue);

	public double DeltaX;

	public double DeltaY;

	public static Vector Zero => default;

	public static Vector AdditiveIdentity => Zero;

	public double X => DeltaX;

	public double Y => DeltaY;

	public double Length => Math.Sqrt(DeltaX * DeltaX + DeltaY * DeltaY);

	public double LengthSquared => DeltaX * DeltaX + DeltaY * DeltaY;

	double IVector.DeltaX => DeltaX;

	double IVector.DeltaY => DeltaY;

	bool IVector3D.HasZ => false;

	double IVector3D.DeltaZ
	{
		get
		{
			throw new NotSupportedException();
		}
	}

	bool IVector4D.HasM => false;

	double IVector4D.DeltaM
	{
		get
		{
			throw new NotSupportedException();
		}
	}

	public double MathBearing
	{
		get
		{
			double piSum = Math.Atan2(DeltaY, DeltaX);
			if (piSum < 0.0)
			{
				piSum += Math.PI * 2.0;
			}
			return piSum;
		}
	}

	public double Direction
	{
		get
		{
			double mathBearing = MathBearing;
			mathBearing /= 0.01745329252;
			mathBearing = 360.0 - mathBearing;
			mathBearing += 90.0;
			if (mathBearing < 0.0)
			{
				mathBearing += 360.0;
			}
			if (mathBearing >= 360.0)
			{
				mathBearing -= 360.0;
			}
			return mathBearing;
		}
	}

	public bool IsNull()
	{
		return this == NullVelocity;
	}

	public Vector(double dx, double dy)
	{
		DeltaX = dx;
		DeltaY = dy;
	}

	public Vector(Point pA, Point pB)
	{
		DeltaX = pB.X - pA.X;
		DeltaY = pB.Y - pA.Y;
	}

	public static implicit operator Vector(Vector2 v)
	{
		return new Vector(v.X, v.Y);
	}

	public static explicit operator Vector2(Vector v)
	{
		return new Vector2((float)v.DeltaX, (float)v.DeltaY);
	}

	public Vector UnitVector()
	{
		double length = Length;
		double dx;
		double dy;
		if (length > 0.0)
		{
			dx = DeltaX / length;
			dy = DeltaY / length;
		}
		else
		{
			dx = 0.0;
			dy = 0.0;
		}
		return new Vector(dx, dy);
	}

	public Vector Ratio(double factor)
	{
		return new Vector(DeltaX * factor, DeltaY * factor);
	}

	public Vector ScaleTo(double newLength)
	{
		return Ratio(newLength / Length);
	}

	public Vector Negate()
	{
		return new Vector(0.0 - DeltaX, 0.0 - DeltaY);
	}

	public Vector Reverse()
	{
		return Negate();
	}

	public double Dot(Vector other)
	{
		return DeltaX * other.DeltaX + DeltaY * other.DeltaY;
	}

	public bool IsCrossProductPositive(Vector other)
	{
		return CrossProduct(other) > 0.0;
	}

	public bool IsSameDirection(Vector other)
	{
		return Dot(other) > 0.0;
	}

	public double CrossProduct(Vector other)
	{
		return DeltaX * other.DeltaY - DeltaY * other.DeltaX;
	}

	public static double CrossProduct(IPoint a, IPoint b, IPoint c)
	{
		return CrossProduct(a.X, a.Y, b.X, b.Y, c.X, c.Y);
	}

	public static double CrossProduct<T>(in T a, in T b, in T c) where T : struct, IPoint
	{
		return CrossProduct(a.X, a.Y, b.X, b.Y, c.X, c.Y);
	}

	public static double CrossProduct(in Point a, in Point b, in Point c)
	{
		return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
	}

	public static double CrossProduct(double ax, double ay, double bx, double by, double cx, double cy)
	{
		return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
	}

	public static double CrossProductFMA(in Point a, in Point b, in Point c)
	{
		double x = b.X - a.X;
		double computed = b.Y - a.Y;
		double computed2 = c.X - a.X;
		double y = c.Y - a.Y;
		return Math.FusedMultiplyAdd(x, y, 0.0 - computed * computed2);
	}

	public static double CrossProductDecimal(in Point a, in Point b, in Point c)
	{
		decimal computed = (decimal)a.X;
		decimal computed2 = (decimal)a.Y;
		decimal computed3 = (decimal)b.X;
		decimal computed4 = (decimal)b.Y;
		decimal computed5 = (decimal)c.X;
		decimal computed6 = (decimal)c.Y;
		return (double)((computed3 - computed) * (computed6 - computed2) - (computed4 - computed2) * (computed5 - computed));
	}

	public static double CrossProduct_DeltaX(double dx, double by, double cy)
	{
		return dx * (by - cy);
	}

	public static double AngleBetween(Vector vector1, Vector vector2)
	{
		double y = vector1.DeltaX * vector2.DeltaY - vector2.DeltaX * vector1.DeltaY;
		double x = vector1.DeltaX * vector2.DeltaX + vector1.DeltaY * vector2.DeltaY;
		return Math.Atan2(y, x) * (180.0 / Math.PI);
	}

	public double AngleBetween(in Vector vec1)
	{
		return AngleBetween(this, vec1);
	}

	public static double AngleBetweenRads(in Vector vector1, in Vector vector2)
	{
		double y = vector1.DeltaX * vector2.DeltaY - vector2.DeltaX * vector1.DeltaY;
		double x = vector1.DeltaX * vector2.DeltaX + vector1.DeltaY * vector2.DeltaY;
		return Math.Atan2(y, x);
	}

	public double AngleBetweenRads(in Vector vec1)
	{
		return AngleBetweenRads(this, in vec1);
	}

	public Vector Rotate(double angleRads)
	{
		double angleRadsTerm = Math.Sin(angleRads);
		double angleRadsTerm2 = Math.Cos(angleRads);
		double dx = DeltaX * angleRadsTerm2 - DeltaY * angleRadsTerm;
		double dy = DeltaX * angleRadsTerm + DeltaY * angleRadsTerm2;
		return new Vector(dx, dy);
	}

	public static double AngleBetween(in Point a, in Point b, in Point c)
	{
		if (b.EqualXY(in c))
		{
			return 0.0;
		}
		double computed = b.X - a.X;
		double computed2 = b.Y - a.Y;
		double computed3 = c.X - a.X;
		double computed4 = c.Y - a.Y;
		double computed5 = computed * computed3 + computed2 * computed4;
		double computed6 = Math.Pow(computed * computed + computed2 * computed2, 0.5);
		double computed7 = Math.Pow(computed3 * computed3 + computed4 * computed4, 0.5);
		double piSum = Math.Acos(computed5 / (computed6 * computed7));
		if (piSum < 0.0)
		{
			piSum += Math.PI;
		}
		return piSum;
	}

	public static double DotProduct(in Point a, in Point b, in Point c)
	{
		double computed = b.X - a.X;
		double computed2 = b.Y - a.Y;
		double computed3 = c.X - a.X;
		double computed4 = c.Y - a.Y;
		return computed * computed3 + computed2 * computed4;
	}

	public static double ComputeLength(double x, double y)
	{
		return Math.Sqrt(x * x + y * y);
	}

	public Vector Perpendicular(bool clockwise)
	{
		if (clockwise)
		{
			Vector result = new Vector(DeltaY, 0.0 - DeltaX);
			result.Normalize();
			return result;
		}
		Vector result2 = new Vector(0.0 - DeltaY, DeltaX);
		result2.Normalize();
		return result2;
	}

	public void Normalize()
	{
		double length = Length;
		DeltaX /= length;
		DeltaY /= length;
	}

	public static bool operator ==(Vector v1, Vector v2)
	{
		if (v1.Equals(null) && v2.Equals(null))
		{
			return true;
		}
		if (v1.Equals(null))
		{
			return false;
		}
		if (v2.Equals(null))
		{
			return false;
		}
		if (v1.DeltaX == v2.DeltaX && v1.DeltaY == v2.DeltaY)
		{
			return true;
		}
		return false;
	}

	public static bool operator !=(Vector v1, Vector v2)
	{
		return !(v1 == v2);
	}

	public static Vector operator *(Vector v, float floatValue)
	{
		return new Vector(v.DeltaX * (double)floatValue, v.DeltaY * (double)floatValue);
	}

	public static Vector operator *(float floatValue, Vector v)
	{
		return v * floatValue;
	}

	public static Vector operator *(Vector v, double doubleValue)
	{
		return new Vector(v.DeltaX * doubleValue, v.DeltaY * doubleValue);
	}

	public static Vector operator *(double doubleValue, Vector v)
	{
		return v * doubleValue;
	}

	public static Vector operator +(Vector v1, Vector v2)
	{
		return new Vector(v1.DeltaX + v2.DeltaX, v1.DeltaY + v2.DeltaY);
	}

	public static Vector operator +(Vector v, float floatValue)
	{
		return new Vector(v.DeltaX + (double)floatValue, v.DeltaY + (double)floatValue);
	}

	public static Vector operator +(Vector v, double doubleValue)
	{
		return new Vector(v.DeltaX + doubleValue, v.DeltaY + doubleValue);
	}

	public static Vector operator +(float floatValue, Vector v)
	{
		return new Vector(v.DeltaX + (double)floatValue, v.DeltaY + (double)floatValue);
	}

	public static Vector operator +(double doubleValue, Vector v)
	{
		return new Vector(v.DeltaX + doubleValue, v.DeltaY + doubleValue);
	}

	public static Vector operator -(Vector v1, Vector v2)
	{
		return new Vector(v1.DeltaX - v2.DeltaX, v1.DeltaY - v2.DeltaY);
	}

	public static Vector operator -(Vector v, float floatValue)
	{
		return new Vector(v.DeltaX - (double)floatValue, v.DeltaY - (double)floatValue);
	}

	public static Vector operator -(Vector v, double doubleValue)
	{
		return new Vector(v.DeltaX - doubleValue, v.DeltaY - doubleValue);
	}

	public static Vector operator -(float floatValue, Vector v)
	{
		return new Vector((double)floatValue - v.DeltaX, (double)floatValue - v.DeltaY);
	}

	public static Vector operator -(double doubleValue, Vector v)
	{
		return new Vector(doubleValue - v.DeltaX, doubleValue - v.DeltaY);
	}

	public static Vector operator -(Vector v)
	{
		return new Vector(0.0 - v.X, 0.0 - v.Y);
	}

	public static Vector operator /(Vector v, float floatValue)
	{
		return new Vector(v.DeltaX / (double)floatValue, v.DeltaY / (double)floatValue);
	}

	public static Vector operator /(Vector v, double doubleValue)
	{
		return new Vector(v.DeltaX / doubleValue, v.DeltaY / doubleValue);
	}

	public override int GetHashCode()
	{
		return base.GetHashCode();
	}

	public override bool Equals(object obj)
	{
		return base.Equals(obj);
	}

	public override string ToString()
	{
		return "(" + X + ", " + Y + ")";
	}
}
