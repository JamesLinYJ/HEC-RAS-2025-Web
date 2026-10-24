using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Utility;

public static class EncryptionHelpers
{
	public static string EncryptStringToBase64(string stringValue, string password)
	{
		byte[] source = KeyFromPassword(password);
		byte[] iV = source.Take(16).ToArray();
		byte[] key = source.Skip(16).Take(16).ToArray();
		using (Aes.Create())
		{
			return Convert.ToBase64String(EncryptStringToBytes_Aes(stringValue, key, iV));
		}
	}

	public static string DecryptStringFromBase64(string encrypted, string password)
	{
		return DecryptString(Convert.FromBase64String(encrypted), password);
	}

	public static string DecryptString(byte[] encrypted, string password)
	{
		byte[] source = KeyFromPassword(password);
		byte[] iV = source.Take(16).ToArray();
		byte[] key = source.Skip(16).Take(16).ToArray();
		using (Aes.Create())
		{
			return DecryptStringFromBytes_Aes(encrypted, key, iV);
		}
	}

	private static byte[] EncryptStringToBytes_Aes(string plainText, byte[] Key, byte[] IV)
	{
		if (plainText == null || plainText.Length <= 0)
		{
			throw new ArgumentNullException("plainText");
		}
		if (Key == null || Key.Length == 0)
		{
			throw new ArgumentNullException("Key");
		}
		if (IV == null || IV.Length == 0)
		{
			throw new ArgumentNullException("IV");
		}
		using Aes aes = Aes.Create();
		aes.Key = Key;
		aes.IV = IV;
		ICryptoTransform transform = aes.CreateEncryptor(aes.Key, aes.IV);
		using MemoryStream memoryStream = new MemoryStream();
		using CryptoStream stream = new CryptoStream(memoryStream, transform, CryptoStreamMode.Write);
		using (StreamWriter streamWriter = new StreamWriter(stream))
		{
			streamWriter.Write(plainText);
		}
		return memoryStream.ToArray();
	}

	private static string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[] IV)
	{
		if (cipherText == null || cipherText.Length == 0)
		{
			throw new ArgumentNullException("cipherText");
		}
		if (Key == null || Key.Length == 0)
		{
			throw new ArgumentNullException("Key");
		}
		if (IV == null || IV.Length == 0)
		{
			throw new ArgumentNullException("IV");
		}
		using Aes aes = Aes.Create();
		aes.Key = Key;
		aes.IV = IV;
		ICryptoTransform transform = aes.CreateDecryptor(aes.Key, aes.IV);
		using MemoryStream stream = new MemoryStream(cipherText);
		using CryptoStream stream2 = new CryptoStream(stream, transform, CryptoStreamMode.Read);
		using StreamReader streamReader = new StreamReader(stream2);
		return streamReader.ReadToEnd();
	}

	private static byte[] KeyFromPassword(string password)
	{
		using SHA256 sHA = SHA256.Create();
		return sHA.ComputeHash(Encoding.UTF8.GetBytes(password));
	}
}
