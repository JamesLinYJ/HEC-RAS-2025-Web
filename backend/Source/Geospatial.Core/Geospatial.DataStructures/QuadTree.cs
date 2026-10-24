using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.DataStructures;

public class QuadTree<T>
{
	public static readonly QuadTree<T> NullQuadTree = new QuadTree<T>(0, 0, 0);

	private RootBinLevel<T> _grid;

	private int _proposedBinSize;

	private int _proposedNumBins;

	private int _maxBinsRoot;

	public int RootBinCount { get; private set; }

	public bool Dirty
	{
		get
		{
			bool dirty = _grid.Dirty;
			_grid.Dirty = false;
			return dirty;
		}
	}

	public QuadTree(Point boundingBoLeft, Point boundingBoRight, int numBins, int binSize, int maxBins)
	{
		_grid = new RootBinLevel<T>(boundingBoLeft.X, boundingBoLeft.Y, boundingBoRight.X, boundingBoRight.Y, numBins, binSize);
		RootBinCount = _grid.BinCount;
		_maxBinsRoot = maxBins;
		if (RootBinCount > _maxBinsRoot)
		{
			_grid = ResizeGridLessBins();
			RootBinCount = _grid.BinCount;
		}
	}

	public bool PeekDirty()
	{
		return _grid.Dirty;
	}

	public QuadTree(int numBins, int binSize, int maxBins)
	{
		_maxBinsRoot = maxBins;
		_proposedBinSize = binSize;
		_proposedNumBins = numBins;
	}

	public QuadTree(Point boundingBoLeft, Point boundingBoRight)
	{
		_grid = new RootBinLevel<T>(boundingBoLeft.X, boundingBoLeft.Y, boundingBoRight.X, boundingBoRight.Y, 64, 64);
		RootBinCount = _grid.BinCount;
		_maxBinsRoot = 100;
		if (RootBinCount > _maxBinsRoot)
		{
			_grid = ResizeGridLessBins();
			RootBinCount = _grid.BinCount;
		}
	}

	public QuadTree(double BoundingBoxLeft, double BoundingBoxBottom, double BoundingBoxRight, double BoundingBoxTop, int numBins, int binSize, int maxBins)
	{
		if (double.IsNaN(BoundingBoxLeft) || double.IsNaN(BoundingBoxRight) || double.IsNaN(BoundingBoxTop) || double.IsNaN(BoundingBoxBottom))
		{
			throw new ArgumentException("Cannot have NaN as a bounds");
		}
		_grid = new RootBinLevel<T>(BoundingBoxLeft, BoundingBoxBottom, BoundingBoxRight, BoundingBoxTop, numBins, binSize);
		RootBinCount = _grid.BinCount;
		_maxBinsRoot = maxBins;
		if (RootBinCount > _maxBinsRoot)
		{
			_grid = ResizeGridLessBins();
			RootBinCount = _grid.BinCount;
		}
	}

	public QuadTree(double BoundingBoxLeft, double BoundingBoxBottom, double BoundingBoxRight, double BoundingBoxTop)
	{
		if (double.IsNaN(BoundingBoxLeft) || double.IsNaN(BoundingBoxRight) || double.IsNaN(BoundingBoxTop) || double.IsNaN(BoundingBoxBottom))
		{
			throw new ArgumentException("Cannot have NaN as a bounds");
		}
		_grid = new RootBinLevel<T>(BoundingBoxLeft, BoundingBoxBottom, BoundingBoxRight, BoundingBoxTop, 64, 64);
		RootBinCount = _grid.BinCount;
		_maxBinsRoot = 100;
		if (RootBinCount > _maxBinsRoot)
		{
			_grid = ResizeGridLessBins();
			RootBinCount = _grid.BinCount;
		}
	}

	public void Insert(double xCart, double yCart, T localValue)
	{
		if (this == NullQuadTree || double.IsNaN(xCart) || double.IsNaN(yCart))
		{
			return;
		}
		if (_grid == null)
		{
			_grid = new RootBinLevel<T>(xCart - 0.001, yCart - 0.001, xCart, yCart, _proposedNumBins, _proposedBinSize);
			RootBinCount = _grid.BinCount;
			if (RootBinCount > _maxBinsRoot)
			{
				_grid = ResizeGridLessBins();
				RootBinCount = _grid.BinCount;
			}
		}
		RootBinLevel<T> grid = _grid.SetPoint(xCart, yCart, localValue, _maxBinsRoot);
		_grid = grid;
		RootBinCount = _grid.BinCount;
	}

