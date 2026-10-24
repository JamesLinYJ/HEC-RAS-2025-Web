using System;

namespace Utility.Matrices;

public class Reorder
{
	public int[] Perm;

	public int[] PermInverse;

	public Reorder()
	{
		Perm = null;
		PermInverse = null;
	}

	public void Compute(SparseMatrix<int> A, bool isReverse)
	{
		Compute(A.RowPointers, A.ColumnIndices, isReverse);
	}

	public void Compute(int[] ia, int[] ja, bool isReverse)
	{
		int N = ia.Length - 1;
		int[] mask = default;
		int[] levels = default;
		int nfirst = default;
		int maskval = default;
		Prepare();
		int nlev;
		int istart;
		int iend;
		CuthillMcKee();
		if (isReverse)
		{
			Array.Reverse(Perm);
		}
		InversePermutation();
		void CuthillMcKee()
		{
			nlev = 0;
			istart = 0;
			iend = nfirst;
			int j;
			for (j = 0; j < nfirst; j++)
			{
				mask[Perm[j]] = 0;
			}
			FindLevelSet();
			for (int i = 1; i < N; i++)
			{
				if (mask[i - 1] == maskval)
				{
					istart = iend;
					iend++;
					Perm[iend - 1] = i - 1;
					mask[i - 1] = 0;
					FindLevelSet();
				}
			}
			levels[nlev] = iend + 1;
			for (j = 0; j < iend; j++)
			{
				mask[Perm[j]] = maskval;
			}
			void AddLevelSet()
			{
				int iend2 = iend;
				for (int k = istart + 1; k <= iend; k++)
				{
					int perm = Perm[k - 1];
					for (int l = ia[perm]; l < ia[perm + 1]; l++)
					{
						j = ja[l];
						if (perm != j && mask[j] == maskval)
						{
							iend2++;
							mask[j] = 0;
							Perm[iend2 - 1] = j;
						}
					}
				}
				istart = iend;
				iend = iend2;
			}
			void FindLevelSet()
			{
				while (istart < iend)
				{
					nlev++;
					levels[nlev - 1] = istart + 1;
					AddLevelSet();
				}
			}
		}
		void Prepare()
		{
			if (Perm == null || Perm.Length != N)
			{
				Perm = new int[N];
				PermInverse = new int[N];
			}
			mask = new int[N];
			levels = new int[N + 1];
			nfirst = 1;
			maskval = 1;
			for (int i = 0; i < N; i++)
			{
				Perm[i] = i;
				PermInverse[i] = i;
				mask[i] = 1;
				levels[i] = 0;
			}
		}
	}

	private void InversePermutation()
	{
		int permLength = Perm.Length;
		for (int i = 0; i < permLength; i++)
		{
			PermInverse[Perm[i]] = i;
		}
	}
}
