using System;
using Geospatial.Vectors;
using Utility;
using Utility.Units;

namespace Geospatial;

public static class VelocityMath
{
	public static void ConvertToVxVy(Memory<float> mag, Memory<float> dir, Memory<float> vx, Memory<float> vy, float? noDataValue, DirectionConvention dirConvention)
	{
		int length = mag.Length;
		if (length != dir.Length)
		{
			throw new ArgumentException("Mag/Dir inputs not sized correctly.");
		}
		if (length == 0)
		{
			return;
		}
		bool notIsEmpty = !vx.IsEmpty;
		bool notIsEmpty2 = !vy.IsEmpty;
		if (notIsEmpty && vx.Length != length)
		{
			throw new ArgumentException("vx");
		}
		if (notIsEmpty2 && vy.Length != length)
		{
			throw new ArgumentException("vy");
		}
		if (!notIsEmpty && !notIsEmpty2)
		{
			return;
		}
		float naN = ((!noDataValue.HasValue) ? float.NaN : noDataValue.Value);
		Span<float> magSpan = mag.Span;
		Span<float> dirSpan = dir.Span;
		Span<float> vxSpan = vx.Span;
		Span<float> vySpan = vy.Span;
		if (notIsEmpty & notIsEmpty2)
		{
			if (dirConvention == DirectionConvention.GoingToward)
			{
				for (int i = 0; i < length; i++)
				{
					float mag2 = magSpan[i];
					float dir2 = dirSpan[i];
					if (mag2 != naN && dir2 != naN)
					{
						vxSpan[i] = TowardVx(mag2, dir2);
						vySpan[i] = TowardVy(mag2, dir2);
					}
				}
				return;
			}
			for (int j = 0; j < length; j++)
			{
				float mag3 = magSpan[j];
				float dir3 = dirSpan[j];
				if (mag3 != naN && dir3 != naN)
				{
					vxSpan[j] = FromVx(mag3, dir3);
					vySpan[j] = FromVy(mag3, dir3);
				}
			}
			return;
		}
		if (notIsEmpty)
		{
			if (dirConvention == DirectionConvention.GoingToward)
			{
				for (int k = 0; k < length; k++)
				{
					float mag4 = magSpan[k];
					float dir4 = dirSpan[k];
					if (mag4 != naN && dir4 != naN)
					{
						vxSpan[k] = TowardVx(mag4, dir4);
					}
				}
				return;
			}
			for (int l = 0; l < length; l++)
			{
				float mag5 = magSpan[l];
				float dir5 = dirSpan[l];
				if (mag5 != naN && dir5 != naN)
				{
					vxSpan[l] = FromVx(mag5, dir5);
				}
			}
			return;
		}
		if (dirConvention == DirectionConvention.GoingToward)
		{
			for (int m = 0; m < length; m++)
			{
				float mag6 = magSpan[m];
				float dir6 = dirSpan[m];
				if (mag6 != naN && dir6 != naN)
				{
					vySpan[m] = TowardVy(mag6, dir6);
				}
			}
			return;
		}
		for (int n = 0; n < length; n++)
		{
			float mag7 = magSpan[n];
			float dir7 = dirSpan[n];
			if (mag7 != naN && dir7 != naN)
			{
				vySpan[n] = FromVy(mag7, dir7);
			}
		}
		static float FromVx(float computed, float computed2)
		{
			return (float)(Math.Sin((double)computed2 * 0.01745329252) * (double)(0f - computed));
		}
		static float FromVy(float computed3, float computed4)
		{
			return (float)(Math.Cos((double)computed4 * 0.01745329252) * (double)(0f - computed3));
		}
		static float TowardVx(float computed5, float computed6)
		{
			return (float)(Math.Sin((double)computed6 * 0.01745329252) * (double)computed5);
		}
		static float TowardVy(float computed7, float computed8)
		{
			return (float)(Math.Cos((double)computed8 * 0.01745329252) * (double)computed7);
		}
	}

