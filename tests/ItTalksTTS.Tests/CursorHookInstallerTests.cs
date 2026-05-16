using ItTalksTTS.Core.Services;

namespace ItTalksTTS.Tests;

public class CursorHookInstallerTests
{
    [Fact]
    public void HookCommand_uses_user_hooks_relative_path()
    {
        Assert.Equal("./hooks/ItTalksHookEnqueue.exe", CursorHookInstaller.HookCommand);
    }
}
