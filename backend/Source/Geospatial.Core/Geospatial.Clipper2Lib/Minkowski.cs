using System;

namespace Geospatial.Clipper2Lib;

public static class Minkowski
{
	private static Paths64 MinkowskiInternal(Path64 pattern, Path64 path, bool isSum, bool isClosed)
	{
		int num = ((!isClosed) ? 1 : 0);
		int count = pattern.Count;
		int count2 = path.Count;
		Paths64 paths = new Paths64(count2);
		foreach (Point64 item in path)
		{
			Path64 path2 = new Path64(count);
			if (isSum)
			{
				foreach (Point64 item2 in pattern)
				{
					path2.Add(item + item2);
				}
			}
			else
			{
				foreach (Point64 item3 in pattern)
				{
					path2.Add(item - item3);
				}
			}
			paths.Add(path2);
		}
		Paths64 paths2 = new Paths64((count2 - num) * count);
		int index = (isClosed ? (count2 - 1) : 0);
		int index2 = count - 1;
		for (int i = num; i < count2; i++)
		{
			for (int j = 0; j < count; j++)
			{
				Path64 path3 = new Path64(4)
				{
					paths[index][index2],
					paths[i][index2],
					paths[i][j],
					paths[index][j]
				};
				if (!Clipper.IsPositive(path3))
				{
					paths2.Add(Clipper.ReversePath(path3));
				}
				else
				{
					paths2.Add(path3);
				}
				index2 = j;
			}
			index = i;
		}
		return paths2;
	}

	public static Paths64 Sum(Path64 pattern, Path64 path, bool isClosed)
	{
		return Clipper.Union(MinkowskiInternal(pattern, path, isSum: true, isClosed), FillRule.NonZero);
	}

	public static PathsD Sum(PathD pattern, PathD path, bool isClosed, int decimalPlaces = 2)
	{
		double num = Math.Pow(10.0, decimalPlaces);
		return Clipper.ScalePathsD(Clipper.Union(MinkowskiInternal(Clipper.ScalePath64(pattern, num), Clipper.ScalePath64(path, num), isSum: true, isClosed), FillRule.NonZero), 1.0 / num);
	}

	public static Paths64 Diff(Path64 pattern, Path64 path, bool isClosed)
	{
		return Clipper.Union(MinkowskiInternal(pattern, path, isSum: false, isClosed), FillRule.NonZero);
	}

	public static PathsD Diff(PathD pattern, PathD path, bool isClosed, int decimalPlaces = 2)
	{
		double num = Math.Pow(10.0, decimalPlaces);
		return Clipper.ScalePathsD(Clipper.Union(MinkowskiInternal(Clipper.ScalePath64(pattern, num), Clipper.ScalePath64(path, num), isSum: false, isClosed), FillRule.NonZero), 1.0 / num);
	}
}
