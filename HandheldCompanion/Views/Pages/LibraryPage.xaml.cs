using HandheldCompanion.Converters;
using HandheldCompanion.Managers;
using HandheldCompanion.ViewModels;
using iNKORE.UI.WPF.Modern.Controls;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Page = System.Windows.Controls.Page;

namespace HandheldCompanion.Views.Pages;

public partial class LibraryPage : Page
{
    private readonly AverageColorConverter averageColorConverter = new AverageColorConverter();
    private LibraryPageViewModel? ViewModel => DataContext as LibraryPageViewModel;

    public LibraryPage()
    {
        Tag = "about";
        DataContext = new LibraryPageViewModel();
        InitializeComponent();
    }

    public LibraryPage(string Tag) : this()
    {
        this.Tag = Tag;
    }

    private void ImageContainer_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateArtwork(sender);
    }

    private void ImageContainer_MouseEnter(object sender, MouseEventArgs e)
    {
        UpdateArtwork(sender);
    }

    private void UpdateArtwork(object sender)
    {
        if (sender is Button button && button.DataContext is ProfileViewModel profile && ViewModel is { } vm)
        {
            vm.HighlightColor = (Color)averageColorConverter.Convert(profile.Cover ?? LibraryResources.MissingCover, typeof(Color), DependencyProperty.UnsetValue, CultureInfo.CurrentCulture);
            vm.Artwork = profile.Artwork ?? LibraryResources.MissingArtwork;
        }

        if (sender is Button collectionButton && collectionButton.DataContext is CollectionGroupViewModel collectionGroup)
            ViewModel?.RememberCollectionsOverviewItem(collectionGroup);
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.BackAvailabilityChanged += LibraryPageViewModel_BackAvailabilityChanged;
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.BackAvailabilityChanged -= LibraryPageViewModel_BackAvailabilityChanged;
    }

    public void Page_Closed()
    { }

    private void LibraryPageViewModel_BackAvailabilityChanged(bool canGoBack)
    {
    }

    public bool TryGoBack()
    {
        return ViewModel?.TryGoBack() ?? false;
    }

    private void navView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem navItem || navItem.Tag is not string key)
            return;

        ViewModel?.SelectNavigationItemByKey(key);
    }

    private void navView_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            // The NavigationView may auto-select the first (disabled L2) item on load.
            // Restore the correct selection from the ViewModel.
            navView.SelectedItem = vm.NavigationViewSelectedItem;
        }
    }
}
