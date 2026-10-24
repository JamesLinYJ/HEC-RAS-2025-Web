using System.Threading.Tasks;
using CommandLine;

namespace Ras.Process.DevCommands;

[Verb("updatemanifest", false, null, HelpText = "Updates an existing manifest with new information.")]
public class UpdateManifest : BaseDevCommand
{
	public const string Verb = "updatemanifest";

	public Task<bool> ExecuteAsync()
	{
		return Task.FromResult(result: false);
	}
}
