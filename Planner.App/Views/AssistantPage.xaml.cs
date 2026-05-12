using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;

namespace Planner.App.Views;

public partial class AssistantPage : System.Windows.Controls.UserControl
{
    private readonly ViewModels.AssistantViewModel _vm = new();
    private INotifyCollectionChanged? _subscribedMessages;

    public AssistantPage()
    {
        Services.AssistantDiagnosticsService.LogMemory("assistant-page-construct-start");
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        SubscribeMessages();
        Services.AssistantDiagnosticsService.LogMemory("assistant-page-constructed");
        Loaded += (_, _) =>
        {
            Services.AssistantDiagnosticsService.LogMemory("assistant-page-loaded");
            _vm.StartLoad();
            ScrollMessagesToBottomSoon();
        };
        Unloaded += (_, _) =>
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            UnsubscribeMessages();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(_vm.Messages)) return;
        SubscribeMessages();
        ScrollMessagesToBottomSoon();
    }

    private void SubscribeMessages()
    {
        UnsubscribeMessages();
        _subscribedMessages = _vm.Messages;
        _subscribedMessages.CollectionChanged += OnMessagesCollectionChanged;
    }

    private void UnsubscribeMessages()
    {
        if (_subscribedMessages == null) return;
        _subscribedMessages.CollectionChanged -= OnMessagesCollectionChanged;
        _subscribedMessages = null;
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollMessagesToBottomSoon();
    }

    private void ScrollMessagesToBottomSoon()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (MessagesList.Items.Count == 0) return;
            var last = MessagesList.Items[MessagesList.Items.Count - 1];
            MessagesList.ScrollIntoView(last);
            MessagesList.UpdateLayout();
        }), DispatcherPriority.ContextIdle);
    }
}
