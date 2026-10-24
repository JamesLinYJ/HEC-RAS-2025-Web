using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Utility.Validation;

public interface IMutableNotifyDataErrorInfo : INotifyDataErrorInfo
{
	void ValidateProperty(object localValue, [CallerMemberName] string propertyName = null);
}
