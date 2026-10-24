using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Geospatial.Vectors;
using Utility;

namespace Geospatial.DataStructures;

public class Bin<T>
{
	public const double SDRATIOTHRESHOLD = 0.01;

	private const double SDABSOLUTETHRESHOLD = 0.1;

	private int _sizeLimit;

	public bool[] ChildrenHaveItems = new bool[4];

	public int ID { get; private set; }

	public int NumItems { get; internal set; }

	public Point Center { get; private set; }

	public Bin<T> Parent { get; private set; }

	public Point ParentUpperLeftCorner { get; private set; }

	public Bin<T> FirstQuadrant { get; internal set; }

	public Bin<T> SecondQuadrant { get; internal set; }

	public Bin<T> ThirdQuadrant { get; internal set; }

	public Bin<T> FourthQuadrant { get; internal set; }

	public Stats StatsObjX { get; internal set; }

	public Stats StatsObjY { get; internal set; }

	public double SideLen { get; internal set; }

	public List<T> Items { get; internal set; }

	public List<Point> ItemCoordinates { get; internal set; }

	public bool AtleastOneChildHasItems
	{
		get
		{
			bool result = true;
			for (int i = 0; i < ChildrenHaveItems.Length; i++)
			{
				if (!ChildrenHaveItems[i])
				{
					result = false;
					break;
				}
			}
			return result;
		}
	}

	public bool IsLeafNode => FirstQuadrant == null;

	internal Bin()
	{
	}

	internal Bin(int sizeLimit, Point center, double sideLen, int id, Point parentUpperLeftCorner)
	{
		_sizeLimit = sizeLimit;
		Center = center;
		SideLen = sideLen;
		NumItems = 0;
		ID = id;
		ParentUpperLeftCorner = parentUpperLeftCorner;
	}

	internal Bin(int sizeLimit, Point center, double sideLen, Bin<T> parent, int id, Point parentUpperLeftCorner)
	{
		_sizeLimit = sizeLimit;
		Center = center;
		SideLen = sideLen;
		NumItems = 0;
		Parent = parent;
		ID = id;
		ParentUpperLeftCorner = parentUpperLeftCorner;
	}

	public static implicit operator List<T>(Bin<T> b)
	{
		return b.Items;
	}

	internal int WhatQuadrant(Point pt)
	{
		if (pt.Y > Center.Y)
		{
			if (pt.X < Center.X)
			{
				return 1;
			}
			return 2;
		}
		if (pt.Y < Center.Y)
		{
			if (pt.X < Center.X)
			{
				return 3;
			}
			return 4;
		}
		if (pt.Y == Center.Y)
		{
			if (pt.X < Center.X)
			{
				return 1;
			}
			_ = pt.X;
			_ = Center.X;
			return 2;
		}
		return 2;
	}

	internal void InsertValue(RootBinLevel<T> root, Point coord, T insertedValue)
	{
		if (StatsObjX == null && IsLeafNode)
		{
			StatsObjX = new Stats(double.MinValue, ParentUpperLeftCorner.X);
			StatsObjY = new Stats(double.MinValue, ParentUpperLeftCorner.Y);
		}
		if (Items == null && IsLeafNode)
		{
			Items = new List<T>(_sizeLimit);
			ItemCoordinates = new List<Point>(_sizeLimit);
		}
		if (!IsLeafNode)
		{
			ChildInsert(root, coord, insertedValue);
			return;
		}
		AddToStatsObj(coord);
		if (NumItems + 1 > _sizeLimit && (StatsObjX.StdDev > Math.Min(0.01 * SideLen, 0.1) || StatsObjY.StdDev > Math.Min(0.01 * SideLen, 0.1)))
		{
			root.Dirty = true;
			double scaledSideLen = SideLen / 2.0;
			Point point = new Point(Center.X - scaledSideLen, Center.Y + scaledSideLen);
			Point point2 = new Point(Center.X - scaledSideLen, Center.Y - scaledSideLen);
			Point point3 = new Point(Center.X + scaledSideLen, Center.Y + scaledSideLen);
			Point point4 = new Point(Center.X + scaledSideLen, Center.Y - scaledSideLen);
			FirstQuadrant = new Bin<T>(center: new Point((Center.X + point.X) / 2.0, (Center.Y + point.Y) / 2.0), sizeLimit: _sizeLimit, sideLen: scaledSideLen, parent: this, id: Interlocked.Increment(ref root.NextIDNumber), parentUpperLeftCorner: ParentUpperLeftCorner);
			SecondQuadrant = new Bin<T>(center: new Point((Center.X + point3.X) / 2.0, (Center.Y + point3.Y) / 2.0), sizeLimit: _sizeLimit, sideLen: scaledSideLen, parent: this, id: Interlocked.Increment(ref root.NextIDNumber), parentUpperLeftCorner: ParentUpperLeftCorner);
			ThirdQuadrant = new Bin<T>(center: new Point((Center.X + point2.X) / 2.0, (Center.Y + point2.Y) / 2.0), sizeLimit: _sizeLimit, sideLen: scaledSideLen, parent: this, id: Interlocked.Increment(ref root.NextIDNumber), parentUpperLeftCorner: ParentUpperLeftCorner);
			FourthQuadrant = new Bin<T>(center: new Point((Center.X + point4.X) / 2.0, (Center.Y + point4.Y) / 2.0), sizeLimit: _sizeLimit, sideLen: scaledSideLen, parent: this, id: Interlocked.Increment(ref root.NextIDNumber), parentUpperLeftCorner: ParentUpperLeftCorner);
			bool[] childrenHaveItems = new bool[4];
			ChildrenHaveItems = childrenHaveItems;
			for (int i = 0; i < NumItems; i++)
			{
				ChildInsert(root, ItemCoordinates[i], Items[i]);
			}
			ChildInsert(root, coord, insertedValue);
			Items = new List<T>();
			ItemCoordinates = new List<Point>();
			NumItems = 0;
			StatsObjX = new Stats(double.MinValue, ParentUpperLeftCorner.X);
			StatsObjY = new Stats(double.MinValue, ParentUpperLeftCorner.Y);
		}
		else
		{
			Items.Add(insertedValue);
			ItemCoordinates.Add(coord);
			NumItems++;
			if (Parent != null)
			{
				Parent.ChildrenHaveItems[Parent.WhatQuadrant(coord) - 1] = true;
			}
		}
	}

