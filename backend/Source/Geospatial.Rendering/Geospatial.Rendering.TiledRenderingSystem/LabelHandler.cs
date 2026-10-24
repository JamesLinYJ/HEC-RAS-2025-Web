using System.Collections.Generic;
using System.Drawing;
using Geospatial.Rendering.Systems;
using Geospatial.Rendering.Systems.Vector;

namespace Geospatial.Rendering.TiledRenderingSystem;

public class LabelHandler : ILabelHandler
{
	public const string SymbologyId = "Label_Text_Symbology";

	private IVectorManager _manager;

	private List<ILabelDefinition> _activeLabels = new List<ILabelDefinition>();

	private Dictionary<VectorSymbology, VectorID> _labelIDs = new Dictionary<VectorSymbology, VectorID>();

	private Dictionary<VectorSymbology, List<ILabelDefinition>> _grouped = new Dictionary<VectorSymbology, List<ILabelDefinition>>();

	private VectorSymbology _labelSymb = DefaultLabelSymbology();

	public bool IsEnabled { get; set; } = true;

	public VectorSymbology LabelSymbology => _labelSymb;

	public static VectorSymbology DefaultLabelSymbology()
	{
		return new VectorSymbology(Color.FromArgb(255, 255, 255, 255), 12.0, Color.FromArgb(255, 255, 255, 255))
		{
			HaloColor = Color.FromArgb(196, 0, 0, 0),
			HaloThickness = 2f,
			TextEmphasis = 1
		};
	}

	public LabelHandler(IVectorManager manager)
	{
		_manager = manager;
	}

	public void Redraw()
	{
		if (!IsEnabled)
		{
			return;
		}
		foreach (List<ILabelDefinition> iLabelDefinitions in _grouped.Values)
		{
			iLabelDefinitions.Clear();
		}
		foreach (ILabelDefinition activeLabel in _activeLabels)
		{
			VectorSymbology key = ((!(activeLabel is ILabelSymbologySource { LabelSymbology: not null } labelSymbologySource)) ? _labelSymb : labelSymbologySource.LabelSymbology);
			if (!_grouped.TryGetValue(key, out var grouped))
			{
				grouped = new List<ILabelDefinition>();
				_grouped[key] = grouped;
			}
			grouped.Add(activeLabel);
		}
		List<VectorSymbology> vectorSymbologies = null;
		foreach (KeyValuePair<VectorSymbology, List<ILabelDefinition>> grouped2 in _grouped)
		{
			VectorSymbology key2 = grouped2.Key;
			List<ILabelDefinition> iLabelDefinitionsList = grouped2.Value;
			_labelIDs.TryGetValue(key2, out var labelIDs);
			if (iLabelDefinitionsList.Count == 0)
			{
				if (!labelIDs.IsNull())
				{
					_manager.TryDeleteVector(ref labelIDs);
					_labelIDs.Remove(key2);
				}
				(vectorSymbologies ?? (vectorSymbologies = new List<VectorSymbology>())).Add(key2);
			}
			else
			{
				_manager.AddOrUpdateVector(ref labelIDs, iLabelDefinitionsList, key2);
				_manager.SetVectorZIndex(labelIDs, 10000);
				_labelIDs[key2] = labelIDs;
			}
		}
		if (vectorSymbologies == null)
		{
			return;
		}
		foreach (VectorSymbology vectorSymbology in vectorSymbologies)
		{
			_grouped.Remove(vectorSymbology);
		}
	}

	public void AddLabel(ILabelDefinition label)
	{
		if (label != null)
		{
			_activeLabels.Add(label);
			Redraw();
		}
	}

	public void AddLabels(IList<ILabelDefinition> labels)
	{
		if (labels != null && labels.Count != 0)
		{
			_activeLabels.AddRange(labels);
			Redraw();
		}
	}

	public void RemoveLabel(ILabelDefinition label)
	{
		if (label != null && _activeLabels.Remove(label))
		{
			Redraw();
		}
	}

	public void RemoveLabels(IList<ILabelDefinition> labels)
	{
		if (labels == null || labels.Count == 0)
		{
			return;
		}
		bool condition = true;
		foreach (ILabelDefinition label in labels)
		{
			condition &= _activeLabels.Remove(label);
		}
		if (condition)
		{
			Redraw();
		}
	}
}
