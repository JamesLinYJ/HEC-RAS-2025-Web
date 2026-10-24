using System;
using System.Collections.Generic;
using System.Linq;
using RasMapperLib.TinHelpers;

namespace RasMapperLib;

public class TINCreator
{
	private List<PointM> _pts;

	private List<TriangleStruct> _triangles;

	public TINCreator()
	{
		_pts = new List<PointM>();
		_triangles = new List<TriangleStruct>();
	}

	private void Clear()
	{
		_triangles.Clear();
		_pts.Clear();
	}

	private int AddTriangle(int p0, int p1, int p2)
	{
		TriangleStruct triangleStruct = default;
		triangleStruct.AssignPoints(p0, p1, p2);
		_triangles.Add(triangleStruct);
		return checked(_triangles.Count - 1);
	}

	public Tin EarClipPolygonTIN(PointMs pts, double filterTolerance = 0.1)
	{
		if (pts.Count < 3)
		{
			return null;
		}
		checked
		{
			if (pts.First().EqualXY(pts.Last()))
			{
				pts.RemoveAt(pts.Count - 1);
			}
			if (pts.Count < 3)
			{
				return null;
			}
			Clear();
			LinkedList<int> linkedList = new LinkedList<int>();
			int count = pts.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				linkedList.AddLast(i);
			}
			LinkedListNode<int> linkedListNode = linkedList.First;
			int linkedListTerm = -1;
			while (linkedList.Count != 3)
			{
				if (linkedListNode == linkedList.First)
				{
					if (linkedListTerm == linkedList.Count)
					{
						return null;
					}
					linkedListTerm = linkedList.Count;
				}
				LinkedListNode<int> linkedListNode2 = LLPrevious(linkedListNode, linkedList);
				LinkedListNode<int> linkedListNode3 = LLNext(linkedListNode, linkedList);
				int linkedListNode2Term = linkedListNode2.Value;
				int linkedListNodeTerm = linkedListNode.Value;
				int linkedListNode3Term = linkedListNode3.Value;
				if (!pts.CLeftOfAB(linkedListNode2Term, linkedListNodeTerm, linkedListNode3Term))
				{
					LinkedListNode<int> linkedListNode4 = LLNext(linkedListNode3, linkedList);
					bool pointInTriangle = false;
					while (linkedListNode4 != linkedListNode2 && !pointInTriangle)
					{
						pointInTriangle = pts.PointInTriangle(pts[linkedListNode4.Value], linkedListNode2Term, linkedListNodeTerm, linkedListNode3Term);
						linkedListNode4 = LLNext(linkedListNode4, linkedList);
					}
					if (!pointInTriangle)
					{
						AddTriangle(linkedListNode2Term, linkedListNode3Term, linkedListNodeTerm);
						linkedList.Remove(linkedListNode);
					}
				}
				linkedListNode = linkedListNode3;
			}
			if (linkedList.Count == 3)
			{
				AddTriangle(linkedList.First.Value, linkedList.Last.Value, linkedList.First.Next.Value);
				return new Tin(pts, _triangles.ToArray());
			}
			throw new Exception("Could not create Cell TIN.");
		}
	}

	private LinkedListNode<int> LLPrevious(LinkedListNode<int> lln, LinkedList<int> ll)
	{
		if (lln.Previous == null)
		{
			return ll.Last;
		}
		return lln.Previous;
	}

	private LinkedListNode<int> LLNext(LinkedListNode<int> lln, LinkedList<int> ll)
	{
		if (lln.Next == null)
		{
			return ll.First;
		}
		return lln.Next;
	}

	private bool CLeftOfAB(int a, int b, int c)
	{
		double ptsTerm = _pts[b].X - _pts[a].X;
		double ptsTerm2 = _pts[b].Y - _pts[a].Y;
		double ptsTerm3 = _pts[c].X - _pts[a].X;
		double ptsTerm4 = _pts[c].Y - _pts[a].Y;
		return ptsTerm * ptsTerm4 - ptsTerm2 * ptsTerm3 > 0.0;
	}
}
