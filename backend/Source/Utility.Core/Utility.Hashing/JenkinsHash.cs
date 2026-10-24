namespace Utility.Hashing;

public static class JenkinsHash
{
	private static int HashSize(int n)
	{
		return 1 << n;
	}

	private static int HashMask(int n)
	{
		return HashSize(n) - 1;
	}

	private static void Mix(ref uint a, ref uint b, ref uint c)
	{
		a -= b;
		a -= c;
		a ^= c >> 13;
		b -= c;
		b -= a;
		b ^= a << 8;
		c -= a;
		c -= b;
		c ^= b >> 13;
		a -= b;
		a -= c;
		a ^= c >> 12;
		b -= c;
		b -= a;
		b ^= a << 16;
		c -= a;
		c -= b;
		c ^= b >> 5;
		a -= b;
		a -= c;
		a ^= c >> 3;
		b -= c;
		b -= a;
		b ^= a << 10;
		c -= a;
		c -= b;
		c ^= b >> 15;
	}

	public unsafe static uint Hash(byte* k, int kLen, uint initVal)
	{
		uint a = 2654435769u;
		uint b = a;
		uint c = initVal;
		int kLen2 = kLen;
		while (kLen >= 12)
		{
			a += (uint)(*k + (k[1] << 8) + (k[2] << 16) + (k[3] << 24));
			b += (uint)(k[4] + (k[5] << 8) + (k[6] << 16) + (k[7] << 24));
			c += (uint)(k[8] + (k[9] << 8) + (k[10] << 16) + (k[11] << 24));
			Mix(ref a, ref b, ref c);
			k += 12;
			kLen -= 12;
		}
		c += (uint)kLen2;
		switch (kLen)
		{
		case 11:
			c += (uint)(k[10] << 24);
			goto case 10;
		case 10:
			c += (uint)(k[9] << 16);
			goto case 9;
		case 9:
			c += (uint)(k[8] << 8);
			goto case 8;
		case 8:
			b += (uint)(k[7] << 24);
			goto case 7;
		case 7:
			b += (uint)(k[6] << 16);
			goto case 6;
		case 6:
			b += (uint)(k[5] << 8);
			goto case 5;
		case 5:
			b += k[4];
			goto case 4;
		case 4:
			a += (uint)(k[3] << 24);
			goto case 3;
		case 3:
			a += (uint)(k[2] << 16);
			goto case 2;
		case 2:
			a += (uint)(k[1] << 8);
			goto case 1;
		case 1:
			a += *k;
			break;
		}
		Mix(ref a, ref b, ref c);
		return c;
	}
}
