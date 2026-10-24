using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.VB;

namespace RasMapperLib;

public class MeshPointLayer : MultiPointFeatureLayer, IGeometryLayer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__18_002D0
	{
		public MultiPoint pts;

		public _Closure_0024__18_002D0(_Closure_0024__18_002D0 other)
		{
			if (other != null)
			{
				pts = other.pts;
			}
		}

		[SpecialName]
		internal PointM _Lambda_0024__0(int fffid)
		{
			return pts[fffid];
		}

		[SpecialName]
		internal PointM _Lambda_0024__1(int fffid)
		{
			return pts[fffid];
		}
	}

	public string HDFGroupName { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "Computation Points";

	private string IGeometryLayer_HDFGroupName => HDFGroupName;

	public MeshPointLayer(RASGeometry geometry)
		: base("Computation Points", geometry.SourceFilename)
	{
		HDFGroupName = "Geometry/2D Flow Areas";
		SimpleFeatureTypeName = "Computation Points";
		ForwardEditRequestsToParent = true;
		Geometry = geometry;
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureAdded)
		{
			Geometry.D2FlowArea.NotifyMeshInputsChanged();
			return;
		}
		if (args.ChangeType == FeatureChangedType.FeatureModified)
		{
			_Closure_0024__18_002D0 capturedLocals = default;
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				capturedLocals = new _Closure_0024__18_002D0(capturedLocals);
				capturedLocals.pts = Points(featureIndicesChanged);
				if (args.SubFeatureChanged == null)
				{
					Geometry.D2FlowArea.NotifyMeshInputsChanged();
					break;
				}
				if (args.SubFeatureChanged.ChangeType == FeatureChangedType.FeatureAdded)
				{
					Geometry.D2FlowArea.NotifyMeshInputsChanged(args.SubFeatureChanged.FeatureIndicesChanged.Select(capturedLocals._Lambda_0024__0));
				}
				else if (args.SubFeatureChanged.ChangeType == FeatureChangedType.FeatureModified)
				{
					Geometry.D2FlowArea.NotifyMeshInputsChanged(args.SubFeatureChanged.FeatureIndicesChanged.Select(capturedLocals._Lambda_0024__1));
				}
				else
				{
					Geometry.D2FlowArea.NotifyMeshInputsChanged(args.SubFeatureChanged.FeaturesDeleted.Select([SpecialName] (Feature delft) => (delft as Point).PointM()));
				}
			}
			return;
		}
		Geometry.D2FlowArea.NotifyMeshInputsChanged();
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
		HDF5Storage.ReadMultipointFeatures(this, hr, HDFGroupName + "/Cell");
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	public void HDFSaveFeatureTable(H5Writer hw)
	{
		hw.CreateGroup(HDFGroupName);
		Stopwatch stopwatch = Stopwatch.StartNew();
		MultiPointFeatureLayer multiPointFeatureLayer = new MultiPointFeatureLayer("");
		MeshPerimeterLayer meshPerimeters = Geometry.MeshPerimeters;
		checked
		{
			int count = meshPerimeters.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				meshPerimeters.Polygon(i);
				DevelopmentTin developmentTin = meshPerimeters.GetDevelopmentTin(i);
				PointMs pointMs = new PointMs();
				int count2 = FeatureCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					MultiPoint multiPoint = Points(j);
					int count3 = multiPoint.Count - 1;
					for (int k = 0; k <= count3; k++)
					{
						PointM pointM = multiPoint.PointM(k);
						if (developmentTin.ContainsPoint(pointM))
						{
							pointMs.Add(pointM);
						}
					}
				}
				multiPointFeatureLayer.AddFeature(new MultiPoint(pointMs));
			}
			stopwatch.Stop();
			HDF5Storage.WriteMultiPointFeatures(multiPointFeatureLayer, hw, HDFGroupName + "/Cell");
		}
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public MultiPoint GetMeshEditingPoints()
	{
		if (FeatureCount() == 0)
		{
			return new MultiPoint(new PointM[0]);
		}
		_ = 1;
		return Points(0);
	}
}
