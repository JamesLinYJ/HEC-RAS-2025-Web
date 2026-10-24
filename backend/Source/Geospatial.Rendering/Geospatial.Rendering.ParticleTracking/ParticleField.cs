using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.VelocityFields;

namespace Geospatial.Rendering.ParticleTracking;

[SupportedOSPlatform("windows")]
public abstract class ParticleField : IDisposable
{
	public const byte TAIL_START_ALPHA = 155;

	public static int TAIL_R;

	public static int TAIL_G;

	public static int TAIL_B;

	protected List<Pixel> ValidCells;

	protected Bitmap _baseBmp;

	protected Vector[,] Velocities;

	private const int NUM_COLOR_DIVS = 25;

	protected static int[] TailColors;

	protected static float[] VertDivValues;

	protected static float[] DiagDivValues;

	public int Width { get; private set; }

	public int Height { get; private set; }

	public IStaticVelocityField Field { get; protected set; }

	public Extent ViewWindow { get; private set; }

	public float TimeStep { get; protected set; }

	static ParticleField()
	{
		TAIL_R = 255;
		TAIL_G = 255;
		TAIL_B = 255;
		ReinitializeStaticTrackingVariables();
	}

	public static void ReinitializeStaticTrackingVariables()
	{
		TailColors = new int[25];
		VertDivValues = new float[25];
		DiagDivValues = new float[25];
		float computed = 6.2f;
		computed /= 2f;
		float computed2 = -0.5f;
		float computed3 = -0.707f;
		float computed4 = 1f / 24f;
		float computed5 = 0.05891667f;
		for (int i = 0; i < TailColors.Length; i++)
		{
			Color color = Color.FromArgb((byte)Math.Max(0f, 155f - (float)i * computed), TAIL_R, TAIL_G, TAIL_B);
			TailColors[i] = color.ToArgb();
			VertDivValues[i] = computed2 + (float)i * computed4;
			DiagDivValues[i] = computed3 + (float)i * computed5;
		}
	}

	public abstract Bitmap Update();

	public abstract void Dispose();

	protected ParticleField(IStaticVelocityField field, Extent viewWindow, int pxWidth, int pxHeight, float timestep)
	{
		Width = pxWidth;
		Height = pxHeight;
		ViewWindow = viewWindow;
		Field = field;
		ViewWindow = viewWindow;
		TimeStep = timestep;
		Velocities = field.Rasterize(viewWindow, pxWidth, pxHeight);
		ValidCells = GetValidVelocityCells();
	}

	protected List<Pixel> GetValidVelocityCells()
	{
		List<Pixel> pixels = new List<Pixel>();
		for (int i = 0; i < Height; i++)
		{
			for (int j = 0; j < Width; j++)
			{
				if (Velocities[i, j] != Vector.NullVelocity)
				{
					pixels.Add(new Pixel(j, i));
				}
			}
		}
		return pixels;
	}

	protected unsafe void UpdateHeatMap()
	{
		Rectangle rect = new Rectangle(0, 0, Width, Height);
		BitmapData bitmapData = _baseBmp.LockBits(rect, ImageLockMode.ReadWrite, _baseBmp.PixelFormat);
		byte* texturePtr = (byte*)bitmapData.Scan0;
		float[,] mag = new float[Height, Width];
		float[] max_arr = new float[Height];
		float[] min_arr = new float[Height];
		Parallel.For(0, Height, (int row) =>
		{
			int y = Height - row - 1;
			float maxValue = float.MaxValue;
			float minValue = float.MinValue;
			for (int i = 0; i < Width; i++)
			{
				Vector velocity = GetVelocity(i, y);
				if (velocity == Vector.NullVelocity)
				{
					mag[row, i] = float.MaxValue;
				}
				else
				{
					float velocityLength = (float)velocity.Length;
					mag[row, i] = velocityLength;
					minValue = Math.Max(minValue, velocityLength);
					maxValue = Math.Min(maxValue, velocityLength);
				}
			}
			max_arr[row] = minValue;
			min_arr[row] = maxValue;
		});
		float max_arrTerm = max_arr.Max();
		float min_vel = min_arr.Min();
		float range = max_arrTerm - min_vel;
		byte min_r = 50;
		byte min_g = 50;
		byte min_b = 200;
		byte max_r = 200;
		byte max_g = 20;
		byte max_b = 20;
		Parallel.For(0, Height, (int row) =>
		{
			for (int i = 0; i < Width; i++)
			{
				int rowWidthTerm = 4 * (row * Width + i);
				if (mag[row, i] == float.MaxValue)
				{
					texturePtr[rowWidthTerm] = 0;
					texturePtr[rowWidthTerm + 1] = 0;
					texturePtr[rowWidthTerm + 2] = 0;
					texturePtr[rowWidthTerm + 3] = byte.MaxValue;
				}
				else
				{
					float magRowTerm = (mag[row, i] - min_vel) / range;
					byte b = (byte)Math.Min(255f, Math.Max(0f, magRowTerm * (float)(int)max_r + (1f - magRowTerm) * (float)(int)min_r));
					byte b2 = (byte)Math.Min(255f, Math.Max(0f, magRowTerm * (float)(int)max_g + (1f - magRowTerm) * (float)(int)min_g));
					byte b3 = (byte)Math.Min(255f, Math.Max(0f, magRowTerm * (float)(int)max_b + (1f - magRowTerm) * (float)(int)min_b));
					texturePtr[rowWidthTerm] = b3;
					texturePtr[rowWidthTerm + 1] = b2;
					texturePtr[rowWidthTerm + 2] = b;
					texturePtr[rowWidthTerm + 3] = byte.MaxValue;
				}
			}
		});
		_baseBmp.UnlockBits(bitmapData);
	}

	protected bool IsOOB(Pixel point)
	{
		return IsOOB(point.X, point.Y);
	}

	protected bool IsOOB(int x, int y)
	{
		if (x < Width && x >= 0 && y < Height)
		{
			return y < 0;
		}
		return true;
	}

	protected Vector GetVelocity(Geospatial.Vectors.Point input)
	{
		int inputTerm = (int)input.X;
		int inputTerm2 = (int)input.Y;
		if (inputTerm < 0 || inputTerm >= Velocities.GetLength(1))
		{
			return Vector.NullVelocity;
		}
		if (inputTerm2 < 0 || inputTerm2 >= Velocities.GetLength(0))
		{
			return Vector.NullVelocity;
		}
		return Velocities[inputTerm2, inputTerm];
	}

	protected Vector GetVelocity(Pixel input)
	{
		if (!IsOOB(input))
		{
			return Velocities[input.Y, input.X];
		}
		return Vector.NullVelocity;
	}

	protected Vector GetVelocity(int x, int y)
	{
		if (!IsOOB(x, y))
		{
			return Velocities[y, x];
		}
		return Vector.NullVelocity;
	}
}
