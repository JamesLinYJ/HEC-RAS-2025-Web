using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Vectors;

namespace Geospatial.DataStructures;

internal class RootBinLevel<T>
{
	internal int NextIDNumber;

	internal bool Dirty;

	private double _sizeX;

	private double _sizeY;

	internal double SmallestBinSideLength;

	public int BinCount { get; private set; }

	public int RowCount { get; private set; }

	public int ColCount { get; private set; }

	public double BinSideLen { get; private set; }

	public double MinX { get; private set; }

	public double MinY { get; private set; }

	public double MaxX { get; private set; }

	public double MaxY { get; private set; }

	public int BinSize { get; private set; }

	public Bin<T>[,] BinArr { get; private set; }

	public IEnumerable<Bin<T>> Bins
	{
		get
		{
			for (int i = 0; i < BinArr.GetLength(0); i++)
			{
				for (int j = 0; j < BinArr.GetLength(1); j++)
				{
					yield return BinArr[i, j];
				}
			}
		}
	}

	internal RootBinLevel(double minX, double minY, double maxX, double maxY, int numBins, int binSize)
	{
		Dirty = true;
		BinSize = binSize;
		if (minX > maxX || minY > maxY)
		{
			throw new Exception("Min coordinates cannot be bigger than max coordinates.");
		}
		if (maxX == minX && maxY == minY)
		{
			numBins = 1;
		}
		if (maxX == minX)
		{
			if (maxX == 0.0)
			{
				maxX += 0.01;
				minX -= 0.01;
			}
			else if (maxX > 0.0)
			{
				maxX *= 1.01;
				minX *= 0.99;
			}
			else
			{
				maxX *= 0.99;
				minX *= 1.01;
			}
		}
		if (maxY == minY)
		{
			if (maxY == 0.0)
			{
				maxY += 0.01;
				minY -= 0.01;
			}
			else if (maxY > 0.0)
			{
				maxY *= 1.01;
				minY *= 0.99;
			}
			else
			{
				maxY *= 0.99;
				minY *= 1.01;
			}
		}
		MinX = minX;
		MinY = minY;
		_sizeX = maxX - minX;
		_sizeY = maxY - minY;
		MaxX = maxX;
		MaxY = maxY;
		double sizeXOverSizeY = _sizeX / _sizeY;
		int rowCount;
		int colCount;
		if (numBins <= 1)
		{
			rowCount = 1;
			colCount = 1;
			numBins = 1;
		}
		else if (minY == maxY)
		{
			rowCount = 1;
			colCount = numBins;
		}
		else if (minX == maxX)
		{
			rowCount = numBins;
			colCount = 1;
		}
		else
		{
			double sqrtNumBinsOverSizeXOverSizeY = Math.Sqrt((double)numBins / sizeXOverSizeY);
			colCount = (int)Math.Floor(sqrtNumBinsOverSizeXOverSizeY * sizeXOverSizeY);
			rowCount = (int)Math.Floor(sqrtNumBinsOverSizeXOverSizeY);
			if (rowCount == 0)
			{
				rowCount = 1;
				colCount = numBins;
			}
			else if (colCount == 0)
			{
				colCount = 1;
				rowCount = numBins;
			}
		}
		BinCount = rowCount * colCount;
		RowCount = rowCount;
		ColCount = colCount;
		BinArr = new Bin<T>[rowCount, colCount];
		double binSideLenSizeXTerm = (BinSideLen = Math.Max(_sizeX / (double)colCount, _sizeY / (double)rowCount));
		SmallestBinSideLength = BinSideLen;
		MaxX = MinX + (double)ColCount * BinSideLen;
		MaxY = MinY + (double)RowCount * BinSideLen;
		_sizeX = MaxX - MinX;
		_sizeY = MaxY - MinY;
		for (int i = 0; i < rowCount; i++)
		{
			for (int j = 0; j < colCount; j++)
			{
				double minXTerm = MinX + (double)j * binSideLenSizeXTerm;
				double minYTerm = MinY + (double)(i + 1) * binSideLenSizeXTerm;
				double computed = minXTerm + binSideLenSizeXTerm;
				double computed2 = minYTerm - binSideLenSizeXTerm;
				double x = (minXTerm + computed) / 2.0;
				double y = (minYTerm + computed2) / 2.0;
				Point center = new Point(x, y);
				BinArr[i, j] = new Bin<T>(binSize, center, binSideLenSizeXTerm, Interlocked.Increment(ref NextIDNumber), new Point(minX, minY));
			}
		}
	}

	internal RootBinLevel(double minX, double minY, double maxX, double maxY, int numBins, int binSize, int numCols, int numRows, bool createGrid)
	{
		Dirty = true;
		BinSize = binSize;
		if (maxX == minX && maxY == minY)
		{
			numBins = 1;
		}
		if (maxX == minX)
		{
			if (maxX == 0.0)
			{
				maxX += 0.01;
				minX -= 0.01;
			}
			else if (maxX > 0.0)
			{
				maxX *= 1.01;
				minX *= 0.99;
			}
			else
			{
				maxX *= 0.99;
				minX *= 1.01;
			}
		}
		if (maxY == minY)
		{
			if (maxY == 0.0)
			{
				maxY += 0.01;
				minY -= 0.01;
			}
			else if (maxY > 0.0)
			{
				maxY *= 1.01;
				minY *= 0.99;
			}
			else
			{
				maxY *= 0.99;
				minY *= 1.01;
			}
		}
		MinX = minX;
		MinY = minY;
		_sizeX = maxX - minX;
		_sizeY = maxY - minY;
		MaxX = maxX;
		MaxY = maxY;
		_ = _sizeX / _sizeY;
		if (numBins <= 1)
		{
			numRows = 1;
			numCols = 1;
			numBins = 1;
		}
		else if (minY == maxY)
		{
			numRows = 1;
			numCols = numBins;
		}
		else if (minX == maxX)
		{
			numRows = numBins;
			numCols = 1;
		}
		else if (numRows == 0)
		{
			numRows = 1;
			numCols = numBins;
		}
		else if (numCols == 0)
		{
			numCols = 1;
			numRows = numBins;
		}
		BinCount = numRows * numCols;
		RowCount = numRows;
		ColCount = numCols;
		BinArr = new Bin<T>[numRows, numCols];
		double binSideLenNumColsTerm = (BinSideLen = ((numCols < numRows) ? (_sizeY / (double)numRows) : (_sizeX / (double)numCols)));
		if (!createGrid)
		{
			return;
		}
		for (int i = 0; i < numRows; i++)
		{
			for (int j = 0; j < numCols; j++)
			{
				double minXTerm = minX + (double)j * binSideLenNumColsTerm;
				double minYTerm = minY + (double)(i + 1) * binSideLenNumColsTerm;
				double computed = minXTerm + binSideLenNumColsTerm;
				double computed2 = minYTerm - binSideLenNumColsTerm;
				double x = (minXTerm + computed) / 2.0;
				double y = (minYTerm + computed2) / 2.0;
				Point center = new Point(x, y);
				BinArr[i, j] = new Bin<T>(binSize, center, binSideLenNumColsTerm, Interlocked.Increment(ref NextIDNumber), new Point(minX, minY));
			}
		}
	}