	private RootBinLevel<T> ResizeGridLessBins()
	{
		int rowCount = _grid.RowCount;
		int colCount = _grid.ColCount;
		int rowCount2 = rowCount;
		int colCount2 = colCount;
		int binCount = _grid.BinCount;
		double maxX = _grid.MaxX;
		double maxY = _grid.MaxY;
		while (binCount > _maxBinsRoot)
		{
			double maxXMinXTerm = maxX - _grid.MinX + 1.0;
			double maxYMinYTerm = maxY - _grid.MinY + 1.0;
			double colCount2Term;
			if (colCount2 > rowCount2)
			{
				colCount2Term = (maxXMinXTerm - 1.0) / (double)colCount2;
			}
			else
			{
				colCount2Term = ((colCount2 >= rowCount2) ? ((maxXMinXTerm - 1.0) / (double)colCount2) : ((maxYMinYTerm - 1.0) / (double)rowCount2));
			}
			if (rowCount % 2 != 0)
			{
				maxY = _grid.MaxY + colCount2Term;
				rowCount2++;
				binCount += colCount2;
			}
			if (colCount % 2 != 0)
			{
				maxX = _grid.MaxX + colCount2Term;
				colCount2++;
				binCount += rowCount2;
			}
			rowCount2 /= 2;
			colCount2 /= 2;
			binCount /= 4;
		}
		return new RootBinLevel<T>(_grid.MinX, _grid.MinY, maxX, maxY, binCount, _grid.BinSize, colCount2, rowCount2, createGrid: true);
	}

	public void QueryRectangle(double Left, double Bottom, double Right, double Top, out List<Bin<T>> partialCover, out List<Bin<T>> fullyCovered)
	{
		if (this == NullQuadTree || double.IsNaN(Left) || double.IsNaN(Right) || double.IsNaN(Top) || double.IsNaN(Bottom))
		{
			partialCover = new List<Bin<T>>();
			fullyCovered = new List<Bin<T>>();
		}
		else
		{
			_grid.QueryRectangle(Left, Bottom, Right, Top, out partialCover, out fullyCovered);
		}
	}

	public List<T> GetAllValuesWithinRectangle(double Left, double Bottom, double Right, double Top)
	{
		if (this == NullQuadTree || double.IsNaN(Left) || double.IsNaN(Right) || double.IsNaN(Top) || double.IsNaN(Bottom))
		{
			return new List<T>();
		}
		return _grid.QueryRectangleReturnKeys(Left, Bottom, Right, Top);
	}

	public List<Point> GetAllCoordinatesWithinRectangle(double Left, double Bottom, double Right, double Top)
	{
		if (this == NullQuadTree || double.IsNaN(Left) || double.IsNaN(Right) || double.IsNaN(Top) || double.IsNaN(Bottom))
		{
			return new List<Point>();
		}
		return _grid.QueryRectangleReturnCoordinates(Left, Bottom, Right, Top);
	}

	public List<Tuple<T, Point>> GetAllValuesAndCoordinatesWithinRectangle(double Left, double Bottom, double Right, double Top)
	{
		if (this == NullQuadTree || double.IsNaN(Left) || double.IsNaN(Right) || double.IsNaN(Top) || double.IsNaN(Bottom))
		{
			return new List<Tuple<T, Point>>();
		}
		return _grid.QueryRectangleReturnKeysAndCoordinates(Left, Bottom, Right, Top);
	}

	public int QueryRectangleValueCount(double Left, double Bottom, double Right, double Top)
	{
		if (this == NullQuadTree || double.IsNaN(Left) || double.IsNaN(Right) || double.IsNaN(Top) || double.IsNaN(Bottom))
		{
			return 0;
		}
		return _grid.QueryRectangleCount(Left, Bottom, Right, Top);
	}

	public int QueryRectangleValueRoughCount(double Left, double Bottom, double Right, double Top)
	{
		if (this == NullQuadTree || double.IsNaN(Left) || double.IsNaN(Right) || double.IsNaN(Top) || double.IsNaN(Bottom))
		{
			return 0;
		}
		return _grid.QueryRectangleRoughCount(Left, Bottom, Right, Top);
	}

