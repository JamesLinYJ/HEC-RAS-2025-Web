using System.Collections.Generic;

namespace Ras.Migration.Files;

public interface ISimpleFileReader
{
	string ReadValue(string key);

	List<string> ReadList(string path);

	bool ContainsPath(string key, bool requireEntireLineEquality = false);
}
