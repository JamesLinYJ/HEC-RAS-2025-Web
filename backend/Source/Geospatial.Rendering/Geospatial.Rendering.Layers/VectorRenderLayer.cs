using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Helpers;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.DataStructures;
using Utility.Interfaces;
using Utility.Logging;

namespace Geospatial.Rendering.Layers;

public abstract class VectorRenderLayer : RenderLayer, IVectorRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IBasicSelection
{
	private static readonly ILogger _hitTestLogger = LogHelper.GetLogger("VectorRenderLayer");

	public const int DefaultHitTestPixelCount = 8;

	public const int ExtendedHitTestPixelCount = 16;

	public const int EndpointHitTestPixelCount = 64;

	public const int SplitHitTestPixelCount = 100;

	private readonly List<Func<int, MapHover>> _vectorHoverActions = new List<Func<int, MapHover>>();

	private bool _labelFIDs;

	public bool CanFeaturesOverlap { get; } = true;

	[PlotOption("LabelFIDs", "Label FIDs")]
	public bool LabelFIDs
	{
		get
		{
			return _labelFIDs;
		}
		set
		{
			if (_labelFIDs != value)
			{
				_labelFIDs = value;
				IncrementRenderState();
			}
		}
	}

	public abstract int Count { get; }

	public VectorSymbology VectorSymbology { get; set; } = VectorSymbology.DefaultSymbology();

	public VectorSymbology SelectedSymbology => VectorSymbology.GetSelectedSymbology();

	public SelectedSet SelectedSet { get; } = new SelectedSet();

	public abstract ISpatialVersion GetSpatialVersionContext();

	public virtual IList<object> GetSelectedVectors()
	{
		return null;
	}

	public virtual IList<object> GetHitTestVectors(HitTestArgs args, out int hitTestedIdx)
	{
		hitTestedIdx = -1;
		return default(CompactList<object>);
	}

	public void AddDefaultHoverHighlight()
	{
		AddVectorHoverAction((int fid) => new MapHover
		{
			Label = "FID: " + fid
		});
	}

	public void AddHoverHighlight(Func<int, string> label)
	{
		AddVectorHoverAction((int fid) => new MapHover
		{
			Label = label(fid)
		});
	}

	public override List<MapHover> MapHover(HitTestArgs args, ref object hitTestCache)
	{
		IEnumerable<int> source = HitTest(args, HitTestSortOrder.Distance);
		List<MapHover> mapHovers = null;
		if (source.Any())
		{
			mapHovers = new List<MapHover>();
			int sourceTerm = source.First();
			if (_vectorHoverActions.Count == 0)
			{
				mapHovers.Add(new MapHover
				{
					Label = "FID: " + sourceTerm
				});
			}
			else
			{
				foreach (Func<int, MapHover> vectorHoverAction in _vectorHoverActions)
				{
					mapHovers.Add(vectorHoverAction(sourceTerm));
				}
			}
		}
		for (int i = 0; i < _hoverActions.Count; i++)
		{
			MapHover mapHover = _hoverActions[i](null, args);
			if (mapHover != null)
			{
				if (mapHovers == null)
				{
					mapHovers = new List<MapHover>();
				}
				mapHovers.Add(mapHover);
			}
		}
		return mapHovers;
	}

	public void AddVectorHoverAction(Func<int, MapHover> onHover)
	{
		_vectorHoverActions.Add(onHover);
	}

	public abstract bool HitTest(int candidateIdx, Geospatial.Vectors.Point pt, double hitDistance);

	public abstract bool HitTest(int candidateIdx, Extent ext);

	public abstract bool HitTest(int candidateIdx, Polyline ext);

	public abstract bool HitTest(int candidateIdx, Polygon lasso, MarqueeHitMode hitMode);

	public abstract bool Highlight(int idx, PlotRequest req);

	public abstract double DistanceTo(int idx, Geospatial.Vectors.Point pt);

