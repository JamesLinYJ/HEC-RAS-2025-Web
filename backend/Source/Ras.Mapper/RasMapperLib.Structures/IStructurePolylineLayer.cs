namespace RasMapperLib.Structures;

public interface IStructurePolylineLayer : IStructureElementLayer
{
	int AddNewElement(int parentIndex, string elementName = "");

	float GetWidth(int fID);

	bool ShouldProcessGeospatialHwTwConnectivity(int fID);
}
