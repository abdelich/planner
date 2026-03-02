using System.Windows.Input;
using System.Windows.Threading;

namespace Planner.App.Views;

public partial class RemindersPage : System.Windows.Controls.UserControl
{
    private readonly ViewModels.RemindersViewModel _vm = new();

    public RemindersPage()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.SetDispatcher(Dispatcher.CurrentDispatcher);
        Loaded += OnLoaded;
        IsVisibleChanged += OnVisibleChanged;
        var timer = new DispatcherTimer(DispatcherPriority.Loaded, Dispatcher.CurrentDispatcher) { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _vm.StartLoad();
        };
        timer.Start();
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _vm.StartLoad();
    }

    private void OnVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            _vm.StartLoad();
    }

    private void AddPanelOverlay_MouseDown(object sender, MouseButtonEventArgs e) =>
        _vm.CloseAddPanelCommand.Execute(null);

    private void AddPanel_MouseDown(object sender, MouseButtonEventArgs e) =>
        e.Handled = true;

    private void EditPanelOverlay_MouseDown(object sender, MouseButtonEventArgs e) =>
        _vm.CloseEditPanelCommand.Execute(null);

    private void EditPanel_MouseDown(object sender, MouseButtonEventArgs e) =>
        e.Handled = true;
}
