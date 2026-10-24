using System;
using System.Diagnostics;

namespace RasMapperLib.Mesh;

[DebuggerDisplay("{CleanString}")]
public struct Vector2D
{
	public static readonly Vector2D NoData = new Vector2D(-9999f, -9999f);

	public float X;

	public float Y;

	private string CleanString => "(" + Math.Round(X, 2) + ", " + Math.Round(Y, 2) + ") = |" + Math.Round(Magnitude, 2) + "|";

	public float Magnitude => (float)Math.Sqrt(X * X + Y * Y);

	public Vector2D(float x, float y)
	{
		this = default;
		X = x;
		Y = y;
	}

	public Vector2D(PointM pm)
	{
		this = default;
		X = (float)pm.X;
		Y = (float)pm.Y;
	}

	public float Dot(Vector2D other)
	{
		return X * other.X + Y * other.Y;
	}

	public Vector2D ScaleToLength(float len)
	{
		float magnitude = Magnitude;
		float lenOverMagnitude = len / magnitude;
		return new Vector2D(X * lenOverMagnitude, Y * lenOverMagnitude);
	}

	public static Vector2D operator +(Vector2D @this, Vector2D other)
	{
		return new Vector2D(@this.X + other.X, @this.Y + other.Y);
	}

	public static Vector2D operator /(Vector2D numerator, float denominator)
	{
		return new Vector2D(numerator.X / denominator, numerator.Y / denominator);
	}

	public static Vector2D operator *(Vector2D v1, float scalar)
	{
		return new Vector2D(v1.X * scalar, v1.Y * scalar);
	}

	public static Vector2D operator *(float scalar, Vector2D v1)
	{
		return new Vector2D(v1.X * scalar, v1.Y * scalar);
	}

	public override bool Equals(object obj) => obj is Vector2D other && this == other;

	public override int GetHashCode() => HashCode.Combine(X, Y);

	public static bool operator ==(Vector2D @this, Vector2D other)
	{
		if (@this.X == other.X && @this.Y == other.Y)
		{
			return true;
		}
		return false;
	}

	public static bool operator !=(Vector2D @this, Vector2D other)
	{
		return !(@this == other);
	}
}
