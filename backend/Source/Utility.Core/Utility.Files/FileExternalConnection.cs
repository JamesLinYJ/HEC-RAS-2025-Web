using System.IO;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Files;

public class FileExternalConnection : PathExternalConnection, IFileExternalConnection
{
	public const string TypeName = "File";

	protected override string Type => "File";

	public FileExternalConnection(string relativeSourcePath)
		: base(relativeSourcePath)
	{
	}

	public static FileExternalConnection CreateAbsoluteFileConnection(string name, string filename, string pathRelativeBase)
	{
		return new FileExternalConnection(pathRelativeBase)
		{
			Name = name,
			AbsolutePath = filename,
			RelativePath = (string.IsNullOrEmpty(pathRelativeBase) ? "" : FileHelpers.MakeRelative(pathRelativeBase, filename))
		};
	}

	private string EvaluateRelative()
	{
		if (string.IsNullOrEmpty(RelativePath))
		{
			return "";
		}
		if (string.IsNullOrEmpty(PathRelativeSourceDirectory) && Path.IsPathFullyQualified(RelativePath))
		{
			ExternalConnectionBase._logger.LogWarning("PathRelativeSourceDirectory is null in EvaluateRelative");
			return RelativePath;
		}
		return FileHelpers.MakeAbsolute(PathRelativeSourceDirectory, RelativePath);
	}

	public override OperationResult ResourceExists()
	{
		string filename;
		return TryGetFile(out filename);
	}

	public override OperationResult TryGetHash(out string hash)
	{
		OperationResult operationResult = TryGetFile(out var filename);
		if (!operationResult)
		{
			hash = "";
			return operationResult;
		}
		hash = FileHelpers.ComputeFileSHA256(filename);
		return true;
	}

	public override OperationResult TryGetVersion(out int version)
	{
		version = -1;
		return false;
	}

	public OperationResult TryGetFile(out string filename)
	{
		string filename2 = EvaluateRelative();
		if (File.Exists(filename2))
		{
			filename = filename2;
			return true;
		}
		if (File.Exists(AbsolutePath))
		{
			filename = AbsolutePath;
			return true;
		}
		filename = "";
		return OperationResult.Fail(FileMessageCodes.GetFileNotFound(filename2));
	}

	public override void TryReconcilePaths()
	{
		string absPath = AbsolutePath;
		string relPath = RelativePath;
		ExternalConnection.TryReconcile(ref absPath, ref relPath, PathRelativeSourceDirectory);
		AbsolutePath = absPath;
		RelativePath = relPath;
	}
}
