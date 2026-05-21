using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FinanceTracker.ViewModels;

namespace FinanceTracker.Views.Dialogs;

public partial class AddTransactionDialog : UserControl
{
    public AddTransactionDialog()
    {
        InitializeComponent();
    }

    private void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        Focus();
    }

    private void OnDialogKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is TransactionsViewModel viewModel)
        {
            viewModel.CloseDialogCommand.Execute(null);
        }
    }

    private void OnOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) && DataContext is TransactionsViewModel viewModel)
        {
            viewModel.CloseDialogCommand.Execute(null);
        }
    }

    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void OnAmountLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is TransactionsViewModel viewModel)
        {
            viewModel.FormatAmount();
        }
    }
}
