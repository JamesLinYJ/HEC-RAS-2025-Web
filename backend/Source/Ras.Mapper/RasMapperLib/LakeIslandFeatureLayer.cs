using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RasMapperLib;

public class LakeIslandFeatureLayer : PolygonFeatureLayer
{
	private HashSet<Polygon> _lakes;

	private HashSet<Polygon> _islands;

	private bool _lakeMode;

	public int IgnoreLakeIslandModeSemaphore;

	public HashSet<Polygon> Lakes => new HashSet<Polygon>(_lakes);

	public HashSet<Polygon> Islands => new HashSet<Polygon>(_islands);

	public bool IsInLakeMode => _lakeMode;

	public bool IsInIslandMode => !_lakeMode;

	public LakeIslandFeatureLayer(string name)
		: base(name)
	{
		_lakes = new HashSet<Polygon>();
		_islands = new HashSet<Polygon>();
		_lakeMode = true;
		IgnoreLakeIslandModeSemaphore = 0;
	}

	public LakeIslandFeatureLayer()
		: base("Lakes and Islands")
	{
		_lakes = new HashSet<Polygon>();
		_islands = new HashSet<Polygon>();
		_lakeMode = true;
		IgnoreLakeIslandModeSemaphore = 0;
	}

	public void CreateLakesAndIslandsFromParts(IList<Polygon> pgonParts)
	{
		checked
		{
			IgnoreLakeIslandModeSemaphore++;
			AddFeatureRange(new List<Feature>(pgonParts.OrderByDescending([SpecialName] (Polygon pgon) => pgon.Area)));
			IgnoreLakeIslandModeSemaphore--;
		}
	}

	public void CreateLakesAndIslandsFromComplexPolygon(Polygon pgon)
	{
		List<Polygon> pgonParts = pgon.ExplodeMultipartPolygon();
		CreateLakesAndIslandsFromParts(pgonParts);
	}

	public Polygon GetComplexPolygonFromLakesAndIslands()
	{
		List<Polygon> polygons = new List<Polygon>();
		foreach (Polygon island in _islands)
		{
			polygons.Add(island);
		}
		foreach (Polygon lake in _lakes)
		{
			polygons.Add(lake);
		}
		return RasMapperLib.Polygon.MergeToMultipartPolygon(polygons);
	}

	public void ClearLakesAndIslands()
	{
		DeleteAllFeatures();
		_lakes.Clear();
		_islands.Clear();
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureAdded)
		{
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				if (IgnoreLakeIslandModeSemaphore > 0)
				{
					if (Polygon(featureIndicesChanged).Area < 0.0)
					{
						_islands.Add(Polygon(featureIndicesChanged));
					}
					else
					{
						_lakes.Add(Polygon(featureIndicesChanged));
					}
				}
				else if (IsInLakeMode)
				{
					if (Polygon(featureIndicesChanged).Area < 0.0)
					{
						_lakes.Remove(Polygon(featureIndicesChanged));
						SetFeature(featureIndicesChanged, Polygon(featureIndicesChanged).Reverse());
						_lakes.Add(Polygon(featureIndicesChanged));
					}
				}
				else if (IsInIslandMode && Polygon(featureIndicesChanged).Area > 0.0)
				{
					_islands.Remove(Polygon(featureIndicesChanged));
					SetFeature(featureIndicesChanged, Polygon(featureIndicesChanged).Reverse());
					_islands.Add(Polygon(featureIndicesChanged));
				}
			}
			return;
		}
		if (args.ChangeType == FeatureChangedType.FeatureDeleted)
		{
			foreach (Polygon featuresDeleted in args.FeaturesDeleted)
			{
				if (_islands.Contains(featuresDeleted))
				{
					_islands.Remove(featuresDeleted);
				}
				if (_lakes.Contains(featuresDeleted))
				{
					_lakes.Remove(featuresDeleted);
				}
			}
			return;
		}
		if (args.ChangeType != FeatureChangedType.FeatureModified)
		{
			return;
		}
		foreach (int featureIndicesChanged2 in args.FeatureIndicesChanged)
		{
			if (IsInLakeMode)
			{
				_lakes.Add(Polygon(featureIndicesChanged2));
			}
			else if (IsInIslandMode)
			{
				_islands.Add(Polygon(featureIndicesChanged2));
			}
		}
	}

	protected override void OnFeatureChanging(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanging(args);
		if (args.ChangeType != FeatureChangedType.FeatureModified)
		{
			return;
		}
		foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
		{
			Polygon polygon = Polygon(featureIndicesChanged);
			if (_islands.Contains(polygon))
			{
				_islands.Remove(polygon);
			}
			if (_lakes.Contains(polygon))
			{
				_lakes.Remove(polygon);
			}
		}
	}

	public override bool FeatureIsHitTestable(int fid)
	{
		if (IsInLakeMode)
		{
			return _lakes.Contains(Polygon(fid));
		}
		return _islands.Contains(Polygon(fid));
	}
}