	internal List<T> RemoveValueWithCoordinatesWithError(RootBinLevel<T> root, Point coord, double error)
	{
		if (!IsLeafNode)
		{
			return ChildRemoveWithError(root, coord, error);
		}
		if (Items == null)
		{
			return null;
		}
		bool condition = false;
		List<T> items = null;
		for (int countOffset = ItemCoordinates.Count - 1; countOffset >= 0; countOffset--)
		{
			if (ItemCoordinates[countOffset].X > coord.X - error && ItemCoordinates[countOffset].X < coord.X + error && ItemCoordinates[countOffset].Y > coord.Y - error && ItemCoordinates[countOffset].Y < coord.Y + error)
			{
				if (!condition)
				{
					items = new List<T>();
				}
				StatsObjX.Remove(ItemCoordinates[countOffset].X);
				StatsObjY.Remove(ItemCoordinates[countOffset].Y);
				items.Add(Items[countOffset]);
				ItemCoordinates.RemoveAt(countOffset);
				Items.RemoveAt(countOffset);
				NumItems--;
				condition = true;
			}
		}
		if (condition)
		{
			int numItemsSum = 0;
			for (Bin<T> parent = Parent; parent != null; parent = parent.Parent)
			{
				if (parent.FirstQuadrant.Items != null)
				{
					numItemsSum += parent.FirstQuadrant.NumItems;
				}
				if (parent.SecondQuadrant.Items != null)
				{
					numItemsSum += parent.SecondQuadrant.NumItems;
				}
				if (parent.ThirdQuadrant.Items != null)
				{
					numItemsSum += parent.ThirdQuadrant.NumItems;
				}
				if (parent.FourthQuadrant.Items != null)
				{
					numItemsSum += parent.FourthQuadrant.NumItems;
				}
				if (numItemsSum <= _sizeLimit && parent.AllChildren((Bin<T> x) => x.IsLeafNode))
				{
					root.Dirty = true;
					if (parent.Items == null)
					{
						parent.Items = new List<T>();
						parent.ItemCoordinates = new List<Point>();
					}
					if (parent.StatsObjX == null)
					{
						parent.StatsObjX = new Stats(double.MinValue, ParentUpperLeftCorner.X);
						parent.StatsObjY = new Stats(double.MinValue, ParentUpperLeftCorner.Y);
					}
					parent.Items.AddRange((from child in parent.AllChildren()
						where child.Items != null
						select child).SelectMany((Bin<T> child) => child.Items));
					parent.ItemCoordinates.AddRange((from child in parent.AllChildren()
						where child.ItemCoordinates != null
						select child).SelectMany((Bin<T> child) => child.ItemCoordinates));
					parent.NumItems += parent.AllChildren().Sum((Bin<T> child) => child.NumItems);
					parent.StatsObjX = Stats.Combine((from child in parent.AllChildren()
						select child.StatsObjX into st
						where st != null
						select st).ToList());
					parent.StatsObjY = Stats.Combine((from child in parent.AllChildren()
						select child.StatsObjY into st
						where st != null
						select st).ToList());
					parent.ThirdQuadrant = null;
					parent.FourthQuadrant = null;
					parent.FirstQuadrant = null;
					parent.SecondQuadrant = null;
					bool[] childrenHaveItems = new bool[4];
					parent.ChildrenHaveItems = childrenHaveItems;
					if (parent.Parent != null)
					{
						parent.Parent.ChildrenHaveItems[parent.Parent.WhatQuadrant(coord) - 1] = true;
					}
				}
			}
		}
		else
		{
			Console.WriteLine("Why we here....");
		}
		return items;
	}

	internal bool RemoveValueWithCoordinatesWithError(RootBinLevel<T> root, Point coord, double error, T localValue)
	{
		if (!IsLeafNode)
		{
			return ChildRemoveWithError(root, coord, error, localValue);
		}
		if (Items == null)
		{
			return false;
		}
		bool condition = false;
		for (int countOffset = ItemCoordinates.Count - 1; countOffset >= 0; countOffset--)
		{
			if (ItemCoordinates[countOffset].X > coord.X - error && ItemCoordinates[countOffset].X < coord.X + error && ItemCoordinates[countOffset].Y > coord.Y - error && ItemCoordinates[countOffset].Y < coord.Y + error && Items[countOffset].Equals(localValue))
			{
				StatsObjX.Remove(ItemCoordinates[countOffset].X);
				StatsObjY.Remove(ItemCoordinates[countOffset].Y);
				ItemCoordinates.RemoveAt(countOffset);
				Items.RemoveAt(countOffset);
				NumItems--;
				condition = true;
			}
		}
		if (condition)
		{
			int numItemsSum = 0;
			for (Bin<T> parent = Parent; parent != null; parent = parent.Parent)
			{
				if (parent.FirstQuadrant.Items != null)
				{
					numItemsSum += parent.FirstQuadrant.NumItems;
				}
				if (parent.SecondQuadrant.Items != null)
				{
					numItemsSum += parent.SecondQuadrant.NumItems;
				}
				if (parent.ThirdQuadrant.Items != null)
				{
					numItemsSum += parent.ThirdQuadrant.NumItems;
				}
				if (parent.FourthQuadrant.Items != null)
				{
					numItemsSum += parent.FourthQuadrant.NumItems;
				}
				if (numItemsSum <= _sizeLimit && parent.AllChildren((Bin<T> x) => x.IsLeafNode))
				{
					root.Dirty = true;
					if (parent.Items == null)
					{
						parent.Items = new List<T>();
						parent.ItemCoordinates = new List<Point>();
					}
					if (parent.StatsObjX == null)
					{
						parent.StatsObjX = new Stats(double.MinValue, ParentUpperLeftCorner.X);
						parent.StatsObjY = new Stats(double.MinValue, ParentUpperLeftCorner.Y);
					}
					parent.Items.AddRange((from child in parent.AllChildren()
						where child.Items != null
						select child).SelectMany((Bin<T> child) => child.Items));
					parent.ItemCoordinates.AddRange((from child in parent.AllChildren()
						where child.ItemCoordinates != null
						select child).SelectMany((Bin<T> child) => child.ItemCoordinates));
					parent.NumItems += parent.AllChildren().Sum((Bin<T> child) => child.NumItems);
					parent.StatsObjX = Stats.Combine((from child in parent.AllChildren()
						select child.StatsObjX into st
						where st != null
						select st).ToList());
					parent.StatsObjY = Stats.Combine((from child in parent.AllChildren()
						select child.StatsObjY into st
						where st != null
						select st).ToList());
					parent.ThirdQuadrant = null;
					parent.FourthQuadrant = null;
					parent.FirstQuadrant = null;
					parent.SecondQuadrant = null;
					bool[] childrenHaveItems = new bool[4];
					parent.ChildrenHaveItems = childrenHaveItems;
					if (parent.Parent != null)
					{
						parent.Parent.ChildrenHaveItems[parent.Parent.WhatQuadrant(coord) - 1] = true;
					}
				}
			}
		}
		else
		{
			Console.WriteLine("Why we here....");
		}
		return condition;
	}

