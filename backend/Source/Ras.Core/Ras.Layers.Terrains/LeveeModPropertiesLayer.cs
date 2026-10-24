using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.Rasters.Modifiers;
using H5Assist;
using Utility.Logging;

namespace Ras.Layers.Terrains;

[DebuggerDisplay("{Count} Levee Properties")]
public class LeveeModPropertiesLayer : TerrainFeatureInput<LeveeModProperties>, ILeveeModPropertiesLookup
{
	public const string GroupName = "Levee Types";

	private string _hdfGroup;

	protected override string HDF5Group => _hdfGroup;

	public long TableVersion
	{
		get
		{
			if (!IsDataLoaded)
			{
				return 0L;
			}
			return VersionXY;
		}
	}

	public LeveeModPropertiesLayer(Terrain tr)
		: base(tr)
	{
		Name = "Levee Types";
		UpdateInternalMetadataHash();
	}

	public void SetOwnerGroup(string ownerGroup)
	{
		_hdfGroup = H5Reader.PathCombine(ownerGroup, "Levee Types");
	}

	protected override List<LeveeModProperties> LoadFeaturesInternal()
	{
		if (string.IsNullOrEmpty(_hdfGroup))
		{
			return _loadedFeatures ?? (_loadedFeatures = new List<LeveeModProperties>());
		}
		List<LeveeModProperties> loadFeaturesInternal = base.LoadFeaturesInternal();
		MigrateLegacyInvertColumn(loadFeaturesInternal);
		return loadFeaturesInternal;
	}

	private void MigrateLegacyInvertColumn(List<LeveeModProperties> properties)
	{
	}

	protected override int PeekCountInternal()
	{
		if (string.IsNullOrEmpty(_hdfGroup))
		{
			return 0;
		}
		return base.PeekCountInternal();
	}

	public bool TryFindByName(string name, out LeveeModProperties properties)
	{
		properties = null;
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		for (int i = 0; i < Count; i++)
		{
			LeveeModProperties leveeModProperties = Features[i];
			if (leveeModProperties.Name == name)
			{
				properties = leveeModProperties;
				return true;
			}
		}
		return false;
	}

	public OperationResult IsValid()
	{
		OperationResult operationResult = true;
		for (int i = 0; i < Count; i++)
		{
			OperationResult operationResult2 = Features[i].IsValid();
			if (!operationResult2)
			{
				operationResult.AddFailure(operationResult2);
			}
		}
		return operationResult;
	}
}
