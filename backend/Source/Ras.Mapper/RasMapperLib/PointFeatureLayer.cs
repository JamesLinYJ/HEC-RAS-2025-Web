using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.DataStructures;
using Geospatial.Vectors;

namespace RasMapperLib;

public class PointFeatureLayer : FeatureLayer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D0
	{
		public int index;

		public _Closure_0024__9_002D0(_Closure_0024__9_002D0 other)
		{
			if (other != null)
			{
				index = other.index;
			}
		}

		[SpecialName]
		internal int _Lambda_0024__0(int oldidx)
		{
			if (oldidx >= index)
			{
				oldidx = checked(oldidx - 1);
			}
			return oldidx;
		}
	}

	private QuadTree<int> _quadtrees;

	private List<SolidBrush> alphaGradations;

	private Pen spatialIndexPolyPen;

	public Point this[int index] => Point(index);

	public PointFeatureLayer()
		: base("Point Layer")
	{
		alphaGradations = null;
		spatialIndexPolyPen = new Pen(Color.Black, 1);
	}

	public PointFeatureLayer(string name)
		: base(name)
	{
		alphaGradations = null;
		spatialIndexPolyPen = new Pen(Color.Black, 1);
	}

	public PointFeatureLayer(string name, string filename)
		: base(name, filename)
	{
		alphaGradations = null;
		spatialIndexPolyPen = new Pen(Color.Black, 1);
	}

	public PointFeatureLayer(string name, DataTable ftTable)
		: base(name, ftTable)
	{
		alphaGradations = null;
		spatialIndexPolyPen = new Pen(Color.Black, 1);
	}

	public bool EnsureQuadtree(Extent ext)
	{
		bool result = false;
		if (_quadtrees == null || _quadtrees.Equals(QuadTree<int>.NullQuadTree))
		{
			CreateQuadTree(ext);
			result = true;
		}
		return result;
	}

	private void CreateQuadTree(Extent ext)
	{
		ComputeExtent();
		Extent extent = Extent;
		if (extent == null)
		{
			extent = ext;
		}
		if (extent == null || Extent.IsNullOrInfinite(extent))
		{
			_quadtrees = QuadTree<int>.NullQuadTree;
		}
		else
		{
			_quadtrees = new QuadTree<int>(new Geospatial.Vectors.Point(extent.Left, extent.Bottom), new Geospatial.Vectors.Point(extent.Right, extent.Top), 1024, 100, 4096);
		}
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Point point = Feature(i) as Point;
				if (!double.IsNaN(point.X) && !double.IsNaN(point.Y))
				{
					_quadtrees.Insert(point.X, point.Y, i);
				}
			}
		}
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (EnsureQuadtree(null))
		{
			return;
		}
		List<int> featureIndicesChanged = args.FeatureIndicesChanged.ToList();
		checked
		{
			switch (args.ChangeType)
			{
			case FeatureChangedType.FeatureAdded:
			{
				Stopwatch stopwatch2 = Stopwatch.StartNew();
				foreach (int featureIndicesChanged2 in featureIndicesChanged)
				{
					Point point4 = Feature(featureIndicesChanged2) as Point;
					_quadtrees.Insert(point4.X, point4.Y, featureIndicesChanged2);
				}
				stopwatch2.Stop();
				break;
			}
			case FeatureChangedType.FeatureDeleted:
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				if ((double)featureIndicesChanged.Count / ((double)Features().Count() + (double)featureIndicesChanged.Count) < 0.000123)
				{
					int count = featureIndicesChanged.Count - 1;
					_Closure_0024__9_002D0 capturedLocals = default;
					for (int i = 0; i <= count; i++)
					{
						capturedLocals = new _Closure_0024__9_002D0(capturedLocals);
						Feature? feature = args.FeaturesDeleted.ElementAtOrDefault(i);
						capturedLocals.index = args.FeatureIndicesChanged.ElementAtOrDefault(i);
						Point point2 = feature as Point;
						_quadtrees.RemoveCoordinate(point2.X, point2.Y);
						VB_0024AnonymousDelegate_0<int, int> lambda_0024__0 = capturedLocals._Lambda_0024__0;
						QuadTree<int> quadtrees = _quadtrees;
						VB_0024AnonymousDelegate_0<int, int> lambda_0024__02 = lambda_0024__0;
						quadtrees.ModifyKeysParallel((lambda_0024__02 == null) ? null : new Func<int, int>(lambda_0024__02.Invoke));
					}
				}
				else
				{
					if (Features().Count() > 0)
					{
						CreateQuadTree(Extent);
					}
					else
					{
						_quadtrees = null;
					}
					int count2 = Features().Count() - 1;
					for (int j = 0; j <= count2; j++)
					{
						Point point3 = Feature(j) as Point;
						_quadtrees.Insert(point3.X, point3.Y, j);
					}
				}
				stopwatch.Stop();
				break;
			}
			case FeatureChangedType.FeatureModified:
			{
				Stopwatch.StartNew();
				Translate translate = (Translate)args.ChangedTransform;
				{
					foreach (int featureIndicesChanged3 in featureIndicesChanged)
					{
						Point point = Feature(featureIndicesChanged3) as Point;
						if (translate != null)
						{
							_quadtrees.RemoveCoordinate(point.X - translate.DeltaX, point.Y - translate.DeltaY, 1E-05, featureIndicesChanged3);
						}
						else
						{
							_quadtrees.RemoveCoordinate(point.X, point.Y, 1E-05, featureIndicesChanged3);
						}
						_quadtrees.Insert(point.X, point.Y, featureIndicesChanged3);
					}
					break;
				}
			}
			}
		}
	}

	private int FindHighestFIDInQuadTree()
	{
		EnsureQuadtree(null);
		List<Bin<int>> allLeafBins = _quadtrees.GetAllLeafBins();
		int highestFIDInQuadTree = -1;
		foreach (Bin<int> allLeafBin in allLeafBins)
		{
			if (allLeafBin.Items == null)
			{
				continue;
			}
			foreach (int highestFIDInQuadTree2 in allLeafBin.Items)
			{
				if (highestFIDInQuadTree2 > highestFIDInQuadTree)
				{
					highestFIDInQuadTree = highestFIDInQuadTree2;
				}
			}
		}
		return highestFIDInQuadTree;
	}

	public override bool SetSelection(Extent ext, SelectionMode selectionMode, SelectionRange selectionRange, ref bool layerNeedsRedraw)
	{
		layerNeedsRedraw = false;
		Stopwatch stopwatch = Stopwatch.StartNew();
		SelectedFeature firstOpenFeature = SelectedFeatures.GetFirstOpenFeature();
		if (firstOpenFeature != null)
		{
			if (!(Feature(firstOpenFeature.FID) is MultiPoint multiPoint))
			{
				return false;
			}
			multiPoint.SelectPoints(ext, selectionMode, selectionRange, firstOpenFeature);
		}
		else
		{
			EnsureQuadtree(ext);
			if (ext.Left == ext.Right || ext.Top == ext.Bottom)
			{
				return false;
			}
			List<int> allValuesWithinRectangle = _quadtrees.GetAllValuesWithinRectangle(ext.Left, ext.Bottom, ext.Right, ext.Top);
			switch (selectionMode)
			{
			case SelectionMode.SelectNew:
				SelectedFeatures.ClearSelection();
				foreach (int allValuesWithinRectangle2 in allValuesWithinRectangle)
				{
					SelectedFeature selectedFeature3 = new SelectedFeature(allValuesWithinRectangle2, fake: true);
					SelectedFeatures.Add(selectedFeature3);
					if (selectionRange == SelectionRange.SelectSingle)
					{
						break;
					}
				}
				break;
			case SelectionMode.Append:
				foreach (int allValuesWithinRectangle3 in allValuesWithinRectangle)
				{
					SelectedFeature selectedFeature2 = new SelectedFeature(allValuesWithinRectangle3, fake: true);
					SelectedFeatures.Add(selectedFeature2);
				}
				break;
			case SelectionMode.Deselect:
				foreach (int allValuesWithinRectangle4 in allValuesWithinRectangle)
				{
					SelectedFeatures.Remove(allValuesWithinRectangle4);
				}
				break;
			case SelectionMode.Toggle:
				foreach (int allValuesWithinRectangle5 in allValuesWithinRectangle)
				{
					if (SelectedFeatures.Contains(allValuesWithinRectangle5))
					{
						SelectedFeatures.Remove(allValuesWithinRectangle5);
						continue;
					}
					SelectedFeature selectedFeature = new SelectedFeature(allValuesWithinRectangle5, fake: true);
					SelectedFeatures.Add(selectedFeature);
				}
				break;
			default:
				throw new NotImplementedException();
			}
		}
		stopwatch.Stop();
		_ = SelectedFeatures.Count;
		return true;
	}

	private void InitializeAlphaGradations()
	{
		alphaGradations = new List<SolidBrush>();
		int ordinal = 30;
		checked
		{
			do
			{
				alphaGradations.Add(new SolidBrush(Color.FromArgb(ordinal * 4, Color.Black)));
				ordinal++;
			}
			while (ordinal <= 63);
		}
	}

	public override List<int> SpatialQueryFIDs(PointM pt, double dist)
	{
		if (FeatureCount() == 0)
		{
			return new List<int>();
		}
		List<int> intList = new List<int>();
		EnsureQuadtree(Extent);
		if (_quadtrees != null)
		{
			List<int> allValuesWithinCircle = _quadtrees.GetAllValuesWithinCircle(pt.X, pt.Y, dist);
			foreach (int allValuesWithinCircle2 in allValuesWithinCircle)
			{
				if ((FeatureRowFilter == null || FeatureRowFilter(FeatureRow(allValuesWithinCircle2))) && FeatureIsHitTestable(allValuesWithinCircle2))
				{
					intList.Add(allValuesWithinCircle2);
				}
			}
		}
		return intList;
	}

	public override List<int> SpatialQueryFIDs(Extent ext)
	{
		if (FeatureCount() == 0)
		{
			return new List<int>();
		}
		List<int> intList = new List<int>();
		EnsureQuadtree(Extent);
		if (_quadtrees != null)
		{
			List<int> allValuesWithinRectangle = _quadtrees.GetAllValuesWithinRectangle(ext.Left, ext.Bottom, ext.Right, ext.Top);
			foreach (int allValuesWithinRectangle2 in allValuesWithinRectangle)
			{
				if ((FeatureRowFilter == null || FeatureRowFilter(FeatureRow(allValuesWithinRectangle2))) && FeatureIsHitTestable(allValuesWithinRectangle2))
				{
					intList.Add(allValuesWithinRectangle2);
				}
			}
		}
		return intList;
	}

	public override int SpatialQueryFID(PointM pt, double dist)
	{
		if (FeatureCount() == 0)
		{
			return -1;
		}
		EnsureQuadtree(Extent);
		if (_quadtrees != null)
		{
			List<int> allValuesWithinCircle = _quadtrees.GetAllValuesWithinCircle(pt.X, pt.Y, dist);
			allValuesWithinCircle.OrderByDescending([SpecialName] (int x) => x);
			foreach (int allValuesWithinCircle2 in allValuesWithinCircle)
			{
				if (allValuesWithinCircle2 >= 0 && (FeatureRowFilter == null || FeatureRowFilter(FeatureRow(allValuesWithinCircle2))) && FeatureIsHitTestable(allValuesWithinCircle2))
				{
					return allValuesWithinCircle2;
				}
			}
		}
		return -1;
	}

	public override int SpatialQueryFID(Extent ext)
	{
		if (FeatureCount() == 0)
		{
			return -1;
		}
		EnsureQuadtree(Extent);
		if (_quadtrees != null)
		{
			List<int> allValuesWithinRectangle = _quadtrees.GetAllValuesWithinRectangle(ext.Left, ext.Bottom, ext.Right, ext.Top);
			allValuesWithinRectangle.OrderByDescending([SpecialName] (int x) => x);
			foreach (int allValuesWithinRectangle2 in allValuesWithinRectangle)
			{
				if (allValuesWithinRectangle2 >= 0 && (FeatureRowFilter == null || FeatureRowFilter(FeatureRow(allValuesWithinRectangle2))) && FeatureIsHitTestable(allValuesWithinRectangle2))
				{
					return allValuesWithinRectangle2;
				}
			}
		}
		return -1;
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.AddRange(RasMapperLib.Point.GetAvailablePlotOptions(SharedData.DebugMode));
		return staticPlotOptions;
	}

	public IEnumerable<Point> Points()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return Point(i);
			}
		}
	}

	public Point Point(int fid)
	{
		return (Point)Feature(fid);
	}

	public override List<Type> AvailableFeatureTypes()
	{
		return new Type[1] { typeof(Point) }.ToList();
	}

	protected override string FeatureToCSV(int fId)
	{
		Point point = Point(fId);
		if (point == null)
		{
			return "";
		}
		return point.X + "," + point.Y + "," + point.Z + "," + point.M;
	}

	protected override Feature CSVToFeature(string featureCSV, string typeName)
	{
		string[] parts = featureCSV.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 2)
		{
			return null;
		}
		double result = double.NaN;
		double result2 = double.NaN;
		if (double.TryParse(parts[0], out var result3) && double.TryParse(parts[1], out var result4))
		{
			if (parts.Length >= 3)
			{
				double.TryParse(parts[2], out result);
			}
			if (parts.Length == 4)
			{
				double.TryParse(parts[3], out result2);
			}
			return new Point(result3, result4, result, result2);
		}
		return null;
	}
}
