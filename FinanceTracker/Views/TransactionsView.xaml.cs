using System.Windows;
using System.Windows.Controls;
using FinanceTracker.ViewModels;

namespace FinanceTracker.Views;

public partial class TransactionsView : UserControl
{
    public TransactionsView()
    {
        InitializeComponent();
    }

    private void OnCategoryFilterButtonClick(object sender, System.Windows.RoutedEventArgs e)
    {
        CategoryFilterPopup.IsOpen = !CategoryFilterPopup.IsOpen;
    }

    private void OnCategoryFilterClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is TransactionsViewModel viewModel &&
            sender is FrameworkElement { DataContext: CategoryFilterOption option })
        {
            viewModel.ToggleCategoryCommand.Execute(option);
        }
    }
}
