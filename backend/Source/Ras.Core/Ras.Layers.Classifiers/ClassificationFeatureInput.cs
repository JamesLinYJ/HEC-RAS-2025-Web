using System.Collections.Generic;
using Geospatial;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Layers.Classifiers;

public abstract class ClassificationFeatureInput<T> : FeatureLayerBase<T>, IFeatureLayer where T : IFeatureRow, new()
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ClassificationFeatureInput");

	public override bool IsReadOnly => false;

	public ClassificationLayer ClassificationLayer { get; protected set; }

	[Metadata("Name")]
	public string Name { get; set; }

	public Extent ValidExtent => Extent;

	public int? NoDataValue => 0;

	public ClassificationFeatureInput(ClassificationLayer tr)
	{
		ClassificationLayer = tr;
	}

	internal ClassificationFeatureInput()
	{
	}

	protected override List<T> LoadFeaturesInternal()
	{
		return ClassificationLayer.CheckInitializeData(ref _loadedFeatures, _readAction);
	}

	protected override int PeekCountInternal()
	{
		if (ClassificationLayer.IsInMemory)
		{
			return Count;
		}
		return ClassificationLayer.UseReader((H5Reader hr) =>
		{
			if (string.IsNullOrEmpty(HDF5Group))
			{
				_logger.LogWarning("HDFGroup name not initialized when calling ClassificationFeatureInput.PeekCount.");
				return 0;
			}
			string pathCombine = H5Reader.PathCombine(HDF5Group, _attrDatasetName);
			return (int)(hr.ExistsAsDataset(pathCombine) ? hr.GetDatasetDimensions(pathCombine)[0] : 0);
		});
	}
}