	public bool CoordinateExists(double xCart, double yCart)
	{
		if (this == NullQuadTree || double.IsNaN(xCart) || double.IsNaN(yCart))
		{
			return false;
		}
		return _grid.QueryPointReturnCoordinate(xCart, yCart).Count != 0;
	}

	public void QueryCircle(double xCenter, double yCenter, double radius, out List<Bin<T>> partialCover, out List<Bin<T>> fullyCovered)
	{
		if (this == NullQuadTree || double.IsNaN(xCenter) || double.IsNaN(yCenter) || double.IsNaN(radius))
		{
			partialCover = new List<Bin<T>>();
			fullyCovered = new List<Bin<T>>();
		}
		else
		{
			_grid.QueryCircle(xCenter, yCenter, radius, out partialCover, out fullyCovered);
		}
	}

	public List<T> GetAllValuesWithinCircle(double xCenter, double yCenter, double radius)
	{
		if (this == NullQuadTree || double.IsNaN(xCenter) || double.IsNaN(yCenter) || double.IsNaN(radius))
		{
			return new List<T>();
		}
		return _grid.QueryCircleReturnKeys(xCenter, yCenter, radius);
	}

	public List<Point> GetAllCoordinatesWithinCircle(double xCenter, double yCenter, double radius)
	{
		if (this == NullQuadTree || double.IsNaN(xCenter) || double.IsNaN(yCenter) || double.IsNaN(radius))
		{
			return new List<Point>();
		}
		return _grid.QueryCircleReturnCoordinates(xCenter, yCenter, radius);
	}

	public List<Tuple<T, Point>> GetAllValuesAndCoordinatesWithinCircle(double xCenter, double yCenter, double radius)
	{
		if (this == NullQuadTree || double.IsNaN(xCenter) || double.IsNaN(yCenter) || double.IsNaN(radius))
		{
			return new List<Tuple<T, Point>>();
		}
		return _grid.QueryCircleReturnKeysAndCoordinates(xCenter, yCenter, radius);
	}

	public void QueryCircleNearestPointToCenter(double xCenter, double yCenter, double radius, out double distanceBetweenPts, out List<T> keys, out Point coords)
	{
		if (this == NullQuadTree || double.IsNaN(xCenter) || double.IsNaN(yCenter) || double.IsNaN(radius))
		{
			distanceBetweenPts = 0.0;
			keys = new List<T>();
			coords = default;
		}
		else
		{
			_grid.QueryCircleNearestPointToCenter(xCenter, yCenter, radius, out distanceBetweenPts, out keys, out coords);
		}
	}

	public List<T> GetAllValuesFromPoint(double xCart, double yCart)
	{
		if (this == NullQuadTree || double.IsNaN(xCart) || double.IsNaN(yCart))
		{
			return new List<T>();
		}
		return _grid.GetAllValuesFromPoint(xCart, yCart);
	}

	public List<T> GetValueFromPoint(double xCart, double yCart)
	{
		if (this == NullQuadTree || double.IsNaN(xCart) || double.IsNaN(yCart))
		{
			return new List<T>();
		}
		return _grid.QueryPointReturnKey(xCart, yCart);
	}

	public List<T> GetAllValuesFromLine(double xCart1, double yCart1, double xCart2, double yCart2)
	{
		if (this == NullQuadTree || double.IsNaN(xCart1) || double.IsNaN(yCart1) || double.IsNaN(xCart2) || double.IsNaN(yCart2))
		{
			return new List<T>();
		}
		return _grid.QueryLineReturnKeys(xCart1, yCart1, xCart2, yCart2);
	}

	public List<Point> GetAllCoordinatesFromLine(double xCart1, double yCart1, double xCart2, double yCart2)
	{
		if (this == NullQuadTree || double.IsNaN(xCart1) || double.IsNaN(yCart1) || double.IsNaN(xCart2) || double.IsNaN(yCart2))
		{
			return new List<Point>();
		}
		return _grid.QueryLineReturnCoordinates(xCart1, yCart1, xCart2, yCart2);
	}

