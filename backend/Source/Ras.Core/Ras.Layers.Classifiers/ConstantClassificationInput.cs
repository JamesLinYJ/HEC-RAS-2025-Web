using System;
using System.Collections.Generic;
using Geospatial;
using Geospatial.IO.ORM;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using Geospatial.Vectors;
using H5Assist;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers.Classifiers;

public class ConstantClassificationInput : IClassificationInput, IDataHash, ISpatialVersion
{
	public const string TypeNameConst = "Constant";

	private ConstantValueResampler<int> _res;

	private Extent _ext;

	private int _val;

	private string _class;

	private Freezer _freezer;

	private long _versionXy = 1L;

	[Metadata("Type")]
	public string TypeName => "Constant";

	[Metadata("Name")]
	public string Name { get; set; }

	[Metadata("Valid Extent")]
	public Extent ValidExtent
	{
		get
		{
			return _ext;
		}
		set
		{
			_ext = value;
			if (_res != null)
			{
				_res.ValidExtent = value;
			}
		}
	}

	[Metadata("Value")]
	public string Value
	{
		get
		{
			return _class;
		}
		set
		{
			_class = value;
			_versionXy++;
		}
	}

	public ClassificationLayer ClassificationLayer { get; private set; }

	public long VersionXY
	{
		get
		{
			if (!_freezer.IsFrozen)
			{
				return _versionXy;
			}
			return _freezer.FrozenVersionXY;
		}
	}

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public ConstantClassificationInput(ClassificationLayer cl, Extent ext, string name)
	{
		ClassificationLayer = cl;
		_class = name;
		ValidExtent = ext;
		Name = name;
		_val = int.MinValue;
	}

	public ConstantClassificationInput(ClassificationLayer cl, Extent ext, string name, int intValue)
	{
		ClassificationLayer = cl;
		_class = name;
		ValidExtent = ext;
		Name = name;
		_val = intValue;
	}

	private ConstantClassificationInput()
	{
	}

	private void RebuildResampler()
	{
		_res = new ConstantValueResampler<int>(_val, ValidExtent, ClassificationLayer.NoDataValue);
	}

	public bool TryInit(ClassificationLayer cl, H5Reader hr, string h5Path)
	{
		if (!hr.ExistsAsGroup(h5Path))
		{
			return false;
		}
		ClassificationLayer = cl;
		GeospatialH5Metadata.Read(this, hr, h5Path);
		RebuildResampler();
		return true;
	}

	public void Save(H5Writer hw, string h5Path)
	{
		GeospatialH5Metadata.Write(this, hw, h5Path);
	}

	public IResample<int> GetResampler()
	{
		int bestID = ClassificationLayer.ClassificationMap.GetBestID(_class);
		if (_res == null || bestID != _val)
		{
			_val = bestID;
			RebuildResampler();
		}
		return _res;
	}

	public bool IsValid(out string err)
	{
		err = "";
		return true;
	}

	public int GetDataHash()
	{
		return HashCode.Combine(ValidExtent.GetHashCode(), Value.GetHashCode(), Name.GetHashCode());
	}

	public Classification GetClassifier()
	{
		return new Classification(new Dictionary<int, string> { { 1, _class } });
	}

	IClassificationInput IClassificationInput.CloneWith(ClassificationLayer newClass)
	{
		return CloneWith(newClass);
	}

	public ConstantClassificationInput CloneWith(ClassificationLayer cls)
	{
		return new ConstantClassificationInput(cls, ValidExtent, Name, _val);
	}
}