	internal RootBinLevel<T> SetPoint(double xCart, double yCart, T point, int maxRootBins)
	{
		double xCartAboveMinX = xCart - MinX;
		double yCartAboveMinY = yCart - MinY;
		RootBinLevel<T> rootBinLevel = this;
		if (xCartAboveMinX < 0.0 || xCartAboveMinX >= _sizeX - 0.0001 || yCartAboveMinY < 0.0 || yCartAboveMinY >= _sizeY - 0.0001)
		{
			rootBinLevel = ResizeGrid(xCart, yCart, maxRootBins);
			rootBinLevel.SetPointNoFail(xCart, yCart, point);
		}
		else
		{
			DetermineWhatBin(xCart, yCart, out var binX, out var binY);
			Point coord = new Point(xCart, yCart);
			rootBinLevel.BinArr[binY, binX].InsertValue(this, coord, point);
		}
		return rootBinLevel;
	}

	internal List<T> RemovePoint(double xCart, double yCart)
	{
		double xCartAboveMinX = xCart - MinX;
		double yCartAboveMinY = yCart - MinY;
		if (xCartAboveMinX < 0.0 || xCartAboveMinX >= _sizeX || yCartAboveMinY < 0.0 || yCartAboveMinY >= _sizeY)
		{
			return null;
		}
		DetermineWhatBin(xCart, yCart, out var binX, out var binY);
		Point coord = new Point(xCart, yCart);
		return BinArr[binY, binX].RemoveValueWithCoordinates(this, coord);
	}

	internal bool RemovePoint(double xCart, double yCart, T localValue)
	{
		double xCartAboveMinX = xCart - MinX;
		double yCartAboveMinY = yCart - MinY;
		if (xCartAboveMinX < 0.0 || xCartAboveMinX >= _sizeX || yCartAboveMinY < 0.0 || yCartAboveMinY >= _sizeY)
		{
			return false;
		}
		DetermineWhatBin(xCart, yCart, out var binX, out var binY);
		Point coord = new Point(xCart, yCart);
		return BinArr[binY, binX].RemoveValueWithCoordinates(this, coord, localValue);
	}

	internal List<T> RemovePointWithError(double xCart, double yCart, double error)
	{
		double xCartAboveMinX = xCart - MinX;
		double yCartAboveMinY = yCart - MinY;
		if (xCartAboveMinX < 0.0 || xCartAboveMinX >= _sizeX - 0.0001 || yCartAboveMinY < 0.0 || yCartAboveMinY >= _sizeY - 0.0001)
		{
			return null;
		}
		DetermineWhatBin(xCart, yCart, out var binX, out var binY);
		Point coord = new Point(xCart, yCart);
		return BinArr[binY, binX].RemoveValueWithCoordinatesWithError(this, coord, error);
	}

	internal bool RemovePointWithError(double xCart, double yCart, double error, T localValue)
	{
		double xCartAboveMinX = xCart - MinX;
		double yCartAboveMinY = yCart - MinY;
		if (xCartAboveMinX < 0.0 || xCartAboveMinX >= _sizeX - 0.0001 || yCartAboveMinY < 0.0 || yCartAboveMinY >= _sizeY - 0.0001)
		{
			return false;
		}
		DetermineWhatBin(xCart, yCart, out var binX, out var binY);
		Point coord = new Point(xCart, yCart);
		return BinArr[binY, binX].RemoveValueWithCoordinatesWithError(this, coord, error, localValue);
	}

	internal List<Bin<T>> GetAllLeafBins(double left, double bottom, double right, double top)
	{
		List<Bin<T>> bins = new List<Bin<T>>();
		if (left > MinX && bottom > MinY && right < MaxX && top < MaxY)
		{
			List<Bin<T>> partialCover = new List<Bin<T>>();
			List<Bin<T>> fullyCovered = new List<Bin<T>>();
			QueryRectangle(left, bottom, right, top, out partialCover, out fullyCovered);
			foreach (Bin<T> partialCover2 in partialCover)
			{
				GetAllLeafBinsRoutine(bins, partialCover2);
			}
			foreach (Bin<T> fullyCovered2 in fullyCovered)
			{
				GetAllLeafBinsRoutine(bins, fullyCovered2);
			}
		}
		else
		{
			for (int i = 0; i < BinArr.GetLength(0); i++)
			{
				for (int j = 0; j < BinArr.GetLength(1); j++)
				{
					Bin<T> bin = BinArr[i, j];
					GetAllLeafBinsRoutine(bins, bin);
				}
			}
		}
		return bins;
	}