	public double DistanceTo(int idx, HitTestArgs args)
	{
		return DistanceTo(idx, args.QueryPoint);
	}

	public bool HitTest(int candidateIdx, HitTestArgs args, int pixelCount = 8)
	{
		return HitTest(candidateIdx, args.QueryPoint, pixelCount * args.Window.CellCount);
	}

	public virtual IEnumerable<int> HitTest(HitTestArgs args, HitTestSortOrder sortOrder = HitTestSortOrder.None, int pixelCount = 8)
	{
		SpatialIndex<int> spatialIndex = EnsureGetSpatialIndex();
		if (spatialIndex == null)
		{
			_hitTestLogger.LogWarning("No spatial index available to hit-test {layer} ({name}).", GetType().Name, DisplayName);
			return Array.Empty<int>();
		}
		return HitTestHelpers.HitTest(args.QueryPoint, spatialIndex, HitTest, (double)pixelCount * args.Window.CellSize, DistanceTo, sortOrder);
	}

	public virtual bool Select(Extent selectExt, SelectMode mode)
	{
		return SelectInternal(selectExt, this, selectExt, (int candidate, VectorRenderLayer vrl, Extent ext) => vrl.HitTest(candidate, ext), mode);
	}

	public bool Select(Polyline intersectLine, SelectMode mode)
	{
		return SelectInternal(intersectLine.Extent, this, intersectLine, (int candidate, VectorRenderLayer vrl, Polyline ext) => vrl.HitTest(candidate, ext), mode);
	}

