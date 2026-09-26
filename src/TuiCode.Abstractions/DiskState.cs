namespace TuiCode.Abstractions;

/// <summary>
/// What's on disk now, as against the file an editor tab loaded (#271). One state with three values
/// rather than two flags: a file can't be both changed underneath you and gone, and the transitions
/// between them — deleted, then put back by a branch you checked out again — are a move from one to another.
/// </summary>
public enum DiskState
{
    /// <summary>The file on disk is the one this tab loaded.</summary>
    Unchanged,

    /// <summary>Someone else has changed it, so saving would overwrite theirs (#268).</summary>
    Changed,

    /// <summary>It isn't there any more, deleted or renamed away. The buffer is the only copy left (#271).</summary>
    Gone,
}
