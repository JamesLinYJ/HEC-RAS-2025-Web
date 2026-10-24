using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.VelocityFields;

namespace Geospatial.Rendering.ParticleTracking;

[SupportedOSPlatform("windows")]
public class TracerField : ParticleField
{
	public const byte TAIL_CONST_DECAY = 3;

	public const float DEFAULT_LINE_RADIUS = 0.75f;

	public const RungeKuttaOrder DEFAULT_RK = RungeKuttaOrder.RK2;

	public const float DEFAULT_DENSITY = 1.5f;

	public const int DEFAULT_LIFETIME = 100;

	public const int DEFAULT_METHOD = 2;

	protected List<Particle> _particles;

	private bool _tsIsQueued;

	private float _tsQueueValue;

	private bool _fieldIsQueued;

	private IStaticVelocityField _fieldQueueValue;

	private bool _densityIsQueued;

	private float _densityQueuedValue;

	private bool _colorIsQueued;

	private byte _rQ = (byte)ParticleField.TAIL_R;

	private byte _gQ = (byte)ParticleField.TAIL_G;

	private byte _bQ = (byte)ParticleField.TAIL_B;

	private bool _methodIsQueued;

	private int _methodQueuedValue;

	private bool _radiusIsQueued;

	private float _radiusQueueValue;

	private bool _lifetimeIsQueued;

	private int _lifetimeQueuedValue;

	private bool _tsRelativeToZoomIsQueued;

	private bool _tsRelativeTOZoomValue;

	protected Random _r = new Random();

	private double[] _rands = Array.Empty<double>();

	private readonly Bitmap _particleBmp;

	private readonly BitmapData _particleBmpData;

	private long _frameTime;

	private int _frameCount;

	private HashSet<Pixel> _globalColoredPixels = new HashSet<Pixel>();

	public float LineRadius { get; private set; }

	public RungeKutta RungeKuttaMethod { get; protected set; }

	public int NumParticles => _particles.Count;

	public int LineDrawMethod { get; private set; }

	public int ParticleLifetime { get; protected set; }

	public bool HeatmapEnabled { get; private set; }

	public float ParticlesPer1kPixels { get; private set; }

	public bool TimestepRelativeToZoom { get; private set; }

	public override void Dispose()
	{
		_particleBmp.UnlockBits(_particleBmpData);
		_particleBmp.Dispose();
	}

	public TracerField(int pxWidth, int pxHeight, Extent viewWindow, IStaticVelocityField field, float timestep, bool enableHeatMap = false)
		: base(field, viewWindow, pxWidth, pxHeight, timestep)
	{
		ParticleLifetime = 100;
		LineDrawMethod = 2;
		ParticlesPer1kPixels = 1.5f;
		LineRadius = 0.75f;
		HeatmapEnabled = enableHeatMap;
		RungeKuttaMethod = new RasterRungeKutta(RungeKuttaOrder.RK2, Velocities, TimeStep);
		GenerateParticleList();
		_particleBmp = new Bitmap(Width, Height);
		Rectangle rect = new Rectangle(0, 0, Width, Height);
		_particleBmpData = _particleBmp.LockBits(rect, ImageLockMode.ReadWrite, _particleBmp.PixelFormat);
		if (HeatmapEnabled)
		{
			_baseBmp = new Bitmap(Width, Height);
			UpdateHeatMap();
		}
	}

	public TracerField(int pxWidth, int pxHeight, Extent viewWindow, Vector[,] field, float timestep, bool enableHeatMap = false)
		: this(pxWidth, pxHeight, viewWindow, new ArrayStaticVelocityField(field, viewWindow), timestep, enableHeatMap)
	{
	}

	private void GenerateParticleList()
	{
		int particlesPer1kPixelsValidCellsTerm = (int)(ParticlesPer1kPixels * (float)ValidCells.Count / 1000f);
		_particles = new List<Particle>(particlesPer1kPixelsValidCellsTerm);
		AddNewParticles(particlesPer1kPixelsValidCellsTerm);
	}

