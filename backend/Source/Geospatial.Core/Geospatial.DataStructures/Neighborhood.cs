using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.DataStructures;

public class Neighborhood<T>
{
	private List<Tuple<Point, T>> _items;

	public IEnumerable<Point> Points
	{
		get
		{
			foreach (Tuple<Point, T> tuple in _items)
			{
				yield return tuple.Item1;
			}
		}
	}

	public IEnumerable<T> Keys
	{
		get
		{
			foreach (Tuple<Point, T> tuple in _items)
			{
				yield return tuple.Item2;
			}
		}
	}

	public IEnumerable<Tuple<Point, T>> Items
	{
		get
		{
			foreach (Tuple<Point, T> tuple in _items)
			{
				yield return tuple;
			}
		}
	}

	public Neighborhood()
	{
		_items = new List<Tuple<Point, T>>();
	}

	public void Add(Point pt, T key)
	{
		_items.Add(new Tuple<Point, T>(pt, key));
	}

	public void Add(Tuple<Point, T> kvp)
	{
		_items.Add(kvp);
	}

	public double DistanceTo(Neighborhood<T> other)
	{
		double maxValue = double.MaxValue;
		foreach (Point point in Points)
		{
			maxValue = Math.Min(maxValue, other.DistanceTo(point));
		}
		return maxValue;
	}

	public double DistanceTo(Point pt)
	{
		double maxValue = double.MaxValue;
		foreach (Point point in Points)
		{
			maxValue = Math.Min(maxValue, point.DistanceTo(pt));
		}
		return maxValue;
	}

	public void Import(Neighborhood<T> other)
	{
		foreach (Tuple<Point, T> tuple in other.Items)
		{
			Add(tuple);
		}
	}
}