	internal Dictionary<Bin<T>, List<Bin<T>>> GetAllNeighborsOfBins()
	{
		Dictionary<Bin<T>, List<Bin<T>>> allNeighborsOfBins = new Dictionary<Bin<T>, List<Bin<T>>>();
		List<Bin<T>> allLeafBins = GetAllLeafBins(MinX, MinY, MaxX, MaxY);
		double binSideLen = BinSideLen;
		foreach (Bin<T> allLeafBin in allLeafBins)
		{
			if (allLeafBin.SideLen < binSideLen)
			{
				binSideLen = allLeafBin.SideLen;
			}
		}
		double scaledBinSideLen = binSideLen / 10.0;
		foreach (Bin<T> leafBin in allLeafBins)
		{
			double unity = 0.0;
			double unity2 = 0.0;
			double unity3 = 0.0;
			double unity4 = 0.0;
			HashSet<Bin<T>> bins = new HashSet<Bin<T>>();
			if (leafBin.Parent != null)
			{
				IEnumerable<Bin<T>> enumerable = from bin6 in leafBin.Parent.AllChildren()
					where bin6 != leafBin && bin6.IsLeafNode
					select bin6;
				Vector vector = new Vector((float)leafBin.Center.X, (float)leafBin.Center.Y);
				foreach (Bin<T> enumerable2 in enumerable)
				{
					Vector vector2 = new Vector((float)enumerable2.Center.X, (float)enumerable2.Center.Y);
					double angleBetween = Vector.AngleBetween(vector, vector2);
					if (angleBetween == 90.0)
					{
						unity = 1.0;
					}
					else if (angleBetween == 270.0)
					{
						unity2 = 1.0;
					}
					else if (angleBetween == 0.0)
					{
						unity4 = 1.0;
					}
					else if (angleBetween == 180.0)
					{
						unity3 = 1.0;
					}
					bins.Add(enumerable2);
				}
			}
			Point point = new Point(leafBin.Center.X - leafBin.SideLen / 2.0, leafBin.Center.Y + leafBin.SideLen / 2.0);
			Point point2 = new Point(leafBin.Center.X + leafBin.SideLen / 2.0, leafBin.Center.Y + leafBin.SideLen / 2.0);
			Point point3 = new Point(leafBin.Center.X - leafBin.SideLen / 2.0, leafBin.Center.Y - leafBin.SideLen / 2.0);
			Point point4 = new Point(leafBin.Center.X + leafBin.SideLen / 2.0, leafBin.Center.Y - leafBin.SideLen / 2.0);
			double sideLenSum = 0.0;
			while (unity < 1.0)
			{
				Bin<T> bin = QueryPoint(point.X + sideLenSum + scaledBinSideLen, point.Y + scaledBinSideLen);
				if (bin == null)
				{
					break;
				}
				unity += bin.SideLen / leafBin.SideLen;
				sideLenSum += bin.SideLen;
				bins.Add(bin);
			}
			sideLenSum = 0.0;
			while (unity2 < 1.0)
			{
				Bin<T> bin2 = QueryPoint(point3.X + sideLenSum + scaledBinSideLen, point3.Y - scaledBinSideLen);
				if (bin2 == null)
				{
					break;
				}
				unity2 += bin2.SideLen / leafBin.SideLen;
				sideLenSum += bin2.SideLen;
				bins.Add(bin2);
			}
			sideLenSum = 0.0;
			while (unity3 < 1.0)
			{
				Bin<T> bin3 = QueryPoint(point3.X - scaledBinSideLen, point3.Y + sideLenSum + scaledBinSideLen);
				if (bin3 == null)
				{
					break;
				}
				unity3 += bin3.SideLen / leafBin.SideLen;
				sideLenSum += bin3.SideLen;
				bins.Add(bin3);
			}
			sideLenSum = 0.0;
			while (unity4 < 1.0)
			{
				Bin<T> bin4 = QueryPoint(point4.X + scaledBinSideLen, point4.Y + sideLenSum + scaledBinSideLen);
				if (bin4 == null)
				{
					break;
				}
				unity4 += bin4.SideLen / leafBin.SideLen;
				sideLenSum += bin4.SideLen;
				bins.Add(bin4);
			}
			Bin<T> bin5 = QueryPoint(point.X - scaledBinSideLen, point.Y + scaledBinSideLen);
			if (bin5 != null)
			{
				bins.Add(bin5);
			}
			bin5 = QueryPoint(point2.X + scaledBinSideLen, point.Y + scaledBinSideLen);
			if (bin5 != null)
			{
				bins.Add(bin5);
			}
			bin5 = QueryPoint(point3.X - scaledBinSideLen, point3.Y - scaledBinSideLen);
			if (bin5 != null)
			{
				bins.Add(bin5);
			}
			bin5 = QueryPoint(point4.X + scaledBinSideLen, point4.Y - scaledBinSideLen);
			if (bin5 != null)
			{
				bins.Add(bin5);
			}
			allNeighborsOfBins.Add(leafBin, bins.ToList());
		}
		return allNeighborsOfBins;
	}

	private void GetAllLeafBinsRoutine(List<Bin<T>> ret, Bin<T> bin)
	{
		Queue<Bin<T>> bins = new Queue<Bin<T>>();
		bins.Enqueue(bin);
		while (bins.Count > 0)
		{
			Bin<T> bin2 = bins.Dequeue();
			if (bin2 != null)
			{
				for (int i = 0; i < bin2.ChildrenHaveItems.Length && bin2.ChildrenHaveItems[i]; i++)
				{
				}
				if (bin2.IsLeafNode && bin2.Items != null && bin2.Items.Count > 0)
				{
					ret.Add(bin2);
				}
				if (bin2.ChildrenHaveItems[0])
				{
					bins.Enqueue(bin2.FirstQuadrant);
				}
				if (bin2.ChildrenHaveItems[1])
				{
					bins.Enqueue(bin2.SecondQuadrant);
				}
				if (bin2.ChildrenHaveItems[2])
				{
					bins.Enqueue(bin2.ThirdQuadrant);
				}
				if (bin2.ChildrenHaveItems[3])
				{
					bins.Enqueue(bin2.FourthQuadrant);
				}
			}
		}
	}