	public static void ConvertToVxVy(float[,] dir, float[,] mag, float[,] vx, float[,] vy, float? noDataValue, DirectionConvention dirConvention)
	{
		float naN = ((!noDataValue.HasValue) ? float.NaN : noDataValue.Value);
		for (int i = 0; i < mag.GetLength(0); i++)
		{
			for (int j = 0; j < mag.GetLength(1); j++)
			{
				float magTerm = mag[i, j];
				float dirTerm = dir[i, j];
				if (magTerm != naN && dirTerm != naN)
				{
					if (dirConvention == DirectionConvention.GoingToward)
					{
						vx[i, j] = (float)(Math.Sin((double)dirTerm * 0.01745329252) * (double)magTerm);
						vy[i, j] = (float)(Math.Cos((double)dirTerm * 0.01745329252) * (double)magTerm);
					}
					else
					{
						vx[i, j] = (float)(Math.Sin((double)dirTerm * 0.01745329252) * (double)(0f - magTerm));
						vy[i, j] = (float)(Math.Cos((double)dirTerm * 0.01745329252) * (double)(0f - magTerm));
					}
				}
			}
		}
	}

	public static void ConvertToVxVy(float dir, float mag, DirectionConvention dirConvention, out float vx, out float vy)
	{
		if (dirConvention == DirectionConvention.GoingToward)
		{
			vx = (float)(Math.Sin((double)dir * 0.01745329252) * (double)mag);
			vy = (float)(Math.Cos((double)dir * 0.01745329252) * (double)mag);
		}
		else
		{
			vx = (float)(Math.Sin((double)dir * 0.01745329252) * (double)(0f - mag));
			vy = (float)(Math.Cos((double)dir * 0.01745329252) * (double)(0f - mag));
		}
	}

	public static void ConvertToVxVy(double dir, double mag, DirectionConvention dirConvention, out double vx, out double vy)
	{
		if (dirConvention == DirectionConvention.GoingToward)
		{
			vx = Math.Sin(dir * 0.01745329252) * mag;
			vy = Math.Cos(dir * 0.01745329252) * mag;
		}
		else
		{
			vx = Math.Sin(dir * 0.01745329252) * (0.0 - mag);
			vy = Math.Cos(dir * 0.01745329252) * (0.0 - mag);
		}
	}

	public static void ConvertToMagDir(Memory<float> vx, Memory<float> vy, Memory<float> mag, Memory<float> dir, float? noDataValue, DirectionConvention dirConvention)
	{
		float naN = ((!noDataValue.HasValue) ? float.NaN : noDataValue.Value);
		Span<float> vxSpan = vx.Span;
		Span<float> vySpan = vy.Span;
		Span<float> dirSpan = dir.Span;
		Span<float> magSpan = mag.Span;
		for (int i = 0; i < vx.Length; i++)
		{
			float vx2 = vxSpan[i];
			float vy2 = vySpan[i];
			if (vx2 != naN && vy2 != naN)
			{
				Segment segment = new Segment(0.0, 0.0, vx2, vy2);
				magSpan[i] = (float)segment.Length;
				double bearing = segment.Bearing;
				bearing /= 0.01745329252;
				bearing = 360.0 - bearing;
				bearing = ((dirConvention != DirectionConvention.GoingToward) ? (bearing - 90.0) : (bearing + 90.0));
				if (bearing < 0.0)
				{
					bearing += 360.0;
				}
				if (bearing >= 360.0)
				{
					bearing -= 360.0;
				}
				dirSpan[i] = (float)bearing;
			}
		}
	}

	public static void ConvertToDir(Memory<float> vx, Memory<float> vy, Memory<float> dir, float? noDataValue, DirectionConvention dirConvention)
	{
		float naN = ((!noDataValue.HasValue) ? float.NaN : noDataValue.Value);
		Span<float> vxSpan = vx.Span;
		Span<float> vySpan = vy.Span;
		Span<float> dirSpan = dir.Span;
		for (int i = 0; i < vx.Length; i++)
		{
			float vx2 = vxSpan[i];
			float vy2 = vySpan[i];
			if (vx2 != naN && vy2 != naN)
			{
				double bearing = new Segment(0.0, 0.0, vx2, vy2).Bearing;
				bearing /= 0.01745329252;
				bearing = 360.0 - bearing;
				bearing = ((dirConvention != DirectionConvention.GoingToward) ? (bearing - 90.0) : (bearing + 90.0));
				if (bearing < 0.0)
				{
					bearing += 360.0;
				}
				if (bearing >= 360.0)
				{
					bearing -= 360.0;
				}
				dirSpan[i] = (float)bearing;
			}
		}
	}

