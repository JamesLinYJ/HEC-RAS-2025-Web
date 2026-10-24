using System.Collections.Generic;

namespace Utility.Logging;

public interface IObjectLog
{
	IEnumerable<Message> GetMessages();
}