	private void DetermineWhatBin(double xCart, double yCart, out int binX, out int binY)
	{
		binX = (int)Math.Floor((xCart - MinX) / BinSideLen);
		binY = (int)Math.Floor((yCart - MinY) / BinSideLen);
	}

	private void SetPointNoFail(double xCart, double yCart, T localValue)
	{
		_ = MinX;
		_ = MinY;
		DetermineWhatBin(xCart, yCart, out var binX, out var binY);
		Point coord = new Point(xCart, yCart);
		BinArr[binY, binX].InsertValue(this, coord, localValue);
	}

	internal List<T> GetAllValuesFromPoint(double xCart, double yCart)
	{
		double xCartAboveMinX = xCart - MinX;
		double yCartAboveMinY = yCart - MinY;
		if (xCartAboveMinX < 0.0 || xCartAboveMinX >= _sizeX || yCartAboveMinY < 0.0 || yCartAboveMinY >= _sizeY)
		{
			throw new Exception("Cannot get a point outside the grid.");
		}
		DetermineWhatBin(xCart, yCart, out var binX, out var binY);
		return BinArr[binY, binX].GetItemsFromBin(xCart, yCart);
	}

	internal void ModifyKeysParallel(Func<T, T> modification)
	{
		new List<T>();
		Parallel.ForEach(Bins, (Bin<T> bin) =>
		{
			bin.DoModificationOnItemsFromBin(modification);
		});
	}

	internal void ModifyKeys(Func<T, T> modification)
	{
		new List<T>();
		foreach (Bin<T> bin in Bins)
		{
			bin.DoModificationOnItemsFromBin(modification);
		}
	}

	internal List<T> GetAllValuesFromBin(Bin<T> bin)
	{
		return bin.GetValues();
	}

	internal Bin<T> QueryPoint(double xCart, double yCart)
	{
		double xCartAboveMinX = xCart - MinX;
		double yCartAboveMinY = yCart - MinY;
		int binY = 0;
		int binX = 0;
		if (xCartAboveMinX < 0.0 || xCartAboveMinX >= _sizeX || yCartAboveMinY < 0.0 || yCartAboveMinY >= _sizeY)
		{
			return null;
		}
		DetermineWhatBin(xCart, yCart, out binX, out binY);
		return BinArr[binY, binX].GetBinUsingPoint(xCart, yCart);
	}

	internal List<T> QueryPointReturnKey(double xCart, double yCart)
	{
		Bin<T> bin = QueryPoint(xCart, yCart);
		if (bin == null)
		{
			return new List<T>();
		}
		return bin.GetValuesInPoint(xCart, yCart);
	}

	internal List<Point> QueryPointReturnCoordinate(double xCart, double yCart)
	{
		Bin<T> bin = QueryPoint(xCart, yCart);
		if (bin == null)
		{
			return new List<Point>();
		}
		return bin.GetCoordinateInPoint(xCart, yCart);
	}

	internal List<Tuple<T, Point>> QueryPointReturnKeyAndCoordinate(double xCart, double yCart)
	{
		Bin<T> bin = QueryPoint(xCart, yCart);
		if (bin == null)
		{
			return new List<Tuple<T, Point>>();
		}
		return bin.GetValueAndCoordinateInPoint(xCart, yCart);
	}

	internal List<Bin<T>> QueryLine(double xCart1, double yCart1, double xCart2, double yCart2)
	{
		List<Bin<T>> bins = new List<Bin<T>>();
		if (xCart1 == xCart2 && yCart1 == yCart2)
		{
			bins.Add(QueryPoint(xCart1, yCart2));
			return bins;
		}
		foreach (Point pointsOnLine in GeometryTools.GetPointsOnLine(xCart1, yCart1, xCart2, yCart2))
		{
			Bin<T> bin = QueryPoint(pointsOnLine.X, pointsOnLine.Y);
			if (bin != null)
			{
				bins.Add(bin);
			}
		}
		return bins.Distinct().ToList();
	}

	internal int QueryRectangleCount(double left, double bottom, double right, double top)
	{
		if (left > right || bottom > top)
		{
			throw new Exception("Bad query bounds.");
		}
		if ((left == right && top != bottom) || (left != right && top == bottom))
		{
			throw new Exception("Bad query bounds.");
		}
		if (left == right && bottom == top)
		{
			throw new Exception("Bad query bounds.");
		}
		int ordinal = 0;
		QueryRectangle(left, bottom, right, top, out var partialCover, out var fullyCovered);
		foreach (Bin<T> fullyCovered2 in fullyCovered)
		{
			ordinal += fullyCovered2.GetCountValuesFromBin();
		}
		foreach (Bin<T> partialCover2 in partialCover)
		{
			ordinal += partialCover2.GetNumberOfValuesWithinRectangle(left, bottom, right, top);
		}
		return ordinal;
	}

	internal int QueryRectangleRoughCount(double left, double bottom, double right, double top)
	{
		if (left > right || bottom > top)
		{
			throw new Exception("Bad query bounds.");
		}
		if ((left == right && top != bottom) || (left != right && top == bottom))
		{
			throw new Exception("Bad query bounds.");
		}
		if (left == right && bottom == top)
		{
			throw new Exception("Bad query bounds.");
		}
		int ordinal = 0;
		QueryRectangle(left, bottom, right, top, out var partialCover, out var fullyCovered);
		foreach (Bin<T> fullyCovered2 in fullyCovered)
		{
			ordinal += fullyCovered2.GetCountValuesFromBin();
		}
		foreach (Bin<T> partialCover2 in partialCover)
		{
			ordinal += partialCover2.GetRoughNumValuesFromBin(left, bottom, right, top);
		}
		return ordinal;
	}