	public List<Tuple<T, Point>> GetAllValuesAndCoordinatesFromLine(double xCart1, double yCart1, double xCart2, double yCart2)
	{
		if (this == NullQuadTree || double.IsNaN(xCart1) || double.IsNaN(yCart1) || double.IsNaN(xCart2) || double.IsNaN(yCart2))
		{
			return new List<Tuple<T, Point>>();
		}
		return _grid.QueryLineReturnKeysAndCoordinates(xCart1, yCart1, xCart2, yCart2);
	}

	public Bin<T> QueryPoint(double xCart, double yCart)
	{
		if (this == NullQuadTree || double.IsNaN(xCart) || double.IsNaN(yCart))
		{
			return new Bin<T>();
		}
		return _grid.QueryPoint(xCart, yCart);
	}

	public List<Bin<T>> GetAllLeafBins(double Left, double Bottom, double Right, double Top)
	{
		if (this == NullQuadTree || double.IsNaN(Left) || double.IsNaN(Right) || double.IsNaN(Top) || double.IsNaN(Bottom))
		{
			return new List<Bin<T>>();
		}
		return _grid.GetAllLeafBins(Left, Bottom, Right, Top);
	}

	public List<Bin<T>> GetAllLeafBins()
	{
		if (this == NullQuadTree)
		{
			return new List<Bin<T>>();
		}
		return _grid.GetAllLeafBins(_grid.MinX, _grid.MinY, _grid.MaxX, _grid.MaxY);
	}

	public Dictionary<Bin<T>, List<Bin<T>>> GetAllNeighborsOfBins()
	{
		if (this == NullQuadTree)
		{
			return new Dictionary<Bin<T>, List<Bin<T>>>();
		}
		return _grid.GetAllNeighborsOfBins();
	}

	public List<Bin<T>> QueryLine(double xCart1, double yCart1, double xCart2, double yCart2)
	{
		if (this == NullQuadTree)
		{
			return new List<Bin<T>>();
		}
		return _grid.QueryLine(xCart1, yCart1, xCart2, yCart2);
	}

	public List<T> RemoveCoordinate(double xCart, double yCart)
	{
		if (this == NullQuadTree || double.IsNaN(xCart) || double.IsNaN(yCart))
		{
			return new List<T>();
		}
		return _grid.RemovePoint(xCart, yCart);
	}

	public List<T> RemoveCoordinate(double xCart, double yCart, double error)
	{
		if (this == NullQuadTree || double.IsNaN(xCart) || double.IsNaN(yCart) || double.IsNaN(error))
		{
			return new List<T>();
		}
		return _grid.RemovePointWithError(xCart, yCart, error);
	}

	public bool RemoveCoordinate(double xCart, double yCart, double error, T localValue)
	{
		if (this == NullQuadTree || double.IsNaN(xCart) || double.IsNaN(yCart) || double.IsNaN(error))
		{
			return false;
		}
		return _grid.RemovePointWithError(xCart, yCart, error, localValue);
	}

	public bool RemoveCoordinate(double xCart, double yCart, T localValue)
	{
		if (this == NullQuadTree || double.IsNaN(xCart) || double.IsNaN(yCart))
		{
			return false;
		}
		return _grid.RemovePoint(xCart, yCart, localValue);
	}

	public List<T> GetAllValuesFromBin(Bin<T> bin)
	{
		if (this == NullQuadTree)
		{
			return new List<T>();
		}
		return _grid.GetAllValuesFromBin(bin);
	}

	public void ModifyKeysParallel(Func<T, T> modification)
	{
		if (this != NullQuadTree)
		{
			_grid.ModifyKeysParallel(modification);
		}
	}

	public void ModifyKeys(Func<T, T> modification)
	{
		if (this != NullQuadTree)
		{
			_grid.ModifyKeys(modification);
		}
	}

	public double Left()
	{
		if (this == NullQuadTree)
		{
			return double.NaN;
		}
		return _grid.MinX;
	}

	public double Bottom()
	{
		if (this == NullQuadTree)
		{
			return double.NaN;
		}
		return _grid.MinY;
	}

	public double Right()
	{
		if (this == NullQuadTree)
		{
			return double.NaN;
		}
		return _grid.MaxX;
	}

	public double Top()
	{
		if (this == NullQuadTree)
		{
			return double.NaN;
		}
		return _grid.MaxY;
	}
}
