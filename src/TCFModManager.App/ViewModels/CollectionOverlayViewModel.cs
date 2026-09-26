namespace TCFModManager.App.ViewModels;

/// <summary>A collection to show: one of this install's mod lists, or one of sp-mod.com's public
/// lists by its id and slug.</summary>
public sealed record CollectionRequest(Guid? ListId, int PublicId = 0, string? PublicSlug = null)
{
    public bool IsPublic => PublicId > 0 && PublicSlug is not null;
}

// Shared, app-lifetime signal for showing a collection's Workshop page. MainWindow subscribes to
// Requested and shows WorkshopCollectionView.
public sealed class CollectionOverlayViewModel
{
    public event EventHandler<CollectionRequest>? Requested;

    public void Show(Guid listId) => Requested?.Invoke(this, new CollectionRequest(listId));

    /// <summary>One of sp-mod.com's public lists, read from its page.</summary>
    public void ShowPublic(int id, string slug) => Requested?.Invoke(this, new CollectionRequest(null, id, slug));
}
