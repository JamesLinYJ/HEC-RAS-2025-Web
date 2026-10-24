namespace Geospatial.Rendering.Editing;

public struct CompleteArcResultPackage
{
	public CompleteArcResult Result;

	public bool CreatedNode;

	public int CreatedNodeFID;

	public static implicit operator CompleteArcResultPackage(CompleteArcResult res)
	{
		return new CompleteArcResultPackage
		{
			Result = res
		};
	}
}
