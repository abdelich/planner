using System.Windows.Controls;
using System.Windows.Input;

namespace Planner.App.Views;

public partial class GoalsPage : System.Windows.Controls.UserControl
{
    public GoalsPage()
    {
        InitializeComponent();
    }

    private void AddPanelOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Resources["Vm"] is ViewModels.GoalsViewModel vm)
            vm.CloseAddPanelCommand.Execute(null);
    }

    private void AddPanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}
