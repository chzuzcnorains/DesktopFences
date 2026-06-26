namespace DesktopFences.UI.Controls;

/// <summary>
/// A container that owns a visual selection (a fence panel or the desktop overlay).
/// </summary>
public interface ISelectionContainer
{
    /// <summary>Clear this container's current selection (highlight + state).</summary>
    void ClearSelection();
}

/// <summary>
/// Enforces a single mutually-exclusive selection across the whole desktop: at most
/// one container (a <c>FencePanel</c> or the <c>DesktopIconOverlay</c>) holds a
/// selection at a time — like Windows Explorer, where only the focused window shows
/// selected items.
///
/// Containers register on load and call <see cref="NotifyActivated"/> right before they
/// establish a new selection; the coordinator then clears every *other* container.
/// Weak references are used so a closed fence/overlay never keeps the coordinator alive.
/// </summary>
public sealed class DesktopSelectionCoordinator
{
    private readonly List<WeakReference<ISelectionContainer>> _containers = [];

    public void Register(ISelectionContainer container)
    {
        Prune();
        if (!_containers.Any(w => w.TryGetTarget(out var t) && ReferenceEquals(t, container)))
            _containers.Add(new WeakReference<ISelectionContainer>(container));
    }

    public void Unregister(ISelectionContainer container)
    {
        _containers.RemoveAll(w => !w.TryGetTarget(out var t) || ReferenceEquals(t, container));
    }

    /// <summary>Clear every container except <paramref name="active"/> (never clears itself).</summary>
    public void NotifyActivated(ISelectionContainer active)
    {
        Prune();
        foreach (var w in _containers)
            if (w.TryGetTarget(out var c) && !ReferenceEquals(c, active))
                c.ClearSelection();
    }

    private void Prune() => _containers.RemoveAll(w => !w.TryGetTarget(out _));
}
