using System.Runtime.CompilerServices;

namespace RasMapperLib;

public class BilinearInterpolation
{
	public struct Weights
	{
		public float LowerLeft;

		public float LowerRight;

		public float UpperLeft;

		public float UpperRight;
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__27_002D0
	{
		public int topIdx;

		public int leftIdx;

		public int rightIdx;

		public float ulX;

		public float dx;

		public float ulY;

		public float dy;

		public Weights[,] retn;

		public _Closure_0024__27_002D0(_Closure_0024__27_002D0 other)
		{
			if (other != null)
			{
				topIdx = other.topIdx;
				leftIdx = other.leftIdx;
				rightIdx = other.rightIdx;
				ulX = other.ulX;
				dx = other.dx;
				ulY = other.ulY;
				dy = other.dy;
				retn = other.retn;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			checked
			{
				int startTerm = topIdx + start;
				int count = topIdx + finish;
				for (int i = startTerm; i <= count; i++)
				{
					int leftIdx = this.leftIdx;
					int rightIdx = this.rightIdx;
					for (int j = leftIdx; j <= rightIdx; j++)
					{
						int ordinal = i - topIdx;
						int ordinal2 = j - leftIdx;
						float unity = ulX + dx * (float)ordinal2;
						float unity2 = ulY + dy * (float)ordinal;
						if (unity < 0f)
						{
							unity = 0f;
						}
						if (unity > 1f)
						{
							unity = 1f;
						}
						if (unity2 < 0f)
						{
							unity2 = 0f;
						}
						if (unity2 > 1f)
						{
							unity2 = 1f;
						}
						float computed = 1f - unity2;
						float computed2 = 1f - unity;
						Weights weights = new Weights
						{
							LowerLeft = computed2 * computed,
							LowerRight = unity * computed,
							UpperLeft = computed2 * unity2,
							UpperRight = unity * unity2
						};
						retn[ordinal, ordinal2] = weights;
					}
				}
			}
		}
	}

	private Point2D _upperLeft;

	private float _width;

	private float _height;

	private float _bottom;

	public Point2D UpperLeft => _upperLeft;

	public Point2D UpperRight => new Point2D(_upperLeft.X + _width, _upperLeft.Y);

	public Point2D LowerLeft => new Point2D(_upperLeft.X, _upperLeft.Y - _height);

	public Point2D LowerRight => new Point2D(_upperLeft.X + _width, _upperLeft.Y - _height);

	public float Left => _upperLeft.X;

	public float Right => _upperLeft.X + _width;

	public float Top => _upperLeft.Y;

	public float Bottom => _upperLeft.Y - _height;

	public float Width => _width;

	public float Height => _height;

	public BilinearInterpolation(Point2D upperLeft, float width)
	{
		_upperLeft = upperLeft;
		_width = width;
		_height = width;
		_bottom = _upperLeft.Y - _height;
	}

	public BilinearInterpolation(Point2D upperLeft, Point2D lowerRight)
	{
		_upperLeft = upperLeft;
		_width = lowerRight.X - upperLeft.X;
		_height = upperLeft.Y - lowerRight.Y;
		_bottom = _upperLeft.Y - _height;
	}

	public Weights Sample(Point2D query)
	{
		float unity = (query.X - _upperLeft.X) / _width;
		float unity2 = (query.Y - _bottom) / _height;
		if (unity < 0f)
		{
			unity = 0f;
		}
		if (unity > 1f)
		{
			unity = 1f;
		}
		if (unity2 < 0f)
		{
			unity2 = 0f;
		}
		if (unity2 > 1f)
		{
			unity2 = 1f;
		}
		float computed = 1f - unity2;
		float computed2 = 1f - unity;
		return new Weights
		{
			LowerLeft = computed2 * computed,
			LowerRight = unity * computed,
			UpperLeft = computed2 * unity2,
			UpperRight = unity * unity2
		};
	}

	public Weights[,] MultiSample(RasterM backgroundRaster, int leftIdx, int rightIdx, int topIdx, int botIdx)
	{
		_Closure_0024__27_002D0 capturedLocals = new _Closure_0024__27_002D0(null);
		capturedLocals.leftIdx = leftIdx;
		capturedLocals.rightIdx = rightIdx;
		capturedLocals.topIdx = topIdx;
		checked
		{
			capturedLocals.retn = new Weights[botIdx - capturedLocals.topIdx + 1, capturedLocals.rightIdx - capturedLocals.leftIdx + 1];
			float cellCenterX = (float)backgroundRaster.CellCenterX(capturedLocals.leftIdx);
			float cellCenterY = (float)backgroundRaster.CellCenterY(capturedLocals.topIdx);
			capturedLocals.ulX = (cellCenterX - _upperLeft.X) / _width;
			capturedLocals.ulY = (cellCenterY - _bottom) / _height;
			capturedLocals.dx = (float)(backgroundRaster.CellSize / (double)_width);
			capturedLocals.dy = (float)(-1.0 * backgroundRaster.CellSize / (double)_height);
			for (int i = capturedLocals.topIdx; i <= botIdx; i++)
			{
				int capturedLocals2 = capturedLocals.leftIdx;
				int count = capturedLocals.rightIdx;
				for (int j = capturedLocals2; j <= count; j++)
				{
					int capturedLocals3 = i - capturedLocals.topIdx;
					int capturedLocals4 = j - capturedLocals.leftIdx;
					float unity = capturedLocals.ulX + capturedLocals.dx * (float)capturedLocals4;
					float unity2 = capturedLocals.ulY + capturedLocals.dy * (float)capturedLocals3;
					if (unity < 0f)
					{
						unity = 0f;
					}
					if (unity > 1f)
					{
						unity = 1f;
					}
					if (unity2 < 0f)
					{
						unity2 = 0f;
					}
					if (unity2 > 1f)
					{
						unity2 = 1f;
					}
					float computed = 1f - unity2;
					float computed2 = 1f - unity;
					Weights weights = new Weights
					{
						LowerLeft = computed2 * computed,
						LowerRight = unity * computed,
						UpperLeft = computed2 * unity2,
						UpperRight = unity * unity2
					};
					capturedLocals.retn[capturedLocals3, capturedLocals4] = weights;
				}
			}
			return capturedLocals.retn;
		}
	}
}
