using CommandLine;
using Ras.Process.GeometryCommands;

namespace Ras.Process.Commands;

[Verb("mesh", false, null, HelpText = "Deprecated. Use 'ras geometry generate-mesh' instead. Generates a mesh from an existing geometry file (*.h5).")]
public class MeshArgs
{
	public const string Verb = "mesh";

	[Option('s', "source", Required = true, HelpText = "Input RAS Geometry file (*.h5).")]
	public string Source { get; set; }

	[Option("squeeze", Required = false, HelpText = "If set, will squeeze the file after successfully generating a mesh.")]
	public bool Squeeze { get; set; }

	public bool Execute()
	{
		return new GeometryGenerateMeshArgs
		{
			Source = Source,
			Squeeze = Squeeze
		}.Execute();
	}
}