	private void ResizeParticleList()
	{
		int particlesPer1kPixelsValidCellsTerm = (int)(ParticlesPer1kPixels * (float)ValidCells.Count / 1000f);
		int count = _particles.Count;
		if (count != particlesPer1kPixelsValidCellsTerm)
		{
			if (count > particlesPer1kPixelsValidCellsTerm)
			{
				int countTerm = count - particlesPer1kPixelsValidCellsTerm;
				_particles.RemoveRange(count - countTerm - 1, countTerm);
			}
			else
			{
				int count2 = particlesPer1kPixelsValidCellsTerm - count;
				AddNewParticles(count2);
			}
		}
	}

	protected void AddNewParticles(int count)
	{
		for (int i = 0; i < count; i++)
		{
			double nextDouble = _r.NextDouble();
			double nextDouble2 = _r.NextDouble();
			Pixel pixel = ValidCells[(int)(nextDouble2 * (double)ValidCells.Count)];
			Geospatial.Vectors.Point position = new Geospatial.Vectors.Point((float)pixel.X + 0.5f, (float)pixel.Y + 0.5f);
			_particles.Add(new Particle((int)((float)nextDouble * (float)ParticleLifetime), position));
		}
	}

	public void QueueParticleLifetimeUpdate(int newLifetime)
	{
		_lifetimeIsQueued = true;
		_lifetimeQueuedValue = newLifetime;
	}

	public void QueueRadiusUpdate(float newRadius)
	{
		_radiusIsQueued = true;
		_radiusQueueValue = newRadius;
	}

	public void QueueTimestepUpdate(float newTimestep)
	{
		_tsIsQueued = true;
		_tsQueueValue = newTimestep;
	}

	public void QueueTimestepRelativeToZoomUpdate(bool newValue)
	{
		_tsRelativeToZoomIsQueued = true;
		_tsRelativeTOZoomValue = newValue;
		_tsIsQueued = true;
	}

	public void QueueMethodUpdate(int newMethod)
	{
		_methodIsQueued = true;
		_methodQueuedValue = newMethod;
	}

	public void QueueDensityUpdate(float particlesPer1kPx)
	{
		_densityIsQueued = true;
		_densityQueuedValue = particlesPer1kPx;
	}

	public void QueueRedUpdate(byte r)
	{
		_colorIsQueued = true;
		_rQ = r;
	}

	public void QueueGreenUpdate(byte g)
	{
		_colorIsQueued = true;
		_gQ = g;
	}

	public void QueueBlueUpdate(byte b)
	{
		_colorIsQueued = true;
		_bQ = b;
	}

	public void QueueColorUpdate(byte r, byte g, byte b)
	{
		_colorIsQueued = true;
		_rQ = r;
		_gQ = g;
		_bQ = b;
	}

	public void QueueFieldUpdate(Vector[,] field)
	{
		QueueFieldUpdate(new ArrayStaticVelocityField(field, ViewWindow));
	}

	public void QueueFieldUpdate(IStaticVelocityField field)
	{
		_fieldIsQueued = true;
		_fieldQueueValue = field;
	}

