namespace Geospatial.Clipper2Lib;

public class RectClipLines64 : RectClip64
{
	internal RectClipLines64(Rect64 rect)
		: base(rect)
	{
	}

	public new Paths64 Execute(Paths64 paths)
	{
		Paths64 paths2 = new Paths64();
		if (rect_.IsEmpty())
		{
			return paths2;
		}
		foreach (Path64 path2 in paths)
		{
			if (path2.Count < 2)
			{
				continue;
			}
			pathBounds_ = Clipper.GetBounds(path2);
			if (!rect_.Intersects(pathBounds_))
			{
				continue;
			}
			ExecuteInternal(path2);
			foreach (OutPt2 item in results_)
			{
				Path64 path = GetPath(item);
				if (path.Count > 0)
				{
					paths2.Add(path);
				}
			}
			results_.Clear();
			for (int i = 0; i < 8; i++)
			{
				edges_[i].Clear();
			}
		}
		return paths2;
	}

	private static Path64 GetPath(OutPt2? op)
	{
		Path64 path = new Path64();
		if (op == null || op == op.next)
		{
			return path;
		}
		op = op.next;
		path.Add(op.pt);
		for (OutPt2 next = op.next; next != op; next = next.next)
		{
			path.Add(next.pt);
		}
		return path;
	}

	private void ExecuteInternal(Path64 path)
	{
		results_.Clear();
		if (path.Count < 2 || rect_.IsEmpty())
		{
			return;
		}
		Location loc = Location.inside;
		int i = 1;
		int num = path.Count - 1;
		if (!RectClip64.GetLocation(rect_, path[0], out var loc2))
		{
			for (; i <= num && !RectClip64.GetLocation(rect_, path[i], out loc); i++)
			{
			}
			if (i > num)
			{
				{
					foreach (Point64 item in path)
					{
						Add(item);
					}
					return;
				}
			}
			if (loc == Location.inside)
			{
				loc2 = Location.inside;
			}
			i = 1;
		}
		if (loc2 == Location.inside)
		{
			Add(path[0]);
		}
		while (i <= num)
		{
			loc = loc2;
			GetNextLocation(path, ref loc2, ref i, num);
			if (i <= num)
			{
				Point64 point = path[i - 1];
				Location loc3 = loc2;
				if (!RectClip64.GetIntersection(rectPath_, path[i], point, ref loc3, out var ip))
				{
					i++;
				}
				else if (loc2 == Location.inside)
				{
					Add(ip, startingNewPath: true);
				}
				else if (loc != Location.inside)
				{
					loc3 = loc;
					RectClip64.GetIntersection(rectPath_, point, path[i], ref loc3, out var ip2);
					Add(ip2, startingNewPath: true);
					Add(ip);
				}
				else
				{
					Add(ip);
				}
				continue;
			}
			break;
		}
	}
}
