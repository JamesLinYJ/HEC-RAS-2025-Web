using System;
using System.Collections.Generic;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Rasters.Modifiers;
using H5Assist;
using Utility;
using Utility.Hashing;

namespace Ras.Layers.Terrains;

public class LeveeModTerrainInput : TerrainRasterModificationLayer<LeveeMod>, ITerrainInput, IDataHash, IDisposable, ISpatialVersion
{
	public const string TypeNameConst = "Levee";

	private readonly object _leveeModPropertiesLock = new object();

	private LeveeModPropertiesLayer _leveeModProperties;

	private Versioned<IResample<float>> _resampler = new Versioned<IResample<float>>();

	public override string TypeName => "Levee";

	public LeveeModPropertiesLayer PropertiesTable => LazyThreadsafe<LeveeModPropertiesLayer>.AllocateOnce(ref _leveeModProperties, _leveeModPropertiesLock, () => new LeveeModPropertiesLayer(Terrain));

	public override long VersionXY => base.VersionXY + PropertiesTable.TableVersion;

	public LeveeModTerrainInput(Terrain terrain, string name)
		: base(terrain)
	{
		Name = name;
	}

	internal LeveeModTerrainInput()
	{
	}

	public override void Add(LeveeMod localValue)
	{
		base.Add(localValue);
		localValue.PropertiesSource = PropertiesTable;
	}

	public override void Insert(int index, LeveeMod localValue)
	{
		base.Insert(index, localValue);
		localValue.PropertiesSource = PropertiesTable;
	}

	protected override List<LeveeMod> LoadFeaturesInternal()
	{
		List<LeveeMod> loadFeaturesInternal = base.LoadFeaturesInternal();
		for (int i = 0; i < loadFeaturesInternal.Count; i++)
		{
			loadFeaturesInternal[i].PropertiesSource = PropertiesTable;
		}
		return loadFeaturesInternal;
	}

	public override int GetDataHash()
	{
		return StableHash.Combine(base.GetDataHash(), PropertiesTable.GetDataHash());
	}

	public override bool NeedsSave()
	{
		if (!base.NeedsSave())
		{
			return PropertiesTable.NeedsSave();
		}
		return true;
	}

	public override void Save(H5Writer hw, string h5Path)
	{
		base.Save(hw, h5Path);
		PropertiesTable.SetOwnerGroup(h5Path);
		PropertiesTable.Save(hw);
	}

	public override bool TryInit(Terrain terrain, H5Reader hr, string h5Path)
	{
		if (!base.TryInit(terrain, hr, h5Path))
		{
			return false;
		}
		PropertiesTable.SetOwnerGroup(h5Path);
		return true;
	}

	public override IResample<float> GetResampler()
	{
		return _resampler.Get(VersionXY, () =>
		{
			List<IResample<float>> iResamples = new List<IResample<float>>();
			foreach (LeveeMod feature in Features)
			{
				if (feature.TryEvaluateAssociations(out var properties))
				{
					IResample<float> resample = LeveeResampler.Create(feature.Line, feature.Profile, properties.TopWidth, properties.LeftGrade, properties.EffectiveRightGrade, properties.LeftWidth, properties.EffectiveRightWidth, properties.Mode, properties.ModType);
					if (resample != null)
					{
						iResamples.Add(resample);
					}
				}
			}
			return new UnionResampler<float>(iResamples);
		});
	}

	protected override void LoadMetadataInternal(H5Reader hr)
	{
		base.LoadMetadataInternal(hr);
		if (Name == null)
		{
			Name = "Simple Cut/Fill Layer";
		}
	}
}
