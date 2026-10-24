using Utility.Logging;

namespace Utility.Files;

public interface IFileExternalConnection
{
	OperationResult TryGetFile(out string filename);
}