	internal bool RemoveValueWithCoordinates(RootBinLevel<T> root, Point coord, T localValue)
	{
		if (FirstQuadrant != null)
		{
			return ChildRemove(root, coord, localValue);
		}
		if (Items == null)
		{
			return false;
		}
		bool condition = false;
		for (int countOffset = ItemCoordinates.Count - 1; countOffset >= 0; countOffset--)
		{
			if (ItemCoordinates[countOffset].X == coord.X && ItemCoordinates[countOffset].Y == coord.Y && Items[countOffset].Equals(localValue))
			{
				StatsObjX.Remove(ItemCoordinates[countOffset].X);
				StatsObjY.Remove(ItemCoordinates[countOffset].Y);
				ItemCoordinates.RemoveAt(countOffset);
				Items.RemoveAt(countOffset);
				NumItems--;
				condition = true;
			}
		}
		if (condition)
		{
			int numItemsSum = 0;
			for (Bin<T> parent = Parent; parent != null; parent = parent.Parent)
			{
				if (parent.FirstQuadrant.Items != null)
				{
					numItemsSum += parent.FirstQuadrant.NumItems;
				}
				if (parent.SecondQuadrant.Items != null)
				{
					numItemsSum += parent.SecondQuadrant.NumItems;
				}
				if (parent.ThirdQuadrant.Items != null)
				{
					numItemsSum += parent.ThirdQuadrant.NumItems;
				}
				if (parent.FourthQuadrant.Items != null)
				{
					numItemsSum += parent.FourthQuadrant.NumItems;
				}
				if (numItemsSum <= _sizeLimit && parent.AllChildren((Bin<T> x) => x.IsLeafNode))
				{
					root.Dirty = true;
					if (parent.Items == null)
					{
						parent.Items = new List<T>();
						parent.ItemCoordinates = new List<Point>();
					}
					if (parent.StatsObjX == null)
					{
						parent.StatsObjX = new Stats(double.MinValue, ParentUpperLeftCorner.X);
						parent.StatsObjY = new Stats(double.MinValue, ParentUpperLeftCorner.Y);
					}
					parent.Items.AddRange((from child in parent.AllChildren()
						where child.Items != null
						select child).SelectMany((Bin<T> child) => child.Items));
					parent.ItemCoordinates.AddRange((from child in parent.AllChildren()
						where child.ItemCoordinates != null
						select child).SelectMany((Bin<T> child) => child.ItemCoordinates));
					parent.NumItems += parent.AllChildren().Sum((Bin<T> child) => child.NumItems);
					parent.StatsObjX = Stats.Combine((from child in parent.AllChildren()
						select child.StatsObjX into st
						where st != null
						select st).ToList());
					parent.StatsObjY = Stats.Combine((from child in parent.AllChildren()
						select child.StatsObjY into st
						where st != null
						select st).ToList());
					parent.ThirdQuadrant = null;
					parent.FourthQuadrant = null;
					parent.FirstQuadrant = null;
					parent.SecondQuadrant = null;
					bool[] childrenHaveItems = new bool[4];
					parent.ChildrenHaveItems = childrenHaveItems;
					if (parent.Parent != null)
					{
						parent.Parent.ChildrenHaveItems[parent.Parent.WhatQuadrant(coord) - 1] = true;
					}
				}
			}
		}
		return condition;
	}

