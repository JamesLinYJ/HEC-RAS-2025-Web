using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Rendering.Systems;
using Geospatial.Vectors;
using Utility.Extensions;
using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public class RenderLayerCollection : RenderLayer, IRenderLayerCollection, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IEnumerable<IRenderLayer>, IEnumerable, IList<IRenderLayer>, ICollection<IRenderLayer>
{
	public static readonly RenderLayerCollection Empty = new RenderLayerCollection();

	public List<IRenderLayer> Layers { get; }

	public RenderOrder RenderOrder { get; set; } = RenderOrder.HighestPriorityLast;

	public override Extent ValidExtent => Extent.Union(Layers.Select((IRenderLayer l) => l?.ValidExtent ?? Extent.NullExtent));

	public int Count => Layers.Count;

	public bool IsReadOnly => false;

	public IRenderLayer this[int index]
	{
		get
		{
			return ((IList<IRenderLayer>)Layers)[index];
		}
		set
		{
			((IList<IRenderLayer>)Layers)[index] = value;
		}
	}

	public RenderLayerCollection()
	{
		Layers = new List<IRenderLayer>();
	}

	public RenderLayerCollection(IEnumerable<IRenderLayer> layers)
	{
		Layers = layers?.ToList() ?? new List<IRenderLayer>();
	}

	public RenderLayerCollection(params IRenderLayer[] layers)
	{
		Layers = layers.ToList();
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		if (!ValidExtent.Overlaps(plot.RasterQuery.Extent))
		{
			return false;
		}
		List<object> cacheList = cache as List<object>;
		if (cacheList == null)
		{
			cacheList = new List<object>(Count);
			for (int i = 0; i < Count; i++)
			{
				cacheList.Add(null);
			}
			cache = cacheList;
		}
		bool condition = false;
		for (int j = 0; j < Count; j++)
		{
			if (plot.Reporter.CancelRequested)
			{
				return false;
			}
			IRenderLayer renderLayer = this[j];
			object cache2 = cacheList[j];
			condition |= renderLayer.Render(plot, ref cache2);
			cacheList[j] = cache2;
		}
		return condition;
	}

	public override List<MapHover> MapHover(HitTestArgs args, ref object hitTestCache)
	{
		List<MapHover> mapHovers = new List<MapHover>();
		using IEnumerator<IRenderLayer> iRenderLayers = GetEnumerator();
		while (iRenderLayers.MoveNext())
		{
			List<MapHover> mapHover = iRenderLayers.Current.MapHover(args, ref hitTestCache);
			if (mapHover != null)
			{
				mapHovers.AddRange(mapHover);
			}
		}
		return mapHovers;
	}

	public override Dictionary<string, List<ILabelDefinition>> GetEnabledScreenAnnotations(RasterDefinition raster)
	{
		Dictionary<string, List<ILabelDefinition>> enabledScreenAnnotations2 = new Dictionary<string, List<ILabelDefinition>>();
		for (int i = 0; i < Count; i++)
		{
			Dictionary<string, List<ILabelDefinition>> enabledScreenAnnotations = this[i].GetEnabledScreenAnnotations(raster);
			if (enabledScreenAnnotations == null)
			{
				continue;
			}
			string textValue = i + "_";
			foreach (KeyValuePair<string, List<ILabelDefinition>> enabledScreenAnnotation in enabledScreenAnnotations)
			{
				if (!enabledScreenAnnotation.Value.IsNullOrEmpty())
				{
					enabledScreenAnnotations2[textValue + enabledScreenAnnotation.Key] = enabledScreenAnnotation.Value;
				}
			}
		}
		return enabledScreenAnnotations2;
	}

	public void Insert(int idx, IRenderLayer lyr)
	{
		Layers.Insert(idx, lyr);
	}

	public void Add(IRenderLayer lyr)
	{
		Layers.Add(lyr);
	}

	public void AddRange(IList<IRenderLayer> lyrs)
	{
		Layers.AddRange(lyrs);
	}

	public void AddRange(IReadOnlyList<IRenderLayer> lyrs)
	{
		Layers.AddRange(lyrs);
	}

	public void AddIfNotNull(IRenderLayer lyr)
	{
		if (lyr != null)
		{
			Layers.Add(lyr);
		}
	}

	public int IndexOf(IRenderLayer iRenderLayer)
	{
		return ((IList<IRenderLayer>)Layers).IndexOf(iRenderLayer);
	}

	public void RemoveAt(int index)
	{
		((IList<IRenderLayer>)Layers).RemoveAt(index);
	}

	public void Clear()
	{
		((ICollection<IRenderLayer>)Layers).Clear();
	}

	public bool Contains(IRenderLayer iRenderLayer)
	{
		return ((ICollection<IRenderLayer>)Layers).Contains(iRenderLayer);
	}

	public void CopyTo(IRenderLayer[] layers, int arrayIndex)
	{
		((ICollection<IRenderLayer>)Layers).CopyTo(layers, arrayIndex);
	}

	public bool Remove(IRenderLayer iRenderLayer)
	{
		return ((ICollection<IRenderLayer>)Layers).Remove(iRenderLayer);
	}

	public IEnumerator<IRenderLayer> GetEnumerator()
	{
		return GetEnumeratorInternal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumeratorInternal();
	}

	private IEnumerator<IRenderLayer> GetEnumeratorInternal()
	{
		if (RenderOrder == RenderOrder.HighestPriorityFirst)
		{
			return Enumerable.Reverse(Layers).GetEnumerator();
		}
		return Layers.GetEnumerator();
	}
}