	private void ExecuteUpdateQueue()
	{
		if (_radiusIsQueued)
		{
			LineRadius = _radiusQueueValue;
		}
		if (_methodIsQueued)
		{
			LineDrawMethod = _methodQueuedValue;
		}
		if (_tsIsQueued)
		{
			TimeStep = _tsQueueValue;
		}
		if (_lifetimeIsQueued)
		{
			ParticleLifetime = _lifetimeQueuedValue;
		}
		if (_densityIsQueued)
		{
			ParticlesPer1kPixels = _densityQueuedValue;
		}
		if (_fieldIsQueued)
		{
			Field = _fieldQueueValue;
		}
		if (_tsRelativeToZoomIsQueued)
		{
			TimestepRelativeToZoom = _tsRelativeTOZoomValue;
		}
		if (_colorIsQueued)
		{
			ParticleField.TAIL_R = _rQ;
			ParticleField.TAIL_G = _gQ;
			ParticleField.TAIL_B = _bQ;
			ParticleField.ReinitializeStaticTrackingVariables();
		}
		if (TimestepRelativeToZoom)
		{
			TimeStep = _tsQueueValue;
			float widthOverWidth = (float)((double)Width / ViewWindow.Width);
			TimeStep /= widthOverWidth;
		}
		if (_tsIsQueued || _fieldIsQueued)
		{
			if (_fieldIsQueued)
			{
				Velocities = Field.Rasterize(ViewWindow, Width, Height);
			}
			RungeKuttaMethod = new RasterRungeKutta(RungeKuttaMethod.Order, Velocities, TimeStep);
		}
		if (_lifetimeIsQueued && !_fieldIsQueued)
		{
			foreach (Particle particle in _particles)
			{
				particle.Age = (int)(_r.NextDouble() * (double)ParticleLifetime);
			}
		}
		if (_densityIsQueued && !_fieldIsQueued)
		{
			ResizeParticleList();
		}
		if (_fieldIsQueued)
		{
			ValidCells = GetValidVelocityCells();
			GenerateParticleList();
			if (HeatmapEnabled)
			{
				UpdateHeatMap();
			}
		}
		_fieldIsQueued = false;
		_densityIsQueued = false;
		_lifetimeIsQueued = false;
		_tsIsQueued = false;
		_methodIsQueued = false;
		_radiusIsQueued = false;
		_colorIsQueued = false;
		_tsRelativeToZoomIsQueued = false;
	}

	protected void ResetParticle(int particleIdx, double rand)
	{
		Pixel pixel = ValidCells[(int)(rand * (double)ValidCells.Count)];
		Geospatial.Vectors.Point position = new Geospatial.Vectors.Point((float)pixel.X + 0.5f, (float)pixel.Y + 0.5f);
		_particles[particleIdx] = new Particle(0, position);
	}

	public override Bitmap Update()
	{
		ExecuteUpdateQueue();
		Stopwatch stopwatch = Stopwatch.StartNew();
		Bitmap result = ((!HeatmapEnabled) ? DrawLinesNoHeatmap() : DrawLinesWithHeatmap());
		stopwatch.Stop();
		_frameTime += stopwatch.ElapsedTicks;
		_frameCount++;
		if (_frameCount == 100)
		{
			long frequency = Stopwatch.Frequency;
			_ = (float)_frameTime * 1000f / (float)(frequency * _frameCount);
			_frameTime = 0L;
			_frameCount = 0;
		}
		return result;
	}

	private unsafe Bitmap DrawLinesWithHeatmap()
	{
		Bitmap bitmap = new Bitmap(Width, Height);
		Rectangle rect = new Rectangle(0, 0, Width, Height);
		BitmapData bitmapData = _baseBmp.LockBits(rect, ImageLockMode.ReadWrite, _baseBmp.PixelFormat);
		BitmapData bitmapData2 = bitmap.LockBits(rect, ImageLockMode.ReadWrite, bitmap.PixelFormat);
		byte* scan = (byte*)_particleBmpData.Scan0;
		int* scan2 = (int*)_particleBmpData.Scan0;
		byte* scan3 = (byte*)bitmapData.Scan0;
		byte* scan4 = (byte*)bitmapData2.Scan0;
		MoveParticles(scan2, scan);
		DecayAlphaBlendHeatmap(scan, scan4, scan3);
		_baseBmp.UnlockBits(bitmapData);
		bitmap.UnlockBits(bitmapData2);
		return bitmap;
	}

