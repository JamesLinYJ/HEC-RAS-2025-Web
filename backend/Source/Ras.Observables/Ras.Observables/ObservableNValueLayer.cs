using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Ras.Layers;
using Utility.Logging;
using Utility.Validation;

namespace Ras.Observables;

public class ObservableNValueLayer : ObservableCoreLayer
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ObservableNValueLayer");

	private ObservableClassificationLayer _classificationLayer;

	private ObservableNValueTable _interpretationTable;

	public static Type Type => typeof(ObservableNValueLayer);

	public new NValueLayer Layer => base.Layer as NValueLayer;

	public NValueLayer NVLayer => Layer;

	public NValueSource Mode
	{
		get
		{
			return Layer.Mode;
		}
		set
		{
			SetProperty(Layer.Mode, value, Layer, (NValueLayer g, NValueSource v) =>
			{
				g.Mode = v;
			}, "Mode");
		}
	}

	[AbsoluteBounds(0.0, 3.4028234663852886E+38, "The Manning's N value is invalid.")]
	[RecommendedBounds(0.009, 0.15, "The Manning's N value is outside of the recommended bounds.", false)]
	public float ConstantValue
	{
		get
		{
			return Layer.ConstantValue;
		}
		set
		{
			SetProperty(Layer.ConstantValue, value, Layer, (NValueLayer g, float v) =>
			{
				g.ConstantValue = v;
			}, "ConstantValue");
		}
	}

	public ObservableClassificationLayer ClassificationLayer
	{
		get
		{
			return _classificationLayer;
		}
		set
		{
			ObservableClassificationLayer classificationLayer = ClassificationLayer;
			NVLayer.AssociatedClassificationLayer = value?.ClassificationLayer;
			SetProperty(ref _classificationLayer, value, "ClassificationLayer");
			OnClassificationLayerChanged(classificationLayer, value);
			UpdateNValueTable();
		}
	}

	public ObservableNValueTable InterpretationTable
	{
		get
		{
			if (_interpretationTable == null)
			{
				_interpretationTable = new ObservableNValueTable(NVLayer.InterpretationTable);
			}
			return _interpretationTable;
		}
	}

	public ObservableNValueLayer(ObservableProject project, NValueLayer lyr)
		: base(project, lyr)
	{
		// Observation must not prune persisted mappings or clear unresolved names.
		// Reconcile only after an explicit association edit or classification change.
		_classificationLayer = Project.Classifications.FirstOrDefault((ObservableClassificationLayer obsClassificationLayer) => obsClassificationLayer.ClassificationLayer == NVLayer.AssociatedClassificationLayer);
		OnClassificationLayerChanged(null, _classificationLayer);
	}

	private void OnClassificationLayerChanged(ObservableClassificationLayer oldVal, ObservableClassificationLayer newVal)
	{
		if (oldVal != null)
		{
			oldVal.ClassMapRebuilt -= ClassificationLayerMapRebuilt;
		}
		if (newVal != null)
		{
			newVal.ClassMapRebuilt += ClassificationLayerMapRebuilt;
		}
	}

	private void ClassificationLayerMapRebuilt(ObservableClassificationLayer sender)
	{
		UpdateNValueTable();
	}

	private void UpdateNValueTable()
	{
		if (ClassificationLayer == null)
		{
			InterpretationTable.Rows.Clear();
			return;
		}
		for (int i = 0; i < InterpretationTable.Rows.Count; i++)
		{
			ObservableNValueTableRow observableNValueTableRow = InterpretationTable.Rows[i];
			if (!ClassificationLayer.ClassificationLayer.ClassificationMap.Map.ContainsValue(observableNValueTableRow.Key))
			{
				InterpretationTable.Rows.Remove(observableNValueTableRow);
				i--;
			}
		}
		foreach (string textValue in ClassificationLayer.ClassificationLayer.ClassificationMap.Values)
		{
			if (!InterpretationTable.Table.RawData.ContainsKey(textValue))
			{
				InterpretationTable.Rows.Add(new ObservableNValueTableRow(textValue, 0f));
			}
		}
	}

	protected override void DeleteSelfInternal()
	{
		Project.RemoveNValue(this);
		NVLayer.DisposeAndDeleteAllBackingFiles();
	}

	public override bool TryRenameByDeleteAndSaveAsH5(string inputName, out string errorText)
	{
		if (!TryRenameByDeleteAndSaveAsH5Internal(inputName, NValueLayer.MakeFilename, out errorText))
		{
			return false;
		}
		foreach (ObservableGeometry geometry in Project.Geometries)
		{
			if (geometry.AssociatedNValueLayer == this)
			{
				Project.Project.GetGeometryAssociation(geometry.Geometry).AlertNValueRenamed(inputName);
			}
		}
		return true;
	}
}