	public static void ConvertToMagDir(float vx, float vy, DirectionConvention desiredDirection, out float dir, out float mag)
	{
		Segment segment = new Segment(0.0, 0.0, vx, vy);
		mag = (float)segment.Length;
		double bearing = segment.Bearing;
		bearing /= 0.01745329252;
		bearing = 360.0 - bearing;
		bearing = ((desiredDirection != DirectionConvention.GoingToward) ? (bearing - 90.0) : (bearing + 90.0));
		if (bearing < 0.0)
		{
			bearing += 360.0;
		}
		if (bearing >= 360.0)
		{
			bearing -= 360.0;
		}
		dir = (float)bearing;
	}

	public static void ChangeMagnitude(Memory<float> vxMem, Memory<float> vyMem, Memory<float> newMagnitudes, float? noDataValue)
	{
		float ndv;
		if (noDataValue.HasValue)
		{
			ndv = noDataValue.Value;
		}
		else
		{
			ndv = float.NaN;
		}
		Parallel.SmartFor(newMagnitudes.Length, (int start, int end) =>
		{
			Span<float> vxMemSpan = vxMem.Span;
			Span<float> vyMemSpan = vyMem.Span;
			Span<float> newMagnitudesSpan = newMagnitudes.Span;
			for (int i = start; i <= end; i++)
			{
				float newMagnitude = newMagnitudesSpan[i];
				if (newMagnitude == ndv)
				{
					vxMemSpan[i] = ndv;
					vyMemSpan[i] = ndv;
				}
				else if (newMagnitude == 0f)
				{
					vxMemSpan[i] = 0f;
					vyMemSpan[i] = 0f;
				}
				else
				{
					float vxMem2 = vxMemSpan[i];
					float vyMem2 = vyMemSpan[i];
					if (vxMem2 == 0f && vyMem2 == 0f)
					{
						vxMemSpan[i] = 0f;
						vyMemSpan[i] = 0f;
					}
					else
					{
						float vxMem2VyMem2Term = (float)Math.Sqrt(vxMem2 * vxMem2 + vyMem2 * vyMem2);
						float newMagnitudeTerm = newMagnitude / vxMem2VyMem2Term;
						vxMemSpan[i] = vxMem2 * newMagnitudeTerm;
						vyMemSpan[i] = vyMem2 * newMagnitudeTerm;
					}
				}
			}
		});
	}

	public static void ChangeDirection(Memory<float> vxMem, Memory<float> vyMem, Memory<float> newDirections, DirectionConvention dc, float? noDataValue)
	{
		float ndv;
		if (noDataValue.HasValue)
		{
			ndv = noDataValue.Value;
		}
		else
		{
			ndv = float.NaN;
		}
		Parallel.SmartFor(newDirections.Length, (int start, int end) =>
		{
			Span<float> vxMemSpan = vxMem.Span;
			Span<float> vyMemSpan = vyMem.Span;
			Span<float> newDirectionsSpan = newDirections.Span;
			for (int i = start; i <= end; i++)
			{
				float newDirection = newDirectionsSpan[i];
				if (newDirection == ndv)
				{
					vxMemSpan[i] = ndv;
					vyMemSpan[i] = ndv;
				}
				else
				{
					float vx = vxMemSpan[i];
					float vy = vyMemSpan[i];
					if (vx != 0f || vy != 0f)
					{
						float mag = (float)Math.Sqrt(vx * vx + vy * vy);
						ConvertToVxVy(newDirection, mag, dc, out vx, out vy);
						vxMemSpan[i] = vx;
						vyMemSpan[i] = vy;
					}
				}
			}
		});
	}

	public static void ComputeMagnitude(Memory<float> vx, Memory<float> vy, Memory<float> mag, float? noDataValue)
	{
		float naN = ((!noDataValue.HasValue) ? float.NaN : noDataValue.Value);
		Span<float> vxSpan = vx.Span;
		Span<float> vySpan = vy.Span;
		Span<float> magSpan = mag.Span;
		for (int i = 0; i < vx.Length; i++)
		{
			float vx2 = vxSpan[i];
			float vy2 = vySpan[i];
			if (vx2 != naN && vy2 != naN)
			{
				magSpan[i] = (float)Math.Sqrt(vx2 * vx2 + vy2 * vy2);
			}
		}
	}
}
