// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Caly.Core.ViewModels;

public sealed partial class PrintDialogViewModel : ViewModelBase
{
    private readonly IPrintService _printService;
    private readonly IPdfDocumentService _documentService;

    /// <summary>
    /// Maps page number (1-based) to user-applied rotation (0/90/180/270).
    /// </summary>
    private readonly IReadOnlyDictionary<int, int> _pageRotations;

    /// <summary>
    /// Raised when a successful print job has been submitted and the dialog should close.
    /// </summary>
    public event EventHandler? PrintCompleted;

    [ObservableProperty] private ObservableCollection<PrinterInfo> _availablePrinters = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    private PrinterInfo? _selectedPrinter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPageLabel))]
    private int _currentPageNumber;

    [ObservableProperty] private int _totalPages;

    // --- Page range options (mutually exclusive) ---

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    private bool _isAllPagesSelected = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    private bool _isCurrentPageSelected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    private bool _isCustomRangeSelected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    private string _customPageRange = string.Empty;

    // --- Status ---

    [ObservableProperty] private bool _isPrinting;
    [ObservableProperty] private bool _isLoadingPrinters;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _hasError;

    // --- Print progress ---

    [ObservableProperty] private int _printPagesProcessed;

    [ObservableProperty] private int _printTotalPages;

    public string CurrentPageLabel => $"Current page ({CurrentPageNumber})";

    public PrintDialogViewModel(
        IPrintService printService,
        IPdfDocumentService documentService,
        int currentPageNumber)
    {
        _printService = printService;
        _documentService = documentService;
        _pageRotations = new Dictionary<int, int>(); // We ignore page rotation for now

        CurrentPageNumber = currentPageNumber;
        TotalPages = documentService.NumberOfPages;
    }

    // Keep radio buttons mutually exclusive when set programmatically.
    partial void OnIsAllPagesSelectedChanged(bool value)
    {
        if (!value) return;
        IsCurrentPageSelected = false;
        IsCustomRangeSelected = false;
    }

    partial void OnIsCurrentPageSelectedChanged(bool value)
    {
        if (!value) return;
        IsAllPagesSelected = false;
        IsCustomRangeSelected = false;
    }

    partial void OnIsCustomRangeSelectedChanged(bool value)
    {
        if (!value) return;
        IsAllPagesSelected = false;
        IsCurrentPageSelected = false;
    }

    [RelayCommand]
    private async Task LoadPrinters(CancellationToken token)
    {
        IsLoadingPrinters = true;
        StatusMessage = null;
        HasError = false;
        AvailablePrinters.Clear();

        try
        {
            var printers = await _printService.GetAvailablePrintersAsync(token);

            foreach (var p in printers.OrderBy(p => p.Name))
            {
                AvailablePrinters.Add(p);
            }

            if (AvailablePrinters.Count > 0)
            {
                SelectedPrinter = AvailablePrinters[0];
            }
            else
            {
                StatusMessage = "No printers found.";
                HasError = true;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled – no action.
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load printers: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsLoadingPrinters = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPrint))]
    private async Task Print(CancellationToken token)
    {
        var pageNumbers = GetSelectedPageNumbers();
        if (pageNumbers is null || pageNumbers.Count == 0 || SelectedPrinter is null)
        {
            return;
        }

        IsPrinting = true;
        PrintPagesProcessed = 0;
        PrintTotalPages = pageNumbers.Count;
        StatusMessage = "Sending to printer…";
        HasError = false;

        var progress = new Progress<int>(pagesProcessed =>
        {
            if (pagesProcessed < PrintTotalPages)
            {
                PrintPagesProcessed = pagesProcessed;
                StatusMessage = $"Printing page {pagesProcessed} of {PrintTotalPages}…";
            }
            else
            {
                PrintPagesProcessed = 0;
                StatusMessage = "Finishing…";
            }
        });

        var printer = SelectedPrinter;
        try
        {
            await Task.Run(async () =>
                {
                    var pages = pageNumbers
                        .Select(n => new PrintPageInfo(n, _pageRotations.GetValueOrDefault(n, 0)))
                        .ToArray();
                    await _printService.PrintDocumentAsync(printer, _documentService, pages, progress, token);
                },
                token);

            StatusMessage = "Print job sent.";
            PrintCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Print cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Print failed: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsPrinting = false;
        }
    }

    private bool CanPrint()
    {
        if (SelectedPrinter is null)
        {
            return false;
        }

        if (IsCustomRangeSelected && GetSelectedPageNumbers() is null)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the list of 1-based page numbers to print, or <c>null</c> if the range
    /// is invalid (e.g. a custom range that cannot be parsed).
    /// </summary>
    public IReadOnlyCollection<int>? GetSelectedPageNumbers()
    {
        if (IsAllPagesSelected)
        {
            return Enumerable.Range(1, TotalPages).ToArray();
        }

        if (IsCurrentPageSelected)
        {
            return [CurrentPageNumber];
        }

        if (IsCustomRangeSelected)
        {
            return ParseCustomRange(CustomPageRange, TotalPages);
        }

        return null;
    }

    private static IReadOnlyCollection<int>? ParseCustomRange(string range, int totalPages)
    {
        if (string.IsNullOrWhiteSpace(range))
        {
            return null;
        }

        var span = range.AsSpan();

        var result = new SortedSet<int>();
        var ranges = span.Split(',');

        foreach (var r in ranges)
        {
            var part = span[r].Trim();

            if (part.IsEmpty)
            {
                continue;
            }

            var dash = part.IndexOf('-');
            if (dash > 0)
            {
                if (int.TryParse(part[..dash].Trim(), out int start) &&
                    int.TryParse(part[(dash + 1)..].Trim(), out int end))
                {
                    start = Math.Clamp(start, 1, totalPages);
                    end = Math.Clamp(end, 1, totalPages);
                    if (start <= end)
                    {
                        for (int i = start; i <= end; i++)
                        {
                            result.Add(i);
                        }
                    }
                }
            }
            else if (int.TryParse(part, out int page))
            {
                if (page >= 1 && page <= totalPages)
                {
                    result.Add(page);
                }
            }
        }

        return result.Count > 0 ? result : null;
    }
}
