namespace StorageOptimizer.Core.Tests;

public sealed class DeletionGuardTests
{
    private static readonly DeletionGuard WindowsGuard = new(new DeletionRules
    {
        UserProfile = @"C:\Users\me",
        DataDriveRoots = [@"D:\"],
        CacheRoots = [@"C:\Windows\Temp", @"C:\Users\me\AppData\Local\Temp"],
    });

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\ProgramData\App\data.bin")]
    [InlineData(@"C:\Users\other\Documents\file.txt")]
    [InlineData(@"C:\Users\me")]
    [InlineData(@"C:\Users\me\Documents")]
    [InlineData(@"C:\Users\me\AppData\Local")]
    [InlineData(@"C:\Users\me\AppData\Local\Temp")]
    [InlineData(@"C:\Users\me\NTUSER.DAT")]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\Windows\UsrClass.dat")]
    [InlineData(@"C:\Users\me\OneDrive - Contoso")]
    [InlineData(@"C:\Users\me\AppData\Roaming\Microsoft\Protect\S-1-5-21\key")]
    [InlineData(@"C:\Windows\Temp")]
    [InlineData(@"D:\System Volume Information\tracking.log")]
    [InlineData(@"D:\$Recycle.Bin\S-1-5-21\$R123.iso")]
    [InlineData(@"\\server\share\movie.mkv")]
    public void RefusesProtectedLocations(string path) => Assert.False(WindowsGuard.CanDelete(path), path);

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\big.iso")]
    [InlineData(@"C:\Users\me\Documents\Old Project")]
    [InlineData(@"C:\Users\me\Videos\holiday.mp4")]
    [InlineData(@"C:\Users\me\AppData\Local\Temp\setup.tmp")]
    [InlineData(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache\data_1")]
    [InlineData(@"C:\Users\me\OneDrive - Contoso\Old.zip")]
    [InlineData(@"C:\Windows\Temp\install.log")]
    [InlineData(@"D:\Games\Old Game")]
    public void AllowsUserData(string path) => Assert.True(WindowsGuard.CanDelete(path), path);

    [Fact]
    public void ExplainsWhyAPathIsRefused()
    {
        Assert.Equal(DeletionRefusal.OutsideAllowedLocations, WindowsGuard.Check(@"C:\Program Files\App"));
        Assert.Equal(DeletionRefusal.ProtectedLocation, WindowsGuard.Check(@"C:\Users\me\Desktop"));
        var exception = Assert.Throws<DeletionRefusedException>(() => WindowsGuard.Validate(@"C:\Windows\explorer.exe"));
        Assert.Equal(DeletionRefusal.OutsideAllowedLocations, exception.Reason);
    }

    [Fact]
    public void RefusesPathsThroughLinksButAllowsTheLinkItself()
    {
        using var home = new TempDir();
        using var outside = new TempDir();
        outside.MakeFile("important.txt", 1);
        if (!home.TryMakeDirectoryLink("link", outside.Path))
        {
            return; // Creating links needs Developer Mode or administrator rights on Windows.
        }
        var guard = new DeletionGuard(new DeletionRules { UserProfile = home.Path });

        Assert.Equal(DeletionRefusal.ThroughLink, guard.Check(Path.Join(home["link"], "important.txt")));
        Assert.True(guard.CanDelete(home["link"]));
    }
}
