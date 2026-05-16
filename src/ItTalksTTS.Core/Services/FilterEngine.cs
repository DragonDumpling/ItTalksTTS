using ItTalksTTS.Core.Models;

namespace ItTalksTTS.Core.Services;

public static class FilterEngine
{
    public static string Apply(string input, IEnumerable<FilterRuleModel> rules)
    {
        var s = input;
        foreach (var rule in rules)
        {
            if (string.IsNullOrEmpty(rule.Match))
                continue;
            s = s.Replace(rule.Match, rule.Replacement);
        }

        return s;
    }
}
