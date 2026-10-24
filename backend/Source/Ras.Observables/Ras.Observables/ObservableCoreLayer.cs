using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Microsoft.Extensions.Logging;
using Ras.Layers;
using Utility.Extensions;
using Utility.Files;
using Utility.Interfaces;
using Utility.Logging;
using Visual.Observables.Interfaces;

namespace Ras.Observables;

public abstract class ObservableCoreLayer : ObservableBaseLayer, IObservableReadOnlyDisplayName, IReadOnlyDisplayName
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ObservableCoreLayer");

	private CoreLayerBase _layer;

	public CoreLayerBase Layer
	{
		get
		{
			return _layer;
		}
		private set
		{
			SetProperty(ref _layer, value, "Layer");
		}
	}

	string IReadOnlyDisplayName.DisplayName => Name;

	[MinLength(3)]
	public string Name
	{
		get
		{
			return Layer.Name;
		}
		protected set
		{
			if (SetProperty(Layer.Name, value, Layer, (CoreLayerBase g, string v) =>
			{
				g.Name = v;
			}, "Name"))
			{
				NameChanged?.Invoke(this, value);
				OnPropertyChanged("DisplayName");
			}
		}
	}

	public override bool IsInMemory => Layer.IsInMemory;

	public override string Filename => Layer.Filename;

	public IReadOnlyList<Message> LayerErrors => Layer.LayerErrors;

	public bool HasLoadErrors => Layer.HasLoadErrors;

	public string Description
	{
		get
		{
			return Layer.Description;
		}
		set
		{
			SetProperty(Layer.Description, value, Layer, (CoreLayerBase a, string b) =>
			{
				a.Description = b;
			}, "Description");
		}
	}

	public EmbeddedImageCollection EmbeddedImages => Layer.EmbeddedImages;

	public event IObservableReadOnlyDisplayName.NameChangedEvent NameChanged;

	public ObservableCoreLayer(ObservableProject project, CoreLayerBase lyr)
		: base(project, lyr)
	{
		Layer = lyr;
		Layer.ErrorsChanged += Layer_ErrorsChanged;
	}

	public bool DuplicateBySaveAsH5(out string fileName)
	{
		return DuplicateBySaveAsH5(out fileName, null);
	}

	/// <summary>
	/// Duplicates this layer using the source-selected filename. A destination
	/// selector may redirect the HDF5 write while the source-selected filename
	/// remains available to callers for project naming and association logic.
	/// </summary>
	public bool DuplicateBySaveAsH5(out string fileName, Func<string, string>? selectDestination)
	{
		string path = ((!(Layer is IResultSubLayer { IsResult: not false })) ? Layer.SourceDirectory : Ras.Layers.Project.GetDefaultDirectory(Layer.GetType(), Project.Project.ProjectDirectory));
		int unity = 1;
		while (true)
		{
			fileName = Path.Combine(path, Name + " (" + unity + ").h5");
			if (!File.Exists(fileName))
			{
				break;
			}
			unity++;
		}
		string destination = selectDestination is null ? fileName : selectDestination(fileName);
		if (string.IsNullOrWhiteSpace(destination))
		{
			throw new ArgumentException("A duplicate destination is required.", nameof(selectDestination));
		}
		Layer.SaveAs(destination);
		return true;
	}

	public abstract bool TryRenameByDeleteAndSaveAsH5(string inputName, out string errorText);

	protected bool TryRenameByDeleteAndSaveAsH5Internal(string inputName, Func<string, string, string> makeFilename, out string errorText)
	{
		string filename = Layer.Filename;
		errorText = string.Empty;
		if (!CoreLayerBase.IsLegalName(inputName, out var errorMessage))
		{
			errorText = errorMessage;
			return false;
		}
		string makeFilename2 = makeFilename(Project.Project.ProjectDirectory, inputName);
		if (string.Equals(makeFilename2, filename, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (File.Exists(makeFilename2))
		{
			errorText = "A file already exists at " + makeFilename2.InDoubleQuotes() + ". Pick a different name.";
			return false;
		}
		AlertFileRenaming(makeFilename2);
		File.Copy(Filename, makeFilename2);
		if (!IsInMemory)
		{
			AlertFileRenamed(makeFilename2);
		}
		else
		{
			Name = inputName;
		}
		if (IsInMemory)
		{
			return true;
		}
		FileHelpers.TryDelete(filename);
		return true;
	}

	private void Layer_ErrorsChanged(object sender, EventArgs e)
	{
		Project.SynchronizationContext.Sync(() =>
		{
			OnPropertyChanged("LayerErrors");
			OnPropertyChanged("HasLoadErrors");
		});
	}

	public void AlertFileRename_CrossThread(string newFilename)
	{
		Project.SynchronizationContext.Sync(() =>
		{
			AlertFileRenamed(newFilename);
		});
	}

	private void AlertFileRenaming(string newFilename)
	{
		if (!IsInMemory)
		{
			Layer.Save();
		}
		Layer.AlertFileRenaming(newFilename);
	}

	public void AlertFileRenamed(string newFilename)
	{
		Layer.AlertFileRenamed(newFilename);
		OnPropertyChanged("Filename");
		OnPropertyChanged("Name");
		NameChanged?.Invoke(this, Name);
	}

	public override int GetHashCode()
	{
		return Layer.GetHashCode();
	}

	public void DebugAddError(string msg)
	{
		Project.SynchronizationContext.Sync(() =>
		{
			Layer.DebugAddError(new Message(msg));
			OnPropertyChanged("LayerErrors");
		});
	}
}
