namespace DesktopFences.UI.Controls;

/// <summary>
/// Custom OLE data formats used to recognize drags that originate inside the
/// app (fence file tiles, desktop icon overlay). Internal drags use MOVE
/// semantics (source removes its entry on success); drags from Explorer keep
/// COPY semantics — reporting Move back to Explorer could make it delete the
/// source file on disk.
/// </summary>
internal static class InternalDragFormats
{
    /// <summary>Marker set on the DataObject of all in-app drags.</summary>
    public const string Marker = "DesktopFences.InternalDrag";

    /// <summary>
    /// Phase 14: the source fence's <c>Id</c> (as string) carried by a file-tile
    /// drag. When a drop lands on the same fence it identifies a self-drag, so
    /// <c>OnDrop</c> reorders the item in place instead of add/remove.
    /// </summary>
    public const string SourceFenceId = "DesktopFences.SourceFenceId";
}
