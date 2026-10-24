namespace Geospatial.Rasters.Modifiers;

public interface ILeveeModPropertiesLookup
{
	bool TryFindByName(string name, out LeveeModProperties properties);
}