	internal List<T> RemoveValueWithCoordinates(RootBinLevel<T> root, Point coord)
	{
		if (!IsLeafNode)
		{
			return ChildRemove(root, coord);
		}
		if (Items == null)
		{
			return null;
		}
		bool condition = false;
		List<T> items = null;
		for (int countOffset = ItemCoordinates.Count - 1; countOffset >= 0; countOffset--)
		{
			if (ItemCoordinates[countOffset].X == coord.X && ItemCoordinates[countOffset].Y == coord.Y)
			{
				if (!condition)
				{
					items = new List<T>();
				}
				StatsObjX.Remove(ItemCoordinates[countOffset].X);
				StatsObjY.Remove(ItemCoordinates[countOffset].Y);
				items.Add(Items[countOffset]);
				ItemCoordinates.RemoveAt(countOffset);
				Items.RemoveAt(countOffset);
				NumItems--;
				condition = true;
			}
		}
		if (condition)
		{
			int numItemsSum = 0;
			for (Bin<T> parent = Parent; parent != null; parent = parent.Parent)
			{
				if (parent.FirstQuadrant.Items != null)
				{
					numItemsSum += parent.FirstQuadrant.NumItems;
				}
				if (parent.SecondQuadrant.Items != null)
				{
					numItemsSum += parent.SecondQuadrant.NumItems;
				}
				if (parent.ThirdQuadrant.Items != null)
				{
					numItemsSum += parent.ThirdQuadrant.NumItems;
				}
				if (parent.FourthQuadrant.Items != null)
				{
					numItemsSum += parent.FourthQuadrant.NumItems;
				}
				if (numItemsSum <= _sizeLimit && parent.AllChildren((Bin<T> x) => x.IsLeafNode))
				{
					root.Dirty = true;
					if (parent.Items == null)
					{
						parent.Items = new List<T>();
						parent.ItemCoordinates = new List<Point>();
					}
					if (parent.StatsObjX == null)
					{
						parent.StatsObjX = new Stats(double.MinValue, ParentUpperLeftCorner.X);
						parent.StatsObjY = new Stats(double.MinValue, ParentUpperLeftCorner.Y);
					}
					parent.Items.AddRange((from child in parent.AllChildren()
						where child.Items != null
						select child).SelectMany((Bin<T> child) => child.Items));
					parent.ItemCoordinates.AddRange((from child in parent.AllChildren()
						where child.ItemCoordinates != null
						select child).SelectMany((Bin<T> child) => child.ItemCoordinates));
					parent.NumItems += parent.AllChildren().Sum((Bin<T> child) => child.NumItems);
					parent.StatsObjX = Stats.Combine((from child in parent.AllChildren()
						select child.StatsObjX into st
						where st != null
						select st).ToList());
					parent.StatsObjY = Stats.Combine((from child in parent.AllChildren()
						select child.StatsObjY into st
						where st != null
						select st).ToList());
					parent.ThirdQuadrant = null;
					parent.FourthQuadrant = null;
					parent.FirstQuadrant = null;
					parent.SecondQuadrant = null;
					bool[] childrenHaveItems = new bool[4];
					parent.ChildrenHaveItems = childrenHaveItems;
					if (parent.Parent != null)
					{
						parent.Parent.ChildrenHaveItems[parent.Parent.WhatQuadrant(coord) - 1] = true;
					}
				}
			}
		}
		return items;
	}

	private void AddToStatsObj(Point coord)
	{
		StatsObjX.Insert(coord.X);
		StatsObjY.Insert(coord.Y);
	}

	private void ChildInsert(RootBinLevel<T> root, Point coord, T localValue)
	{
		switch (WhatQuadrant(coord))
		{
		case 1:
			FirstQuadrant.InsertValue(root, coord, localValue);
			break;
		case 2:
			SecondQuadrant.InsertValue(root, coord, localValue);
			break;
		case 3:
			ThirdQuadrant.InsertValue(root, coord, localValue);
			break;
		default:
			FourthQuadrant.InsertValue(root, coord, localValue);
			break;
		}
		IfAllChildrenHaveItemsTellParent(coord);
	}

	private void IfAllChildrenHaveItemsTellParent(Point coord)
	{
		if (Parent == null)
		{
			return;
		}
		bool condition = true;
		for (int i = 0; i < ChildrenHaveItems.Length; i++)
		{
			if (!ChildrenHaveItems[i])
			{
				condition = false;
				break;
			}
		}
		if (condition)
		{
			Parent.ChildrenHaveItems[Parent.WhatQuadrant(coord) - 1] = true;
		}
	}

	private List<T> ChildRemoveWithError(RootBinLevel<T> root, Point coord, double error)
	{
		int whatQuadrant = WhatQuadrant(coord);
		List<T> items = null;
		items = whatQuadrant switch
		{
			1 => FirstQuadrant.RemoveValueWithCoordinatesWithError(root, coord, error), 
			2 => SecondQuadrant.RemoveValueWithCoordinatesWithError(root, coord, error), 
			3 => ThirdQuadrant.RemoveValueWithCoordinatesWithError(root, coord, error), 
			_ => FourthQuadrant.RemoveValueWithCoordinatesWithError(root, coord, error), 
		};
		IfAllChildrenHaveItemsTellParent(coord);
		return items;
	}

	private bool ChildRemoveWithError(RootBinLevel<T> root, Point coord, double error, T localValue)
	{
		bool result = WhatQuadrant(coord) switch
		{
			1 => FirstQuadrant.RemoveValueWithCoordinatesWithError(root, coord, error, localValue), 
			2 => SecondQuadrant.RemoveValueWithCoordinatesWithError(root, coord, error, localValue), 
			3 => ThirdQuadrant.RemoveValueWithCoordinatesWithError(root, coord, error, localValue), 
			_ => FourthQuadrant.RemoveValueWithCoordinatesWithError(root, coord, error, localValue), 
		};
		IfAllChildrenHaveItemsTellParent(coord);
		return result;
	}

	private List<T> ChildRemove(RootBinLevel<T> root, Point coord)
	{
		List<T> result = WhatQuadrant(coord) switch
		{
			1 => FirstQuadrant.RemoveValueWithCoordinates(root, coord), 
			2 => SecondQuadrant.RemoveValueWithCoordinates(root, coord), 
			3 => ThirdQuadrant.RemoveValueWithCoordinates(root, coord), 
			_ => FourthQuadrant.RemoveValueWithCoordinates(root, coord), 
		};
		IfAllChildrenHaveItemsTellParent(coord);
		return result;
	}

	private bool ChildRemove(RootBinLevel<T> root, Point coord, T localValue)
	{
		bool result = WhatQuadrant(coord) switch
		{
			1 => FirstQuadrant.RemoveValueWithCoordinates(root, coord, localValue), 
			2 => SecondQuadrant.RemoveValueWithCoordinates(root, coord, localValue), 
			3 => ThirdQuadrant.RemoveValueWithCoordinates(root, coord, localValue), 
			_ => FourthQuadrant.RemoveValueWithCoordinates(root, coord, localValue), 
		};
		IfAllChildrenHaveItemsTellParent(coord);
		return result;
	}

	public List<T> GetItemsFromBin(double xCart, double yCart)
	{
		if (!IsLeafNode)
		{
			Point pt = new Point(xCart, yCart);
			return WhatQuadrant(pt) switch
			{
				1 => FirstQuadrant.GetItemsFromBin(xCart, yCart), 
				2 => SecondQuadrant.GetItemsFromBin(xCart, yCart), 
				3 => ThirdQuadrant.GetItemsFromBin(xCart, yCart), 
				_ => FourthQuadrant.GetItemsFromBin(xCart, yCart), 
			};
		}
		return Items;
	}

