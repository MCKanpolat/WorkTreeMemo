namespace WorkTreeMemo.Core.Models;

public enum WipKind
{
    Unpushed,
    Dirty,
    Stashed,
    StaleUnmerged,
    Parked,
    CleanCandidate
}
