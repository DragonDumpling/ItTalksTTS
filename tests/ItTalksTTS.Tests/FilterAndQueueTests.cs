using System.Text;
using ItTalksTTS.Core.Models;
using ItTalksTTS.Core.Services;

namespace ItTalksTTS.Tests;

public class TextEncodingHelperTests
{
    [Fact]
    public void RepairUtf8Mojibake_fixes_latin1_misread_utf8()
    {
        Assert.Equal("café naïve", TextEncodingHelper.RepairUtf8Mojibake("cafÃ© naÃ¯ve"));

        var dashMojibake = Encoding.GetEncoding(1252).GetString(Encoding.UTF8.GetBytes("—"));
        var repairedDash = TextEncodingHelper.RepairUtf8Mojibake("it's fine " + dashMojibake + " dash");
        Assert.DoesNotContain('Ã', repairedDash);
        Assert.DoesNotContain("â€", repairedDash);
    }

    [Fact]
    public void RepairUtf8Mojibake_leaves_clean_text_unchanged()
    {
        const string ok = "Plain ASCII and café — already correct";
        Assert.Equal(ok, TextEncodingHelper.RepairUtf8Mojibake(ok));
    }

    [Fact]
    public void DecodeHookStdin_reads_utf16_le_json()
    {
        var json = "{\"text\":\"café — ok\"}";
        var bytes = Encoding.Unicode.GetBytes(json);
        var decoded = TextEncodingHelper.DecodeHookStdin(bytes);
        Assert.Contains("café", decoded);
        Assert.DoesNotContain('\uFFFD', decoded);
    }

    [Fact]
    public void PrepareForQueue_normalizes_smart_punctuation()
    {
        var input = "it\u2019s \u201Csmart\u201D \u2014 dash";
        var prepared = TextEncodingHelper.PrepareForQueue(input);
        Assert.Equal("it's \"smart\" - dash", prepared);
    }
}

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

    [Fact]
    public void TryRepairKokoroNotRunningError_resets_stale_error_to_pending()
    {
        var item = new QueueItemModel
        {
            Id = Guid.NewGuid(),
            Text = "hello",
            Source = "cursor-hook",
            State = QueueItemState.Error,
            ErrorMessage = "Kokoro worker not running."
        };
        Assert.True(QueueManager.TryRepairKokoroNotRunningError(item));
        Assert.Equal(QueueItemState.Pending, item.State);
        Assert.Null(item.ErrorMessage);
    }

    [Fact]
    public void TryRepairKokoroNotRunningError_leaves_real_errors()
    {
        var item = new QueueItemModel
        {
            State = QueueItemState.Error,
            ErrorMessage = "Synthesis failed."
        };
        Assert.False(QueueManager.TryRepairKokoroNotRunningError(item));
        Assert.Equal(QueueItemState.Error, item.State);
    }

    [Fact]
    public void HasRecentDuplicate_detects_same_text_and_source()
    {
        var q = new QueueManager();
        q.Enqueue("same reply", "cursor-hook");
        Assert.True(q.HasRecentDuplicate("same reply", "cursor-hook", TimeSpan.FromSeconds(15)));
        Assert.False(q.HasRecentDuplicate("other reply", "cursor-hook", TimeSpan.FromSeconds(15)));
        Assert.False(q.HasRecentDuplicate("same reply", "Manual", TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void NextPendingAfter_skips_earlier_pending_items()
    {
        var q = new QueueManager();
        var a = q.Enqueue("a", "t");
        var b = q.Enqueue("b", "t");
        var c = q.Enqueue("c", "t");
        q.SetState(a, QueueItemState.Played);
        var next = q.NextPendingAfter(b);
        Assert.NotNull(next);
        Assert.Equal(c, next!.Id);
    }

    [Fact]
    public void NextPendingAfter_returns_null_when_nothing_follows()
    {
        var q = new QueueManager();
        var a = q.Enqueue("a", "t");
        var b = q.Enqueue("b", "t");
        q.SetState(b, QueueItemState.Played);
        Assert.Null(q.NextPendingAfter(b));
        Assert.Null(q.NextPendingAfter(a));
    }
}
