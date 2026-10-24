using Geospatial.Helpers;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing;

public static class CompleteArcHelpers
{
	public static CompleteArcResultPackage TryCompleteArc(NodeArcEditContext context, int arcFid, EditContext ctx, HitTestArgs loc, CompleteArcMode mode)
	{
		BaseTopology network = context.Network;
		if (loc == null)
		{
			CompleteArcOperation completeArcOperation = new CompleteArcOperation(context, arcFid);
			ctx.ExecuteEditOperation(completeArcOperation);
			return new CompleteArcResultPackage
			{
				Result = CompleteArcResult.Success,
				CreatedNode = completeArcOperation.CreatedNode,
				CreatedNodeFID = completeArcOperation.CreatedNodeFID
			};
		}
		bool hitTestNode = NetworkRenderLayer.HitTestNode(network, loc, out var elementIndex);
		bool hitTestArc = NetworkRenderLayer.HitTestArc(network, loc, HitTestSortOrder.Distance, out var _, out var elementIndex2, out var _);
		if (mode == CompleteArcMode.SplitArcOrExistingNode && !hitTestNode && !hitTestArc)
		{
			return CompleteArcResult.Failure;
		}
		if (hitTestNode)
		{
			if (!NodeArcEditGeometry.CanFinishAddingAtNode(network, arcFid, elementIndex))
			{
				return CompleteArcResult.Rejection;
			}
		}
		else if (hitTestArc && !NodeArcEditGeometry.CanSplitArcAtPoint(network, arcFid, loc.QueryPoint, elementIndex2))
		{
			return CompleteArcResult.Rejection;
		}
		CompleteArcOperation completeArcOperation2 = new CompleteArcOperation(context, arcFid, loc);
		ctx.ExecuteEditOperation(completeArcOperation2);
		return new CompleteArcResultPackage
		{
			Result = CompleteArcResult.Success,
			CreatedNode = completeArcOperation2.CreatedNode,
			CreatedNodeFID = completeArcOperation2.CreatedNodeFID
		};
	}
}
