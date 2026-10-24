using System;
using System.Numerics;

namespace Geospatial.Rasters.Convolution;

public static class Matrix3x3DTransformationHelpers
{
	public static void AssertNotNaN(this Matrix3x3D matrix)
	{
		if (double.IsNaN(matrix.M11))
		{
			throw new InvalidOperationException();
		}
	}

	public static Matrix3x3D TranslateScaleSensitiveSafe(this Matrix3x3D matrix, double deltaX, double deltaY)
	{
		if (double.IsNaN(deltaX) || double.IsNaN(deltaY))
		{
			throw new InvalidOperationException();
		}
		matrix.ExtractScale(out var scale);
		Matrix3x3D matrix3x3D = matrix * Matrix3x3D.Translation(deltaX / scale, deltaY / scale);
		matrix3x3D.AssertNotNaN();
		return matrix3x3D;
	}

	public static Matrix3x3D ResetTranslationSafe(this Matrix3x3D matrix)
	{
		matrix.ExtractTranslation(out var translationX, out var translationY);
		return matrix.TranslateScaleSensitiveSafe(0.0 - translationX, 0.0 - translationY);
	}

	public static Matrix3x3D ScaleSafe(this Matrix3x3D matrix, double scale, double centerX, double centerY)
	{
		if (double.IsNaN(scale))
		{
			throw new InvalidOperationException();
		}
		Matrix3x3D matrix3x3D = Matrix3x3D.Scale(scale, scale, centerX, centerY);
		Matrix3x3D matrix3x3D2 = matrix * matrix3x3D;
		matrix3x3D2.AssertNotNaN();
		return matrix3x3D2;
	}

	public static Matrix3x3D SetScaleSafe(this Matrix3x3D matrix, double scale, double centerX, double centerY)
	{
		matrix.ExtractScale(out var scale2);
		double scale3 = scale / scale2;
		return matrix.ScaleSafe(scale3, centerX, centerY);
	}

	public static void ExtractScale(this Matrix3x3D matrix, out double scale)
	{
		scale = Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M21 * matrix.M21);
	}

	public static void ExtractScale(this Matrix3x3D matrix, out double scaleX, out double scaleY)
	{
		scaleX = matrix.TopLeft;
		scaleY = matrix.Center;
	}

	public static void ExtractTranslation(this Matrix3x3D matrix, out double translationX, out double translationY)
	{
		translationX = matrix.M13;
		translationY = matrix.M23;
	}

	public static Matrix4x4 AsMatrix4x4(this Matrix3x3D matrix)
	{
		return new Matrix4x4
		{
			M11 = (float)matrix.M11,
			M12 = (float)matrix.M12,
			M13 = (float)matrix.M13,
			M14 = 0f,
			M21 = (float)matrix.M21,
			M22 = (float)matrix.M22,
			M23 = (float)matrix.M23,
			M24 = 0f,
			M31 = 0f,
			M32 = 0f,
			M33 = 1f,
			M34 = 0f,
			M41 = (float)matrix.M13,
			M42 = (float)matrix.M23,
			M43 = 0f,
			M44 = 1f
		};
	}
}
