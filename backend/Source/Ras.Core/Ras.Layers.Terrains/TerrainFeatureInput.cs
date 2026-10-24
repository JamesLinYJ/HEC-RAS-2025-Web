using System.Collections.Generic;
using Geospatial;
using Geospatial.Vectors;
using H5Assist;
using Utility.ORM;

namespace Ras.Layers.Terrains;

public abstract class TerrainFeatureInput<T> : FeatureLayerBase<T>, IFeatureLayer where T : IFeatureRow, new()
{
	public override bool IsReadOnly => false;

	public Terrain Terrain { get; protected set; }

	[Metadata("Name")]
	public string Name { get; set; }

	public Extent ValidExtent => Extent;

	public float? NoDataValue => -9999f;

	public TerrainFeatureInput(Terrain tr)
	{
		Terrain = tr;
	}

	internal TerrainFeatureInput()
	{
	}

	protected override List<T> LoadFeaturesInternal()
	{
		return Terrain.CheckInitializeData(ref _loadedFeatures, _readAction);
	}

	protected override int PeekCountInternal()
	{
		if (Terrain.IsInMemory)
		{
			return Count;
		}
		return Terrain.UseReader((H5Reader hr) =>
		{
			string pathCombine = H5Reader.PathCombine(HDF5Group, _attrDatasetName);
			return (int)(hr.ExistsAsDataset(pathCombine) ? hr.GetDatasetDimensions(pathCombine)[0] : 0);
		});
	}
}