	internal List<T> QueryLineReturnKeys(double xCart1, double yCart1, double xCart2, double yCart2)
	{
		List<T> items = new List<T>();
		if (xCart1 == xCart2 && yCart1 == yCart2)
		{
			return QueryPointReturnKey(xCart1, yCart2);
		}
		foreach (Bin<T> queryLine in QueryLine(xCart1, yCart1, xCart2, yCart2))
		{
			List<T> valuesWithinLine = queryLine.GetValuesWithinLine(xCart1, yCart1, xCart2, yCart2);
			if (valuesWithinLine != null)
			{
				items.AddRange(valuesWithinLine);
			}
		}
		return items;
	}

	internal List<Point> QueryLineReturnCoordinates(double xCart1, double yCart1, double xCart2, double yCart2)
	{
		List<Point> points = new List<Point>();
		if (xCart1 == xCart2 && yCart1 == yCart2)
		{
			return QueryPointReturnCoordinate(xCart1, yCart2);
		}
		foreach (Bin<T> queryLine in QueryLine(xCart1, yCart1, xCart2, yCart2))
		{
			List<Point> coordinatesWithinLine = queryLine.GetCoordinatesWithinLine(xCart1, yCart1, xCart2, yCart2);
			if (coordinatesWithinLine != null)
			{
				points.AddRange(coordinatesWithinLine);
			}
		}
		return points;
	}

	internal List<Tuple<T, Point>> QueryLineReturnKeysAndCoordinates(double xCart1, double yCart1, double xCart2, double yCart2)
	{
		List<Tuple<T, Point>> valueTupleList = new List<Tuple<T, Point>>();
		if (xCart1 == xCart2 && yCart1 == yCart2)
		{
			return QueryPointReturnKeyAndCoordinate(xCart1, yCart2);
		}
		foreach (Bin<T> queryLine in QueryLine(xCart1, yCart1, xCart2, yCart2))
		{
			List<Tuple<T, Point>> valuesAndCoordinatesWithinLine = queryLine.GetValuesAndCoordinatesWithinLine(xCart1, yCart1, xCart2, yCart2);
			if (valuesAndCoordinatesWithinLine != null)
			{
				valueTupleList.AddRange(valuesAndCoordinatesWithinLine);
			}
		}
		return valueTupleList;
	}

	internal void QueryRectangle(double left, double bottom, double right, double top, out List<Bin<T>> partialCover, out List<Bin<T>> fullyCovered)
	{
		partialCover = new List<Bin<T>>();
		fullyCovered = new List<Bin<T>>();
		if (left > right || bottom > top)
		{
			throw new Exception("Bad query bounds.");
		}
		if ((left == right && top != bottom) || (left != right && top == bottom))
		{
			throw new Exception("Bad query bounds.");
		}
		if (left == right && bottom == top)
		{
			throw new Exception("Bad query bounds.");
		}
		for (int i = 0; i < RowCount; i++)
		{
			for (int j = 0; j < ColCount; j++)
			{
				if (BinArr[i, j].Center.X - BinSideLen / 2.0 < left)
				{
					if (BinArr[i, j].Center.X + BinSideLen / 2.0 > left)
					{
						if (BinArr[i, j].Center.Y + BinSideLen / 2.0 > top && BinArr[i, j].Center.Y - BinSideLen / 2.0 < top)
						{
							BinArr[i, j].DeterminePartialOrFullChildren(left, bottom, right, top, ref partialCover, ref fullyCovered);
						}
						else if (BinArr[i, j].Center.Y + BinSideLen / 2.0 > bottom && BinArr[i, j].Center.Y - BinSideLen / 2.0 < bottom)
						{
							BinArr[i, j].DeterminePartialOrFullChildren(left, bottom, right, top, ref partialCover, ref fullyCovered);
						}
						else if (BinArr[i, j].Center.Y + BinSideLen / 2.0 <= top && BinArr[i, j].Center.Y - BinSideLen / 2.0 >= bottom)
						{
							BinArr[i, j].DeterminePartialOrFullChildren(left, bottom, right, top, ref partialCover, ref fullyCovered);
						}
					}
				}
				else if (BinArr[i, j].Center.X + BinSideLen / 2.0 > right)
				{
					if (!(BinArr[i, j].Center.X - BinSideLen / 2.0 >= right))
					{
						if (BinArr[i, j].Center.Y + BinSideLen / 2.0 > top && BinArr[i, j].Center.Y - BinSideLen / 2.0 < top)
						{
							BinArr[i, j].DeterminePartialOrFullChildren(left, bottom, right, top, ref partialCover, ref fullyCovered);
						}
						else if (BinArr[i, j].Center.Y + BinSideLen / 2.0 > bottom && BinArr[i, j].Center.Y - BinSideLen / 2.0 < bottom)
						{
							BinArr[i, j].DeterminePartialOrFullChildren(left, bottom, right, top, ref partialCover, ref fullyCovered);
						}
						else if (BinArr[i, j].Center.Y + BinSideLen / 2.0 <= top && BinArr[i, j].Center.Y - BinSideLen / 2.0 >= bottom)
						{
							BinArr[i, j].DeterminePartialOrFullChildren(left, bottom, right, top, ref partialCover, ref fullyCovered);
						}
					}
				}
				else if (BinArr[i, j].Center.Y + BinSideLen / 2.0 > top && BinArr[i, j].Center.Y - BinSideLen / 2.0 < top)
				{
					BinArr[i, j].DeterminePartialOrFullChildren(left, bottom, right, top, ref partialCover, ref fullyCovered);
				}
				else if (BinArr[i, j].Center.Y + BinSideLen / 2.0 > bottom && BinArr[i, j].Center.Y - BinSideLen / 2.0 < bottom)
				{
					BinArr[i, j].DeterminePartialOrFullChildren(left, bottom, right, top, ref partialCover, ref fullyCovered);
				}
				else if (BinArr[i, j].Center.Y + BinSideLen / 2.0 <= top && BinArr[i, j].Center.Y - BinSideLen / 2.0 >= bottom)
				{
					BinArr[i, j].DeterminePartialOrFullChildren(left, bottom, right, top, ref partialCover, ref fullyCovered);
				}
			}
		}
	}