	public bool IsNeighborBin(Bin<T> bin)
	{
		Point point = new Point(Center.X + SideLen / 2.0, Center.Y + SideLen / 2.0);
		Point point2 = new Point(Center.X - SideLen / 2.0, Center.Y - SideLen / 2.0);
		Point point3 = new Point(bin.Center.X + bin.SideLen / 2.0, bin.Center.Y + bin.SideLen / 2.0);
		Point point4 = new Point(bin.Center.X - bin.SideLen / 2.0, bin.Center.Y - bin.SideLen / 2.0);
		if (point.X == point4.X || point3.X == point2.X || point.Y == point4.Y || point3.Y == point2.Y)
		{
			return true;
		}
		return false;
	}

	internal bool AllSiblings(Func<Bin<T>, bool> predicate)
	{
		if (predicate(Parent.FirstQuadrant) && predicate(Parent.SecondQuadrant) && predicate(Parent.ThirdQuadrant) && predicate(Parent.FourthQuadrant))
		{
			return true;
		}
		return false;
	}

	internal bool AllChildren(Func<Bin<T>, bool> predicate)
	{
		if (predicate(FirstQuadrant) && predicate(SecondQuadrant) && predicate(ThirdQuadrant) && predicate(FourthQuadrant))
		{
			return true;
		}
		return false;
	}

	public IEnumerable<Bin<T>> AllChildren()
	{
		yield return FirstQuadrant;
		yield return SecondQuadrant;
		yield return ThirdQuadrant;
		yield return FourthQuadrant;
	}

	public IEnumerable<Bin<T>> AllChildren(Bin<T> firstQuad, Bin<T> secondQuad, Bin<T> thirdQuad, Bin<T> fourthQuad)
	{
		yield return firstQuad;
		yield return secondQuad;
		yield return thirdQuad;
		yield return fourthQuad;
	}

	public int GetRoughNumValuesFromBin(double Left, double Bottom, double Right, double Top)
	{
		if (!IsLeafNode)
		{
			if (Items == null)
			{
				Items = new List<T>();
			}
			return (from bin in AllChildren()
				where bin.Items != null
				select bin).Sum((Bin<T> bin) => bin.GetRoughNumValuesFromBin(Left, Bottom, Right, Top));
		}
		double xBottomLeftRect = Center.X - SideLen / 2.0;
		double yBottomLeftRect = Center.Y - SideLen / 2.0;
		double xTopRightRect = Center.X + SideLen / 2.0;
		double yTopRightRect = Center.Y + SideLen / 2.0;
		double areaOfIntersectingRectangles = GeometryTools.GetAreaOfIntersectingRectangles(xBottomLeftRect, yBottomLeftRect, xTopRightRect, yTopRightRect, Left, Bottom, Right, Top);
		return (int)Math.Ceiling((double)NumItems * areaOfIntersectingRectangles / Math.Pow(SideLen, 2.0));
	}

	public int GetCountValuesFromBin()
	{
		if (!IsLeafNode)
		{
			if (Items == null)
			{
				Items = new List<T>();
			}
			return (from bin in AllChildren()
				where bin.Items != null
				select bin).Sum((Bin<T> bin) => bin.GetCountValuesFromBin());
		}
		return NumItems;
	}

	public List<T> GetValues()
	{
		if (!IsLeafNode)
		{
			List<T> items = new List<T>();
			items.AddRange((from bin in AllChildren()
				where bin.Items != null
				select bin).SelectMany((Bin<T> bin) => bin.GetItemsFromBinInternal()));
			return items;
		}
		return Items;
	}

	private IEnumerable<T> GetItemsFromBinInternal()
	{
		if (!IsLeafNode)
		{
			if (Items == null)
			{
				Items = new List<T>();
			}
			return (from bin in AllChildren()
				where bin.Items != null
				select bin).SelectMany((Bin<T> bin) => bin.GetItemsFromBinInternal());
		}
		return Items;
	}

	internal void DoModificationOnItemsFromBin(Func<T, T> modification)
	{
		if (!IsLeafNode)
		{
			if (Items == null)
			{
				Items = new List<T>();
			}
			FirstQuadrant.DoModificationOnItemsFromBin(modification);
			SecondQuadrant.DoModificationOnItemsFromBin(modification);
			ThirdQuadrant.DoModificationOnItemsFromBin(modification);
			FourthQuadrant.DoModificationOnItemsFromBin(modification);
		}
		else if (Items != null)
		{
			for (int i = 0; i < Items.Count; i++)
			{
				Items[i] = modification(Items[i]);
			}
		}
	}

	public List<Point> GetCoordinatesFromBin()
	{
		if (!IsLeafNode)
		{
			List<Point> points = new List<Point>();
			points.AddRange((from bin in AllChildren()
				where bin.ItemCoordinates != null
				select bin).SelectMany((Bin<T> bin) => bin.GetCoordinatesFromBinInternal()));
			return points;
		}
		return ItemCoordinates;
	}

	private IEnumerable<Point> GetCoordinatesFromBinInternal()
	{
		if (!IsLeafNode)
		{
			if (ItemCoordinates == null)
			{
				ItemCoordinates = new List<Point>();
			}
			return (from bin in AllChildren()
				where bin.ItemCoordinates != null
				select bin).SelectMany((Bin<T> bin) => bin.GetCoordinatesFromBinInternal());
		}
		return ItemCoordinates;
	}

