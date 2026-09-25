namespace TCFModManager.App.ViewModels;

// Shared, app-lifetime signal for showing a collection's Workshop page. MainWindow subscribes to
// Requested and shows WorkshopCollectionView.
public sealed class CollectionOverlayViewModel
{
    public event EventHandler<Guid>? Requested;

    public void Show(Guid listId) => Requested?.Invoke(this, listId);
}
