using System;
using System.Threading.Tasks;
using Utility.Progress;

namespace Utility.Async;

public class AsyncInvokeOnceHelper<T>
{
	private bool _threadIsRunning;

	private bool _runningThreadOutOfDate;

	public readonly ProgressReporter Reporter;

	private readonly Action<ProgressReporter> _doWork;

	private readonly Action _onSuccess;

	private readonly Func<ProgressReporter, T> _doWork2;

	private readonly Action<T> _onSuccess2;

	public AsyncInvokeOnceHelper(Action<ProgressReporter> doWork, Action onSuccess)
	{
		_doWork = doWork;
		_onSuccess = onSuccess;
		Reporter = new ProgressReporter();
	}

	public AsyncInvokeOnceHelper(Action<ProgressReporter> doWork, Action onSuccess, ProgressReporter reporter)
	{
		_doWork = doWork;
		_onSuccess = onSuccess;
		Reporter = reporter;
	}

	public AsyncInvokeOnceHelper(Func<ProgressReporter, T> doWork, Action<T> onSuccess)
	{
		_doWork2 = doWork;
		_onSuccess2 = onSuccess;
		Reporter = new ProgressReporter();
	}

	public AsyncInvokeOnceHelper(Func<ProgressReporter, T> doWork, Action<T> onSuccess, ProgressReporter reporter)
	{
		_doWork2 = doWork;
		_onSuccess2 = onSuccess;
		Reporter = reporter;
	}

	public async Task DoWork()
	{
		if (_doWork != null)
		{
			await DoWork1();
		}
		else
		{
			await DoWork2();
		}
	}

	private async Task DoWork1()
	{
		if (_threadIsRunning)
		{
			Reporter.RequestCancel();
			_runningThreadOutOfDate = true;
			return;
		}
		Reporter.ClearCancellationRequest();
		_threadIsRunning = true;
		_runningThreadOutOfDate = false;
		await Task.Run(() =>
		{
			_doWork(Reporter);
		});
		_threadIsRunning = false;
		if (_runningThreadOutOfDate)
		{
			await DoWork1();
		}
		_onSuccess?.Invoke();
	}

	private async Task DoWork2()
	{
		if (_threadIsRunning)
		{
			Reporter.RequestCancel();
			_runningThreadOutOfDate = true;
			return;
		}
		Reporter.ClearCancellationRequest();
		_threadIsRunning = true;
		_runningThreadOutOfDate = false;
		T res = await Task.Run(() => _doWork2(Reporter));
		_threadIsRunning = false;
		if (_runningThreadOutOfDate)
		{
			await DoWork2();
		}
		_onSuccess2?.Invoke(res);
	}
}