	[DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint memcpy(nint dest, nint src, int count);

	private unsafe Bitmap DrawLinesNoHeatmap()
	{
		Bitmap bitmap = new Bitmap(Width, Height);
		Rectangle rect = new Rectangle(0, 0, Width, Height);
		BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadWrite, bitmap.PixelFormat);
		byte* scan = (byte*)_particleBmpData.Scan0;
		int* scan2 = (int*)_particleBmpData.Scan0;
		MoveParticles(scan2, scan);
		DecayAlphaNoHeatmap(scan);
		memcpy(bitmapData.Scan0, _particleBmpData.Scan0, Width * Height * 4);
		bitmap.UnlockBits(bitmapData);
		return bitmap;
	}

	private unsafe void MoveParticles(int* particleIntPtr, byte* particleBytePtr)
	{
		int scaledCount = _particles.Count / 100;
		int count = _particles.Count;
		if (_rands.Length < count)
		{
			_rands = new double[count];
		}
		double[] rands = _rands;
		for (int i = 0; i < count; i++)
		{
			rands[i] = _r.NextDouble();
		}
		Parallel.For(0, scaledCount, (int t) =>
		{
			int ordinal = 100 * t;
			int end = ordinal + 100;
			MoveParticleRange(ordinal, end);
		});
		MoveParticleRange(scaledCount * 100, _particles.Count);
		unsafe void MoveParticleRange(int start, int end)
		{
			HashSet<Pixel> candidates = new HashSet<Pixel>();
			for (int j = start; j < end; j++)
			{
				Particle particle = _particles[j];
				Geospatial.Vectors.Point position = particle.Position;
				Vector velocity = GetVelocity(position);
				if (position == Geospatial.Vectors.Point.NullPoint || velocity == Vector.NullVelocity)
				{
					ResetParticle(j, rands[j]);
				}
				else
				{
					Geospatial.Vectors.Point point = RungeKuttaMethod.Step(position);
					if (point == Geospatial.Vectors.Point.NullPoint)
					{
						point = position.Move(velocity * TimeStep);
					}
					if (LineDrawMethod == 1)
					{
						NaiiveBresenhamThickness(position, point, particleIntPtr);
					}
					else
					{
						if (LineDrawMethod != 2)
						{
							throw new NotImplementedException();
						}
						LineWithAA(position, point, particleIntPtr, particleBytePtr, candidates);
					}
					particle.Position = point;
					particle.Age++;
					if (particle.Age > ParticleLifetime)
					{
						ResetParticle(j, rands[j]);
					}
				}
			}
		}
	}

	private unsafe void NaiiveBresenhamThickness(Geospatial.Vectors.Point curPos, Geospatial.Vectors.Point nextPos, int* particleIntPtr)
	{
		BresenhamLine(curPos, nextPos, particleIntPtr);
		if (LineRadius != 0f)
		{
			float widthOverWidth = (float)(ViewWindow.Width / (double)Width);
			Vector vector = nextPos - curPos;
			Vector vector2 = vector.Perpendicular(clockwise: true);
			Vector vector3 = vector.Perpendicular(clockwise: false);
			vector2.Normalize();
			vector3.Normalize();
			Geospatial.Vectors.Point point = curPos + vector2 * LineRadius * widthOverWidth * 0.5f;
			Geospatial.Vectors.Point point2 = curPos + vector3 * LineRadius * widthOverWidth * 0.5f;
			Geospatial.Vectors.Point nextPos2 = point + vector;
			Geospatial.Vectors.Point nextPos3 = point2 + vector;
			BresenhamLine(point, nextPos2, particleIntPtr);
			BresenhamLine(point2, nextPos3, particleIntPtr);
		}
	}