	internal List<T> QueryRectangleReturnKeys(double left, double bottom, double right, double top)
	{
		List<T> items = new List<T>();
		if (left > right || bottom > top)
		{
			throw new Exception("Bad query bounds.");
		}
		if ((left == right && top != bottom) || (left != right && top == bottom))
		{
			throw new Exception("Bad query bounds.");
		}
		if (left == right && bottom == top)
		{
			throw new Exception("Bad query bounds.");
		}
		QueryRectangle(left, bottom, right, top, out var partialCover, out var fullyCovered);
		foreach (Bin<T> fullyCovered2 in fullyCovered)
		{
			List<T> values = fullyCovered2.GetValues();
			if (values != null)
			{
				items.AddRange(values);
			}
		}
		foreach (Bin<T> partialCover2 in partialCover)
		{
			List<T> valuesWithinRectangle = partialCover2.GetValuesWithinRectangle(left, bottom, right, top);
			if (valuesWithinRectangle != null)
			{
				items.AddRange(valuesWithinRectangle);
			}
		}
		return items;
	}

	internal List<Point> QueryRectangleReturnCoordinates(double left, double bottom, double right, double top)
	{
		List<Point> points = new List<Point>();
		if (left > right || bottom > top)
		{
			throw new Exception("Bad query bounds.");
		}
		if ((left == right && top != bottom) || (left != right && top == bottom))
		{
			throw new Exception("Bad query bounds.");
		}
		if (left == right && bottom == top)
		{
			throw new Exception("Bad query bounds.");
		}
		QueryRectangle(left, bottom, right, top, out var partialCover, out var fullyCovered);
		foreach (Bin<T> fullyCovered2 in fullyCovered)
		{
			List<Point> coordinatesFromBin = fullyCovered2.GetCoordinatesFromBin();
			if (coordinatesFromBin != null)
			{
				points.AddRange(coordinatesFromBin);
			}
		}
		foreach (Bin<T> partialCover2 in partialCover)
		{
			List<Point> coordinatesWithinRectangle = partialCover2.GetCoordinatesWithinRectangle(left, bottom, right, top);
			if (coordinatesWithinRectangle != null)
			{
				points.AddRange(coordinatesWithinRectangle);
			}
		}
		return points;
	}

	internal List<Tuple<T, Point>> QueryRectangleReturnKeysAndCoordinates(double left, double bottom, double right, double top)
	{
		List<Tuple<T, Point>> valueTupleList = new List<Tuple<T, Point>>();
		if (left > right || bottom > top)
		{
			throw new Exception("Bad query bounds.");
		}
		if ((left == right && top != bottom) || (left != right && top == bottom))
		{
			throw new Exception("Bad query bounds.");
		}
		if (left == right && bottom == top)
		{
			throw new Exception("Bad query bounds.");
		}
		QueryRectangle(left, bottom, right, top, out var partialCover, out var fullyCovered);
		foreach (Bin<T> fullyCovered2 in fullyCovered)
		{
			List<Tuple<T, Point>> valuesAndCoordinatesFromBin = fullyCovered2.GetValuesAndCoordinatesFromBin();
			if (valuesAndCoordinatesFromBin != null)
			{
				valueTupleList.AddRange(valuesAndCoordinatesFromBin);
			}
		}
		foreach (Bin<T> partialCover2 in partialCover)
		{
			List<Tuple<T, Point>> valuesAndCoordinatesWithinRectangle = partialCover2.GetValuesAndCoordinatesWithinRectangle(left, bottom, right, top);
			if (valuesAndCoordinatesWithinRectangle != null)
			{
				valueTupleList.AddRange(valuesAndCoordinatesWithinRectangle);
			}
		}
		return valueTupleList;
	}

	internal void QueryCircle(double xCenter, double yCenter, double radius, out List<Bin<T>> partialCover, out List<Bin<T>> fullyCovered)
	{
		partialCover = new List<Bin<T>>();
		fullyCovered = new List<Bin<T>>();
		if (radius < 0.0)
		{
			throw new Exception("Bad query bounds.");
		}
		if (radius == 0.0)
		{
			throw new Exception("Bad query bounds.");
		}
		Point circleCenter = new Point(xCenter, yCenter);
		for (int i = 0; i < RowCount; i++)
		{
			for (int j = 0; j < ColCount; j++)
			{
				if (GeometryTools.CheckIfSquareAndCircleInteresct(circleCenter, radius, BinArr[i, j].Center, BinArr[i, j].SideLen))
				{
					if (GeometryTools.CheckIfSquareIsCompletelyInsideCircle(circleCenter, radius, BinArr[i, j].Center, BinArr[i, j].SideLen))
					{
						fullyCovered.Add(BinArr[i, j]);
					}
					else
					{
						partialCover.Add(BinArr[i, j]);
					}
				}
			}
		}
	}

	internal List<T> QueryCircleReturnKeys(double xCenter, double yCenter, double radius)
	{
		List<T> items = new List<T>();
		if (radius < 0.0)
		{
			throw new Exception("Bad query bounds.");
		}
		if (radius == 0.0)
		{
			throw new Exception("Bad query bounds.");
		}
		QueryCircle(xCenter, yCenter, radius, out var partialCover, out var fullyCovered);
		foreach (Bin<T> fullyCovered2 in fullyCovered)
		{
			List<T> values = fullyCovered2.GetValues();
			if (values != null)
			{
				items.AddRange(values);
			}
		}
		foreach (Bin<T> partialCover2 in partialCover)
		{
			List<T> valuesWithinCircle = partialCover2.GetValuesWithinCircle(xCenter, yCenter, radius);
			if (valuesWithinCircle != null)
			{
				items.AddRange(valuesWithinCircle);
			}
		}
		return items;
	}

