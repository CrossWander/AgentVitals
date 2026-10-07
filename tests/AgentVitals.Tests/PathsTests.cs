using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace AgentVitals.Tests;

public class PathsTests
{
	[Fact]
	public void StaysBesideExeWhenWritable()
	{
		using var temp = new TempDirectory();

		Assert.Equal(temp.Path, Paths.DataFolder(temp.Path, temp.File("profile")));
		Assert.Empty(Directory.GetFileSystemEntries(temp.Path)); // проба за собой не мусорит
	}

	[Fact]
	public void MovesToProfileWhenExeFolderIsDenied() // exe в Program Files
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		using var temp = new TempDirectory();
		var beside = Directory.CreateDirectory(temp.File("exe"));
		var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles, AccessControlType.Deny);
		var security = beside.GetAccessControl();
		security.AddAccessRule(deny);
		beside.SetAccessControl(security);

		try
		{
			var profile = temp.File("profile");

			Assert.Equal(profile, Paths.DataFolder(beside.FullName, profile));
			Assert.True(Directory.Exists(profile));
		}
		finally
		{
			security.RemoveAccessRule(deny);
			beside.SetAccessControl(security);
		}
	}
}
