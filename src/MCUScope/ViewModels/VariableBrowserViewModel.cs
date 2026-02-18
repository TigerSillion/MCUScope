using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCUScope.Models;
using MCUScope.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace MCUScope.ViewModels
{
    public partial class VariableBrowserItem : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private uint _address;
        [ObservableProperty] private VariableType _type;
        [ObservableProperty] private int _size;
        [ObservableProperty] private string _category = string.Empty;
        [ObservableProperty] private bool _isGlobal = true;
        [ObservableProperty] private double _value = double.NaN;
        [ObservableProperty] private bool _hasValue;
        [ObservableProperty] private bool _isSelected;

        public string AddressHex => $"0x{Address:X8}";
        public string ScopeText => IsGlobal ? "G" : "L";
        public string TypeText => Type.ToString();
        public string SizeText => Size.ToString();
    }

    public partial class VariableBrowserViewModel : ObservableObject
    {
        private readonly SessionState _session;

        public VariableBrowserViewModel()
        {
            _session = SessionState.Instance;
            _session.IcsService.VariableValueReceived += OnVariableValueReceived;

            ItemsView = CollectionViewSource.GetDefaultView(Items);
            ItemsView.Filter = FilterPredicate;
        }

        public ObservableCollection<VariableBrowserItem> Items { get; } = new();
        public ICollectionView ItemsView { get; }

        // Filter properties
        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private string _typeFilter = "All";
        [ObservableProperty] private string _scopeFilter = "All";
        [ObservableProperty] private string _categoryFilter = "All";

        [ObservableProperty] private int _totalCount;
        [ObservableProperty] private int _filteredCount;
        [ObservableProperty] private string _sortColumn = "Name";
        [ObservableProperty] private bool _sortAscending = true;

        public ObservableCollection<string> TypeOptions { get; } = new() { "All" };
        public ObservableCollection<string> CategoryOptions { get; } = new() { "All" };

        partial void OnSearchTextChanged(string value) => RefreshFilter();
        partial void OnTypeFilterChanged(string value) => RefreshFilter();
        partial void OnScopeFilterChanged(string value) => RefreshFilter();
        partial void OnCategoryFilterChanged(string value) => RefreshFilter();

        public void LoadFromVariables()
        {
            Items.Clear();
            TypeOptions.Clear();
            TypeOptions.Add("All");
            CategoryOptions.Clear();
            CategoryOptions.Add("All");

            var typeSet = new System.Collections.Generic.HashSet<string>();
            var catSet = new System.Collections.Generic.HashSet<string>();

            foreach (var v in _session.IcsService.Variables)
            {
                if (v.IsLikelyInternal)
                    continue;
                Items.Add(new VariableBrowserItem
                {
                    Name = v.Name,
                    Address = v.Address,
                    Type = v.ModifiedType,
                    Size = v.EffectiveSize,
                    Category = v.Category,
                    IsGlobal = v.IsGlobal
                });

                typeSet.Add(v.ModifiedType.ToString());
                if (!string.IsNullOrEmpty(v.Category))
                    catSet.Add(v.Category);
            }

            foreach (var t in typeSet.OrderBy(x => x)) TypeOptions.Add(t);
            foreach (var c in catSet.OrderBy(x => x)) CategoryOptions.Add(c);

            TotalCount = Items.Count;
            ApplySort("Name", true);
            RefreshFilter();
        }

        private bool FilterPredicate(object obj)
        {
            if (obj is not VariableBrowserItem item) return false;

            if (!string.IsNullOrEmpty(SearchText) &&
                !item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                return false;

            if (TypeFilter != "All" && item.Type.ToString() != TypeFilter)
                return false;

            if (ScopeFilter == "Global" && !item.IsGlobal) return false;
            if (ScopeFilter == "Local" && item.IsGlobal) return false;
            if (ScopeFilter == "Struct" && !item.Name.Contains('.')) return false;

            if (CategoryFilter != "All" && item.Category != CategoryFilter)
                return false;

            return true;
        }

        private void RefreshFilter()
        {
            ItemsView.Refresh();
            FilteredCount = ItemsView.Cast<object>().Count();
        }

        [RelayCommand]
        private void SortBy(string column)
        {
            bool ascending = (column == SortColumn) ? !SortAscending : true;
            ApplySort(column, ascending);
        }

        private void ApplySort(string column, bool ascending)
        {
            SortColumn = column;
            SortAscending = ascending;

            ItemsView.SortDescriptions.Clear();
            var direction = ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
            ItemsView.SortDescriptions.Add(new SortDescription(column, direction));
        }

        [RelayCommand]
        private void SetPrefixFilter(string prefix)
        {
            SearchText = prefix;
        }

        [RelayCommand]
        private void ReadSelected()
        {
            int count = 0;
            foreach (var item in Items.Where(i => i.IsSelected))
            {
                _session.IcsService.RequestReadVariable(item.Name);
                count++;
            }
            if (count == 0)
            {
                // Read all visible if none selected
                foreach (var item in ItemsView.Cast<VariableBrowserItem>().Take(50))
                {
                    _session.IcsService.RequestReadVariable(item.Name);
                    count++;
                }
            }
            LogService.Info($"VariableBrowser: sent {count} read requests");
        }

        [RelayCommand]
        private void ClearFilter()
        {
            SearchText = string.Empty;
            TypeFilter = "All";
            ScopeFilter = "All";
            CategoryFilter = "All";
        }

        // --- Context Menu Commands ---

        [RelayCommand]
        private void AddToScope(VariableBrowserItem? item)
        {
            if (item == null) return;
            if (!_session.IcsService.TryResolveVariable(item.Name, out var variable) ||
                variable.IsLikelyInternal || !variable.IsProtocolScalar)
            {
                LogService.Warn($"Cannot bind '{item.Name}' to scope: variable is not protocol-scalar.");
                return;
            }
            // Find parent MainViewModel through SessionState to access ScopeViewModel
            // Use the variable name to find next empty scope channel
            var scopeValues = GetScopeValues();
            if (scopeValues == null) return;

            var emptyChannel = scopeValues.FirstOrDefault(sv => string.IsNullOrEmpty(sv.VariableName));
            if (emptyChannel != null)
            {
                emptyChannel.VariableName = item.Name;
                emptyChannel.Visible = true;
                LogService.Info($"Added '{item.Name}' to scope channel {emptyChannel.ChannelId}");
            }
            else
            {
                LogService.Warn("No empty scope channel available");
            }
        }

        [RelayCommand]
        private void AddToWatch(VariableBrowserItem? item)
        {
            if (item == null) return;
            if (!_session.IcsService.TryResolveVariable(item.Name, out var variable) ||
                variable.IsLikelyInternal || !variable.IsProtocolScalar)
            {
                LogService.Warn($"Cannot add '{item.Name}' to watch: variable is not protocol-scalar.");
                return;
            }
            var watchItems = GetWatchItems();
            if (watchItems == null) return;

            var emptySlot = watchItems.FirstOrDefault(w => string.IsNullOrEmpty(w.Name));
            if (emptySlot != null)
            {
                emptySlot.Name = item.Name;
                emptySlot.ReadEnabled = true;
                LogService.Info($"Added '{item.Name}' to watch");
            }
            else
            {
                LogService.Warn("No empty watch slot available");
            }
        }

        [RelayCommand]
        private void CopyName(VariableBrowserItem? item)
        {
            if (item == null) return;
            Clipboard.SetText(item.Name);
            LogService.Info($"Copied variable name: {item.Name}");
        }

        [RelayCommand]
        private void ReadSingleVariable(VariableBrowserItem? item)
        {
            if (item == null) return;
            _session.IcsService.RequestReadVariable(item.Name);
            LogService.Info($"Read request sent for: {item.Name}");
        }

        // Helper to access ScopeValues from the main view model via the Window's DataContext
        private ObservableCollection<ScopeValueItem>? GetScopeValues()
        {
            if (Application.Current?.MainWindow?.DataContext is MainViewModel vm)
                return vm.Scope.ScopeValues;
            return null;
        }

        private ObservableCollection<WatchItem>? GetWatchItems()
        {
            if (Application.Current?.MainWindow?.DataContext is MainViewModel vm)
                return vm.Watch.WatchItems;
            return null;
        }

        private void OnVariableValueReceived(object? sender, VariableReadEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var item = Items.FirstOrDefault(i =>
                    string.Equals(i.Name, e.VariableName, StringComparison.Ordinal));
                if (item != null)
                {
                    item.Value = e.Value;
                    item.HasValue = true;
                }
            });
        }
    }
}
