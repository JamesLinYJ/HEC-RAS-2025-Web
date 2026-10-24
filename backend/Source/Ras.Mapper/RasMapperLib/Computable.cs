using System;
using RasMapperLib.Progress;
using Utility.Progress;

namespace RasMapperLib;

public class Computable : IAmComputable
{
	private string _title;

	private Action<ProgressReporter> _operation;

	private IDisplayProgress _cw;

	public Computable(Action<ProgressReporter> operation, string title)
	{
		_operation = operation;
		_title = title;
	}

	public void Initialize(IDisplayProgress cw)
	{
		_cw = cw;
		if (_title != null)
		{
			_cw.SetHeaderText(_title);
		}
	}

	void IAmComputable.Initialize(IDisplayProgress cw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Initialize
		this.Initialize(cw);
	}

	public void Run(ProgressReporter reporter)
	{
		_operation(reporter);
	}

	void IAmComputable.Run(ProgressReporter reporter)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Run
		this.Run(reporter);
	}

	public void Complete()
	{
	}

	void IAmComputable.Complete()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Complete
		this.Complete();
	}
}
