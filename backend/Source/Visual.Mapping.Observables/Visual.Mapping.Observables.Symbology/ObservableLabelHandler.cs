using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Rendering.TiledRenderingSystem;
using Utility;

namespace Visual.Mapping.Observables.Symbology;

public class ObservableLabelHandler : ObservableObject
{
	private static readonly VectorSymbologyDefinition _defaultDefinition = new VectorSymbologyDefinition("Label_Text_Symbology", "Text", VectorType.Text, LabelHandler.DefaultLabelSymbology(), "", "Map Labels");

	private readonly LabelHandler _labelHandler;

	public LabelHandler LabelHandler => _labelHandler;

	public ObservableVectorSymbology LabelSymbology { get; }

	public VectorSymbologyDefinition LabelSymbologyDefinition { get; }

	public ObservableLabelHandler(LabelHandler labelHandler, ISynchronizationContext syncContext)
	{
		GlobalVectorSymbologyRegistry.RegisterDefaultDefinition(_defaultDefinition);
		_labelHandler = labelHandler;
		VectorSymbology labelSymbology = _labelHandler.LabelSymbology;
		VectorSymbology vectorSymbology = GlobalVectorSymbologyRegistry.GetDefaultSymbology("Label_Text_Symbology") ?? labelSymbology;
		VectorSymbologyHelpers.CloneInto(vectorSymbology, labelSymbology);
		LabelSymbology = new ObservableVectorSymbology(labelSymbology, syncContext);
		LabelSymbologyDefinition = _defaultDefinition.GetUpdatedDefinition(vectorSymbology.Clone());
	}

	public void Redraw()
	{
		LabelHandler.Redraw();
	}
}
