using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Rendering.TiledRenderingSystem;
using Utility;
using Utility.Extensions;
using Utility.Memory;
using Visual.Mapping.Observables.Symbology;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public class VectorSymbologyOption : ObservableObject
{
	private delegate bool TryParseFunc<T>(string input, out T result);

	private struct ConditionPredicateResult<T>
	{
		public Func<object, bool> Predicate;

		public int ColIdx;

		public T Result;
	}

	private bool _isDirty;

	private bool _isSyncing;
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableVectorSymbology Symbology
	{
		get
		{
			return field;
		}
		private set
		{
			if (!EqualityComparer<ObservableVectorSymbology>.Default.Equals(field, value))
			{
				OnPropertyChanging("Symbology");
				field = value;
				OnPropertyChanged("Symbology");
			}
		}
	}

	public VectorSymbologyDefinition Definition { get; private set; }
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableCollection<ObservableVectorSymbologyCondition> PenColorConditions
	{
		get
		{
			return field;
		}
		private set
		{
			if (!EqualityComparer<ObservableCollection<ObservableVectorSymbologyCondition>>.Default.Equals(field, value))
			{
				OnPropertyChanging("PenColorConditions");
				field = value;
				OnPropertyChanged("PenColorConditions");
			}
		}
	} = new ObservableCollection<ObservableVectorSymbologyCondition>();
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableCollection<ObservableVectorSymbologyCondition> PenWidthConditions
	{
		get
		{
			return field;
		}
		private set
		{
			if (!EqualityComparer<ObservableCollection<ObservableVectorSymbologyCondition>>.Default.Equals(field, value))
			{
				OnPropertyChanging("PenWidthConditions");
				field = value;
				OnPropertyChanged("PenWidthConditions");
			}
		}
	} = new ObservableCollection<ObservableVectorSymbologyCondition>();
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableCollection<ObservableVectorSymbologyCondition> FillColorConditions
	{
		get
		{
			return field;
		}
		private set
		{
			if (!EqualityComparer<ObservableCollection<ObservableVectorSymbologyCondition>>.Default.Equals(field, value))
			{
				OnPropertyChanging("FillColorConditions");
				field = value;
				OnPropertyChanged("FillColorConditions");
			}
		}
	} = new ObservableCollection<ObservableVectorSymbologyCondition>();
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableCollection<ObservableVectorSymbologyCondition> PointSizeConditions
	{
		get
		{
			return field;
		}
		private set
		{
			if (!EqualityComparer<ObservableCollection<ObservableVectorSymbologyCondition>>.Default.Equals(field, value))
			{
				OnPropertyChanging("PointSizeConditions");
				field = value;
				OnPropertyChanged("PointSizeConditions");
			}
		}
	} = new ObservableCollection<ObservableVectorSymbologyCondition>();
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableTable AttributeTable
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<ObservableTable>.Default.Equals(field, value))
			{
				OnPropertyChanging("AttributeTable");
				field = value;
				OnAttributeTableChanged(value);
				OnPropertyChanged("AttributeTable");
			}
		}
	}

	private bool HasNoConditions
	{
		get
		{
			if (PenColorConditions.IsNullOrEmpty() && PenWidthConditions.IsNullOrEmpty() && FillColorConditions.IsNullOrEmpty())
			{
				return PointSizeConditions.IsNullOrEmpty();
			}
			return false;
		}
	}

	public VectorSymbologyOption(VectorSymbologyDefinition def, ObservableVectorSymbology symb, bool syncToGlobalDefault = true)
	{
		Definition = def;
		Symbology = symb;
		if (syncToGlobalDefault)
		{
			Symbology.PropertyChanged += Symbology_PropertyChanged;
		}
	}

	public void Serialize(XElement serializeTo)
	{
		bool vectorSymbologiesEqual = VectorSymbologyHelpers.VectorSymbologiesEqual(Definition.Type, Definition.DefaultSymbology, Symbology.Symbology);
		if (vectorSymbologiesEqual && HasNoConditions)
		{
			return;
		}
		XElement xElement = new XElement("Symbology");
		serializeTo.Add(xElement);
		xElement.SetAttributeValue("ID", Definition.Id);
		if (!vectorSymbologiesEqual)
		{
			if (Definition.Type == VectorType.Line)
			{
				VectorSymbologyHelpers.SerializeLineSymbology(xElement, Symbology.Symbology);
			}
			else if (Definition.Type == VectorType.Polygon)
			{
				VectorSymbologyHelpers.SerializePolygonSymbology(xElement, Symbology.Symbology);
			}
			else if (Definition.Type == VectorType.Point)
			{
				VectorSymbologyHelpers.SerializePointSymbology(xElement, Symbology.Symbology);
			}
		}
		VectorSymbologyHelpers.SerializeConditions(xElement, PenColorConditions.Select((ObservableVectorSymbologyCondition c) => c.Condition).ToArray(), "PenColorConditions");
		VectorSymbologyHelpers.SerializeConditions(xElement, PenWidthConditions.Select((ObservableVectorSymbologyCondition c) => c.Condition).ToArray(), "PenWidthConditions");
		VectorSymbologyHelpers.SerializeConditions(xElement, FillColorConditions.Select((ObservableVectorSymbologyCondition c) => c.Condition).ToArray(), "FillColorConditions");
		VectorSymbologyHelpers.SerializeConditions(xElement, PointSizeConditions.Select((ObservableVectorSymbologyCondition c) => c.Condition).ToArray(), "PointSizeConditions");
	}

	public static VectorSymbologyOption Deserialize(XElement element, VectorSymbologyDefinition def, ISynchronizationContext syncContext, VectorSymbology symbologyToUse)
	{
		VectorSymbologyHelpers.DeserializeVectorSymbology(element, symbologyToUse);
		ObservableVectorSymbology observableVectorSymbology = new ObservableVectorSymbology(symbologyToUse, syncContext);
		VectorSymbologyOption vectorSymbologyOption = new VectorSymbologyOption(def, observableVectorSymbology);
		DeserializeConditionsInto(element, "PenColorConditions", vectorSymbologyOption.PenColorConditions);
		DeserializeConditionsInto(element, "PenWidthConditions", vectorSymbologyOption.PenWidthConditions);
		DeserializeConditionsInto(element, "FillColorConditions", vectorSymbologyOption.FillColorConditions);
		DeserializeConditionsInto(element, "PointSizeConditions", vectorSymbologyOption.PointSizeConditions);
		vectorSymbologyOption._isDirty = !VectorSymbologyHelpers.VectorSymbologiesEqual(def.Type, def.DefaultSymbology, observableVectorSymbology.Symbology);
		return vectorSymbologyOption;
	}

	private static void DeserializeConditionsInto(XElement element, string conditionsElementName, ObservableCollection<ObservableVectorSymbologyCondition> target)
	{
		VectorSymbologyCondition[] deserializeConditions = VectorSymbologyHelpers.DeserializeConditions(element, conditionsElementName);
		foreach (VectorSymbologyCondition vectorSymbologyCondition in deserializeConditions)
		{
			ConditionOperatorMap.Map.TryGetKey(vectorSymbologyCondition.Operator, out var key);
			target.Add(new ObservableVectorSymbologyCondition
			{
				Column = vectorSymbologyCondition.Column,
				Operator = key,
				Threshold = vectorSymbologyCondition.Threshold,
				ResultValue = vectorSymbologyCondition.ResultValue
			});
		}
	}

	public void ResetSymbologyToDefault()
	{
		_isSyncing = true;
		try
		{
			VectorSymbologyHelpers.CloneInto(Definition.DefaultSymbology, Symbology);
		}
		finally
		{
			_isSyncing = false;
		}
		_isDirty = false;
	}

	public void SyncToNewDefault(VectorSymbology symbology)
	{
		if (!_isDirty)
		{
			_isSyncing = true;
			try
			{
				VectorSymbologyHelpers.CloneInto(symbology, Symbology);
			}
			finally
			{
				_isSyncing = false;
			}
		}
		Definition.UpdateDefaultSymbology(symbology);
	}

	public void CompileAndApplyConditions()
	{
		if (!(Symbology.Symbology is IConditionalVectorSymbology conditionalVectorSymbology))
		{
			return;
		}
		ConditionalVectorSymbology conditional = conditionalVectorSymbology as ConditionalVectorSymbology;
		if (conditional == null)
		{
			throw new NotImplementedException("TModel not supported");
		}
		if (AttributeTable != null)
		{
			Table table = AttributeTable.Table;
			VectorSymbologyCondition[] conditions = PenWidthConditions.Select((ObservableVectorSymbologyCondition c) => c.Condition).ToArray();
			VectorSymbologyCondition[] conditions2 = PointSizeConditions.Select((ObservableVectorSymbologyCondition c) => c.Condition).ToArray();
			VectorSymbologyCondition[] conditions3 = PenColorConditions.Select((ObservableVectorSymbologyCondition c) => c.Condition).ToArray();
			VectorSymbologyCondition[] conditions4 = FillColorConditions.Select((ObservableVectorSymbologyCondition c) => c.Condition).ToArray();
			Func<int, double?> penWidthOverride = BuildOverride<double>(conditions, table, double.TryParse);
			Func<int, int?> pointSizeOverride = BuildOverride<int>(conditions2, table, int.TryParse);
			Func<int, Color?> penColorOverride = BuildOverride<Color>(conditions3, table, TryParseHexColor);
			Func<int, Color?> fillColorOverride = BuildOverride<Color>(conditions4, table, TryParseHexColor);
			PyramidedTileBoss.Instance.MapSyncContext.Sync(() =>
			{
				conditional.GetPenWidthOverride = penWidthOverride;
				conditional.GetPointSizeOverride = pointSizeOverride;
				conditional.GetPenColorOverride = penColorOverride;
				conditional.GetFillColorOverride = fillColorOverride;
			});
		}
	}

	private static Func<int, T?> BuildOverride<T>(VectorSymbologyCondition[] conditions, Table table, TryParseFunc<T> tryParse) where T : struct
	{
		if (conditions.Length == 0)
		{
			return null;
		}
		ConditionPredicateResult<T>[] compiled = new ConditionPredicateResult<T>[conditions.Length];
		int count = 0;
		foreach (VectorSymbologyCondition vectorSymbologyCondition in conditions)
		{
			if (!string.IsNullOrEmpty(vectorSymbologyCondition.Column) && !string.IsNullOrEmpty(vectorSymbologyCondition.Threshold) && !string.IsNullOrEmpty(vectorSymbologyCondition.ResultValue) && table.TryGetColumnIndex(vectorSymbologyCondition.Column, out var idx) && tryParse(vectorSymbologyCondition.ResultValue, out var result))
			{
				Type type = table.Columns[idx].Type;
				Func<object, bool> predicate = ConditionPredicates.Build(vectorSymbologyCondition.Operator, vectorSymbologyCondition.Threshold, type);
				compiled[count] = new ConditionPredicateResult<T>
				{
					Predicate = predicate,
					ColIdx = idx,
					Result = result
				};
				count++;
			}
		}
		if (count == 0)
		{
			return null;
		}
		return (int featureId) =>
		{
			TableRow tableRow = table[featureId];
			for (int j = 0; j < count; j++)
			{
				ConditionPredicateResult<T> conditionPredicateResult = compiled[j];
				if (conditionPredicateResult.Predicate(tableRow[conditionPredicateResult.ColIdx]))
				{
					return conditionPredicateResult.Result;
				}
			}
			return (T?)null;
		};
	}

	private static bool TryParseHexColor(string input, out Color result)
	{
		try
		{
			result = ColorTranslator.FromHtml(input);
			return true;
		}
		catch
		{
			result = default;
			return false;
		}
	}

	private void Symbology_PropertyChanged(object sender, PropertyChangedEventArgs e)
	{
		if (!_isSyncing)
		{
			_isDirty = true;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnAttributeTableChanged(ObservableTable observableTable)
	{
		CompileAndApplyConditions();
	}
}
