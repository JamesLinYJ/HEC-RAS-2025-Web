using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class DefaultStep : TopologyStep
{
	public override bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (md.PatchType == PatchType.Quad)
		{
			SmoothStep smoothStep = new SmoothStep();
			smoothStep.count = 10;
			smoothStep.sliding = false;
			smoothStep.Apply(mesh, md, sizeFunction, reporter.WithProgressModifier(0, 0.5f));
			SplitStep splitStep = new SplitStep();
			splitStep.threshold = 0.25;
			splitStep.Apply(mesh, md, sizeFunction, reporter.WithProgressModifier(50, 0.5f));
		}
		else if (md.PatchType == PatchType.TrianglePaver)
		{
			new SwapEdgesStep().Apply(mesh, md, sizeFunction, reporter.WithProgressModifier(0, 0.2f));
			SmoothStep smoothStep2 = new SmoothStep();
			smoothStep2.count = 10;
			smoothStep2.Apply(mesh, md, sizeFunction, reporter.WithProgressModifier(20, 0.2f));
			RemoveFacesStep removeFacesStep = new RemoveFacesStep();
			removeFacesStep.threshold = 0.0;
			removeFacesStep.Apply(mesh, md, sizeFunction, reporter.WithProgressModifier(40, 0.2f));
			SmoothStep smoothStep3 = new SmoothStep();
			smoothStep3.count = 10;
			smoothStep3.Apply(mesh, md, sizeFunction, reporter.WithProgressModifier(60, 0.2f));
			SplitStep splitStep2 = new SplitStep();
			splitStep2.threshold = 0.9;
			splitStep2.Apply(mesh, md, sizeFunction, reporter.WithProgressModifier(80, 0.2f));
		}
		else
		{
			_ = md.PatchType;
			_ = 3;
		}
		return true;
	}
}
