namespace RasMapperLib;

public interface IHasRRRSLayer
{
	int RiverStationDecimalPlaces { get; set; }

	SharedData.EnumRSUnits UnitsRiverStation { get; set; }
}
