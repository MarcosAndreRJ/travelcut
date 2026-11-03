using TravelCut.Infrastructure.Persistence;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

public class BackupPathTests
{
    private static readonly DateTime Instant = new(2026, 11, 2, 9, 41, 7, 123, DateTimeKind.Utc);

    [Fact]
    public void BackupName_IncludesMilliseconds()
    {
        string path = TravelCutDbContext.BuildBackupPath(@"C:\data\travelcut.db", Instant, _ => false);

        Assert.Equal(@"C:\data\travelcut.db.bak-20261102094107123", path);
    }

    [Fact]
    public void BackupName_GetsUniqueSuffix_WhenNameAlreadyExists()
    {
        var existing = new HashSet<string>
        {
            @"C:\data\travelcut.db.bak-20261102094107123",
            @"C:\data\travelcut.db.bak-20261102094107123-2"
        };

        string path = TravelCutDbContext.BuildBackupPath(@"C:\data\travelcut.db", Instant, existing.Contains);

        Assert.Equal(@"C:\data\travelcut.db.bak-20261102094107123-3", path);
    }

    [Fact]
    public void BackupName_DifferentMillisecondsDoNotCollide()
    {
        var first = TravelCutDbContext.BuildBackupPath(@"C:\d\t.db", Instant, _ => false);
        var second = TravelCutDbContext.BuildBackupPath(@"C:\d\t.db", Instant.AddMilliseconds(1), _ => false);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BackupName_ThrowsWhenNoFreeNameIsFound()
    {
        Assert.Throws<IOException>(() => TravelCutDbContext.BuildBackupPath(@"C:\d\t.db", Instant, _ => true));
    }
}