	public virtual bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		return SelectInternal(lasso.Extent, this, (lasso, hitMode), (int candidate, VectorRenderLayer vrl, (Polygon lasso, MarqueeHitMode hitMode) payload) => vrl.HitTest(candidate, payload.lasso, payload.hitMode), mode);
	}

	private bool SelectInternal<T>(Extent selectExt, VectorRenderLayer model, T payload, Func<int, VectorRenderLayer, T, bool> hitTest, SelectMode mode)
	{
		return SelectHelper(selectExt, SelectedSet, Count, mode, model, payload, hitTest, (VectorRenderLayer vectorRenderLayer) => vectorRenderLayer.EnsureGetSpatialIndex());
	}

	public static bool SelectHelper<T, K>(Extent selectExt, SelectedSet set, int totalVectorCount, SelectMode mode, K model, T hitTestQuerier, Func<int, K, T, bool> hitTest, Func<K, SpatialIndex<int>> ensureSpatialIndex)
	{
		if (selectExt.Boundedness == Extent.State.Invalid)
		{
			return false;
		}
		if (selectExt.Boundedness == Extent.State.Infinite)
		{
			return InfiniteExtentSelectHelper(set, mode, totalVectorCount);
		}
		SpatialIndex<int> spatialIndex = ensureSpatialIndex?.Invoke(model);
		if (spatialIndex == null)
		{
			return false;
		}
		int[] elements = spatialIndex.GetElements(selectExt).ToArray();
		switch (mode)
		{
		case SelectMode.Append:
		{
			if (set.Count == totalVectorCount)
			{
				return false;
			}
			bool result2 = false;
			int[] elementsArray = elements;
			foreach (int element in elementsArray)
			{
				if (!set.Contains(element) && hitTest(element, model, hitTestQuerier))
				{
					result2 = true;
					set.Add(element);
				}
			}
			return result2;
		}
		case SelectMode.Replace:
		{
			int count = set.Count;
			set.Clear();
			int[] elements2 = elements;
			foreach (int element2 in elements2)
			{
				if (hitTest(element2, model, hitTestQuerier))
				{
					set.Add(element2);
				}
			}
			if (count == 0 && set.Count == 0)
			{
				return false;
			}
			return true;
		}
		case SelectMode.EnsureIncluded:
			throw new NotSupportedException();
		case SelectMode.Toggle:
		{
			List<int> intList = new List<int>();
			bool condition = false;
			int[] elements3 = elements;
			foreach (int element3 in elements3)
			{
				if (hitTest(element3, model, hitTestQuerier))
				{
					intList.Add(element3);
					if (!set.Contains(element3))
					{
						condition = true;
					}
				}
			}
			if (intList.Count == 0)
			{
				return false;
			}
			foreach (int ordinal in intList)
			{
				if (condition)
				{
					set.Add(ordinal);
				}
				else
				{
					set.Remove(ordinal);
				}
			}
			return true;
		}
		case SelectMode.Remove:
		{
			if (set.Count == 0)
			{
				return false;
			}
			bool result = false;
			int[] elements4 = elements;
			foreach (int element4 in elements4)
			{
				if (set.Contains(element4) && hitTest(element4, model, hitTestQuerier))
				{
					result = true;
					set.Remove(element4);
				}
			}
			return result;
		}
		default:
			return false;
		}
	}

	public static bool InfiniteExtentSelectHelper(SelectedSet set, SelectMode mode, int count)
	{
		switch (mode)
		{
		case SelectMode.Append:
			if (set.Count == count)
			{
				return false;
			}
			set.AddRange(0, count);
			return true;
		case SelectMode.Replace:
			if (set.Count == count)
			{
				return false;
			}
			set.AddRange(0, count);
			return true;
		case SelectMode.Toggle:
			if (set.Count == count)
			{
				set.Clear();
			}
			else
			{
				set.AddRange(0, count);
			}
			return true;
		case SelectMode.Remove:
			if (set.Count == 0)
			{
				return false;
			}
			set.Clear();
			return true;
		default:
			throw new NotImplementedException();
		}
	}

	public virtual bool Select(HitTestArgs selectArgs, SelectMode mode)
	{
		return SelectHelperPoint(HitTest(selectArgs, HitTestSortOrder.Distance), selectArgs, mode, SelectedSet);
	}

	public static bool SelectHelperPoint(IEnumerable<int> elements, HitTestArgs selectArgs, SelectMode mode, SelectedSet selectedSet)
	{
		if (!elements.Any())
		{
			if (mode == SelectMode.Replace)
			{
				int count = selectedSet.Count;
				selectedSet.Clear();
				if (count > 0)
				{
					return true;
				}
				return false;
			}
			return false;
		}
		int idx = elements.First();
		switch (mode)
		{
		case SelectMode.Append:
			if (selectedSet.Contains(idx))
			{
				return false;
			}
			selectedSet.Add(idx);
			return true;
		case SelectMode.Replace:
			if (selectedSet.Count == 1 && selectedSet.Contains(idx))
			{
				return false;
			}
			selectedSet.Clear();
			selectedSet.Add(idx);
			return true;
		case SelectMode.Toggle:
			if (selectedSet.Contains(idx))
			{
				selectedSet.Remove(idx);
			}
			else
			{
				selectedSet.Add(idx);
			}
			return true;
		case SelectMode.Remove:
			if (selectedSet.Contains(idx))
			{
				selectedSet.Remove(idx);
				return true;
			}
			return false;
		case SelectMode.EnsureIncluded:
			foreach (int element in elements)
			{
				if (selectedSet.Contains(element))
				{
					return false;
				}
			}
			selectedSet.Clear();
			foreach (int element2 in elements)
			{
				selectedSet.Add(element2);
			}
			return true;
		default:
			return false;
		}
	}

	public void DebugRenderSpatialIndex()
	{
		RasterDefinitionRenderLayer rasterDefinitionRenderLayer = new RasterDefinitionRenderLayer(EnsureGetSpatialIndex().Raster);
		rasterDefinitionRenderLayer.VectorSymbology.PenColor = Color.DeepPink;
		AddPreRenderStage(rasterDefinitionRenderLayer);
	}

	public abstract SpatialIndex<int> EnsureGetSpatialIndex();

	public virtual void NotifyDataModified()
	{
		IncrementRenderState();
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + SelectedSet.VersionXY + VectorSymbology.VersionXY;
	}
}
