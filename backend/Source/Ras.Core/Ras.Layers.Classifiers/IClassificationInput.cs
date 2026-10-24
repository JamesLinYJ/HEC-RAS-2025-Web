using Geospatial;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using H5Assist;
using Utility.Hashing;

namespace Ras.Layers.Classifiers;

public interface IClassificationInput : IDataHash, ISpatialVersion
{
	string TypeName { get; }

	string Name { get; set; }

	ClassificationLayer ClassificationLayer { get; }

	Classification GetClassifier();

	IResample<int> GetResampler();

	bool IsValid(out string err);

	bool TryInit(ClassificationLayer layer, H5Reader hr, string h5Path);

	void Save(H5Writer hw, string h5Path);

	IClassificationInput CloneWith(ClassificationLayer newClass);
}