	private unsafe void BresenhamLine(Geospatial.Vectors.Point curPos, Geospatial.Vectors.Point nextPos, int* particleIntPtr)
	{
		Pixel pixel = new Pixel((int)curPos.X, (int)curPos.Y);
		Pixel pixel2 = new Pixel((int)nextPos.X, (int)nextPos.Y);
		if (IsOOB(pixel))
		{
			return;
		}
		int y = pixel.Y;
		int y2 = pixel2.Y;
		int x = pixel.X;
		int x2 = pixel2.X;
		if (pixel == pixel2)
		{
			ColorPixel(particleIntPtr, pixel.X, pixel.Y);
			return;
		}
		if (pixel.X == pixel2.X)
		{
			int ordinal = y;
			int y22 = y2;
			if (y22 < ordinal)
			{
				int ordinal2 = ordinal;
				while (ordinal2 >= y22 && !(GetVelocity(x, ordinal2) == Vector.NullVelocity))
				{
					ColorPixel(particleIntPtr, pixel.X, ordinal2);
					ordinal2--;
				}
			}
			else
			{
				for (int i = ordinal; i <= y22 && !(GetVelocity(x, i) == Vector.NullVelocity); i++)
				{
					ColorPixel(particleIntPtr, pixel.X, i);
				}
			}
			return;
		}
		if (pixel.Y == pixel2.Y)
		{
			int ordinal3 = x;
			int x22 = x2;
			if (x2 > x)
			{
				for (int j = ordinal3; j <= x22 && !(GetVelocity(j, y) == Vector.NullVelocity); j++)
				{
					ColorPixel(particleIntPtr, j, y);
				}
				return;
			}
			int ordinal4 = ordinal3;
			while (ordinal4 >= x22 && !(GetVelocity(ordinal4, y) == Vector.NullVelocity))
			{
				ColorPixel(particleIntPtr, ordinal4, y);
				ordinal4--;
			}
			return;
		}
		int pixelTerm = pixel.X;
		int x3 = pixel2.X;
		int pixelTerm2 = pixel.Y;
		int y3 = pixel2.Y;
		int pixel2PixelTerm = Math.Abs(pixel2.X - pixel.X);
		int pixel2PixelTerm2 = Math.Abs(pixel2.Y - pixel.Y);
		int x3Term = ((pixelTerm < x3) ? 1 : (-1));
		int y3Term = ((pixelTerm2 < y3) ? 1 : (-1));
		int ordinal5 = pixel2PixelTerm - pixel2PixelTerm2;
		while (true)
		{
			Pixel pixel3 = new Pixel(pixelTerm, pixelTerm2);
			if (!IsOOB(pixel3) && !(GetVelocity(pixel3) == Vector.NullVelocity))
			{
				ColorPixel(particleIntPtr, pixelTerm, pixelTerm2);
				if (pixelTerm != x3 || pixelTerm2 != y3)
				{
					int ordinal6 = 2 * ordinal5;
					if (ordinal6 > -pixel2PixelTerm2)
					{
						ordinal5 -= pixel2PixelTerm2;
						pixelTerm += x3Term;
					}
					if (ordinal6 < pixel2PixelTerm)
					{
						ordinal5 += pixel2PixelTerm;
						pixelTerm2 += y3Term;
					}
					continue;
				}
				break;
			}
			break;
		}
	}

	private unsafe void ColorPixel(int* particleIntPtr, int x, int y)
	{
		int heightTerm = Height - y - 1;
		particleIntPtr[Width * heightTerm + x] = ParticleField.TailColors[0];
	}

	private unsafe void ColorPixel(int* particleIntPtr, int x, int y, int lvl)
	{
		int heightTerm = Height - y - 1;
		particleIntPtr[Width * heightTerm + x] = ParticleField.TailColors[lvl];
	}

	private static void AddRing(HashSet<Pixel> candidates, int x, int y, int pxRadius)
	{
		for (int i = x - pxRadius; i <= x + pxRadius; i++)
		{
			for (int j = y - pxRadius; j <= y + pxRadius; j++)
			{
				candidates.Add(new Pixel(i, j));
			}
		}
	}

