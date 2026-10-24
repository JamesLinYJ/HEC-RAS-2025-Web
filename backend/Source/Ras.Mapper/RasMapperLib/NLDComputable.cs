using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Progress;
using Utility.Progress;

namespace RasMapperLib;

public class NLDComputable : IAmComputable
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D0
	{
		public int tablesfound;

		public NLDComputable nldComputable;

		public _Closure_0024__9_002D0(_Closure_0024__9_002D0 other)
		{
			if (other != null)
			{
				tablesfound = other.tablesfound;
			}
		}

		[SpecialName]
		internal List<FeatureLayer> _Lambda_0024__0(string[] filenames)
		{
			List<FeatureLayer> featureLayers = new List<FeatureLayer>();
			checked
			{
				foreach (string filename in filenames)
				{
					string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
					FeatureLayer featureLayer = ShapefileStorage.LoadFeatureLayerShell(filename);
					if (featureLayer == null || featureLayer.FeatureCount() == 0)
					{
						ShapefileStorage.Delete(filename);
						continue;
					}
					featureLayer.Name = fileNameWithoutExtension;
					nldComputable._cw.AddLine("\tTable found: " + fileNameWithoutExtension, Color.ForestGreen);
					featureLayers.Add(featureLayer);
					tablesfound++;
				}
				return featureLayers;
			}
		}
	}

	private IDisplayProgress _cw;

	private List<string> _tableNames;

	private string _downloadDir;

	private string _layerNamePrefix;

	private List<PolylineFeatureLayer> _clineLayers;

	private List<PolylineFeatureLayer> _fwallLayers;

	private List<PolygonFeatureLayer> _leveeAreaLayers;

	public NLDComputable(List<string> tablenames, string downloadDirectory, string layerNamePrefix)
	{
		_clineLayers = new List<PolylineFeatureLayer>();
		_fwallLayers = new List<PolylineFeatureLayer>();
		_leveeAreaLayers = new List<PolygonFeatureLayer>();
		_tableNames = tablenames;
		_downloadDir = downloadDirectory;
		_layerNamePrefix = layerNamePrefix;
	}

	public void Initialize(IDisplayProgress cw)
	{
		_cw = cw;
		_cw.SetHeaderText("Compute Window - Downloading NLD Data");
	}

	void IAmComputable.Initialize(IDisplayProgress cw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Initialize
		this.Initialize(cw);
	}

	public void Run(ProgressReporter reporter)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		new List<string>();
		Stopwatch sw = new Stopwatch();
		_cw.AddLine("Querying " + _tableNames.Count + " tables...");
		_cw.AddBarrier();
		checked
		{
			int count = _tableNames.Count - 1;
			_Closure_0024__9_002D0 capturedLocals = default;
			for (int i = 0; i <= count; i++)
			{
				reporter.ReportProgress((int)Math.Round((double)(100 * i) / (double)_tableNames.Count));
				string tableName = _tableNames[i];
				try
				{
					capturedLocals = new _Closure_0024__9_002D0(capturedLocals);
					capturedLocals.nldComputable = this;
					_cw.StepStart(sw, tableName + " (" + (i + 1) + " of " + _tableNames.Count + ")...");
					string[] strings = null;
					string[] stringsArray = null;
					string[] strings2 = null;
					if (reporter.CancelRequested)
					{
						return;
					}
					_cw.StepStop(sw);
					if (strings == null || strings.Count() == 0 || stringsArray == null || stringsArray.Count() == 0 || strings2 == null || strings2.Count() == 0)
					{
						_cw.AddLine("\t--Unknown Error--", Color.Red);
						continue;
					}
					capturedLocals.tablesfound = 0;
					Func<string[], List<FeatureLayer>> func = capturedLocals._Lambda_0024__0;
					List<FeatureLayer> funcList = func(strings);
					int count2 = funcList.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						if (funcList[j] is PolylineFeatureLayer polylineFeatureLayer)
						{
							_clineLayers.Add(polylineFeatureLayer);
						}
					}
					funcList = func(stringsArray);
					int count3 = funcList.Count - 1;
					for (int k = 0; k <= count3; k++)
					{
						if (funcList[k] is PolylineFeatureLayer polylineFeatureLayer2)
						{
							_fwallLayers.Add(polylineFeatureLayer2);
						}
					}
					funcList = func(strings2);
					int count4 = funcList.Count - 1;
					for (int l = 0; l <= count4; l++)
					{
						if (funcList[l] is PolygonFeatureLayer polygonFeatureLayer)
						{
							_leveeAreaLayers.Add(polygonFeatureLayer);
						}
					}
					if (capturedLocals.tablesfound == 0)
					{
						_cw.AddLine("\tNo data found.", Color.Red);
					}
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					_cw.AddLine("Error during download:\r" + ex2.Message, Color.Red);
					ProjectData.ClearProjectError();
				}
			}
			reporter.ReportProgress(100);
		}
	}

	void IAmComputable.Run(ProgressReporter reporter)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Run
		this.Run(reporter);
	}

	public void Complete()
	{
		string layerNamePrefixTerm = _layerNamePrefix + "_Centerlines";
		FeatureLayer featureLayer = CombineSaveLayers(_clineLayers, layerNamePrefixTerm, _downloadDir + Conversions.ToString(Path.DirectorySeparatorChar) + layerNamePrefixTerm);
		SharedData.RasMapper.MapLayersGroup.Nodes.Add(featureLayer);
		featureLayer.Checked = true;
		foreach (PolylineFeatureLayer clineLayer in _clineLayers)
		{
			clineLayer.DeleteSourceFiles();
		}
		layerNamePrefixTerm = _layerNamePrefix + "_Floodwalls";
		FeatureLayer featureLayer2 = CombineSaveLayers(_fwallLayers, layerNamePrefixTerm, _downloadDir + Conversions.ToString(Path.DirectorySeparatorChar) + layerNamePrefixTerm);
		SharedData.RasMapper.MapLayersGroup.Nodes.Add(featureLayer2);
		featureLayer2.Checked = true;
		foreach (PolylineFeatureLayer fwallLayer in _fwallLayers)
		{
			fwallLayer.DeleteSourceFiles();
		}
		layerNamePrefixTerm = _layerNamePrefix + "_LeveedAreas";
		FeatureLayer featureLayer3 = CombineSaveLayers(_leveeAreaLayers, layerNamePrefixTerm, _downloadDir + Conversions.ToString(Path.DirectorySeparatorChar) + layerNamePrefixTerm);
		SharedData.RasMapper.MapLayersGroup.Nodes.Add(featureLayer3);
		featureLayer3.Checked = true;
		foreach (PolygonFeatureLayer leveeAreaLayer in _leveeAreaLayers)
		{
			leveeAreaLayer.DeleteSourceFiles();
		}
		_cw.AddBarrier();
		_cw.AddLine("NLD Download complete.", Color.ForestGreen);
		SharedData.RASMapperReplot();
	}

	void IAmComputable.Complete()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Complete
		this.Complete();
	}

	private PolylineFeatureLayer CombineSaveLayers(List<PolylineFeatureLayer> inputLayers, string layerName, string outputFilenamePrefix)
	{
		return CombineSaveLayers(new PolylineFeatureLayer(layerName), inputLayers, outputFilenamePrefix);
	}

	private PolygonFeatureLayer CombineSaveLayers(List<PolygonFeatureLayer> inputLayers, string layerName, string outputFilenamePrefix)
	{
		return CombineSaveLayers(new PolygonFeatureLayer(layerName), inputLayers, outputFilenamePrefix);
	}

	private T CombineSaveLayers<T>(T combined, List<T> inputLayers, string outputFilenamePrefix) where T : FeatureLayer
	{
		foreach (FeatureLayer inputLayer in inputLayers)
		{
			combined.CopyFeaturesAndAttributes(inputLayer);
		}
		string sourceFilename = Path.ChangeExtension(outputFilenamePrefix, ".shp");
		combined.SourceFilename = sourceFilename;
		combined.Save();
		return combined;
	}
}
