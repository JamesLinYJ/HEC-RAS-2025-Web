using Ras.Layers.Meteorology;

namespace Ras.Layers.BoundaryConditions;

public interface IInitialConditionLayer
{
	string LayerTypeName { get; }

	bool IsEnabled { get; set; }

	SpatialDataType SpatialDataType { get; set; }

	DataLocation GridDataLocation { get; set; }

	string ExternalGriddedConnection { get; set; }

	string ExternalGriddedPath { get; set; }

	string SourceTimestep { get; set; }
}
