using Geospatial;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Ras.Layers.BoundaryConditions;

public abstract class BoundaryConditionFeatureLayerBase<T> : FeatureLayerBase<T>, IBoundaryConditionFeatureLayer, IBoundaryConditionLayer, IFeatureLayer where T : IFeatureRow, new()
{
	protected static readonly ILogger _logger = LogHelper.GetLogger("BoundaryConditionFeatureLayerBase");

	public BoundaryCondition BoundaryCondition { get; set; }

	public override bool IsReadOnly => BoundaryCondition.IsResult;

	public BoundaryConditionFeatureLayerBase(BoundaryCondition bc)
	{
		BoundaryCondition = bc;
	}

	protected override int PeekCountInternal()
	{
		return BoundaryCondition.UseReader((H5Reader hr) =>
		{
			string hdF5GroupAttrDatasetNameTerm = HDF5Group + "/" + _attrDatasetName;
			return (int)(hr.ExistsAsDataset(hdF5GroupAttrDatasetNameTerm) ? hr.GetDatasetDimensions(hdF5GroupAttrDatasetNameTerm)[0] : 0);
		});
	}
}
