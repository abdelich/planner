namespace Planner.App.Views;

public partial class AssistantPage : System.Windows.Controls.UserControl
{
    private readonly ViewModels.AssistantViewModel _vm = new();

    public AssistantPage()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += (_, _) => _vm.StartLoad();
        Unloaded += (_, _) => _vm.Dispose();
    }
}
