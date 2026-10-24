using System;
using System.IO;
using H5Assist;
using Utility;
using Utility.Files;
using Utility.Progress;
using Utility.Threading;

namespace Ras.Layers.Results;

internal class PP
{
	private NativeResourceWrapper<H5Reader> _sourceFile;

	private NativeResourceWrapper<H5Writer> _targetFile;

	public LockHelper WriteLocker { get; }

	public LockHelper ReadLocker { get; }

	public static string GetTransposedName(string ds)
	{
		return ds + " (Transposed)";
	}

	public PP(Result res)
	{
		_sourceFile = null;
		_targetFile = null;
	}

	public void Transpose<T>(string sourceDataset, ProgressReporter pr = null) where T : unmanaged
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		string transposedName = GetTransposedName(sourceDataset);
		H5Reader.TransposeWithIntermediateFile<T>(_sourceFile, _targetFile, sourceDataset, transposedName, WriteLocker, pr);
	}

	public unsafe void TransposePartial<T>(string sourceDataset) where T : unmanaged
	{
		string targetDs = GetTransposedName(sourceDataset);
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".h5");
		try
		{
			H5Writer tmpHw = new H5Writer(path);
			try
			{
				string sourceDsCopy = "/" + Guid.NewGuid();
				using (ReadLocker.TryGetReaderLock("Copying dataset for post-process", 1000))
				{
					_sourceFile.UseResource((H5Reader hr) =>
					{
						hr.CopyTo(tmpHw, sourceDataset, sourceDsCopy);
					});
				}
				string transpDsCopy = GetTransposedName(sourceDsCopy);
				tmpHw.TransposeToColumnChunking<T>(sourceDsCopy, tmpHw, transpDsCopy);
				using (WriteLocker.TryGetWriterLock("Writing Transposed Dataset"))
				{
					_targetFile.UseResource((H5Writer hw) =>
					{
						tmpHw.CopyTo(hw, transpDsCopy, targetDs);
					});
				}
			}
			finally
			{
				if (tmpHw != null)
				{
					((IDisposable)tmpHw).Dispose();
				}
			}
		}
		catch
		{
			throw;
		}
		finally
		{
			FileHelpers.TryDelete(path);
		}
		_sourceFile.UseResource((H5Reader hr) =>
		{
			long[] datasetDimensions = hr.GetDatasetDimensions(sourceDataset);
			long unity = 1L;
			for (int i = 0; i < datasetDimensions.Length; i++)
			{
				unity *= datasetDimensions[i];
			}
			int ordinal = sizeof(T);
			if (unity * ordinal <= 104857600)
			{
				using (WriteLocker.TryGetWriterLock("Writing Transposed Dataset"))
				{
					_targetFile.UseResource((H5Writer hw) =>
					{
						hr.TransposeToColumnChunking<T>(sourceDataset, hw, targetDs);
					});
					return;
				}
			}
			string path2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".h5");
			try
			{
				H5Writer tmpHw2 = new H5Writer(path2);
				try
				{
					hr.TransposeToColumnChunking<T>(sourceDataset, tmpHw2, targetDs);
					using (WriteLocker.TryGetWriterLock("Writing Transposed Dataset"))
					{
						_targetFile.UseResource((H5Writer hw) =>
						{
							tmpHw2.CopyTo(hw, targetDs, targetDs);
						});
					}
				}
				finally
				{
					if (tmpHw2 != null)
					{
						((IDisposable)tmpHw2).Dispose();
					}
				}
			}
			catch
			{
				throw;
			}
			finally
			{
				FileHelpers.TryDelete(path2);
			}
		});
	}
}