	private unsafe void LineWithAA(Geospatial.Vectors.Point curPos, Geospatial.Vectors.Point nextPos, int* particleIntPtr, byte* particleBytePtr, HashSet<Pixel> candidates)
	{
		if (curPos == nextPos)
		{
			Pixel pixel = new Pixel((int)curPos.X, (int)curPos.Y);
			int heightPixelTerm = Height - pixel.Y - 1;
			particleIntPtr[Width * heightPixelTerm + pixel.X] = ParticleField.TailColors[0];
			return;
		}
		double nextPosCurPosTerm = nextPos.Y - curPos.Y;
		double nextPosCurPosTerm2 = nextPos.X - curPos.X;
		int curPosTerm = (int)curPos.Y;
		int curPosTerm2 = (int)curPos.X;
		int nextPosTerm = (int)nextPos.Y;
		int nextPosTerm2 = (int)nextPos.X;
		int ordinal = nextPosTerm - curPosTerm;
		int ordinal2 = nextPosTerm2 - curPosTerm2;
		if (IsOOB(curPosTerm2, curPosTerm))
		{
			return;
		}
		int lineRadiusTerm = (int)Math.Ceiling(LineRadius);
		candidates.Clear();
		if (curPosTerm == nextPosTerm && curPosTerm2 == nextPosTerm2)
		{
			for (int i = curPosTerm - lineRadiusTerm; i <= curPosTerm + lineRadiusTerm; i++)
			{
				for (int j = curPosTerm2 - lineRadiusTerm; j <= curPosTerm2 + lineRadiusTerm; j++)
				{
					candidates.Add(new Pixel(j, i));
				}
			}
		}
		else if (curPosTerm2 == nextPosTerm2)
		{
			int ordinal3 = curPosTerm;
			int ordinal4 = nextPosTerm;
			if (nextPosTerm > curPosTerm)
			{
				ordinal3 -= lineRadiusTerm;
				ordinal4 += lineRadiusTerm;
				for (int k = ordinal3; k <= ordinal4 && !(GetVelocity(curPosTerm2, k) == Vector.NullVelocity); k++)
				{
					for (int l = curPosTerm2 - lineRadiusTerm; l <= curPosTerm2 + lineRadiusTerm; l++)
					{
						candidates.Add(new Pixel(l, k));
					}
				}
			}
			else
			{
				ordinal3 += lineRadiusTerm;
				ordinal4 -= lineRadiusTerm;
				int ordinal5 = ordinal3;
				while (ordinal5 >= ordinal4 && !(GetVelocity(curPosTerm2, ordinal5) == Vector.NullVelocity))
				{
					for (int m = curPosTerm2 - lineRadiusTerm; m <= curPosTerm2 + lineRadiusTerm; m++)
					{
						candidates.Add(new Pixel(m, ordinal5));
					}
					ordinal5--;
				}
			}
		}
		else if (curPosTerm == nextPosTerm)
		{
			int ordinal6 = curPosTerm2;
			int ordinal7 = nextPosTerm2;
			if (nextPosTerm2 > curPosTerm2)
			{
				ordinal6 -= lineRadiusTerm;
				ordinal7 += lineRadiusTerm;
				for (int n = curPosTerm - lineRadiusTerm; n <= curPosTerm + lineRadiusTerm; n++)
				{
					for (int ordinal8 = ordinal6; ordinal8 <= ordinal7 && (n != curPosTerm - lineRadiusTerm || !(GetVelocity(ordinal8, curPosTerm) == Vector.NullVelocity)); ordinal8++)
					{
						candidates.Add(new Pixel(ordinal8, n));
					}
				}
			}
			else
			{
				ordinal6 += lineRadiusTerm;
				ordinal7 -= lineRadiusTerm;
				for (int ordinal9 = curPosTerm - lineRadiusTerm; ordinal9 <= curPosTerm + lineRadiusTerm; ordinal9++)
				{
					int ordinal10 = ordinal6;
					while (ordinal10 >= ordinal7 && (ordinal9 != curPosTerm - lineRadiusTerm || !(GetVelocity(ordinal10, curPosTerm) == Vector.NullVelocity)))
					{
						candidates.Add(new Pixel(ordinal10, ordinal9));
						ordinal10--;
					}
				}
			}
		}
		else
		{
			int ordinal11 = curPosTerm2;
			int ordinal12 = curPosTerm;
			AddRing(candidates, ordinal11, ordinal12, lineRadiusTerm);
			if (ordinal2 > 0)
			{
				if (ordinal < 0)
				{
					while ((ordinal11 != nextPosTerm2 || ordinal12 != nextPosTerm) && !(GetVelocity(ordinal11, ordinal12) == Vector.NullVelocity))
					{
						double curPosTerm3 = ((double)ordinal12 - curPos.Y) / nextPosCurPosTerm;
						double curPosTerm4 = ((double)(ordinal11 + 1) - curPos.X) / nextPosCurPosTerm2;
						if ((curPosTerm3 < curPosTerm4 && ordinal12 > nextPosTerm) || ordinal11 == nextPosTerm2)
						{
							ordinal12--;
						}
						else
						{
							ordinal11++;
						}
						AddRing(candidates, ordinal11, ordinal12, lineRadiusTerm);
					}
				}
				else
				{
					while ((ordinal11 != nextPosTerm2 || ordinal12 != nextPosTerm) && !(GetVelocity(ordinal11, ordinal12) == Vector.NullVelocity))
					{
						double curPosTerm5 = ((double)(ordinal12 + 1) - curPos.Y) / nextPosCurPosTerm;
						double curPosTerm6 = ((double)(ordinal11 + 1) - curPos.X) / nextPosCurPosTerm2;
						if ((curPosTerm5 < curPosTerm6 && ordinal12 < nextPosTerm) || ordinal11 == nextPosTerm2)
						{
							ordinal12++;
						}
						else
						{
							ordinal11++;
						}
						AddRing(candidates, ordinal11, ordinal12, lineRadiusTerm);
					}
				}
			}
			else if (ordinal < 0)
			{
				while ((ordinal11 != nextPosTerm2 || ordinal12 != nextPosTerm) && !(GetVelocity(ordinal11, ordinal12) == Vector.NullVelocity))
				{
					double curPosTerm7 = ((double)ordinal12 - curPos.Y) / nextPosCurPosTerm;
					double curPosTerm8 = ((double)ordinal11 - curPos.X) / nextPosCurPosTerm2;
					if ((curPosTerm7 < curPosTerm8 && ordinal12 > nextPosTerm) || ordinal11 == nextPosTerm2)
					{
						ordinal12--;
					}
					else
					{
						ordinal11--;
					}
					AddRing(candidates, ordinal11, ordinal12, lineRadiusTerm);
				}
			}
			else
			{
				while ((ordinal11 != nextPosTerm2 || ordinal12 != nextPosTerm) && !(GetVelocity(ordinal11, ordinal12) == Vector.NullVelocity))
				{
					double curPosTerm9 = ((double)(ordinal12 + 1) - curPos.Y) / nextPosCurPosTerm;
					double curPosTerm10 = ((double)ordinal11 - curPos.X) / nextPosCurPosTerm2;
					if ((curPosTerm9 < curPosTerm10 && ordinal12 < nextPosTerm) || ordinal11 == nextPosTerm2)
					{
						ordinal12++;
					}
					else
					{
						ordinal11--;
					}
					AddRing(candidates, ordinal11, ordinal12, lineRadiusTerm);
				}
			}
		}
		double computed = 0.0;
		if (nextPosCurPosTerm2 != 0.0)
		{
			computed = Math.Abs(nextPosCurPosTerm / nextPosCurPosTerm2);
		}
		double computed2 = Math.Sqrt(nextPosCurPosTerm2 * nextPosCurPosTerm2 + nextPosCurPosTerm * nextPosCurPosTerm);
		foreach (Pixel candidate in candidates)
		{
			if (IsOOB(candidate))
			{
				continue;
			}
			double candidateCurPosTerm = (double)candidate.X + 0.5 - curPos.X;
			double candidateCurPosTerm2 = (double)candidate.Y + 0.5 - curPos.Y;
			double computed3 = Math.Abs((nextPosCurPosTerm2 * candidateCurPosTerm2 - nextPosCurPosTerm * candidateCurPosTerm) / computed2);
			int widthHeightTerm = (Width * (Height - candidate.Y - 1) + candidate.X) * 4 + 3;
			byte b = particleBytePtr[widthHeightTerm];
			if (computed > 0.5 && computed < 2.0)
			{
				for (int intervalIndex = 0; intervalIndex < ParticleField.DiagDivValues.Length; intervalIndex++)
				{
					if (computed3 < (double)(LineRadius + ParticleField.DiagDivValues[intervalIndex]))
					{
						ColorPixel(particleIntPtr, candidate.X, candidate.Y, intervalIndex);
						break;
					}
				}
			}
			else
			{
				for (int intervalIndex2 = 0; intervalIndex2 < ParticleField.VertDivValues.Length; intervalIndex2++)
				{
					if (computed3 < (double)(LineRadius + ParticleField.VertDivValues[intervalIndex2]))
					{
						ColorPixel(particleIntPtr, candidate.X, candidate.Y, intervalIndex2);
						break;
					}
				}
			}
			byte b2 = particleBytePtr[widthHeightTerm];
			if (b > b2)
			{
				particleBytePtr[widthHeightTerm] = b;
			}
		}
	}

