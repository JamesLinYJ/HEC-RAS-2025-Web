using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Geospatial;
using Geospatial.DataStructures;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using Geospatial.Vectors;
using H5Assist;
using Utility;
using Utility.Hashing;

namespace Ras.Layers.Classifiers;

public class PolygonClassificationInput : ClassificationFeatureInput<PolygonClassifierMod>, IClassificationInput, IDataHash, ISpatialVersion, IPolygonCollection, IVectorCollection<Polygon>, IReadOnlyVectorCollection<Polygon>, IEnumerable<Polygon>, IEnumerable, IExtent, IReadOnlyList<Polygon>, IReadOnlyCollection<Polygon>, IReadOnlyPolygonCollection, ISpatiallyIndexedSegmentCollection, ILineSegmentSource
{
	public const string TypeNameStatic = "PolyClassifier";

	private readonly Versioned<SpatialIndex<FeatureSegmentSubset>> _segSpIdxCache = new Versioned<SpatialIndex<FeatureSegmentSubset>>();

	private readonly Versioned<SpatialIndex<int>> _spIdxCache = new Versioned<SpatialIndex<int>>();

	private readonly object _loadLock = new object();

	private string _hdfGroup;

	private Versioned<IResample<int>> _resamplerVersioned = new Versioned<IResample<int>>();

	private IPolygonCollection _plWrapper;

	public string TypeName => "PolyClassifier";

	protected override string HDF5Group => _hdfGroup;

	Polygon IVectorCollection<Polygon>.this[int index]
	{
		get
		{
			return Features[index].Polygon;
		}
		set
		{
			Features[index].Polygon = value;
		}
	}

	public PolygonClassificationInput(ClassificationLayer tr, string name)
		: base(tr)
	{
		Name = name;
	}

	internal PolygonClassificationInput()
	{
	}

	public IPolygonCollection AsPolygonCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolygonFtLayerWrapped<PolygonClassifierMod>(this, (PolygonClassifierMod sa) => sa.Polygon, CreateDefaultMod);
		}
		return _plWrapper;
	}

	public bool IsValid(out string err)
	{
		err = "";
		return true;
	}

	public void Save(H5Writer hw, string h5Path)
	{
		_hdfGroup = h5Path;
		Save(hw);
	}

	public bool TryInit(ClassificationLayer lyr, H5Reader hr, string h5Path)
	{
		ClassificationLayer = lyr;
		_hdfGroup = h5Path;
		LoadMetadata(hr);
		return true;
	}

	public PolygonClassifierMod CreateDefaultMod(Polygon pg)
	{
		string cls = "NoData";
		if (Count != 0)
		{
			IReadOnlyList<PolygonClassifierMod> features = Features;
			cls = features[features.Count - 1].Classification;
		}
		else if (ClassificationLayer.ClassificationMap != null && ClassificationLayer.ClassificationMap.Count != 0)
		{
			cls = ClassificationLayer.ClassificationMap.Values.First();
		}
		return new PolygonClassifierMod(pg, cls);
	}

	public void Add(Polygon pg, string classifier)
	{
		Add(new PolygonClassifierMod(pg, classifier));
	}

	public IResample<int> GetResampler()
	{
		return _resamplerVersioned.Get(VersionXY, BuildResampler);
	}

	public IResample<int> BuildResampler()
	{
		return new PolygonClassificationResampler(ClassificationLayer.ClassificationMap, Features);
	}

	IEnumerator<Polygon> IEnumerable<Polygon>.GetEnumerator()
	{
		throw new NotImplementedException();
	}

	public SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex()
	{
		return _segSpIdxCache.Get(VersionXY, CreateSegmentSpatialIndex);
	}

	public SpatialIndex<int> GetSpatialIndex()
	{
		return _spIdxCache.Get(VersionXY, CreateAreaSpatialIndex);
	}

	private SpatialIndex<FeatureSegmentSubset> CreateSegmentSpatialIndex()
	{
		return SpatialIndex.ComputeSegmentSpatialIndex(Features.Select((PolygonClassifierMod f) => f.Polygon).ToList());
	}

	private SpatialIndex<int> CreateAreaSpatialIndex()
	{
		return SpatialIndex.FromPolygons(Features.Select((PolygonClassifierMod f) => f.Polygon).ToList());
	}

	public void Add(Polygon polygon)
	{
		Add(polygon, "NoData");
	}

	public void Insert(int index, Polygon polygon)
	{
		throw new NotImplementedException();
	}

	public Classification GetClassifier()
	{
		Classification classification = new Classification();
		IReadOnlyList<PolygonClassifierMod> features = Features;
		if (features == null)
		{
			return classification;
		}
		int unity = 1;
		HashSet<string> stringSet = new HashSet<string>();
		foreach (PolygonClassifierMod feature in features)
		{
			string classification2 = feature.Classification;
			if (stringSet.Add(classification2))
			{
				classification.Set(unity, classification2);
				unity++;
			}
		}
		return classification;
	}

	IClassificationInput IClassificationInput.CloneWith(ClassificationLayer newClass)
	{
		return CloneWith(newClass);
	}

	public PolygonClassificationInput CloneWith(ClassificationLayer cls)
	{
		PolygonClassificationInput polygonClassificationInput = new PolygonClassificationInput(cls, Name);
		polygonClassificationInput.AddRange(Features);
		return polygonClassificationInput;
	}
}
