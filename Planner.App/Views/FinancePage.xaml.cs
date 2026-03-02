using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Planner.App.Views;

public partial class FinancePage : System.Windows.Controls.UserControl
{
    public FinancePage()
    {
        InitializeComponent();
        DataContext = new ViewModels.FinanceViewModel();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.FinanceViewModel vm)
            vm.StartLoad();
    }

    private ViewModels.FinanceViewModel? GetVm() => DataContext as ViewModels.FinanceViewModel;

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var vm = GetVm();
        if (vm == null) return;
        if (vm.IsAddTransactionOpen) vm.CloseAddTransactionCommand.Execute(null);
        if (vm.IsAddCategoryOpen) vm.CloseAddCategoryCommand.Execute(null);
        if (vm.IsAddSavingsOpen) vm.CloseAddSavingsCommand.Execute(null);
        if (vm.IsEditSavingsOpen) vm.CloseEditSavingsCommand.Execute(null);
        if (vm.IsAddSavingsCategoryOpen) vm.CloseAddSavingsCategoryCommand.Execute(null);
        if (vm.IsEditSavingsCategoryOpen) vm.CloseEditSavingsCategoryCommand.Execute(null);
        if (vm.IsEditFinanceCategoryOpen) vm.CloseEditFinanceCategoryCommand.Execute(null);
        if (vm.IsCategoriesPanelOpen) vm.CloseCategoriesPanelCommand.Execute(null);
        if (vm.IsSavingsPanelOpen) vm.CloseSavingsPanelCommand.Execute(null);
        if (vm.IsSavingsChartsOpen) vm.CloseSavingsChartsCommand.Execute(null);
    }

    private void Panel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}
