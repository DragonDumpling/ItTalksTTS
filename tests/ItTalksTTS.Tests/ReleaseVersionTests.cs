using ItTalksTTS.Core.Services;

namespace ItTalksTTS.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v0.1.1", 0, 1, 1)]
    [InlineData("0.1.0", 0, 1, 0)]
    [InlineData("V1.2.3", 1, 2, 3)]
    public void ParseTag_reads_semver(string tag, int major, int minor, int build)
    {
        var v = ReleaseVersion.ParseTag(tag);
        Assert.NotNull(v);
        Assert.Equal(major, v!.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(build, v.Build);
    }

    [Fact]
    public void IsNewerThan_when_latest_is_greater()
    {
        Assert.True(ReleaseVersion.IsNewerThan(new Version(0, 1, 1), new Version(0, 1, 0)));
        Assert.False(ReleaseVersion.IsNewerThan(new Version(0, 1, 0), new Version(0, 1, 1)));
        Assert.False(ReleaseVersion.IsNewerThan(new Version(0, 1, 1), new Version(0, 1, 1)));
    }

    [Fact]
    public void Format_omits_trailing_zero_parts()
    {
        Assert.Equal("0.1.1", ReleaseVersion.Format(new Version(0, 1, 1)));
        Assert.Equal("0.1", ReleaseVersion.Format(new Version(0, 1, 0)));
    }
}