	public List<Tuple<T, Point>> GetValuesAndCoordinatesFromBin()
	{
		if (!IsLeafNode)
		{
			List<Tuple<T, Point>> valueTupleList = new List<Tuple<T, Point>>();
			valueTupleList.AddRange((from bin in AllChildren()
				where bin.ItemCoordinates != null
				select bin).SelectMany((Bin<T> bin) => bin.GetValuesAndCoordinatesFromBinInternal()));
			return valueTupleList;
		}
		List<Tuple<T, Point>> valueTupleListList = new List<Tuple<T, Point>>();
		int ordinal = 0;
		while (ItemCoordinates != null && Items != null && ordinal < ItemCoordinates.Count)
		{
			Tuple<T, Point> pointItemCoordinatesTerm = new Tuple<T, Point>(Items[ordinal], ItemCoordinates[ordinal]);
			valueTupleListList.Add(pointItemCoordinatesTerm);
			ordinal++;
		}
		return valueTupleListList;
	}

	private IEnumerable<Tuple<T, Point>> GetValuesAndCoordinatesFromBinInternal()
	{
		if (!IsLeafNode)
		{
			if (ItemCoordinates == null)
			{
				ItemCoordinates = new List<Point>();
			}
			if (Items == null)
			{
				Items = new List<T>();
			}
			return (from bin in AllChildren()
				where bin.ItemCoordinates != null
				select bin).SelectMany((Bin<T> bin) => bin.GetValuesAndCoordinatesFromBinInternal());
		}
		List<Tuple<T, Point>> valueTupleList = new List<Tuple<T, Point>>();
		int ordinal = 0;
		while (ItemCoordinates != null && Items != null && ordinal < ItemCoordinates.Count)
		{
			Tuple<T, Point> pointItemCoordinatesTerm = new Tuple<T, Point>(Items[ordinal], ItemCoordinates[ordinal]);
			valueTupleList.Add(pointItemCoordinatesTerm);
			ordinal++;
		}
		return valueTupleList;
	}

	public void ReplaceExistingItemsWith(List<T> values)
	{
		Items = values;
		if (Items == null)
		{
			NumItems = 0;
		}
		else
		{
			NumItems = Items.Count;
		}
	}

	public void ReplaceExistingCoord(List<Point> coords)
	{
		ItemCoordinates = coords;
	}

	internal Bin<T> GetBinUsingPoint(double xCart, double yCart)
	{
		if (!IsLeafNode)
		{
			Point pt = new Point(xCart, yCart);
			return WhatQuadrant(pt) switch
			{
				1 => FirstQuadrant.GetBinUsingPoint(xCart, yCart), 
				2 => SecondQuadrant.GetBinUsingPoint(xCart, yCart), 
				3 => ThirdQuadrant.GetBinUsingPoint(xCart, yCart), 
				_ => FourthQuadrant.GetBinUsingPoint(xCart, yCart), 
			};
		}
		return this;
	}

	internal void DeterminePartialOrFullChildren(double Left, double Bottom, double Right, double Top, ref List<Bin<T>> partialCover, ref List<Bin<T>> fullyCovered)
	{
		if (FirstQuadrant != null)
		{
			FirstQuadrant.DeterminePartialOrFullChildren(Left, Bottom, Right, Top, ref partialCover, ref fullyCovered);
			SecondQuadrant.DeterminePartialOrFullChildren(Left, Bottom, Right, Top, ref partialCover, ref fullyCovered);
			ThirdQuadrant.DeterminePartialOrFullChildren(Left, Bottom, Right, Top, ref partialCover, ref fullyCovered);
			FourthQuadrant.DeterminePartialOrFullChildren(Left, Bottom, Right, Top, ref partialCover, ref fullyCovered);
		}
		else if (Center.X - SideLen / 2.0 < Left)
		{
			if (Center.X + SideLen / 2.0 > Left)
			{
				if (Center.Y + SideLen / 2.0 > Top && Center.Y - SideLen / 2.0 < Top)
				{
					partialCover.Add(this);
				}
				else if (Center.Y + SideLen / 2.0 > Bottom && Center.Y - SideLen / 2.0 < Bottom)
				{
					partialCover.Add(this);
				}
				else if (Center.Y + SideLen / 2.0 <= Top && Center.Y - SideLen / 2.0 >= Bottom)
				{
					partialCover.Add(this);
				}
			}
		}
		else if (Center.X + SideLen / 2.0 > Right)
		{
			if (!(Center.X - SideLen / 2.0 >= Right))
			{
				if (Center.Y + SideLen / 2.0 > Top && Center.Y - SideLen / 2.0 < Top)
				{
					partialCover.Add(this);
				}
				else if (Center.Y + SideLen / 2.0 > Bottom && Center.Y - SideLen / 2.0 < Bottom)
				{
					partialCover.Add(this);
				}
				else if (Center.Y + SideLen / 2.0 <= Top && Center.Y - SideLen / 2.0 >= Bottom)
				{
					partialCover.Add(this);
				}
			}
		}
		else if (Center.Y + SideLen / 2.0 > Top && Center.Y - SideLen / 2.0 < Top)
		{
			partialCover.Add(this);
		}
		else if (Center.Y + SideLen / 2.0 > Bottom && Center.Y - SideLen / 2.0 < Bottom)
		{
			partialCover.Add(this);
		}
		else if (Center.Y + SideLen / 2.0 <= Top && Center.Y - SideLen / 2.0 >= Bottom)
		{
			fullyCovered.Add(this);
		}
	}

	internal List<T> GetValuesWithinRectangle(double Left, double Bottom, double Right, double Top)
	{
		if (!IsLeafNode)
		{
			List<T> items = new List<T>();
			items.AddRange(AllChildren().SelectMany((Bin<T> bin) => bin.GetValuesWithinRectangle(Left, Bottom, Right, Top)));
			return items;
		}
		List<T> itemsList = new List<T>();
		if (ItemCoordinates == null)
		{
			return itemsList;
		}
		for (int intervalIndex = 0; intervalIndex < ItemCoordinates.Count; intervalIndex++)
		{
			if (ItemCoordinates[intervalIndex].X >= Left && ItemCoordinates[intervalIndex].Y >= Bottom && ItemCoordinates[intervalIndex].X <= Right && ItemCoordinates[intervalIndex].Y <= Top)
			{
				itemsList.Add(Items[intervalIndex]);
			}
		}
		return itemsList;
	}

