using H5Assist.VB;

namespace RasMapperLib;

public interface IGeometryLayer
{
	RASGeometry Geometry { get; }

	string HDFGroupName { get; }

	void HDFLoadFeatureTable(H5Reader hr);

	void HDFSaveFeatureTable(H5Writer hw);
}
