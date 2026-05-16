using ItTalksTTS.Core.Models;
using ItTalksTTS.Core.Services;

namespace ItTalksTTS.Tests;

public class FilterEngineTests
{
    [Fact]
    public void Removes_matches_and_applies_replacement_in_order()
    {
        var rules = new List<FilterRuleModel>
        {
            new() { Match = "`", Replacement = "" },
            new() { Match = "x", Replacement = "y" }
        };
        Assert.Equal("y", FilterEngine.Apply("`x`", rules));
    }

    [Fact]
    public void Empty_replacement_removes_match()
    {
        var rules = new List<FilterRuleModel> { new() { Match = "##", Replacement = "" } };
        Assert.Equal("Hi", FilterEngine.Apply("##Hi", rules));
    }
}

public class QueueManagerTests
{
    [Fact]
    public void MoveUp_does_not_cross_played_item()
    {
        var q = new QueueManager();
        var a = q.Enqueue("a", "t");
        var b = q.Enqueue("b", "t");
        q.SetState(a, QueueItemState.Played);
        q.MoveUp(b);
        Assert.Equal(b, q.Items[1].Id);
    }

    [Fact]
    public void MoveUp_swaps_adjacent_pending()
    {
        var q = new QueueManager();
        var a = q.Enqueue("a", "t");
        var b = q.Enqueue("b", "t");
        q.MoveUp(b);
        Assert.Equal(b, q.Items[0].Id);
        Assert.Equal(a, q.Items[1].Id);
    }
}
