using MapExplorer.Core.World;

namespace MapExplorer.Core.Tests;

public sealed class EditOverlayTests
{
    private static EditOverlay MakeOverlay()
    {
        var overlay = new EditOverlay();
        overlay.RebindWorld(new WorldChunkStore(Path.GetTempPath())); // no region files needed for these tests
        return overlay;
    }

    [Fact]
    public void Set_IsVisibleImmediatelyThroughGetOverride()
    {
        var overlay = MakeOverlay();

        overlay.BeginBatch();
        overlay.Set(1, 64, 2, "minecraft:dirt");
        overlay.EndBatch();

        Assert.Equal("minecraft:dirt", overlay.GetOverride(1, 64, 2));
        Assert.Null(overlay.GetOverride(1, 65, 2));
    }

    [Fact]
    public void Undo_RestoresThePreEditValue_AndRedoReappliesIt()
    {
        var overlay = MakeOverlay();

        overlay.BeginBatch();
        overlay.Set(0, 0, 0, "minecraft:dirt");
        overlay.EndBatch();
        Assert.True(overlay.CanUndo);

        overlay.Undo();
        Assert.Equal("minecraft:air", overlay.GetOverride(0, 0, 0)); // no region on disk -> WorldChunkStore falls back to air
        Assert.False(overlay.CanUndo);
        Assert.True(overlay.CanRedo);

        overlay.Redo();
        Assert.Equal("minecraft:dirt", overlay.GetOverride(0, 0, 0));
        Assert.False(overlay.CanRedo);
    }

    [Fact]
    public void EndBatch_WithNoActualChanges_DoesNotPushAnUndoStep()
    {
        var overlay = MakeOverlay();

        overlay.BeginBatch();
        overlay.Set(0, 0, 0, "minecraft:air"); // same as the (fallback) original value
        overlay.EndBatch();

        Assert.False(overlay.CanUndo);
    }

    [Fact]
    public void Set_WithTheValueAlreadyThere_DoesNotMarkTheChunkDirty()
    {
        var overlay = MakeOverlay();

        overlay.BeginBatch();
        overlay.Set(0, 0, 0, "minecraft:air"); // same as the (fallback) original value — a true no-op
        overlay.EndBatch();

        // Not just "no undo step" (already covered above) — Save shouldn't rewrite this chunk's
        // region file either, since nothing about it actually changed.
        Assert.Empty(overlay.DirtyChunks);
        Assert.Empty(overlay.GetEditsForChunk(0, 0));
    }

    [Fact]
    public void Set_RepaintingTheSameMaterialASecondTime_DoesNotPushAPhantomUndoStep()
    {
        var overlay = MakeOverlay();

        overlay.BeginBatch();
        overlay.Set(0, 0, 0, "minecraft:dirt");
        overlay.EndBatch();

        overlay.BeginBatch();
        overlay.Set(0, 0, 0, "minecraft:dirt"); // already dirt from the batch above — no-op
        overlay.EndBatch();

        // If the no-op batch had pushed an (empty) undo step anyway, a single Undo() here would only
        // consume that phantom step and leave the real paint from the first batch still in place.
        overlay.Undo();
        Assert.Equal("minecraft:air", overlay.GetOverride(0, 0, 0));
        Assert.False(overlay.CanUndo);
    }

    [Fact]
    public void Set_CalledTwiceInOneBatch_CollapsesToASingleUndoStepUsingTheFirstBeforeValue()
    {
        var overlay = MakeOverlay();

        overlay.BeginBatch();
        overlay.Set(5, 5, 5, "minecraft:dirt");
        overlay.Set(5, 5, 5, "minecraft:stone");
        overlay.EndBatch();

        Assert.Equal("minecraft:stone", overlay.GetOverride(5, 5, 5));
        overlay.Undo();
        // Restored to the true original ("air", since there's no region file backing this test) —
        // Undo always leaves an explicit overlay entry rather than removing it, which renders
        // identically either way.
        Assert.Equal("minecraft:air", overlay.GetOverride(5, 5, 5));
    }

    [Fact]
    public void DirtyChunks_TracksEveryChunkTouched()
    {
        var overlay = MakeOverlay();

        overlay.BeginBatch();
        overlay.Set(0, 0, 0, "minecraft:dirt");
        overlay.Set(20, 0, 0, "minecraft:dirt"); // chunk (1,0)
        overlay.EndBatch();

        var dirty = overlay.DirtyChunks;
        Assert.Contains((0, 0), dirty);
        Assert.Contains((1, 0), dirty);
    }

    [Fact]
    public void GetEditsForChunk_ReturnsChunkLocalCoordinates()
    {
        var overlay = MakeOverlay();

        overlay.BeginBatch();
        overlay.Set(17, 64, 33, "minecraft:dirt"); // chunk (1, 2), local (1, 1)
        overlay.EndBatch();

        var edits = overlay.GetEditsForChunk(1, 2);
        Assert.Equal("minecraft:dirt", edits[(1, 64, 1)]);
    }

    // GetEditsForChunk/DirtyChunks are backed by a per-chunk index of touched coordinates that's only
    // ever added to (see EditOverlay's _editsByChunk) — Undo/Redo change values at already-indexed
    // keys, never remove them from the index. This locks in that a chunk stays visible to Save even
    // after every edit in it has been undone back to its original value.
    [Fact]
    public void GetEditsForChunk_StillFindsTheChunk_AfterAnUndo()
    {
        var overlay = MakeOverlay();

        overlay.BeginBatch();
        overlay.Set(17, 64, 33, "minecraft:dirt"); // chunk (1, 2)
        overlay.EndBatch();
        overlay.Undo();

        Assert.Contains((1, 2), overlay.DirtyChunks);
        var edits = overlay.GetEditsForChunk(1, 2);
        Assert.Equal("minecraft:air", edits[(1, 64, 1)]);
    }

    [Fact]
    public void RebindWorld_ClearsAllStateAndFiresReset()
    {
        var overlay = MakeOverlay();
        overlay.BeginBatch();
        overlay.Set(0, 0, 0, "minecraft:dirt");
        overlay.EndBatch();

        bool resetFired = false;
        overlay.Reset += () => resetFired = true;
        overlay.RebindWorld(new WorldChunkStore(Path.GetTempPath()));

        Assert.True(resetFired);
        Assert.Null(overlay.GetOverride(0, 0, 0));
        Assert.False(overlay.CanUndo);
        Assert.Empty(overlay.DirtyChunks);
    }

    [Fact]
    public void ChunksInvalidated_FiresOnBatchCommitAndOnUndo()
    {
        var overlay = MakeOverlay();
        var invalidated = new List<(int, int)>();
        overlay.ChunksInvalidated += chunks => invalidated.AddRange(chunks);

        overlay.BeginBatch();
        overlay.Set(0, 0, 0, "minecraft:dirt");
        overlay.EndBatch();
        Assert.Contains((0, 0), invalidated);

        invalidated.Clear();
        overlay.Undo();
        Assert.Contains((0, 0), invalidated);
    }
}
