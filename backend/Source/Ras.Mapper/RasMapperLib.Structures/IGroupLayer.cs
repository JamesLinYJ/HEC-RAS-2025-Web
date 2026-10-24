namespace RasMapperLib.Structures;

public interface IGroupLayer : IStructureElementLayer
{
	float GetWidth(int fID);

	void SetWidth(int fID, float width);

	int AddNewGroup(int parentStructureIndex, string groupName = "");

	void SetAssociatedStructure(int fID, int parentStructureIndex);

	void UpdateAssociations(int fromStructureFID, int toStructureFID);

	void SetActualStructureIndex(int fid, int idx);
}
