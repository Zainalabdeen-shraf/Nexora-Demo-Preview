using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.NetworkOperations.DemoData;
using Nexora.Demo.Desktop.Features.NetworkOperations.Models;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Views;

public partial class NetworkMaintenanceView : UserControl, INetworkOperationsNavigationSource, INetworkOperationsRefreshable
{
    private bool _initializing = true;
    private NetworkMaintenanceTaskDemo? _selectedTask;
    public event EventHandler<NetworkOperationsNavigationRequestedEventArgs>? NavigationRequested;
    public ObservableCollection<NetworkMaintenanceTaskDemo> VisibleTasks { get; } = new();

    public NetworkMaintenanceView()
    {
        InitializeComponent();
        DataContext = this;
        TaskDeviceComboBox.ItemsSource = NetworkOperationsDemoStore.Devices;
        TaskPriorityComboBox.Items.Add("عالية");
        TaskPriorityComboBox.Items.Add("متوسطة");
        TaskPriorityComboBox.Items.Add("منخفضة");
        TaskPriorityComboBox.SelectedIndex = 1;
        TaskStatusFilterComboBox.Items.Add("كل الحالات");
        TaskStatusFilterComboBox.Items.Add("جديدة");
        TaskStatusFilterComboBox.Items.Add("قيد التنفيذ");
        TaskStatusFilterComboBox.Items.Add("مجدولة");
        TaskStatusFilterComboBox.Items.Add("مكتملة");
        TaskStatusFilterComboBox.SelectedIndex = 0;
        _initializing = false;
        ApplyFilter();
        UpdateCounters();
    }

    private void UpdateCounters()
    {
        TasksTotalTextBlock.Text = NetworkOperationsDemoStore.MaintenanceTasks.Count.ToString("N0");
        TasksOpenTextBlock.Text = NetworkOperationsDemoStore.MaintenanceTasks.Count(task => task.Status != "مكتملة").ToString("N0");
        TasksCompletedTextBlock.Text = NetworkOperationsDemoStore.MaintenanceTasks.Count(task => task.Status == "مكتملة").ToString("N0");
    }

    public void RefreshFromSharedState()
    {
        ApplyFilter();
        UpdateCounters();
    }

    private void TaskStatusFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = TaskStatusFilterComboBox.SelectedItem as string;
        var query = NetworkOperationsDemoStore.MaintenanceTasks.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filter) && filter != "كل الحالات") query = query.Where(task => task.Status == filter);
        VisibleTasks.Clear();
        foreach (var task in query) VisibleTasks.Add(task);
        TasksDataGrid.Items.Refresh();
    }

    private void NewTaskButton_Click(object sender, RoutedEventArgs e)
    {
        TaskEditorPanel.Visibility = Visibility.Visible;
        TaskTitleTextBox.Clear();
        TaskNotesTextBox.Clear();
        TaskAssigneeTextBox.Text = "غير معيّن";
        TaskDeviceComboBox.SelectedItem = null;
        TaskPriorityComboBox.SelectedIndex = 1;
        TaskEditorStatusTextBlock.Text = "المهمة ستُحفظ في بيانات الديمو المشتركة بين الشاشات.";
        TaskTitleTextBox.Focus();
    }

    private void CancelTaskButton_Click(object sender, RoutedEventArgs e) => TaskEditorPanel.Visibility = Visibility.Collapsed;

    private void SaveTaskButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TaskTitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            TaskEditorStatusTextBlock.Text = "أدخل عنوان المهمة.";
            return;
        }
        if (TaskDeviceComboBox.SelectedItem is not NetworkDeviceDemo device)
        {
            TaskEditorStatusTextBlock.Text = "اختر الجهاز المرتبط بالمهمة.";
            return;
        }
        var task = new NetworkMaintenanceTaskDemo
        {
            Id = NetworkOperationsDemoStore.NewId("MNT"),
            Title = title,
            DeviceName = device.Name,
            SiteName = device.SiteName,
            Priority = TaskPriorityComboBox.SelectedItem as string ?? "متوسطة",
            Status = "جديدة",
            AssignedTo = string.IsNullOrWhiteSpace(TaskAssigneeTextBox.Text) ? "غير معيّن" : TaskAssigneeTextBox.Text.Trim(),
            Notes = TaskNotesTextBox.Text.Trim(),
            CreatedAt = DateTime.Now
        };
        NetworkOperationsDemoStore.MaintenanceTasks.Insert(0, task);
        TaskEditorPanel.Visibility = Visibility.Collapsed;
        TaskStatusFilterComboBox.SelectedIndex = 0;
        ApplyFilter();
        UpdateCounters();
        TasksDataGrid.SelectedItem = task;
        TaskActionStatusTextBlock.Text = $"تم إنشاء المهمة {task.Id} في بيانات الديمو.";
    }

    private void TasksDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedTask = TasksDataGrid.SelectedItem as NetworkMaintenanceTaskDemo;
        if (_selectedTask is null)
        {
            SelectedTaskPanel.Visibility = Visibility.Collapsed;
            return;
        }
        SelectedTaskPanel.Visibility = Visibility.Visible;
        SelectedTaskDetailsTextBlock.Text = $"{_selectedTask.Id} · {_selectedTask.Title}\nالجهاز: {_selectedTask.DeviceName}\nالموقع: {_selectedTask.SiteName}\nالأولوية: {_selectedTask.Priority}\nالحالة: {_selectedTask.Status}\nالفني: {_selectedTask.AssignedTo}\nالتفاصيل: {_selectedTask.Notes}\nالمصدر: {(_selectedTask.SourceAlertId ?? "إنشاء يدوي")}";
        TaskActionStatusTextBlock.Text = "يمكنك تغيير حالة المهمة لتجربة سير العمل.";
    }

    private void ChangeSelectedTaskStatus(string status)
    {
        if (_selectedTask is null)
        {
            TaskActionStatusTextBlock.Text = "اختر مهمة من القائمة أولًا.";
            return;
        }
        var changedTask = _selectedTask;
        changedTask.Status = status;
        TasksDataGrid.Items.Refresh();
        SelectedTaskDetailsTextBlock.Text = $"{changedTask.Id} · {changedTask.Title}\nالجهاز: {changedTask.DeviceName}\nالموقع: {changedTask.SiteName}\nالأولوية: {changedTask.Priority}\nالحالة: {changedTask.Status}\nالفني: {changedTask.AssignedTo}\nالتفاصيل: {changedTask.Notes}\nالمصدر: {(changedTask.SourceAlertId ?? "إنشاء يدوي")}";
        ApplyFilter();
        var visibleTask = VisibleTasks.FirstOrDefault(task => task.Id == changedTask.Id);
        if (visibleTask is not null) TasksDataGrid.SelectedItem = visibleTask;
        UpdateCounters();
        TaskActionStatusTextBlock.Text = $"تم تغيير حالة المهمة إلى «{status}» في الديمو.";
    }

    private void StartTaskButton_Click(object sender, RoutedEventArgs e) => ChangeSelectedTaskStatus("قيد التنفيذ");
    private void CompleteTaskButton_Click(object sender, RoutedEventArgs e) => ChangeSelectedTaskStatus("مكتملة");
    private void ReopenTaskButton_Click(object sender, RoutedEventArgs e) => ChangeSelectedTaskStatus("جديدة");
    private void OpenDevicesButton_Click(object sender, RoutedEventArgs e) => NavigationRequested?.Invoke(this, new NetworkOperationsNavigationRequestedEventArgs("المواقع والأجهزة"));
}