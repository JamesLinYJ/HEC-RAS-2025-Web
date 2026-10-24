using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility;
using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public class BandedTopologyRenderLayer : RenderLayer, IBandedRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IProfileStateProvider, IVectorRenderLayer, IBasicSelection
{
	private List<Topology> _topos;

	private List<TopologyRenderLayer> _renderLayers;

	public IProfileState ProfileState { get; }

	public int Count => ProfileState.ProfileCount;

	public VectorSymbology VectorSymbology { get; set; }

	public SelectedSet SelectedSet { get; } = new SelectedSet();

	public override Extent ValidExtent => _topos.First().Extent;

	public BandedTopologyRenderLayer(IList<Topology> topos)
	{
		_topos = topos.ToList();
		_renderLayers = topos.Select((Topology t) => (TopologyRenderLayer)null).ToList();
		VectorSymbology = VectorSymbology.DefaultSymbology();
		ProfileState = new ProfileState((from ii in Enumerable.Range(0, topos.Count)
			select ii.ToString()).ToList());
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		if (!ProfileState.IsRegularProfile)
		{
			return false;
		}
		int currentProfileIndex = ProfileState.CurrentProfileIndex;
		Topology topo = _topos[currentProfileIndex];
		TopologyRenderLayer inst = _renderLayers[currentProfileIndex];
		if (inst == null)
		{
			LazyThreadsafe<TopologyRenderLayer>.AllocateOnce(ref inst, _renderLayers, () => new TopologyRenderLayer(topo));
			_renderLayers[currentProfileIndex] = inst;
		}
		return inst?.Render(request, ref cache) ?? false;
	}

	public IList<object> GetHitTestVectors(HitTestArgs args, out int _hitTestedIdx)
	{
		_hitTestedIdx = -1;
		return null;
	}

	public IList<object> GetSelectedVectors()
	{
		return null;
	}

	public ISpatialVersion GetSpatialVersionContext()
	{
		return _topos.FirstOrDefault();
	}

	public bool Select(Extent selectExt, SelectMode mode)
	{
		return false;
	}

	public bool Select(Polyline lineIntersection, SelectMode mode)
	{
		return false;
	}

	public bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		return false;
	}

	public bool Select(HitTestArgs selectArgs, SelectMode mode)
	{
		return false;
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + (ProfileState?.Version ?? 0);
	}
}
