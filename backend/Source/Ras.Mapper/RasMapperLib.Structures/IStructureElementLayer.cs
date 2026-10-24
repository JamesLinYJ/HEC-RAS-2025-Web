namespace RasMapperLib.Structures;

public interface IStructureElementLayer
{
	int TryGetStructureIndex(int fid);

	IStructureLayer GetAssociatedStructureLayer();
}
