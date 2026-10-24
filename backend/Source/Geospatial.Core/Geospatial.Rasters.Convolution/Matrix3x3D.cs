using System;
using System.Diagnostics;
using System.Text;
using Geospatial.Vectors;

namespace Geospatial.Rasters.Convolution;

[DebuggerDisplay("{ToPrettyString()}")]
public struct Matrix3x3D
{
	private static readonly Matrix3x3D _identity = new Matrix3x3D(1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0);

	public static Matrix3x3D NullMatrix = new Matrix3x3D(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

	public readonly double TopLeft;

	public readonly double TopMid;

	public readonly double TopRight;

	public readonly double MidLeft;

	public readonly double Center;

	public readonly double MidRight;

	public readonly double BotLeft;

	public readonly double BotMid;

	public readonly double BotRight;

	public static Matrix3x3D Identity => _identity;

	public bool IsNull
	{
		get
		{
			if (M11 == 0.0 && M12 == 0.0 && M13 == 0.0 && M21 == 0.0 && M22 == 0.0 && M23 == 0.0 && M31 == 0.0 && M32 == 0.0)
			{
				return M33 == 0.0;
			}
			return false;
		}
	}

	public double M11 => TopLeft;

	public double M12 => TopMid;

	public double M13 => TopRight;

	public double M21 => MidLeft;

	public double M22 => Center;

	public double M23 => MidRight;

	public double M31 => BotLeft;

	public double M32 => BotMid;

	public double M33 => BotRight;

	private double Minor11 => M22 * M33 - M23 * M32;

	private double Minor12 => M21 * M33 - M23 * M31;

	private double Minor13 => M21 * M32 - M22 * M31;

	private double Minor21 => M12 * M33 - M13 * M32;

	private double Minor22 => M11 * M33 - M13 * M31;

	private double Minor23 => M11 * M32 - M12 * M31;

	private double Minor31 => M12 * M23 - M13 * M22;

	private double Minor32 => M11 * M23 - M13 * M21;

	private double Minor33 => M11 * M22 - M12 * M21;

	public Matrix3x3D(double tL, double tM, double tR, double mL, double mM, double mR, double bL, double bM, double bR)
	{
		TopLeft = tL;
		TopMid = tM;
		TopRight = tR;
		MidLeft = mL;
		Center = mM;
		MidRight = mR;
		BotLeft = bL;
		BotMid = bM;
		BotRight = bR;
	}

	public static Matrix3x3D Translation(double dx, double dy)
	{
		return new Matrix3x3D(1.0, 0.0, dx, 0.0, 1.0, dy, 0.0, 0.0, 1.0);
	}

	public static Matrix3x3D Scale(double scaleX, double scaleY)
	{
		return new Matrix3x3D(scaleX, 0.0, 0.0, 0.0, scaleY, 0.0, 0.0, 0.0, 1.0);
	}

	public static Matrix3x3D Scale(double scaleX, double scaleY, double centerX, double centerY)
	{
		Matrix3x3D matrix3x3D = Translation(centerX, centerY);
		Matrix3x3D other = Scale(scaleX, scaleY);
		Matrix3x3D other2 = Translation(0.0 - centerX, 0.0 - centerY);
		return matrix3x3D.Multiply(other).Multiply(other2);
	}

	public static Matrix3x3D Rotate(double angleRads)
	{
		return new Matrix3x3D(Math.Cos(angleRads), Math.Sin(angleRads), 0.0, 0.0 - Math.Sin(angleRads), Math.Cos(angleRads), 0.0, 0.0, 0.0, 1.0);
	}

	public static Matrix3x3D RotateCounterClockwise(double angleRads)
	{
		return new Matrix3x3D(Math.Cos(angleRads), 0.0 - Math.Sin(angleRads), 0.0, Math.Sin(angleRads), Math.Cos(angleRads), 0.0, 0.0, 0.0, 1.0);
	}

	public Point Transform(Point pt)
	{
		double x = pt.X * TopLeft + pt.Y * TopMid + 1.0 * TopRight;
		double y = pt.X * MidLeft + pt.Y * Center + 1.0 * MidRight;
		return new Point(x, y);
	}

	public Matrix3x3D Multiply(Matrix3x3D other)
	{
		double tL = TopLeft * other.TopLeft + TopMid * other.MidLeft + TopRight * other.BotLeft;
		double tM = TopLeft * other.TopMid + TopMid * other.Center + TopRight * other.BotMid;
		double tR = TopLeft * other.TopRight + TopMid * other.MidRight + TopRight * other.BotRight;
		double mL = MidLeft * other.TopLeft + Center * other.MidLeft + MidRight * other.BotLeft;
		double mM = MidLeft * other.TopMid + Center * other.Center + MidRight * other.BotMid;
		double mR = MidLeft * other.TopRight + Center * other.MidRight + MidRight * other.BotRight;
		double bL = BotLeft * other.TopLeft + BotMid * other.MidLeft + BotRight * other.BotLeft;
		double bM = BotLeft * other.TopMid + BotMid * other.Center + BotRight * other.BotMid;
		double bR = BotLeft * other.TopRight + BotMid * other.MidRight + BotRight * other.BotRight;
		return new Matrix3x3D(tL, tM, tR, mL, mM, mR, bL, bM, bR);
	}

	public Matrix3x3D Multiply(double scalar)
	{
		return new Matrix3x3D(M11 * scalar, M12 * scalar, M13 * scalar, M21 * scalar, M22 * scalar, M23 * scalar, M31 * scalar, M32 * scalar, M33 * scalar);
	}

	public Matrix3x3D Invert()
	{
		double determinant = Determinant();
		if (determinant == 0.0)
		{
			return NullMatrix;
		}
		return Adjoint().Multiply(1.0 / determinant);
	}

	public double Determinant()
	{
		return TopLeft * Center * BotRight + TopMid * MidRight * BotLeft + TopRight * MidLeft * BotMid - TopLeft * MidRight * BotMid - TopMid * MidLeft * BotRight - TopRight * Center * BotLeft;
	}

	public Matrix3x3D Transpose()
	{
		return new Matrix3x3D(M11, M21, M31, M12, M22, M32, M13, M23, M33);
	}

	public Matrix3x3D Cofactor()
	{
		return new Matrix3x3D(Minor11, 0.0 - Minor12, Minor13, 0.0 - Minor21, Minor22, 0.0 - Minor23, Minor31, 0.0 - Minor32, Minor33);
	}

	public Matrix3x3D Adjoint()
	{
		return Cofactor().Transpose();
	}

	public Matrix3x3F ToMatrixF()
	{
		return new Matrix3x3F((float)TopLeft, (float)TopMid, (float)TopRight, (float)MidLeft, (float)Center, (float)MidRight, (float)BotLeft, (float)BotMid, (float)BotRight);
	}

	public string ToPrettyString(string displayFormat = "F3")
	{
		string[] strings = new string[9]
		{
			TopLeft.ToString(displayFormat),
			TopMid.ToString(displayFormat),
			TopRight.ToString(displayFormat),
			MidLeft.ToString(displayFormat),
			Center.ToString(displayFormat),
			MidRight.ToString(displayFormat),
			BotLeft.ToString(displayFormat),
			BotMid.ToString(displayFormat),
			BotRight.ToString(displayFormat)
		};
		int ordinal = 0;
		for (int i = 0; i < 9; i++)
		{
			ordinal = Math.Max(ordinal, strings[i].Length);
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("[");
		stringBuilder.Append(strings[0].PadLeft(ordinal));
		stringBuilder.Append(",");
		stringBuilder.Append(strings[1].PadLeft(ordinal));
		stringBuilder.Append(",");
		stringBuilder.Append(strings[2].PadLeft(ordinal));
		stringBuilder.AppendLine("],");
		stringBuilder.Append("[");
		stringBuilder.Append(strings[3].PadLeft(ordinal));
		stringBuilder.Append(",");
		stringBuilder.Append(strings[4].PadLeft(ordinal));
		stringBuilder.Append(",");
		stringBuilder.Append(strings[5].PadLeft(ordinal));
		stringBuilder.AppendLine("],");
		stringBuilder.Append("[");
		stringBuilder.Append(strings[6].PadLeft(ordinal));
		stringBuilder.Append(",");
		stringBuilder.Append(strings[7].PadLeft(ordinal));
		stringBuilder.Append(",");
		stringBuilder.Append(strings[8].PadLeft(ordinal));
		stringBuilder.Append("],");
		return stringBuilder.ToString();
	}

	public static Matrix3x3D operator *(Matrix3x3D a, Matrix3x3D b)
	{
		return a.Multiply(b);
	}
}
