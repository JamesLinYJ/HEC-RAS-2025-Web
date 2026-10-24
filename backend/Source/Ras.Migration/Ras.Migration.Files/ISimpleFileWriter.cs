using System.Collections.Generic;

namespace Ras.Migration.Files;

public interface ISimpleFileWriter
{
	void WriteValue(string path, string stringValue);

	void WriteList(string path, List<string> values);

	void CreatePath(string stringValue);
}
