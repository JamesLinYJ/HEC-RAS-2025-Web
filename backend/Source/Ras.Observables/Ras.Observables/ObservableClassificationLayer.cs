using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers;
using Ras.Layers.Classifiers;
using Utility.DataStructures;
using Visual.Observables;

namespace Ras.Observables;

public class ObservableClassificationLayer : ObservableCoreLayer
{
	public delegate void ClassMapRebuiltEvent(ObservableClassificationLayer sender);

	private AdvancedCollection<IObservableClassificationInput> _classificationInputs;

	private AdvancedCollection<IObservableClassificationInput> _classificationInputsPriorityOrder;
	private AdvancedCollection<string> _classificationValues = new AdvancedCollection<string>();
	private AdvancedCollection<BatchJob> _currentlyImportingInputs = new AdvancedCollection<BatchJob>();

	public static Type Type => typeof(ObservableClassificationLayer);

	public ClassificationLayer ClassificationLayer => Layer as ClassificationLayer;

	public int LayerCount => ClassificationLayer.LayerCount;

	public IReadOnlyAdvancedCollection<IObservableClassificationInput> ClassificationInputs => _classificationInputs;

	public IReadOnlyAdvancedCollection<IObservableClassificationInput> ClassificationInputsPriorityOrder => _classificationInputsPriorityOrder;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<string> ClassificationValues
	{
		get
		{
			return _classificationValues;
		}
		[MemberNotNull("_classificationValues")]
		set
		{
			if (!EqualityComparer<AdvancedCollection<string>>.Default.Equals(_classificationValues, value))
			{
				OnPropertyChanging("ClassificationValues");
				_classificationValues = value;
				OnPropertyChanged("ClassificationValues");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<BatchJob> CurrentlyImportingInputs
	{
		get
		{
			return _currentlyImportingInputs;
		}
		[MemberNotNull("_currentlyImportingInputs")]
		set
		{
			if (!EqualityComparer<AdvancedCollection<BatchJob>>.Default.Equals(_currentlyImportingInputs, value))
			{
				OnPropertyChanging("CurrentlyImportingInputs");
				_currentlyImportingInputs = value;
				OnPropertyChanged("CurrentlyImportingInputs");
			}
		}
	}

	public event ClassMapRebuiltEvent ClassMapRebuilt;

	public ObservableClassificationLayer(ObservableProject project, ClassificationLayer lyr)
		: base(project, lyr)
	{
		InitializeInputs();
	}

	private void InitializeInputs()
	{
		_classificationInputs = new AdvancedCollection<IObservableClassificationInput>();
		_classificationInputs.AddAll(((IEnumerable<IClassificationInput>)ClassificationLayer.Layers).Select((Func<IClassificationInput, IObservableClassificationInput>)((IClassificationInput input) =>
		{
			if (input is ConstantClassificationInput input2)
			{
				return new ObservableConstantClassificationInput(input2);
			}
			if (input is RasterClassificationInput input3)
			{
				return new ObservableRasterClassificationInput(this, input3);
			}
			if (!(input is PolygonClassificationInput input4))
			{
				throw new NotImplementedException();
			}
			return new ObservablePolygonClassificationInput(this, input4);
		})));
		_classificationInputsPriorityOrder = new AdvancedCollection<IObservableClassificationInput>(ClassificationInputs.Reverse());
		foreach (IObservableClassificationInput classificationInput in ClassificationInputs)
		{
			InitializeEventListeners(classificationInput);
		}
	}

	private void InitializeEventListeners(IObservableClassificationInput input)
	{
		input.ClassMapRebuilt += Rci_ClassMapRebuilt;
	}

	private void RemoveEventListeners(IObservableClassificationInput input)
	{
		input.ClassMapRebuilt -= Rci_ClassMapRebuilt;
	}

	private void NotifyClassMapRebuilt()
	{
		ClassMapRebuilt?.Invoke(this);
		ClassificationValues.SetAll(ClassificationLayer.ClassificationMap.Values);
	}

	private void Rci_ClassMapRebuilt(IObservableClassificationInput sender)
	{
		NotifyClassMapRebuilt();
	}

	public void ReorderInput(int fromIndex, int toIndex)
	{
		if (IsLocked)
		{
			throw new Exception("Cannot reorder while the layer is locked");
		}
		if (fromIndex >= LayerCount || toIndex >= LayerCount)
		{
			throw new ArgumentException("Bad index reordering");
		}
		if (fromIndex != toIndex)
		{
			int countOffset = _classificationInputsPriorityOrder.Count - 1;
			ClassificationLayer.Reorder(fromIndex, toIndex);
			_classificationInputs.Move(fromIndex, toIndex);
			_classificationInputsPriorityOrder.Move(countOffset - fromIndex, countOffset - toIndex);
		}
	}

	public void AddClassificationInput(IObservableClassificationInput input)
	{
		if (IsLocked)
		{
			throw new Exception("Cannot add while the layer is locked");
		}
		ClassificationLayer.Add(input.ClassificationInput);
		_classificationInputs.Add(input);
		_classificationInputsPriorityOrder.Insert(0, input);
		InitializeEventListeners(input);
		NotifyClassMapRebuilt();
	}

	public void InsertClassificationInput(int index, IObservableClassificationInput input)
	{
		if (IsLocked)
		{
			throw new Exception("Cannot add while the layer is locked");
		}
		ClassificationLayer.Insert(index, input.ClassificationInput);
		_classificationInputs.Insert(index, input);
		_classificationInputsPriorityOrder.Insert(_classificationInputsPriorityOrder.Count - index, input);
		InitializeEventListeners(input);
		NotifyClassMapRebuilt();
	}

	public void RemoveClassificationInput(IObservableClassificationInput input)
	{
		if (IsLocked)
		{
			throw new Exception("Cannot remove while the layer is locked");
		}
		ClassificationLayer.Remove(input.ClassificationInput);
		_classificationInputs.Remove(input);
		_classificationInputsPriorityOrder.Remove(input);
		RemoveEventListeners(input);
		NotifyClassMapRebuilt();
	}

	public ObservablePolygonClassificationInput AddDefaultPolygonClassificationInput()
	{
		ObservablePolygonClassificationInput observablePolygonClassificationInput = new ObservablePolygonClassificationInput(this, "Classification Polygons");
		AddClassificationInput(observablePolygonClassificationInput);
		return observablePolygonClassificationInput;
	}

	public bool HasPolygonClassificationInput(out ObservablePolygonClassificationInput polyInput)
	{
		foreach (IObservableClassificationInput classificationInput in ClassificationInputs)
		{
			if (classificationInput is ObservablePolygonClassificationInput observablePolygonClassificationInput)
			{
				polyInput = observablePolygonClassificationInput;
				return true;
			}
		}
		polyInput = null;
		return false;
	}

	public ObservablePolygonClassificationInput GetOrMakePolygonClassificationInput()
	{
		if (HasPolygonClassificationInput(out var polyInput))
		{
			return polyInput;
		}
		return AddDefaultPolygonClassificationInput();
	}

	protected override void DeleteSelfInternal()
	{
		Project.RemoveClassification(this);
		ClassificationLayer.DisposeAndDeleteAllBackingFiles();
	}

	public override bool TryRenameByDeleteAndSaveAsH5(string inputName, out string errorText)
	{
		string newFilename = ClassificationLayer.MakeFilename(Project.Project.ProjectDirectory, inputName);
		if (!TryRenameByDeleteAndSaveAsH5Internal(inputName, ClassificationLayer.MakeFilename, out errorText))
		{
			return false;
		}
		foreach (ObservableNValueLayer nValue in Project.NValues)
		{
			if (nValue.ClassificationLayer == this)
			{
				nValue.ClassificationLayer.AlertFileRenamed(newFilename);
			}
		}
		return true;
	}
}
