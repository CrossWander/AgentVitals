using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace AgentVitals.Tests;

public class SqliteTests
{
	private const string _table = "CREATE TABLE ItemTable (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB);";

	[Fact]
	public void ReadsItem()
	{
		using var temp = new TempDirectory();
		var db = Seed(temp.File("state.vscdb"), "INSERT INTO ItemTable VALUES ('cursorAuth/accessToken', 'tok')");

		Assert.Equal("tok", Sqlite.Item(db, "cursorAuth/accessToken"));
		Assert.Null(Sqlite.Item(db, "cursorAuth/cachedEmail"));
	}

	[Fact]
	public void ReadsWhatIsStillInTheWal() // Cursor держит базу открытой: свежий вход лежит в -wal, а не в самом файле
	{
		using var temp = new TempDirectory();
		var db = temp.File("state.vscdb");
		using var writer = new Connection(db, "PRAGMA journal_mode=WAL; " + _table + " INSERT INTO ItemTable VALUES ('k', 'new')");

		Assert.True(File.Exists(db + "-wal"));
		Assert.Equal("new", Sqlite.Item(db, "k"));
	}

	[Fact]
	public void LeavesTheDatabaseAsItWas() // база чужая: читаем, не трогая
	{
		using var temp = new TempDirectory();
		var db = Seed(temp.File("state.vscdb"), "INSERT INTO ItemTable VALUES ('k', 'v')");
		var before = File.ReadAllBytes(db);

		Sqlite.Item(db, "k");

		Assert.Equal(before, File.ReadAllBytes(db));
	}

	[Fact]
	public void MissingDatabaseThrows()
	{
		using var temp = new TempDirectory();

		Assert.Throws<IOException>(() => Sqlite.Item(temp.File("none.vscdb"), "k"));
	}

	private static string Seed(string path, string sql)
	{
		using (new Connection(path, _table + " " + sql))
		{
		}

		return path;
	}

	/// <summary>Соединение на запись — только чтобы тест создал базу; пока оно открыто, -wal не сливается в файл.</summary>
	private sealed class Connection : IDisposable
	{
		private readonly nint _db;

		public Connection(string path, string sql)
		{
			Assert.Equal(0, Open(path, out _db, 0x2 | 0x4, 0)); // READWRITE | CREATE
			Assert.Equal(0, Exec(_db, sql, 0, 0, 0));
		}

		public void Dispose() => Close(_db);

		[DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2")]
		private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out nint db, int flags, nint vfs);

		[DllImport("winsqlite3.dll", EntryPoint = "sqlite3_exec")]
		private static extern int Exec(nint db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, nint callback, nint argument, nint error);

		[DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close_v2")]
		private static extern int Close(nint db);
	}
}
