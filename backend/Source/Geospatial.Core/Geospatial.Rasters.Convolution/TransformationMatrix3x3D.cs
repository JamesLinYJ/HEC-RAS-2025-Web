using System.Numerics;

namespace Geospatial.Rasters.Convolution;

public struct TransformationMatrix3x3D
{
	private Matrix3x3D matrix = Matrix3x3D.Identity;

	public TransformationMatrix3x3D()
	{
	}

	public void Translate(double deltaX, double deltaY)
	{
		matrix = matrix.TranslateScaleSensitiveSafe(deltaX, deltaY);
	}

	public void ResetTranslation()
	{
		matrix = matrix.ResetTranslationSafe();
	}

	public void Scale(double scale, double centerX, double centerY)
	{
		matrix = matrix.ScaleSafe(scale, centerX, centerY);
	}

	public void SetScale(double scale, double centerX, double centerY)
	{
		matrix = matrix.SetScaleSafe(scale, centerX, centerY);
	}

	public void ExtractScale(out double scale)
	{
		matrix.ExtractScale(out scale);
	}

	public void ExtractTranslation(out double translationX, out double translationY)
	{
		matrix.ExtractTranslation(out translationX, out translationY);
	}

	public Matrix4x4 AsMatrix4x4()
	{
		return matrix.AsMatrix4x4();
	}

	public Matrix3x3D AsMatrix3x3()
	{
		return matrix;
	}
}
