using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.GDALAssist.Vectors;
using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Utility.Interfaces;
using Visual.Mapping.Observables.Map;

namespace Visual.Mapping.Observables;

public class GdbDatasetVisualContext : VisualContext, IMapLayerVisualContext, IFilename
{
	public const string GdbDatasetXmlNodeIdentifier = "GdbDataset";

	private readonly string _iconResource = NamedResources.Layer.Default;

	public string XmlNodeIdentifier => "GdbDataset";

	public override string DefaultName => "Geodatabase";

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

	public List<GdbLayerInfo> Layers { get; set; } = new List<GdbLayerInfo>();

	public GdbDatasetVisualContext(string gdbPath, MapBase map)
		: base(map)
	{
		Filename = gdbPath;
		Name = Path.GetFileNameWithoutExtension(gdbPath);
	}

	protected override IRenderLayer CreateRenderLayer()
	{
		return null;
	}

	public override IReadOnlyList<IRenderLayer> GetActiveRenderLayers()
	{
		return null;
	}

	protected override EditingSession CreateEditLayerCore(ToolOrchestrator editLayer)
	{
		return null;
	}

	public override void Serialize(XElement serializeTo)
	{
	}

	public override void Deserialize(XElement deserializeFrom)
	{
	}
}