	internal List<Point> QueryCircleReturnCoordinates(double xCenter, double yCenter, double radius)
	{
		List<Point> points = new List<Point>();
		if (radius < 0.0)
		{
			throw new Exception("Bad query bounds.");
		}
		if (radius == 0.0)
		{
			throw new Exception("Bad query bounds.");
		}
		QueryCircle(xCenter, yCenter, radius, out var partialCover, out var fullyCovered);
		foreach (Bin<T> fullyCovered2 in fullyCovered)
		{
			List<Point> coordinatesFromBin = fullyCovered2.GetCoordinatesFromBin();
			if (coordinatesFromBin != null)
			{
				points.AddRange(coordinatesFromBin);
			}
		}
		foreach (Bin<T> partialCover2 in partialCover)
		{
			List<Point> coordinatesWithinCircle = partialCover2.GetCoordinatesWithinCircle(xCenter, yCenter, radius);
			if (coordinatesWithinCircle != null)
			{
				points.AddRange(coordinatesWithinCircle);
			}
		}
		return points;
	}

	internal List<Tuple<T, Point>> QueryCircleReturnKeysAndCoordinates(double xCenter, double yCenter, double radius)
	{
		List<Tuple<T, Point>> valueTupleList = new List<Tuple<T, Point>>();
		if (radius < 0.0)
		{
			throw new Exception("Bad query bounds.");
		}
		if (radius == 0.0)
		{
			throw new Exception("Bad query bounds.");
		}
		QueryCircle(xCenter, yCenter, radius, out var partialCover, out var fullyCovered);
		foreach (Bin<T> fullyCovered2 in fullyCovered)
		{
			List<Tuple<T, Point>> valuesAndCoordinatesFromBin = fullyCovered2.GetValuesAndCoordinatesFromBin();
			if (valuesAndCoordinatesFromBin != null)
			{
				valueTupleList.AddRange(valuesAndCoordinatesFromBin);
			}
		}
		foreach (Bin<T> partialCover2 in partialCover)
		{
			List<Tuple<T, Point>> valuesAndCoordinatesWithinCircle = partialCover2.GetValuesAndCoordinatesWithinCircle(xCenter, yCenter, radius);
			if (valuesAndCoordinatesWithinCircle != null)
			{
				valueTupleList.AddRange(valuesAndCoordinatesWithinCircle);
			}
		}
		return valueTupleList;
	}

	internal void QueryCircleNearestPointToCenter(double xCenter, double yCenter, double radius, out double distanceBetweenPts, out List<T> keys, out Point coords)
	{
		QueryCircle(xCenter, yCenter, radius, out var partialCover, out var fullyCovered);
		Point pt = new Point(xCenter, yCenter);
		Point point = default;
		Bin<T> bin = null;
		double maxValue = double.MaxValue;
		foreach (Bin<T> fullyCovered2 in fullyCovered)
		{
			List<Point> coordinatesFromBin = fullyCovered2.GetCoordinatesFromBin();
			if (coordinatesFromBin == null)
			{
				continue;
			}
			foreach (Point coordinatesFromBin3 in coordinatesFromBin)
			{
				double euclideanDistance = GeometryTools.EuclideanDistance(pt, coordinatesFromBin3);
				if (euclideanDistance < maxValue)
				{
					maxValue = euclideanDistance;
					point = coordinatesFromBin3;
					bin = fullyCovered2;
				}
			}
		}
		foreach (Bin<T> partialCover2 in partialCover)
		{
			List<Point> coordinatesFromBin2 = partialCover2.GetCoordinatesFromBin();
			if (coordinatesFromBin2 == null)
			{
				continue;
			}
			foreach (Point coordinatesFromBin22 in coordinatesFromBin2)
			{
				double euclideanDistance2 = GeometryTools.EuclideanDistance(pt, coordinatesFromBin22);
				if (euclideanDistance2 < maxValue)
				{
					maxValue = euclideanDistance2;
					point = coordinatesFromBin22;
					bin = partialCover2;
				}
			}
		}
		if (maxValue != double.MaxValue)
		{
			keys = bin.GetValuesInPoint(point.X, point.Y);
			distanceBetweenPts = maxValue;
			coords = point;
		}
		else
		{
			keys = new List<T>();
			distanceBetweenPts = maxValue;
			coords = point;
		}
	}

