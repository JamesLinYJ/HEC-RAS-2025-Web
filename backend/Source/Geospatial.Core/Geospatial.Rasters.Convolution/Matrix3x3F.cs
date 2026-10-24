using Geospatial.Vectors;

namespace Geospatial.Rasters.Convolution;

public struct Matrix3x3F
{
	public static Matrix3x3F NullMatrix = new Matrix3x3F(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

	public readonly float TopLeft;

	public readonly float TopMid;

	public readonly float TopRight;

	public readonly float MidLeft;

	public readonly float Pixel;

	public readonly float MidRight;

	public readonly float BotLeft;

	public readonly float BotMid;

	public readonly float BotRight;

	public bool IsNull
	{
		get
		{
			if (M11 == 0f && M12 == 0f && M13 == 0f && M21 == 0f && M22 == 0f && M23 == 0f && M31 == 0f && M32 == 0f)
			{
				return M33 == 0f;
			}
			return false;
		}
	}

	public float M11 => TopLeft;

	public float M12 => TopMid;

	public float M13 => TopRight;

	public float M21 => MidLeft;

	public float M22 => Pixel;

	public float M23 => MidRight;

	public float M31 => BotLeft;

	public float M32 => BotMid;

	public float M33 => BotRight;

	public float Center => Pixel;

	private float Minor11 => M22 * M33 - M23 * M32;

	private float Minor12 => M21 * M33 - M23 * M31;

	private float Minor13 => M21 * M32 - M22 * M31;

	private float Minor21 => M12 * M33 - M13 * M32;

	private float Minor22 => M11 * M33 - M13 * M31;

	private float Minor23 => M11 * M32 - M12 * M31;

	private float Minor31 => M12 * M23 - M13 * M22;

	private float Minor32 => M11 * M23 - M13 * M21;

	private float Minor33 => M11 * M22 - M12 * M21;

	public Matrix3x3F(float tL, float tM, float tR, float mL, float mM, float mR, float bL, float bM, float bR)
	{
		TopLeft = tL;
		TopMid = tM;
		TopRight = tR;
		MidLeft = mL;
		Pixel = mM;
		MidRight = mR;
		BotLeft = bL;
		BotMid = bM;
		BotRight = bR;
	}

	public static Matrix3x3F Gaussian()
	{
		return new Matrix3x3F(1f, 2f, 1f, 2f, 4f, 2f, 1f, 2f, 1f);
	}

	public PointF Transform(PointF pt)
	{
		float x = pt.X * TopLeft + pt.Y * TopMid + 1f * TopRight;
		float y = pt.X * MidLeft + pt.Y * Center + 1f * MidRight;
		return new PointF(x, y);
	}

	public Matrix3x3F Multiply(Matrix3x3F other)
	{
		float tL = TopLeft * other.TopLeft + TopMid * other.MidLeft + TopRight * other.BotLeft;
		float tM = TopLeft * other.TopMid + TopMid * other.Center + TopRight * other.BotMid;
		float tR = TopLeft * other.TopRight + TopMid * other.MidRight + TopRight * other.BotRight;
		float mL = MidLeft * other.TopLeft + Center * other.MidLeft + MidRight * other.BotLeft;
		float mM = MidLeft * other.TopMid + Center * other.Center + MidRight * other.BotMid;
		float mR = MidLeft * other.TopRight + Center * other.MidRight + MidRight * other.BotRight;
		float bL = BotLeft * other.TopLeft + BotMid * other.MidLeft + BotRight * other.BotLeft;
		float bM = BotLeft * other.TopMid + BotMid * other.Center + BotRight * other.BotMid;
		float bR = BotLeft * other.TopRight + BotMid * other.MidRight + BotRight * other.BotRight;
		return new Matrix3x3F(tL, tM, tR, mL, mM, mR, bL, bM, bR);
	}

	public Matrix3x3F Multiply(float scalar)
	{
		return new Matrix3x3F(M11 * scalar, M12 * scalar, M13 * scalar, M21 * scalar, M22 * scalar, M23 * scalar, M31 * scalar, M32 * scalar, M33 * scalar);
	}

	public Matrix3x3F Invert()
	{
		float determinant = Determinant();
		if (determinant == 0f)
		{
			return NullMatrix;
		}
		return Adjoint().Multiply(1f / determinant);
	}

	public float Determinant()
	{
		return TopLeft * Pixel * BotRight + TopMid * MidRight * BotLeft + TopRight * MidLeft * BotMid - TopLeft * MidRight * BotMid - TopMid * MidLeft * BotRight - TopRight * Pixel * BotLeft;
	}

	public Matrix3x3F Transpose()
	{
		return new Matrix3x3F(M11, M21, M31, M12, M22, M32, M13, M23, M33);
	}

	public Matrix3x3F Cofactor()
	{
		return new Matrix3x3F(Minor11, 0f - Minor12, Minor13, 0f - Minor21, Minor22, 0f - Minor23, Minor31, 0f - Minor32, Minor33);
	}

	public Matrix3x3F Adjoint()
	{
		return Cofactor().Transpose();
	}
}