	internal List<Point> GetCoordinatesWithinRectangle(double Left, double Bottom, double Right, double Top)
	{
		if (!IsLeafNode)
		{
			List<Point> points = new List<Point>();
			points.AddRange(AllChildren().SelectMany((Bin<T> bin) => bin.GetCoordinatesWithinRectangle(Left, Bottom, Right, Top)));
			return points;
		}
		List<Point> pointsList = new List<Point>();
		if (ItemCoordinates == null)
		{
			return pointsList;
		}
		for (int intervalIndex = 0; intervalIndex < ItemCoordinates.Count; intervalIndex++)
		{
			if (ItemCoordinates[intervalIndex].X >= Left && ItemCoordinates[intervalIndex].Y >= Bottom && ItemCoordinates[intervalIndex].X <= Right && ItemCoordinates[intervalIndex].Y <= Top)
			{
				pointsList.Add(ItemCoordinates[intervalIndex]);
			}
		}
		return pointsList;
	}

	internal List<Tuple<T, Point>> GetValuesAndCoordinatesWithinRectangle(double Left, double Bottom, double Right, double Top)
	{
		if (!IsLeafNode)
		{
			List<Tuple<T, Point>> valueTupleList = new List<Tuple<T, Point>>();
			valueTupleList.AddRange(AllChildren().SelectMany((Bin<T> bin) => bin.GetValuesAndCoordinatesWithinRectangle(Left, Bottom, Right, Top)));
			return valueTupleList;
		}
		List<Tuple<T, Point>> valueTupleListList = new List<Tuple<T, Point>>();
		if (ItemCoordinates == null)
		{
			return valueTupleListList;
		}
		for (int intervalIndex = 0; intervalIndex < ItemCoordinates.Count; intervalIndex++)
		{
			if (ItemCoordinates[intervalIndex].X >= Left && ItemCoordinates[intervalIndex].Y >= Bottom && ItemCoordinates[intervalIndex].X <= Right && ItemCoordinates[intervalIndex].Y <= Top)
			{
				Tuple<T, Point> pointItemCoordinatesTerm = new Tuple<T, Point>(Items[intervalIndex], ItemCoordinates[intervalIndex]);
				valueTupleListList.Add(pointItemCoordinatesTerm);
			}
		}
		return valueTupleListList;
	}

	internal int GetNumberOfValuesWithinRectangle(double Left, double Bottom, double Right, double Top)
	{
		int ordinal = 0;
		if (!IsLeafNode)
		{
			return AllChildren().Sum((Bin<T> bin) => bin.GetNumberOfValuesWithinRectangle(Left, Bottom, Right, Top));
		}
		new List<T>();
		if (ItemCoordinates == null)
		{
			return 0;
		}
		for (int intervalIndex = 0; intervalIndex < ItemCoordinates.Count; intervalIndex++)
		{
			if (ItemCoordinates[intervalIndex].X >= Left && ItemCoordinates[intervalIndex].Y >= Bottom && ItemCoordinates[intervalIndex].X <= Right && ItemCoordinates[intervalIndex].Y <= Top)
			{
				ordinal++;
			}
		}
		return ordinal;
	}

	internal List<T> GetValuesWithinCircle(double xCenter, double yCenter, double radius)
	{
		if (!IsLeafNode)
		{
			List<T> items = new List<T>();
			items.AddRange(AllChildren().SelectMany((Bin<T> bin) => bin.GetValuesWithinCircle(xCenter, yCenter, radius)));
			return items;
		}
		List<T> itemsList = new List<T>();
		if (ItemCoordinates == null)
		{
			return itemsList;
		}
		double radiusSquared = Math.Pow(radius, 2.0);
		for (int intervalIndex = 0; intervalIndex < ItemCoordinates.Count; intervalIndex++)
		{
			if (Math.Pow(ItemCoordinates[intervalIndex].X - xCenter, 2.0) + Math.Pow(ItemCoordinates[intervalIndex].Y - yCenter, 2.0) < radiusSquared)
			{
				itemsList.Add(Items[intervalIndex]);
			}
		}
		return itemsList;
	}

	internal List<Point> GetCoordinatesWithinCircle(double xCenter, double yCenter, double radius)
	{
		if (!IsLeafNode)
		{
			List<Point> points = new List<Point>();
			points.AddRange(AllChildren().SelectMany((Bin<T> bin) => bin.GetCoordinatesWithinCircle(xCenter, yCenter, radius)));
			return points;
		}
		List<Point> pointsList = new List<Point>();
		if (ItemCoordinates == null)
		{
			return pointsList;
		}
		double radiusSquared = Math.Pow(radius, 2.0);
		for (int intervalIndex = 0; intervalIndex < ItemCoordinates.Count; intervalIndex++)
		{
			if (Math.Pow(ItemCoordinates[intervalIndex].X - xCenter, 2.0) + Math.Pow(ItemCoordinates[intervalIndex].Y - yCenter, 2.0) < radiusSquared)
			{
				pointsList.Add(ItemCoordinates[intervalIndex]);
			}
		}
		return pointsList;
	}

	internal List<Tuple<T, Point>> GetValuesAndCoordinatesWithinCircle(double xCenter, double yCenter, double radius)
	{
		if (!IsLeafNode)
		{
			List<Tuple<T, Point>> valueTupleList = new List<Tuple<T, Point>>();
			valueTupleList.AddRange(AllChildren().SelectMany((Bin<T> bin) => bin.GetValuesAndCoordinatesWithinCircle(xCenter, yCenter, radius)));
			return valueTupleList;
		}
		List<Tuple<T, Point>> valueTupleListList = new List<Tuple<T, Point>>();
		if (ItemCoordinates == null)
		{
			return valueTupleListList;
		}
		double radiusSquared = Math.Pow(radius, 2.0);
		for (int intervalIndex = 0; intervalIndex < ItemCoordinates.Count; intervalIndex++)
		{
			if (Math.Pow(ItemCoordinates[intervalIndex].X - xCenter, 2.0) + Math.Pow(ItemCoordinates[intervalIndex].Y - yCenter, 2.0) < radiusSquared)
			{
				Tuple<T, Point> pointItemCoordinatesTerm = new Tuple<T, Point>(Items[intervalIndex], ItemCoordinates[intervalIndex]);
				valueTupleListList.Add(pointItemCoordinatesTerm);
			}
		}
		return valueTupleListList;
	}

