namespace RasMapperLib.Structures;

public interface IOpeningLayer : IStructurePolylineLayer
{
	void SetGroup(int fid, int groupFid);

	int TryGetGroupIndex(int fid);

	IGroupLayer GetAssociatedGroupLayer();

	void UpdateAssociations(int fromGroupFID, int toGroupFID);
}
