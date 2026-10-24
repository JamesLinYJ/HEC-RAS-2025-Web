using System.Data;

namespace RasMapperLib;

public interface IFeatureLayer
{
	string LayerName { get; }

	DataTable FeatureTable();
}
