using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;

namespace Geospatial.DataStructures;

public class NeighborhoodSpatialIndex<T>
{
	private double _radius;

	private List<Neighborhood<T>> _neighborhoods;

	public IEnumerable<Neighborhood<T>> Neighborhoods
	{
		get
		{
			for (int i = 0; i < _neighborhoods.Count; i++)
			{
				yield return _neighborhoods[i];
			}
		}
	}

	public NeighborhoodSpatialIndex(double radius)
	{
		_radius = radius;
		_neighborhoods = new List<Neighborhood<T>>();
	}

	public void Add(Point pt, T key)
	{
		ClosestNeighborhood(pt, out var nIdx, out var dist);
		if (nIdx == -1 || dist > _radius)
		{
			_neighborhoods.Add(new Neighborhood<T>());
			_neighborhoods.Last().Add(pt, key);
		}
		else
		{
			_neighborhoods[nIdx].Add(pt, key);
		}
	}

	public void Merge()
	{
		bool condition;
		do
		{
			condition = false;
			for (int i = 0; i < _neighborhoods.Count; i++)
			{
				Neighborhood<T> neighborhood = _neighborhoods[i];
				for (int j = i + 1; j < _neighborhoods.Count; j++)
				{
					Neighborhood<T> neighborhood2 = _neighborhoods[j];
					if (neighborhood.DistanceTo(neighborhood2) < _radius)
					{
						neighborhood.Import(neighborhood2);
						_neighborhoods.Remove(neighborhood2);
						condition = true;
						break;
					}
				}
				if (condition)
				{
					break;
				}
			}
		}
		while (condition);
	}

	private void ClosestNeighborhood(Point pt, out int nIdx, out double dist)
	{
		dist = double.MaxValue;
		nIdx = -1;
		for (int i = 0; i < _neighborhoods.Count; i++)
		{
			double dist2 = _neighborhoods[i].DistanceTo(pt);
			if (dist2 < dist)
			{
				dist = dist2;
				nIdx = i;
			}
		}
	}
}
