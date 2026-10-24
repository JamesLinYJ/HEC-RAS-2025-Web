using System.Collections.Generic;

namespace Geospatial.Rasters.Classifications;

public interface IClassification
{
	int Count { get; }

	IReadOnlyCollection<int> IDs { get; }

	IReadOnlyCollection<string> Classes { get; }

	string GetName(int intValue);

	int GetID(string className);

	void Set(int id, string className);

	Dictionary<int, string> GetAllClassifications();
}
