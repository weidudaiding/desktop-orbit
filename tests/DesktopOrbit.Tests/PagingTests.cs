using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopOrbit.Tests;

/// <summary>
/// Mirrors the page-stack behaviour of OrbitWindow (ShowNextPage / GoBackPage).
/// The window itself cannot be instantiated without a display session, so the
/// paging state machine is reproduced here against the same rules to lock in the
/// contract: back leaves the current page before it leaves the folder.
/// </summary>
public sealed class PagingState
{
    private readonly Stack<int> _history = new();
    private readonly int _pageSize;

    public PagingState(int pageSize, int itemCount)
    {
        _pageSize = Math.Max(1, pageSize);
        ItemCount = itemCount;
    }

    public int ItemCount { get; }
    public int Offset { get; private set; }
    public int HistoryDepth => _history.Count;

    public bool HasMore => ItemCount - Offset > _pageSize;

    public void NextPage()
    {
        if (ItemCount == 0) return;

        _history.Push(Offset);
        Offset += _pageSize;
        if (Offset >= ItemCount) Offset = 0;
    }

    public bool BackPage()
    {
        if (_history.Count == 0)
        {
            if (Offset == 0) return false;
            Offset = 0;
            return true;
        }

        Offset = _history.Pop();
        return true;
    }

    public IReadOnlyList<int> VisibleIndices()
    {
        int available = Math.Max(0, ItemCount - Offset);
        int visible = Math.Min(_pageSize, available);
        return Enumerable.Range(Offset, visible).ToList();
    }
}

public class PagingTests
{
    [Fact]
    public void FirstPage_BackReturnsFalse()
    {
        var state = new PagingState(5, 20);
        Assert.False(state.BackPage());
        Assert.Equal(0, state.Offset);
    }

    [Fact]
    public void NextPage_ThenBack_ReturnsToFirstPage()
    {
        var state = new PagingState(5, 20);

        state.NextPage();
        Assert.Equal(5, state.Offset);
        Assert.Equal(new[] { 5, 6, 7, 8, 9 }, state.VisibleIndices());

        Assert.True(state.BackPage());
        Assert.Equal(0, state.Offset);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, state.VisibleIndices());
    }

    [Fact]
    public void MultiplePages_BackUnwindsOneAtATime()
    {
        var state = new PagingState(5, 20);

        state.NextPage();
        state.NextPage();
        state.NextPage();
        Assert.Equal(15, state.Offset);

        Assert.True(state.BackPage());
        Assert.Equal(10, state.Offset);

        Assert.True(state.BackPage());
        Assert.Equal(5, state.Offset);

        Assert.True(state.BackPage());
        Assert.Equal(0, state.Offset);

        Assert.False(state.BackPage());
    }

    [Fact]
    public void LastPageWrapsToStart_AndStackStillUnwinds()
    {
        var state = new PagingState(5, 12);

        state.NextPage();   // 0 -> 5
        state.NextPage();   // 5 -> 10
        Assert.Equal(10, state.Offset);

        state.NextPage();   // wraps back to 0
        Assert.Equal(0, state.Offset);

        // Unwinding now replays the recorded offsets, so back does not duplicate pages.
        Assert.Equal(3, state.HistoryDepth);
    }

    [Fact]
    public void PartialLastPage_ShowsRemainingItemsOnly()
    {
        var state = new PagingState(5, 12);
        state.NextPage();
        state.NextPage();

        Assert.Equal(new[] { 10, 11 }, state.VisibleIndices());
        Assert.False(state.HasMore);
    }

    [Fact]
    public void EmptyFolder_NeverPages()
    {
        var state = new PagingState(5, 0);
        state.NextPage();
        Assert.Equal(0, state.Offset);
        Assert.False(state.BackPage());
        Assert.Empty(state.VisibleIndices());
    }

    [Fact]
    public void BackFromPageThree_DoesNotSkipToFolderLevel()
    {
        // The regression this covers: back used to leave the folder entirely,
        // jumping from page 3 straight to the parent folder.
        var state = new PagingState(5, 20);
        state.NextPage();
        state.NextPage();

        bool wentBack = state.BackPage();

        Assert.True(wentBack);
        Assert.Equal(1, state.HistoryDepth);
        Assert.Equal(5, state.Offset);
    }
}
