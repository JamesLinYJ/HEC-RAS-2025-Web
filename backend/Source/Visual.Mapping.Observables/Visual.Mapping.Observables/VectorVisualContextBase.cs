using System;
using System.Collections.Generic;
using System.Drawing;
using System.Xml.Linq;
using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Visual.Mapping.Observables.Map;
using Visual.Mapping.Observables.Selection;

namespace Visual.Mapping.Observables;

public abstract class VectorVisualContextBase : VisualContext, IVectorVisualContext
{
	private SelectedSetFeatureSelection _featureSelection;

	public List<VectorSymbologyOption> SymbologyOptions { get; set; } = new List<VectorSymbologyOption>();

	public override IEnumerable<IFeatureSelection> FeatureSelections
	{
		get
		{
			SelectedSet selectedSet = (NotLazyLoadedRenderLayer as IVectorRenderLayer)?.SelectedSet;
			if (selectedSet == null)
			{
				return null;
			}
			if (_featureSelection == null || _featureSelection.SelectedFeatureIDs != selectedSet)
			{
				_featureSelection = new SelectedSetFeatureSelection(selectedSet);
			}
			return new _003C_003Ez__ReadOnlySingleElementList<IFeatureSelection>(_featureSelection);
		}
	}

	protected virtual IEnumerable<IFeatureSelection> RedrawingSelections
	{
		get
		{
			if (FeatureSelections != null)
			{
				return FeatureSelections;
			}
			return Array.Empty<IFeatureSelection>();
		}
	}

	protected virtual string ToolTag => GetType().Name;

	protected virtual VectorSymbology DefaultRenderSymbology => new VectorSymbology(Color.Black, 1.0, Color.Transparent);

	protected VectorVisualContextBase(MapBase parentMap)
		: base(parentMap)
	{
		RenderLayerInitialized += VectorRenderLayerInitialized;
	}

	private void VectorRenderLayerInitialized(VisualContext sender, IRenderLayer builtLayer)
	{
		RefreshSelectionRedraw();
	}

	public override void Serialize(XElement serializeTo)
	{
		base.Serialize(serializeTo);
		foreach (VectorSymbologyOption symbologyOption in SymbologyOptions)
		{
			symbologyOption.Serialize(serializeTo);
		}
	}

	protected virtual void OnEditSessionCreated(ToolOrchestrator editLayer)
	{
	}

	protected virtual IReadOnlyList<ToolNode> BuildAdditionalTools()
	{
		return Array.Empty<ToolNode>();
	}

	protected virtual IReadOnlyList<ToolNode> BuildEditTools()
	{
		return null;
	}

	protected override EditingSession CreateEditLayerCore(ToolOrchestrator editLayer)
	{
		if (editLayer == null || !(this is IVectorFeatureContext { SelectedFeatureIDs: var selectedFeatureIDs } vectorFeatureContext))
		{
			return null;
		}
		if (selectedFeatureIDs == null)
		{
			return null;
		}
		if (!vectorFeatureContext.TryRegisterSketchHandlers(editLayer, selectedFeatureIDs))
		{
			return null;
		}
		OnEditSessionCreated(editLayer);
		return editLayer;
	}

	protected override IReadOnlyList<ToolNode> BuildContributedToolsInternal()
	{
		if (!(this is IVectorFeatureContext) || this is IPointVectorContext)
		{
			return base.BuildContributedToolsInternal();
		}
		return StandardToolFactories.DefaultVectorTools(this, ToolTag, BuildSelectionTools, BuildAdditionalTools, BuildEditTools);
	}

	protected override IRenderLayer CreateRenderLayer()
	{
		if (!(this is IVectorFeatureContext vectorFeatureContext))
		{
			return null;
		}
		return vectorFeatureContext.CreateDefaultRenderLayer(DefaultRenderSymbology);
	}
}
