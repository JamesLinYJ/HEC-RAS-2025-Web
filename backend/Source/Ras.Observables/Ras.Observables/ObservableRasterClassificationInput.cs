using Geospatial.Rasters.Classifications;
using Ras.Layers.Classifiers;
using Visual.Mapping.Observables;

namespace Ras.Observables;

public class ObservableRasterClassificationInput : ObservableClassificationInput
{
	private ObservableClassificationLayer _parent;

	private RasterClassificationInput _input;

	private ObservableClassification _classification;

	public override IClassificationInput ClassificationInput => _input;

	public string Filename => _input.Filename;

	public string TypeName => _input.TypeName;

	public ObservableClassification Classification
	{
		get
		{
			if (_classification == null && _input.Classification != null)
			{
				Classification = new ObservableClassification(_input.Classification);
			}
			return _classification;
		}
		set
		{
			ObservableClassification classification = _classification;
			_classification = value;
			Classification classification2 = value?.Classification ?? null;
			if (_input.Classification != classification2)
			{
				_input.Classification = classification2;
			}
			OnClassificationChanged(classification, value);
		}
	}

	public override event IObservableClassificationInput.ClassMapRebuiltEvent ClassMapRebuilt;

	public ObservableRasterClassificationInput(ObservableClassificationLayer parent, RasterClassificationInput input)
	{
		_parent = parent;
		_input = input;
	}

	private void OnClassificationChanged(ObservableClassification oldVal, ObservableClassification newVal)
	{
		if (oldVal != null)
		{
			oldVal.ClassificationValueChanged -= ClassificationValueChangedListener;
		}
		if (newVal != null)
		{
			newVal.ClassificationValueChanged += ClassificationValueChangedListener;
		}
	}

	private void ClassificationValueChangedListener(ObservableClassification sender)
	{
		ClassMapRebuilt?.Invoke(this);
	}
}