	internal List<T> GetValuesWithinLine(double xCart1, double yCart1, double xCart2, double yCart2)
	{
		if (!IsLeafNode)
		{
			List<T> items = new List<T>();
			items.AddRange(AllChildren().SelectMany((Bin<T> bin) => bin.GetValuesWithinLine(xCart1, yCart1, xCart2, yCart2)).ToList());
			return items;
		}
		List<T> itemsList = new List<T>();
		int ordinal = 0;
		while (ItemCoordinates != null && ordinal < ItemCoordinates.Count)
		{
			if (GeometryTools.EuclideanDistance(new Point(xCart1, yCart1), ItemCoordinates[ordinal]) + GeometryTools.EuclideanDistance(ItemCoordinates[ordinal], new Point(xCart2, yCart2)) == GeometryTools.EuclideanDistance(new Point(xCart1, yCart1), new Point(xCart2, yCart2)))
			{
				itemsList.Add(Items[ordinal]);
			}
			ordinal++;
		}
		return itemsList;
	}

	internal List<Point> GetCoordinatesWithinLine(double xCart1, double yCart1, double xCart2, double yCart2)
	{
		if (!IsLeafNode)
		{
			List<Point> points = new List<Point>();
			points.AddRange(AllChildren().SelectMany((Bin<T> bin) => bin.GetCoordinatesWithinLine(xCart1, yCart1, xCart2, yCart2)).ToList());
			return points;
		}
		List<Point> pointsList = new List<Point>();
		int ordinal = 0;
		while (ItemCoordinates != null && ordinal < ItemCoordinates.Count)
		{
			if (GeometryTools.EuclideanDistance(new Point(xCart1, yCart1), ItemCoordinates[ordinal]) + GeometryTools.EuclideanDistance(ItemCoordinates[ordinal], new Point(xCart2, yCart2)) == GeometryTools.EuclideanDistance(new Point(xCart1, yCart1), new Point(xCart2, yCart2)))
			{
				pointsList.Add(ItemCoordinates[ordinal]);
			}
			ordinal++;
		}
		return pointsList;
	}

	internal List<Tuple<T, Point>> GetValuesAndCoordinatesWithinLine(double xCart1, double yCart1, double xCart2, double yCart2)
	{
		if (!IsLeafNode)
		{
			List<Tuple<T, Point>> valueTupleList = new List<Tuple<T, Point>>();
			valueTupleList.AddRange(AllChildren().SelectMany((Bin<T> bin) => bin.GetValuesAndCoordinatesWithinLine(xCart1, yCart1, xCart2, yCart2)).ToList());
			return valueTupleList;
		}
		List<Tuple<T, Point>> valueTupleListList = new List<Tuple<T, Point>>();
		int ordinal = 0;
		while (ItemCoordinates != null && ordinal < ItemCoordinates.Count)
		{
			if (GeometryTools.EuclideanDistance(new Point(xCart1, yCart1), ItemCoordinates[ordinal]) + GeometryTools.EuclideanDistance(ItemCoordinates[ordinal], new Point(xCart2, yCart2)) == GeometryTools.EuclideanDistance(new Point(xCart1, yCart1), new Point(xCart2, yCart2)))
			{
				Tuple<T, Point> pointItemCoordinatesTerm = new Tuple<T, Point>(Items[ordinal], ItemCoordinates[ordinal]);
				valueTupleListList.Add(pointItemCoordinatesTerm);
			}
			ordinal++;
		}
		return valueTupleListList;
	}

	internal List<T> GetValuesInPoint(double xCart, double yCart)
	{
		Bin<T> binUsingPoint = GetBinUsingPoint(xCart, yCart);
		List<T> valuesInPoint = new List<T>();
		int ordinal = 0;
		while (binUsingPoint.ItemCoordinates != null && ordinal < binUsingPoint.ItemCoordinates.Count)
		{
			if (binUsingPoint.ItemCoordinates[ordinal].X == xCart && binUsingPoint.ItemCoordinates[ordinal].Y == yCart)
			{
				valuesInPoint.Add(binUsingPoint.Items[ordinal]);
			}
			ordinal++;
		}
		return valuesInPoint;
	}

	internal List<Point> GetCoordinateInPoint(double xCart, double yCart)
	{
		Bin<T> binUsingPoint = GetBinUsingPoint(xCart, yCart);
		List<Point> points = new List<Point>();
		int ordinal = 0;
		while (binUsingPoint.ItemCoordinates != null && ordinal < binUsingPoint.ItemCoordinates.Count)
		{
			if (binUsingPoint.ItemCoordinates[ordinal].X == xCart && binUsingPoint.ItemCoordinates[ordinal].Y == yCart)
			{
				points.Add(binUsingPoint.ItemCoordinates[ordinal]);
			}
			ordinal++;
		}
		return points;
	}

	internal List<Tuple<T, Point>> GetValueAndCoordinateInPoint(double xCart, double yCart)
	{
		Bin<T> binUsingPoint = GetBinUsingPoint(xCart, yCart);
		List<Tuple<T, Point>> valueAndCoordinateInPoint = new List<Tuple<T, Point>>();
		int ordinal = 0;
		while (binUsingPoint.ItemCoordinates != null && ordinal < binUsingPoint.ItemCoordinates.Count)
		{
			if (binUsingPoint.ItemCoordinates[ordinal].X == xCart && binUsingPoint.ItemCoordinates[ordinal].Y == yCart)
			{
				Tuple<T, Point> pointBinUsingPointTerm = new Tuple<T, Point>(binUsingPoint.Items[ordinal], binUsingPoint.ItemCoordinates[ordinal]);
				valueAndCoordinateInPoint.Add(pointBinUsingPointTerm);
			}
			ordinal++;
		}
		return valueAndCoordinateInPoint;
	}
}
