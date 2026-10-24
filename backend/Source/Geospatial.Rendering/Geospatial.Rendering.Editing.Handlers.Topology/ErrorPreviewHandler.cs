using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Rendering.Editing.Handlers.Topology;

public sealed class ErrorPreviewHandler : IEditHandler
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ErrorPreviewHandler");

	private readonly TopologyEditContext _context;

	private HashSet<int> _arcErrorIndicesScratchSpace = new HashSet<int>();

	private HashSet<int> _nodeErrorIndicesScratchSpace = new HashSet<int>();

	private HashSet<int> _regionErrorIndicesScratchSpace = new HashSet<int>();

	private List<Polyline> _arcErrorsScratchSpace = new List<Polyline>();

	private List<Point> _nodeErrorsScratchSpace = new List<Point>();

	private List<ComplexPolygon> _regionErrorsScratchSpace = new List<ComplexPolygon>();

	private const string _previewArcChannel = "Error_PreviewArc";

	private const string _previewArcDottedChannel = "Error_PreviewArcDotted";

	private const string _previewNodeChannel = "Error_PreviewNode";

	private const string _previewRegionChannel = "Error_PreviewRegion";

	public ErrorPreviewHandler(TopologyEditContext context)
	{
		_context = context;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		return HandlerResult.NotHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		TopologyCompletionContext completionContext = _context.CompletionContext;
		if (completionContext == null)
		{
			return false;
		}
		ctx.Preview.TryGetVector("Error_PreviewArc", out var id);
		ctx.Preview.TryGetVector("Error_PreviewArcDotted", out var id2);
		ctx.Preview.TryGetVector("Error_PreviewNode", out var id3);
		ctx.Preview.TryGetVector("Error_PreviewRegion", out var id4);
		if (completionContext.LatestBuildResultEditStackVersion != ctx.DebugGetEditStack().Version)
		{
			if (!id.IsNull())
			{
				ctx.Preview.RegisterVector("Error_PreviewArc", id);
			}
			if (!id2.IsNull())
			{
				ctx.Preview.RegisterVector("Error_PreviewArcDotted", id2);
			}
			if (!id3.IsNull())
			{
				ctx.Preview.RegisterVector("Error_PreviewNode", id3);
			}
			if (!id4.IsNull())
			{
				ctx.Preview.RegisterVector("Error_PreviewRegion", id4);
			}
			return false;
		}
		OperationResult latestBuildResult = completionContext.LatestBuildResult;
		if (latestBuildResult == null || latestBuildResult.Result || latestBuildResult.Messages == null)
		{
			return false;
		}
		_arcErrorIndicesScratchSpace.Clear();
		_nodeErrorIndicesScratchSpace.Clear();
		_regionErrorIndicesScratchSpace.Clear();
		_arcErrorsScratchSpace.Clear();
		_nodeErrorsScratchSpace.Clear();
		_regionErrorsScratchSpace.Clear();
		foreach (Message message in latestBuildResult.Messages)
		{
			for (int i = 0; i < message.LogData.Count; i++)
			{
				KeyValuePair<string, object> logData = message.LogData[i];
				if (!(logData.Value is int ordinal))
				{
					continue;
				}
				if (ordinal < 0)
				{
					_logger.LogWarning("Topology build message with neg. element index for {logVal}, elementIndex: {idx}", logData.Value, ordinal);
					continue;
				}
				try
				{
					if (logData.Key.Equals("ArcIndex"))
					{
						if (!_arcErrorIndicesScratchSpace.Contains(ordinal))
						{
							_arcErrorIndicesScratchSpace.Add(ordinal);
							_arcErrorsScratchSpace.Add(_context.Topology.GetArcPolyline(ordinal));
						}
					}
					else if (logData.Key.Equals("NodeIndex"))
					{
						if (!_nodeErrorIndicesScratchSpace.Contains(ordinal))
						{
							_nodeErrorIndicesScratchSpace.Add(ordinal);
							_nodeErrorsScratchSpace.Add(_context.Topology.Nodes[ordinal].Point);
						}
					}
					else if (logData.Key.Equals("RegionIndex") && !_regionErrorIndicesScratchSpace.Contains(ordinal))
					{
						_regionErrorIndicesScratchSpace.Add(ordinal);
						_regionErrorsScratchSpace.Add(_context.Topology.GetRegionComplexPolygon(ordinal));
					}
				}
				catch (Exception exception)
				{
					_logger.LogError(exception, "Failed to build line/polygon to preview error for region {i}, element index {eleIdx}", i, ordinal);
				}
			}
		}
		ctx.VectorManager.AddOrUpdateVector(ref id, _arcErrorsScratchSpace, DefaultEditSymbology.TopologyErrorSymbology);
		ctx.VectorManager.AddOrUpdateVector(ref id2, _arcErrorsScratchSpace, DefaultEditSymbology.TopologyErrorDottedSymbology);
		ctx.VectorManager.AddOrUpdateVector(ref id3, _nodeErrorsScratchSpace, DefaultEditSymbology.TopologyErrorNodeSymbology);
		ctx.VectorManager.AddOrUpdateVector(ref id4, _regionErrorsScratchSpace, DefaultEditSymbology.TopologyErrorDottedSymbology);
		ctx.VectorManager.SetVectorZIndex(id, 5);
		ctx.VectorManager.SetVectorZIndex(id2, 6);
		ctx.VectorManager.SetVectorZIndex(id3, 5);
		ctx.VectorManager.SetVectorZIndex(id4, 1);
		ctx.Preview.RegisterVector("Error_PreviewArc", id);
		ctx.Preview.RegisterVector("Error_PreviewArcDotted", id2);
		ctx.Preview.RegisterVector("Error_PreviewNode", id3);
		ctx.Preview.RegisterVector("Error_PreviewRegion", id4);
		return false;
	}
}