	private unsafe void DecayAlphaBlendHeatmap(byte* particleBytePtr, byte* resultPtr, byte* texturePtr)
	{
		_ = Width;
		_ = Height;
		int widthTimesHeight = Width * Height;
		int pxPerThread = Width;
		int toExclusive = widthTimesHeight / pxPerThread;
		Parallel.For(0, toExclusive, (int t) =>
		{
			int pxPerThreadTerm = t * pxPerThread * 4;
			int count = pxPerThreadTerm + 4 * pxPerThread;
			for (int i = pxPerThreadTerm; i < count; i += 4)
			{
				byte b = particleBytePtr[i + 3];
				if (b > 3)
				{
					b -= 3;
					particleBytePtr[i + 3] = b;
				}
				else
				{
					b = 0;
					particleBytePtr[i + 3] = 0;
				}
				float computed = (float)(int)b / 255f;
				float computed2 = 1f - computed;
				resultPtr[i] = (byte)((float)ParticleField.TAIL_B * computed + (float)(int)texturePtr[i] * computed2);
				resultPtr[i + 1] = (byte)((float)ParticleField.TAIL_G * computed + (float)(int)texturePtr[i + 1] * computed2);
				resultPtr[i + 2] = (byte)((float)ParticleField.TAIL_R * computed + (float)(int)texturePtr[i + 2] * computed2);
				resultPtr[i + 3] = byte.MaxValue;
			}
		});
	}

	private unsafe void DecayAlphaNoHeatmap(byte* particleBytePtr)
	{
		_ = Width;
		_ = Height;
		int widthTimesHeight = Width * Height;
		int pxPerThread = Width;
		int toExclusive = widthTimesHeight / pxPerThread;
		Parallel.For(0, toExclusive, (int t) =>
		{
			int pxPerThreadTerm = t * pxPerThread * 4;
			int count = pxPerThreadTerm + 4 * pxPerThread;
			for (int i = pxPerThreadTerm; i < count; i += 4)
			{
				byte b = particleBytePtr[i + 3];
				if (b > 3)
				{
					b -= 3;
					particleBytePtr[i + 3] = b;
				}
				else
				{
					b = 0;
					particleBytePtr[i + 3] = 0;
				}
			}
		});
	}

	private unsafe void DecayAlphaNoHeatmapV2(byte* particleBytePtr)
	{
		throw new NotImplementedException();
	}
}
