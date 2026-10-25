using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

// Responsibility: Screen lifecycle, subscriptions, and generated row mapping.
public partial class UserManagerCardsView
{
    private void UserManagerCardsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_eventsSubscribed)
        {
            _issuanceService.Cards.CollectionChanged += IssuanceCards_CollectionChanged;
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _eventsSubscribed = true;
        }
        RebuildRows();
    }
    private void UserManagerCardsView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_eventsSubscribed) return;
        _issuanceService.Cards.CollectionChanged -= IssuanceCards_CollectionChanged;
        _issuanceService.ScenarioChanged -= IssuanceService_ScenarioChanged;
        _eventsSubscribed = false;
    }
    private void IssuanceCards_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Dispatcher.InvokeAsync(RebuildRows);
    private void IssuanceService_ScenarioChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(RebuildRows);
    private void RebuildRows()
    {
        var cards = _issuanceService.Cards
            .Where(card => string.Equals(card.TargetSystem, UserManagerTarget, StringComparison.Ordinal))
            .ToList();
        _allRows.Clear();
        foreach (var card in cards)
        {
            var key = GetCardKey(card);
            if (!_rowStates.TryGetValue(key, out var state))
            {
                state = new LocalRowState();
                _rowStates[key] = state;
            }
            if (!_rowCache.TryGetValue(key, out var row))
            {
                row = new ManagedCardRow(card, state);
                _rowCache[key] = row;
            }
            else
            {
                row.UpdateSource(card);
            }
            _allRows.Add(row);
        }
        _cardsView.Refresh();
        UpdateSelectionSummary();
        UpdateFooter();
    }
    private static string GetCardKey(GeneratedDemoCard card) => $"{card.BatchId}::{card.CardNumber}";
    private bool MatchCard(object item)
    {
        if (item is not ManagedCardRow row || row.State.IsDeleted) return false;
        if (_archiveFilter == ArchiveFilterMode.ActiveOnly && row.State.IsArchived) return false;
        if (_archiveFilter == ArchiveFilterMode.ArchivedOnly && !row.State.IsArchived) return false;
        if (_statusFilter is not null && !string.Equals(row.StatusLabel, _statusFilter, StringComparison.CurrentCultureIgnoreCase)) return false;
        if (_columnFilterKey is not null && _columnFilterValue is not null &&
            !string.Equals(row[_columnFilterKey], _columnFilterValue, StringComparison.CurrentCultureIgnoreCase)) return false;
        var search = SearchTextBox?.Text.Trim();
        if (string.IsNullOrWhiteSpace(search)) return true;
        return row.Values.Values.Any(value => value.Contains(search, StringComparison.CurrentCultureIgnoreCase));
    }
}
