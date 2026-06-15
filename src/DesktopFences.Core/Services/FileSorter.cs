using DesktopFences.Core.Models;

namespace DesktopFences.Core.Services;

/// <summary>
/// Pure, metadata-driven sort key for a file item. Carries pre-resolved values
/// so the comparison logic in <see cref="FileSorter"/> never touches the disk —
/// callers (the UI layer) read <see cref="System.IO.FileInfo"/> once and hand
/// the values here, keeping the sort fully unit-testable in the Core project.
/// </summary>
/// <param name="Name">Display name used for Name / tie-break ordering.</param>
/// <param name="Extension">File extension (e.g. ".txt"); drives Extension sort.</param>
/// <param name="SizeBytes">File length in bytes; <c>-1</c> for folders / IO failure.</param>
/// <param name="DateModifiedTicks">Last-write time ticks; <c>0</c> on IO failure.</param>
/// <param name="DateCreatedTicks">Creation time ticks; <c>0</c> on IO failure.</param>
public readonly record struct FileSortKey(
    string Name,
    string Extension,
    long SizeBytes,
    long DateModifiedTicks,
    long DateCreatedTicks);

/// <summary>
/// Phase 14: stable, deterministic file sorting shared by the ViewModel's
/// <c>ApplySort</c>. Comparison logic lives here (Core, no UI/OS deps) so it can
/// be exercised directly in unit tests.
/// </summary>
public static class FileSorter
{
    /// <summary>
    /// Returns <paramref name="items"/> ordered by <paramref name="field"/> /
    /// <paramref name="direction"/>. <see cref="SortField.Manual"/> preserves the
    /// incoming order (a no-op copy). LINQ ordering is stable, so equal keys keep
    /// their original relative order.
    /// </summary>
    public static List<T> Sort<T>(
        IEnumerable<T> items,
        SortField field,
        SortDirection direction,
        Func<T, FileSortKey> keySelector)
    {
        var list = items.ToList();
        if (field == SortField.Manual) return list;

        bool asc = direction == SortDirection.Ascending;
        var cmp = StringComparer.OrdinalIgnoreCase;

        IOrderedEnumerable<T> ordered = field switch
        {
            SortField.Extension => asc
                ? list.OrderBy(t => keySelector(t).Extension, cmp).ThenBy(t => keySelector(t).Name, cmp)
                : list.OrderByDescending(t => keySelector(t).Extension, cmp).ThenBy(t => keySelector(t).Name, cmp),
            SortField.Size => asc
                ? list.OrderBy(t => keySelector(t).SizeBytes)
                : list.OrderByDescending(t => keySelector(t).SizeBytes),
            SortField.DateModified => asc
                ? list.OrderBy(t => keySelector(t).DateModifiedTicks)
                : list.OrderByDescending(t => keySelector(t).DateModifiedTicks),
            SortField.DateCreated => asc
                ? list.OrderBy(t => keySelector(t).DateCreatedTicks)
                : list.OrderByDescending(t => keySelector(t).DateCreatedTicks),
            // Name (and any future/unknown field) falls back to name ordering.
            _ => asc
                ? list.OrderBy(t => keySelector(t).Name, cmp)
                : list.OrderByDescending(t => keySelector(t).Name, cmp),
        };

        return ordered.ToList();
    }

    /// <summary>
    /// Translates a "drop before index <paramref name="insertIndex"/>" intent into
    /// the final target index for <c>ObservableCollection.Move</c>, which removes
    /// the item first (shifting everything after <paramref name="oldIndex"/> down
    /// by one). Result is clamped to <c>[0, count - 1]</c>.
    /// </summary>
    public static int AdjustMoveIndex(int oldIndex, int insertIndex, int count)
    {
        if (count <= 0) return 0;
        int target = insertIndex;
        if (oldIndex < insertIndex) target--;
        if (target < 0) target = 0;
        if (target > count - 1) target = count - 1;
        return target;
    }
}
