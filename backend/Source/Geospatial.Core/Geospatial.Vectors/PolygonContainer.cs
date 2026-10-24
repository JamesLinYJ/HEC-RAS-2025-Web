using System;
using System.Collections.Generic;
using Utility.DataStructures;

namespace Geospatial.Vectors;

public class PolygonContainer
{
	public readonly Polygon Polygon;

	public readonly IList<PolygonContainer> Children;

	public PolygonContainer(Polygon pgon, int childrenCapacity = 0)
	{
		Polygon = pgon;
		if (childrenCapacity < 2)
		{
			Children = default(CompactList<PolygonContainer>);
		}
		else
		{
			Children = new List<PolygonContainer>(childrenCapacity);
		}
	}

	public PolygonContainer AddChild(Polygon pgon, int childCapacity = 0)
	{
		PolygonContainer polygonContainer = new PolygonContainer(pgon);
		Children.Add(polygonContainer);
		return polygonContainer;
	}

	public bool RecurseCheckAreaInverts()
	{
		int areaPolygonTerm = Math.Sign(Polygon.Area);
		for (int i = 0; i < Children.Count; i++)
		{
			PolygonContainer polygonContainer = Children[i];
			if (Math.Sign(polygonContainer.Polygon.Area) == areaPolygonTerm)
			{
				return false;
			}
			if (!polygonContainer.RecurseCheckAreaInverts())
			{
				return false;
			}
		}
		return true;
	}

	public IList<Polygon> RecurseGetAllPolygons()
	{
		CompactList<Polygon> compactList = new CompactList<Polygon>(Polygon);
		for (int i = 0; i < Children.Count; i++)
		{
			compactList.AddRange(Children[i].RecurseGetAllPolygons());
		}
		return compactList;
	}

	public PolygonContainer WalkAndModify(Func<Polygon, Polygon> func)
	{
		Dictionary<Polygon, Polygon> polygonByPolygon = new Dictionary<Polygon, Polygon>();
		WalkHelper(this, func, polygonByPolygon);
		if (polygonByPolygon.Count > 0)
		{
			return CopyWithMutations(this, polygonByPolygon);
		}
		return null;
		static PolygonContainer CopyWithMutations(PolygonContainer container, Dictionary<Polygon, Polygon> mutations)
		{
			Polygon pgon = container.Polygon;
			if (mutations.TryGetValue(container.Polygon, out var pgon2))
			{
				pgon = pgon2;
			}
			PolygonContainer polygonContainer = new PolygonContainer(pgon, container.Children.Count);
			for (int i = 0; i < container.Children.Count; i++)
			{
				polygonContainer.Children.Add(CopyWithMutations(container.Children[i], mutations));
			}
			return polygonContainer;
		}
		static void WalkHelper(PolygonContainer container, Func<Polygon, Polygon> helperFunc, Dictionary<Polygon, Polygon> mutations)
		{
			Polygon polygon = helperFunc(container.Polygon);
			if (polygon != null)
			{
				mutations[container.Polygon] = polygon;
			}
			for (int i = 0; i < container.Children.Count; i++)
			{
				WalkHelper(container.Children[i], helperFunc, mutations);
			}
		}
	}
}
