using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Vectors;
using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public class BandedTinRenderLayer : RenderLayer, IBandedRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IProfileStateProvider, IVectorRenderLayer, IBasicSelection, IRasterRenderLayer
{
	private Extent _validExt;

	private RasterSymbology _rasterSymb = RasterSymbology.DefaultRasterSymbology();

	private bool _renderEdges = true;

	private bool _renderPoints = true;

	private bool _labelTris;

	private bool _labelEdges;

	private bool _labelPoints;

	private readonly List<Tin> _tins;

	private readonly List<MultiTinRenderLayer> _renderLayers;

	private VectorSymbology _vecSymb;

	private SelectedSet _set = new SelectedSet();

	public override Extent ValidExtent => _validExt;

	public RasterSymbology RasterSymbology
	{
		get
		{
			return _rasterSymb;
		}
		set
		{
			_rasterSymb = value;
			IncrementRenderState();
		}
	}

	public TinSurfaceFillMode SurfaceFillMode { get; set; }

	[PlotOption("PaintZ", "Paint Z Surface")]
	public bool PaintZ
	{
		get
		{
			return SurfaceFillMode == TinSurfaceFillMode.RenderZ;
		}
		set
		{
			if (value)
			{
				SurfaceFillMode = TinSurfaceFillMode.RenderZ;
			}
			else
			{
				SurfaceFillMode = TinSurfaceFillMode.None;
			}
			IncrementRenderState();
		}
	}

	[PlotOption("PaintM", "Paint M Surface")]
	public bool PaintM
	{
		get
		{
			return SurfaceFillMode == TinSurfaceFillMode.RenderM;
		}
		set
		{
			if (value)
			{
				SurfaceFillMode = TinSurfaceFillMode.RenderM;
			}
			else
			{
				SurfaceFillMode = TinSurfaceFillMode.None;
			}
			IncrementRenderState();
		}
	}

	public Func<int, float, float, float> CustomTinRenderFunction { get; set; }

	[PlotOption("RenderEdges", "Render TIN Edges")]
	public bool RenderEdges
	{
		get
		{
			return _renderEdges;
		}
		set
		{
			if (_renderEdges == value)
			{
				return;
			}
			_renderEdges = value;
			if (_renderLayers != null)
			{
				for (int i = 0; i < _renderLayers.Count; i++)
				{
					_renderLayers[i].RenderEdges = _renderEdges;
				}
			}
			IncrementRenderState();
		}
	}

	[PlotOption("RenderPoints", "Render Points")]
	public bool RenderPoints
	{
		get
		{
			return _renderPoints;
		}
		set
		{
			if (_renderPoints == value)
			{
				return;
			}
			_renderPoints = value;
			if (_renderLayers != null)
			{
				for (int i = 0; i < _renderLayers.Count; i++)
				{
					_renderLayers[i].RenderPoints = _renderPoints;
				}
			}
			IncrementRenderState();
		}
	}

	[PlotOption("LabelTriangles", "Label Triangles")]
	public bool LabelTris
	{
		get
		{
			return _labelTris;
		}
		set
		{
			if (_labelTris == value)
			{
				return;
			}
			_labelTris = value;
			if (_renderLayers != null)
			{
				for (int i = 0; i < _renderLayers.Count; i++)
				{
					_renderLayers[i].LabelTris = value;
				}
			}
			IncrementRenderState();
		}
	}

	[PlotOption("LabelEdges", "Label Edges")]
	public bool LabelEdges
	{
		get
		{
			return _labelEdges;
		}
		set
		{
			if (_labelEdges == value)
			{
				return;
			}
			_labelEdges = value;
			if (_renderLayers != null)
			{
				for (int i = 0; i < _renderLayers.Count; i++)
				{
					_renderLayers[i].LabelEdges = value;
				}
			}
			IncrementRenderState();
		}
	}

	[PlotOption("LabelPoints", "Label Points")]
	public bool LabelPoints
	{
		get
		{
			return _labelPoints;
		}
		set
		{
			if (_labelPoints == value)
			{
				return;
			}
			_labelPoints = value;
			if (_renderLayers != null)
			{
				for (int i = 0; i < _renderLayers.Count; i++)
				{
					_renderLayers[i].LabelPoints = value;
				}
			}
			IncrementRenderState();
		}
	}

	public IProfileState ProfileState { get; private set; }

	public int ProfileCount => ProfileState.ProfileCount;

	public int Count => _tins.Count;

	public VectorSymbology VectorSymbology
	{
		get
		{
			return _vecSymb;
		}
		set
		{
			if (_vecSymb == value)
			{
				return;
			}
			_vecSymb = value;
			if (_renderLayers != null)
			{
				for (int i = 0; i < _renderLayers.Count; i++)
				{
					_renderLayers[i].VectorSymbology = _vecSymb;
				}
			}
			IncrementRenderState();
		}
	}

	public SelectedSet SelectedSet => _set;

	public BandedTinRenderLayer()
	{
		_tins = new List<Tin>();
		_renderLayers = new List<MultiTinRenderLayer>();
		VectorSymbology = VectorSymbology.DefaultSymbology();
		Rebuild();
	}

	public void AddTin(Tin tin)
	{
		_tins.Add(tin);
		MultiTinRenderLayer multiTinRenderLayer = new MultiTinRenderLayer(new Tin[1] { tin });
		multiTinRenderLayer.VectorSymbology = VectorSymbology;
		_renderLayers.Add(multiTinRenderLayer);
		Rebuild();
	}

	private void Rebuild()
	{
		int currentProfileIndex = -1;
		if (ProfileState != null)
		{
			currentProfileIndex = ProfileState.CurrentProfileIndex;
		}
		ProfileState profileState = new ProfileState(() =>
		{
			int count = _tins.Count;
			List<string> stringList = new List<string>(count);
			for (int i = 0; i < count; i++)
			{
				stringList.Add("TIN " + i);
			}
			return stringList;
		});
		profileState.CurrentProfileIndex = currentProfileIndex;
		ProfileState = profileState;
		_validExt = Extent.Union(_tins.Select((Tin t) => t.Extent));
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		if (ProfileState.IsNull)
		{
			return false;
		}
		object cache2 = null;
		return _renderLayers[ProfileState.CurrentProfileIndex].Render(request, ref cache2);
	}

	public bool Select(Extent selectExt, SelectMode mode)
	{
		return _renderLayers[ProfileState.CurrentProfileIndex].Select(selectExt, mode);
	}

	public bool Select(Polyline intersectionLine, SelectMode mode)
	{
		return _renderLayers[ProfileState.CurrentProfileIndex].Select(intersectionLine, mode);
	}

	public bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		return _renderLayers[ProfileState.CurrentProfileIndex].Select(lasso, hitMode, mode);
	}

	public bool Select(HitTestArgs selectArgs, SelectMode mode)
	{
		return _renderLayers[ProfileState.CurrentProfileIndex].Select(selectArgs, mode);
	}

	public IList<object> GetSelectedVectors()
	{
		return _renderLayers[ProfileState.CurrentProfileIndex].GetSelectedVectors();
	}

	public IList<object> GetHitTestVectors(HitTestArgs args, out int _hitTestedIdx)
	{
		return _renderLayers[ProfileState.CurrentProfileIndex].GetHitTestVectors(args, out _hitTestedIdx);
	}

	public ISpatialVersion GetSpatialVersionContext()
	{
		return _renderLayers[ProfileState.CurrentProfileIndex].GetSpatialVersionContext();
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + (ProfileState?.Version ?? 0);
	}
}
