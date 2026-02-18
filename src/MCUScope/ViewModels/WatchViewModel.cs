using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCUScope.Models;
using MCUScope.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MCUScope.ViewModels
{
    public partial class WatchViewModel : ObservableObject
    {
        private readonly SessionState _session;
        private CancellationTokenSource? _autoReadCts;
        private Task? _autoReadTask;

        public WatchViewModel()
        {
            _session = SessionState.Instance;
            _session.IcsService.VariableValueReceived += OnVariableValueReceived;

            for (int i = 0; i < 24; i++)
            {
                var item = new WatchItem();
                item.PropertyChanged += OnWatchItemPropertyChanged;
                WatchItems.Add(item);
            }
        }

        private void OnWatchItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WatchItem.Name) && sender is WatchItem item)
                RefreshItemMetadata(item);
        }

        public ObservableCollection<WatchItem> WatchItems { get; } = new();
        public ObservableCollection<string> VariableNames => _session.VariableNames;

        [ObservableProperty] private int _autoReadIntervalUs = 1000;
        [ObservableProperty] private bool _isAutoReading;
        [ObservableProperty] private string _searchText = string.Empty;

        partial void OnSearchTextChanged(string value)
        {
            // Filter visible watch items by search text
            FilterWatchItems();
        }

        private void FilterWatchItems()
        {
            // Filtering is handled in the View via CollectionView if needed
            // For simplicity: highlight matching items via IsSelected
        }

        [RelayCommand]
        private void ReadWatchVariables()
        {
            foreach (var item in WatchItems)
            {
                if (item.ReadEnabled && !string.IsNullOrEmpty(item.Name))
                    _session.IcsService.RequestReadVariable(item.Name);
            }
        }

        [RelayCommand]
        private void WriteWatchVariables()
        {
            foreach (var item in WatchItems)
            {
                if (item.WriteEnabled && !string.IsNullOrEmpty(item.Name) &&
                    double.TryParse(item.WriteValue, out var value))
                {
                    _session.IcsService.RequestWriteVariable(item.Name, value);
                }
            }
        }

        [RelayCommand]
        private void ToggleAutoRead()
        {
            if (IsAutoReading)
                StopAutoReadLoop();
            else
                StartAutoReadLoop();
        }

        /// <summary>Clear all read values (zero reset) without removing variable bindings.</summary>
        [RelayCommand]
        private void ClearReadValues()
        {
            foreach (var item in WatchItems)
            {
                item.ReadValue = string.Empty;
            }
            LogService.Info("Watch: read values cleared");
        }

        /// <summary>Fill WriteValue with current ReadValue for all enabled watch items.</summary>
        [RelayCommand]
        private void SyncWriteFromRead()
        {
            foreach (var item in WatchItems.Where(i => !string.IsNullOrEmpty(i.ReadValue)))
            {
                item.WriteValue = item.ReadValue;
                item.WriteEnabled = true;
            }
        }

        /// <summary>Update address/type info when a variable name is assigned (called from View).</summary>
        public void RefreshItemMetadata(WatchItem item)
        {
            if (string.IsNullOrEmpty(item.Name))
            {
                item.AddressHex = string.Empty;
                item.TypeText = string.Empty;
                return;
            }
            if (_session.IcsService.TryResolveVariable(item.Name, out var variable))
            {
                item.AddressHex = $"0x{variable.Address:X8}";
                item.TypeText = variable.ModifiedType.ToString();
                item.ReadEnabled = true;
            }
        }

        private void OnVariableValueReceived(object? sender, VariableReadEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var watchItem = WatchItems.FirstOrDefault(w =>
                {
                    if (string.Equals(w.Name, e.VariableName, StringComparison.Ordinal))
                        return true;
                    if (_session.IcsService.TryResolveVariable(w.Name, out var variable))
                        return string.Equals(variable.Name, e.VariableName, StringComparison.Ordinal);
                    return false;
                });
                if (watchItem != null)
                {
                    watchItem.ReadValue = e.Value.ToString("G8");
                    // Keep address/type fresh
                    if (string.IsNullOrEmpty(watchItem.AddressHex))
                        RefreshItemMetadata(watchItem);
                }
            });
        }

        private async Task AutoReadLoopAsync(int intervalUs, CancellationToken token)
        {
            long intervalTicks = Math.Max(1L,
                (long)Math.Round(intervalUs * (double)Stopwatch.Frequency / 1_000_000d));
            var stopwatch = Stopwatch.StartNew();
            long nextTick = stopwatch.ElapsedTicks;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (stopwatch.ElapsedTicks >= nextTick)
                    {
                        if (Application.Current != null)
                            await Application.Current.Dispatcher.InvokeAsync(ReadWatchVariables);

                        nextTick += intervalTicks;
                        if (stopwatch.ElapsedTicks > nextTick + intervalTicks * 4)
                            nextTick = stopwatch.ElapsedTicks + intervalTicks;
                        continue;
                    }

                    long remainTicks = nextTick - stopwatch.ElapsedTicks;
                    double remainMs = remainTicks * 1000.0 / Stopwatch.Frequency;

                    if (remainMs >= 1.0)
                        await Task.Delay(1, token);
                    else
                        Thread.SpinWait(120);
                }
            }
            catch (OperationCanceledException) { }
        }

        private void StartAutoReadLoop()
        {
            StopAutoReadLoop();
            AutoReadIntervalUs = Math.Clamp(AutoReadIntervalUs, 1, 10_000_000);
            _autoReadCts = new CancellationTokenSource();
            IsAutoReading = true;
            _autoReadTask = Task.Run(() => AutoReadLoopAsync(AutoReadIntervalUs, _autoReadCts.Token));
        }

        public void StopAutoReadLoop()
        {
            if (_autoReadCts != null)
            {
                _autoReadCts.Cancel();
                _autoReadCts.Dispose();
                _autoReadCts = null;
            }
            _autoReadTask = null;
            IsAutoReading = false;
        }

        partial void OnAutoReadIntervalUsChanged(int value)
        {
            if (value < 1) AutoReadIntervalUs = 1;
        }
    }
}
