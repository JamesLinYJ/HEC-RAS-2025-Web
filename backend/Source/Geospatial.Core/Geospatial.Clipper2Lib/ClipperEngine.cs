using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Geospatial.Clipper2Lib;

internal static class ClipperEngine
{
	internal static void AddLocMin(Vertex vert, PathType polytype, bool isOpen, List<LocalMinima> minimaList)
	{
		if ((vert.flags & VertexFlags.LocalMin) == 0)
		{
			vert.flags |= VertexFlags.LocalMin;
			LocalMinima item = new LocalMinima(vert, polytype, isOpen);
			minimaList.Add(item);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void EnsureCapacity<T>(this List<T> list, int minCapacity)
	{
		if (list.Capacity < minCapacity)
		{
			list.Capacity = minCapacity;
		}
	}

	internal static void AddPathsToVertexList(Paths64 paths, PathType polytype, bool isOpen, List<LocalMinima> minimaList, List<Vertex> vertexList)
	{
		int num = 0;
		foreach (Path64 path in paths)
		{
			num += path.Count;
		}
		vertexList.EnsureCapacity(vertexList.Count + num);
		foreach (Path64 path2 in paths)
		{
			Vertex vertex = null;
			Vertex vertex2 = null;
			foreach (Point64 item in path2)
			{
				if (vertex == null)
				{
					vertex = new Vertex(item, VertexFlags.None, null);
					vertexList.Add(vertex);
					vertex2 = vertex;
				}
				else if (vertex2.pt != item)
				{
					Vertex vertex3 = new Vertex(item, VertexFlags.None, vertex2);
					vertexList.Add(vertex3);
					vertex2.next = vertex3;
					vertex2 = vertex3;
				}
			}
			if (vertex2?.prev == null)
			{
				continue;
			}
			if (!isOpen && vertex2.pt == vertex.pt)
			{
				vertex2 = vertex2.prev;
			}
			vertex2.next = vertex;
			vertex.prev = vertex2;
			if (!isOpen && vertex2.next == vertex2)
			{
				continue;
			}
			bool flag;
			if (isOpen)
			{
				Vertex vertex3 = vertex.next;
				while (vertex3 != vertex && vertex3.pt.Y == vertex.pt.Y)
				{
					vertex3 = vertex3.next;
				}
				flag = vertex3.pt.Y <= vertex.pt.Y;
				if (flag)
				{
					vertex.flags = VertexFlags.OpenStart;
					AddLocMin(vertex, polytype, isOpen: true, minimaList);
				}
				else
				{
					vertex.flags = VertexFlags.OpenStart | VertexFlags.LocalMax;
				}
			}
			else
			{
				vertex2 = vertex.prev;
				while (vertex2 != vertex && vertex2.pt.Y == vertex.pt.Y)
				{
					vertex2 = vertex2.prev;
				}
				if (vertex2 == vertex)
				{
					continue;
				}
				flag = vertex2.pt.Y > vertex.pt.Y;
			}
			bool flag2 = flag;
			vertex2 = vertex;
			for (Vertex vertex3 = vertex.next; vertex3 != vertex; vertex3 = vertex3.next)
			{
				if ((vertex3.pt.Y > vertex2.pt.Y) & flag)
				{
					vertex2.flags |= VertexFlags.LocalMax;
					flag = false;
				}
				else if (vertex3.pt.Y < vertex2.pt.Y && !flag)
				{
					flag = true;
					AddLocMin(vertex2, polytype, isOpen, minimaList);
				}
				vertex2 = vertex3;
			}
			if (isOpen)
			{
				vertex2.flags |= VertexFlags.OpenEnd;
				if (flag)
				{
					vertex2.flags |= VertexFlags.LocalMax;
				}
				else
				{
					AddLocMin(vertex2, polytype, isOpen, minimaList);
				}
			}
			else if (flag != flag2)
			{
				if (flag2)
				{
					AddLocMin(vertex2, polytype, isOpen: false, minimaList);
				}
				else
				{
					vertex2.flags |= VertexFlags.LocalMax;
				}
			}
		}
	}
}
