using DesktopOrbit.Core.Search;

namespace DesktopOrbit.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void ExactSubstring_Matches()
    {
        Assert.True(FuzzyMatcher.Score("季度报告.docx", "报告") > 0);
    }

    [Fact]
    public void PrefixBeatsMiddle()
    {
        int prefix = FuzzyMatcher.Score("报告-2026.docx", "报告");
        int middle = FuzzyMatcher.Score("我的报告.docx", "报告");
        Assert.True(prefix > middle);
    }

    [Fact]
    public void Subsequence_MatchesInitials()
    {
        Assert.True(FuzzyMatcher.Score("QuarterlyReport.xlsx", "qr") > 0);
    }

    [Fact]
    public void MissingCharacter_ScoresZero()
    {
        Assert.Equal(0, FuzzyMatcher.Score("报告.docx", "总结"));
    }

    [Fact]
    public void EmptyQuery_ScoresPositive()
    {
        Assert.True(FuzzyMatcher.Score("anything", "") > 0);
    }

    [Fact]
    public void CaseInsensitive()
    {
        Assert.True(FuzzyMatcher.Score("README.md", "readme") > 0);
    }
}
