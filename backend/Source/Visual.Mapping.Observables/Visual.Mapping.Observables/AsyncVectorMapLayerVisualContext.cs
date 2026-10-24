using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Utility.DataStructures;
using Utility.Interfaces;
using Visual.Mapping.Observables.Map;
using Visual.Mapping.Observables.Symbology;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public abstract class AsyncVectorMapLayerVisualContext : VectorVisualContextBase, IMapLayerVisualContext, IFilename
{
	private readonly string _iconResource = NamedResources.Layer.Default;

	public abstract string XmlNodeIdentifier { get; }

	public override string IconResource => _iconResource;
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Filename
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				OnPropertyChanging("Filename");
				field = value;
				OnPropertyChanged("Filename");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsDataLoaded
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsDataLoaded");
				field = value;
				OnPropertyChanged("IsDataLoaded");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool Initializing
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("Initializing");
				field = value;
				OnPropertyChanged("Initializing");
			}
		}
	}
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
				OnPropertyChanged("AttributeTable");
			}
		}
	}

	public SelectedSet SelectedFeatureIDs => (RenderLayer as IVectorRenderLayer)?.SelectedSet;

	protected AsyncVectorMapLayerVisualContext(string filename, MapBase map)
		: base(map)
	{
		Filename = filename;
	}

	public override IReadOnlyList<IRenderLayer> GetActiveRenderLayers()
	{
		if (!IsDataLoaded)
		{
			Initialize();
			return null;
		}
		return RenderLayer.WrapInCompactList();
	}

	protected override IRenderLayer CreateRenderLayer()
	{
		return null;
	}

	protected void Initialize()
	{
		if (!Initializing)
		{
			Initializing = true;
			Task.Run((Action)LoadInBackground);
		}
	}

	protected abstract void LoadInBackground();

	protected void PublishLoaded(IRenderLayer renderLayer, ObservableTable table, VectorSymbologyDefinition[] symbologyDefinitions, Action publishExtras = null)
	{
		Initializing = false;
		IsDataLoaded = true;
		_renderLayer = renderLayer;
		renderLayer.DeserializeSymbologyOptionsGeneric(ToDeserializeElement, SymbologyOptions, symbologyDefinitions);
		AttributeTable = table;
		publishExtras?.Invoke();
		OnEmittedRenderLayersChanged();
	}
}
