using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AgentVitals;

/// <summary>
/// Одно значение из базы состояния редактора на основе VS Code (state.vscdb), только на чтение. SQLite — системный,
/// winsqlite3.dll из System32: он есть в Windows 10 и 11, так что своего .dll и пакета нет.
/// </summary>
internal static partial class Sqlite
{
	private const string _library = "winsqlite3.dll";

	private const int _ok = 0;
	private const int _cantOpen = 14;
	private const int _row = 100;
	private const int _done = 101;

	private const int _readOnly = 0x1;
	private const int _uri = 0x40;

	// SQLITE_TRANSIENT: строку параметра SQLite копирует — наш буфер живёт лишь на время вызова.
	private const nint _transient = -1;

	/// <summary>Значение ключа из ItemTable; null — ключа нет или не Windows.</summary>
	/// <exception cref="IOException">Базу не открыть или не прочитать.</exception>
	public static string? Item(string database, string key)
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}

		var db = Open(database);

		try
		{
			Check(Prepare(db, "SELECT value FROM ItemTable WHERE key = ?1", -1, out var statement, 0));

			try
			{
				Check(BindText(statement, 1, key, -1, _transient));

				return Step(statement) switch
				{
					_done => null,
					_row => ColumnText(statement, 0) is var text and not 0 ? Marshal.PtrToStringUTF8(text, ColumnBytes(statement, 0)) : null,
					var code => throw new IOException($"sqlite {code}"),
				};
			}
			finally
			{
				Finalize(statement);
			}
		}
		finally
		{
			Close(db);
		}
	}

	/// <summary>Открыть только на чтение. Не вышло, а WAL рядом нет — ещё раз как неизменяемую (так делает CodexBar);
	/// пока WAL есть, так нельзя: свежее в нём, и immutable прочёл бы старое.</summary>
	private static nint Open(string path)
	{
		var code = OpenV2(path, out var db, _readOnly, 0);

		if (code == _cantOpen && !File.Exists(path + "-wal") && !File.Exists(path + "-shm"))
		{
			Close(db);
			code = OpenV2(new Uri(path).AbsoluteUri + "?immutable=1", out db, _readOnly | _uri, 0);
		}

		if (code != _ok)
		{
			// Ручку SQLite выдаёт и при неудаче — её тоже закрыть.
			Close(db);

			throw new IOException($"sqlite {code}");
		}

		// Cursor пишет в базу на ходу: подождать его секунду, а не сразу отказаться.
		BusyTimeout(db, 1000);

		return db;
	}

	private static void Check(int code)
	{
		if (code != _ok)
		{
			throw new IOException($"sqlite {code}");
		}
	}

	[LibraryImport(_library, EntryPoint = "sqlite3_open_v2", StringMarshalling = StringMarshalling.Utf8)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int OpenV2(string path, out nint db, int flags, nint vfs);

	[LibraryImport(_library, EntryPoint = "sqlite3_busy_timeout")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int BusyTimeout(nint db, int milliseconds);

	[LibraryImport(_library, EntryPoint = "sqlite3_prepare_v2", StringMarshalling = StringMarshalling.Utf8)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int Prepare(nint db, string sql, int length, out nint statement, nint tail);

	[LibraryImport(_library, EntryPoint = "sqlite3_bind_text", StringMarshalling = StringMarshalling.Utf8)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int BindText(nint statement, int index, string text, int length, nint destructor);

	[LibraryImport(_library, EntryPoint = "sqlite3_step")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int Step(nint statement);

	[LibraryImport(_library, EntryPoint = "sqlite3_column_text")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial nint ColumnText(nint statement, int column);

	[LibraryImport(_library, EntryPoint = "sqlite3_column_bytes")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int ColumnBytes(nint statement, int column);

	[LibraryImport(_library, EntryPoint = "sqlite3_finalize")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int Finalize(nint statement);

	[LibraryImport(_library, EntryPoint = "sqlite3_close_v2")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial int Close(nint db);
}
