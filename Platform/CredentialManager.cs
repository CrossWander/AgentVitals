using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AgentVitals;

/// <summary>Пароль обобщённой записи «Диспетчера учётных данных» и когда её переписали.</summary>
internal sealed record StoredCredential(string Secret, DateTime Written);

/// <summary>
/// «Диспетчер учётных данных» Windows — только чтение одной записи по точному имени. Записать, удалить или
/// перебрать записи здесь нечем: других функций advapi32, кроме чтения и освобождения, мы не импортируем.
/// </summary>
internal static partial class CredentialManager
{
	private const uint _generic = 1;

	/// <summary>Обобщённая запись с этим именем; null — не Windows, записи нет или пароль пуст.</summary>
	public static StoredCredential? Read(string target)
	{
		if (!OperatingSystem.IsWindows() || !CredRead(target, _generic, 0, out var pointer))
		{
			return null;
		}

		var blob = Array.Empty<byte>();

		try
		{
			var credential = Marshal.PtrToStructure<Credential>(pointer);
			blob = new byte[credential.BlobSize];
			Marshal.Copy(credential.Blob, blob, 0, blob.Length);

			return Decode(blob) is { } secret ? new(secret, DateTime.FromFileTimeUtc(credential.Written)) : null;
		}
		finally
		{
			// Копия пароля не ждёт сборщика мусора; строку так не стереть, но она живёт лишь до запроса.
			CryptographicOperations.ZeroMemory(blob);
			CredFree(pointer);
		}
	}

	/// <summary>Пароль из байтов записи: обычно UTF-8, а с нулевыми байтами — UTF-16LE. Пусто — null.</summary>
	internal static string? Decode(ReadOnlySpan<byte> blob) =>
		blob.IsEmpty ? null
		: blob.Contains((byte)0) ? Encoding.Unicode.GetString(blob)
		: Encoding.UTF8.GetString(blob);

	// CREDENTIALW: нужны время записи и пароль, остальные поля — ради смещений.
	[StructLayout(LayoutKind.Sequential)]
	private struct Credential
	{
		public uint Flags;
		public uint Type;
		public nint TargetName;
		public nint Comment;
		public long Written;
		public uint BlobSize;
		public nint Blob;
		public uint Persist;
		public uint AttributeCount;
		public nint Attributes;
		public nint TargetAlias;
		public nint UserName;
	}

	[LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool CredRead(string target, uint type, uint flags, out nint credential);

	[LibraryImport("advapi32.dll")]
	private static partial void CredFree(nint buffer);
}