	private RootBinLevel<T> ResizeGrid(double xCart, double yCart, int maxRootBins)
	{
		Dirty = true;
		double minX = MinX;
		double minY = MinY;
		double newMaxX = MaxX;
		double newMaxY = MaxY;
		int newColCount = ColCount;
		int newRowCount = RowCount;
		long newRowCountNewColCountTerm = 0L;
		if (xCart - MinX < 0.0)
		{
			int unity = 0;
			if (xCart % BinSideLen == 0.0)
			{
				unity = 1;
			}
			double a = (MinX - xCart) / BinSideLen + (double)unity;
			newColCount += (int)Math.Ceiling(a);
			Math.Ceiling(a);
			minX = MinX - (double)(newColCount - ColCount) * BinSideLen;
		}
		if (yCart - MinY < 0.0)
		{
			int unity2 = 0;
			if (yCart % BinSideLen == 0.0)
			{
				unity2 = 1;
			}
			double a2 = (MinY - yCart) / BinSideLen + (double)unity2;
			newRowCount += (int)Math.Ceiling(a2);
			Math.Ceiling(a2);
			minY = MinY - (double)(newRowCount - RowCount) * BinSideLen;
		}
		if (xCart - MinX >= _sizeX - 0.0001)
		{
			int unity3 = 0;
			if (xCart % BinSideLen == 0.0)
			{
				unity3 = 1;
			}
			double xCartAboveMaxX = xCart - MaxX;
			if (xCartAboveMaxX >= -0.0001 && xCartAboveMaxX <= 0.0001)
			{
				xCartAboveMaxX = 1.0;
			}
			double a3 = xCartAboveMaxX / BinSideLen + (double)unity3;
			newColCount += (int)Math.Ceiling(a3);
			Math.Ceiling(a3);
			newMaxX = MaxX + (double)(newColCount - ColCount) * BinSideLen;
		}
		if (yCart - MinY >= _sizeY - 0.0001)
		{
			int unity4 = 0;
			if (yCart % BinSideLen == 0.0)
			{
				unity4 = 1;
			}
			double yCartAboveMaxY = yCart - MaxY;
			if (yCartAboveMaxY >= -0.0001 && yCartAboveMaxY <= 0.0001)
			{
				yCartAboveMaxY = 1.0;
			}
			double a4 = yCartAboveMaxY / BinSideLen + (double)unity4;
			newRowCount += (int)Math.Ceiling(a4);
			Math.Ceiling(a4);
			newMaxY = MaxY + (double)(newRowCount - RowCount) * BinSideLen;
		}
		newRowCountNewColCountTerm = (long)newRowCount * (long)newColCount - (RowCount - ColCount) - BinCount;
		RootBinLevel<T> rootBinLevel = null;
		if (BinCount + newRowCountNewColCountTerm > maxRootBins)
		{
			long getRowAndColForNextLvlUpMaxRootBinsTerm = BinCount + newRowCountNewColCountTerm;
			int targetBinCount = (int)GetTargetBinCount(maxRootBins, BinCount + newRowCountNewColCountTerm, newRowCount, newColCount, newMaxX, newMaxY, minX, minY);
			newRowCountNewColCountTerm = 0L;
			double newMaxX2 = MaxX;
			double newMaxY2 = MaxY;
			int newRowCount2 = RowCount;
			int newColCount2 = ColCount;
			_ = BinArr;
			while (getRowAndColForNextLvlUpMaxRootBinsTerm != targetBinCount)
			{
				getRowAndColForNextLvlUpMaxRootBinsTerm = GetRowAndColForNextLvlUp(maxRootBins, getRowAndColForNextLvlUpMaxRootBinsTerm, ref newRowCount, ref newColCount, ref newMaxX, ref newMaxY, minX, minY);
				BinCount = (int)GetRowAndColForNextLvlUp(maxRootBins, BinCount, ref newRowCount2, ref newColCount2, ref newMaxX2, ref newMaxY2, minX, minY);
			}
			rootBinLevel = new RootBinLevel<T>(minX, minY, newMaxX, newMaxY, (int)getRowAndColForNextLvlUpMaxRootBinsTerm, BinSize, newColCount, newRowCount, createGrid: true);
			for (int i = 0; i < RowCount; i++)
			{
				for (int j = 0; j < ColCount; j++)
				{
					List<T> values = BinArr[i, j].GetValues();
					List<Point> coordinatesFromBin = BinArr[i, j].GetCoordinatesFromBin();
					if (values != null && coordinatesFromBin != null)
					{
						for (int k = 0; k < values.Count; k++)
						{
							rootBinLevel.SetPoint(coordinatesFromBin[k].X, coordinatesFromBin[k].Y, values[k], maxRootBins);
						}
					}
				}
			}
		}
		else
		{
			rootBinLevel = new RootBinLevel<T>(minX, minY, newMaxX, newMaxY, (int)(BinCount + newRowCountNewColCountTerm), BinSize, newColCount, newRowCount, createGrid: true);
			for (int l = 0; l < RowCount; l++)
			{
				for (int m = 0; m < ColCount; m++)
				{
					List<T> values2 = BinArr[l, m].GetValues();
					List<Point> coordinatesFromBin2 = BinArr[l, m].GetCoordinatesFromBin();
					if (values2 != null && coordinatesFromBin2 != null)
					{
						for (int n = 0; n < values2.Count; n++)
						{
							rootBinLevel.SetPoint(coordinatesFromBin2[n].X, coordinatesFromBin2[n].Y, values2[n], maxRootBins);
						}
					}
				}
			}
		}
		return rootBinLevel;
	}

	private long GetRowAndColForNextLvlUp(int maxRootBins, long newBinCount, ref int newRowCount, ref int newColCount, ref double newMaxX, ref double newMaxY, double newMinX, double newMinY)
	{
		double newMaxXNewMinXTerm = newMaxX - newMinX + 1.0;
		double newMaxYNewMinYTerm = newMaxY - newMinY + 1.0;
		double newColCountTerm;
		if (newColCount > newRowCount)
		{
			newColCountTerm = (newMaxXNewMinXTerm - 1.0) / (double)newColCount;
		}
		else
		{
			newColCountTerm = ((newColCount >= newRowCount) ? ((newMaxXNewMinXTerm - 1.0) / (double)newColCount) : ((newMaxYNewMinYTerm - 1.0) / (double)newRowCount));
		}
		if (newRowCount % 2 != 0)
		{
			newMaxY += newColCountTerm;
			newRowCount++;
			newBinCount += newColCount;
		}
		if (newColCount % 2 != 0)
		{
			newMaxX += newColCountTerm;
			newColCount++;
			newBinCount += newRowCount;
		}
		newRowCount /= 2;
		newColCount /= 2;
		newBinCount /= 4;
		return newBinCount;
	}

	private long GetTargetBinCount(int maxRootBins, long newBinCount, int newRowCount, int newColCount, double newMaxX, double newMaxY, double newMinX, double newMinY)
	{
		double newMaxXNewMinXTerm = newMaxX - newMinX + 1.0;
		double newMaxYNewMinYTerm = newMaxY - newMinY + 1.0;
		if (newColCount > newRowCount)
		{
			_ = (newMaxXNewMinXTerm - 1.0) / (double)newColCount;
		}
		else if (newColCount < newRowCount)
		{
			_ = (newMaxYNewMinYTerm - 1.0) / (double)newRowCount;
		}
		else
		{
			_ = (newMaxXNewMinXTerm - 1.0) / (double)newColCount;
		}
		while (newBinCount > maxRootBins)
		{
			if (newRowCount % 2 != 0)
			{
				newMaxY += BinSideLen;
				newRowCount++;
				newBinCount += newColCount;
			}
			if (newColCount % 2 != 0)
			{
				newMaxX += BinSideLen;
				newColCount++;
				newBinCount += newRowCount;
			}
			newRowCount /= 2;
			newColCount /= 2;
			newBinCount /= 4;
		}
		return newBinCount;
	}
}
