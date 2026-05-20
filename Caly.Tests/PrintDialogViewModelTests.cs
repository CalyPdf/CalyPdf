using Avalonia.Platform.Storage;
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Pdf.Models;
using SkiaSharp;

namespace Caly.Tests;

public sealed class FakePrintService : IPrintService
{
    public IReadOnlyList<PrinterInfo> Printers { get; set; } = Array.Empty<PrinterInfo>();
    public PrinterCapabilities Capabilities { get; set; } = new(true, true, true, new[] { 1, 2, 4 });
    public PrintSettings? LastSettings { get; private set; }

    public Task<IReadOnlyList<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken token = default)
        => Task.FromResult(Printers);

    public Task<PrinterCapabilities> GetPrinterCapabilitiesAsync(PrinterInfo printer, CancellationToken token = default)
        => Task.FromResult(Capabilities);

    public Task PrintDocumentAsync(
        PrinterInfo printer,
        IPdfDocumentService documentService,
        IReadOnlyList<PrintPageInfo> pages,
        PrintSettings settings,
        IProgress<int>? progress,
        CancellationToken token)
    {
        LastSettings = settings;
        return Task.CompletedTask;
    }
}

public sealed class FakePdfDocumentService : IPdfDocumentService
{
    public int NumberOfPages => 5;
    public string? FileName => "test.pdf";

    // All other members throw NotImplementedException so accidental exercising of the
    // document path during VM unit tests fails loudly rather than silently passing.

    public double PpiScale => throw new NotImplementedException();
    // IsActive has an internal setter in the interface. Caly.Core grants internals access to
    // Caly.Tests via InternalsVisibleTo, so we can implement the setter directly.
    public bool IsActive { get; set; }
    public long? FileSize => throw new NotImplementedException();
    public string? LocalPath => throw new NotImplementedException();
    public bool IsPasswordProtected => throw new NotImplementedException();

    public Task<int> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token)
        => throw new NotImplementedException();

    public Task<DocumentPropertiesViewModel?> GetDocumentPropertiesAsync(CancellationToken token)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<PdfBookmarkNode>?> GetPdfBookmark(CancellationToken token)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<PdfEmbeddedFileViewModel>?> GetEmbeddedFileAsync(CancellationToken token)
        => throw new NotImplementedException();

    public Task<UglyToad.PdfPig.Rendering.Skia.PdfPageSize?> GetPageSizeAsync(int pageNumber, CancellationToken token)
        => throw new NotImplementedException();

    public Task<PdfTextLayer?> GetPageTextLayerAsync(int pageNumber, CancellationToken token)
        => throw new NotImplementedException();

    public Task<IRef<SKPicture>?> GetRenderPageAsync(int pageNumber, CancellationToken token)
        => throw new NotImplementedException();

    public ValueTask DisposeAsync() => throw new NotImplementedException();
}

public class PrintDialogViewModelOrientationTests
{
    private static PrintDialogViewModel NewVm()
        => new(new FakePrintService(), new FakePdfDocumentService(), currentPageNumber: 1);

    [Fact]
    public void Orientation_DefaultIsAuto()
    {
        var vm = NewVm();
        Assert.True(vm.IsOrientationAuto);
        Assert.False(vm.IsOrientationPortrait);
        Assert.False(vm.IsOrientationLandscape);
    }

    [Fact]
    public void Orientation_SelectingPortrait_ClearsOthers()
    {
        var vm = NewVm();
        vm.IsOrientationPortrait = true;
        Assert.False(vm.IsOrientationAuto);
        Assert.True(vm.IsOrientationPortrait);
        Assert.False(vm.IsOrientationLandscape);
    }

    [Fact]
    public void Orientation_SelectingLandscape_ClearsOthers()
    {
        var vm = NewVm();
        vm.IsOrientationLandscape = true;
        Assert.False(vm.IsOrientationAuto);
        Assert.False(vm.IsOrientationPortrait);
        Assert.True(vm.IsOrientationLandscape);
    }
}

public class PrintDialogViewModelFitTests
{
    private static PrintDialogViewModel NewVm()
        => new(new FakePrintService(), new FakePdfDocumentService(), currentPageNumber: 1);

    [Fact]
    public void Fit_DefaultIsFitToPage()
    {
        var vm = NewVm();
        Assert.True(vm.IsFitToPage);
    }

    [Fact]
    public void Fit_SelectingActualSize_ClearsOthers()
    {
        var vm = NewVm();
        vm.IsActualSize = true;
        Assert.False(vm.IsFitToPage);
        Assert.False(vm.IsShrinkToFit);
        Assert.False(vm.IsCustomScale);
    }

    [Fact]
    public void Fit_SelectingCustomScale_ClearsOthers()
    {
        var vm = NewVm();
        vm.IsCustomScale = true;
        Assert.True(vm.IsCustomScale);
        Assert.False(vm.IsFitToPage);
    }

    [Fact]
    public void CustomScalePercent_DefaultIs100()
    {
        var vm = NewVm();
        Assert.Equal(100, vm.CustomScalePercent);
    }
}

public class PrintDialogViewModelOtherPropsTests
{
    private static PrintDialogViewModel NewVm()
        => new(new FakePrintService(), new FakePdfDocumentService(), currentPageNumber: 1);

    [Fact]
    public void PagesPerSheet_DefaultIs1()
    {
        Assert.Equal(1, NewVm().PagesPerSheet);
    }

    [Fact]
    public void IsBlackAndWhite_DefaultIsFalse()
    {
        Assert.False(NewVm().IsBlackAndWhite);
    }
}

public class PrintDialogViewModelCapabilitiesTests
{
    [Fact]
    public async Task SelectingPrinter_LoadsCapabilities()
    {
        var svc = new FakePrintService();
        var caps = new PrinterCapabilities(true, true, true, new[] { 1, 2, 4 });
        svc.Capabilities = caps;
        var vm = new PrintDialogViewModel(svc, new FakePdfDocumentService(), 1);

        vm.SelectedPrinter = new PrinterInfo("p1", null);
        await Task.Delay(50);  // capability load is fire-and-forget

        Assert.Equal(caps, vm.SelectedPrinterCapabilities);
    }

    [Fact]
    public async Task SwitchingToPortraitOnlyPrinter_ResetsLandscape()
    {
        var svc = new FakePrintService();
        var vm = new PrintDialogViewModel(svc, new FakePdfDocumentService(), 1);

        // Start with all-supported.
        svc.Capabilities = new PrinterCapabilities(true, true, true, new[] { 1, 2, 4 });
        vm.SelectedPrinter = new PrinterInfo("color", null);
        await Task.Delay(50);
        vm.IsOrientationLandscape = true;

        // Switch to a printer that does not support landscape.
        svc.Capabilities = new PrinterCapabilities(false, true, true, new[] { 1, 2, 4 });
        vm.SelectedPrinter = new PrinterInfo("portrait-only", null);
        await Task.Delay(50);

        Assert.True(vm.IsOrientationAuto);
        Assert.False(vm.IsOrientationLandscape);
    }
}

public class PrintDialogViewModelPrintCommandTests
{
    [Fact]
    public async Task PrintCommand_BuildsSettingsFromVmState()
    {
        var svc = new FakePrintService();
        var vm = new PrintDialogViewModel(svc, new FakePdfDocumentService(), 1);
        vm.SelectedPrinter = new PrinterInfo("p1", null);
        await Task.Delay(50);

        vm.IsOrientationLandscape = true;
        vm.IsCustomScale = true;
        vm.CustomScalePercent = 75;
        vm.PagesPerSheet = 2;
        vm.IsBlackAndWhite = true;

        await vm.PrintCommand.ExecuteAsync(null);

        Assert.NotNull(svc.LastSettings);
        Assert.Equal(PrintOrientation.Landscape, svc.LastSettings!.Orientation);
        Assert.Equal(PrintFitMode.CustomScale, svc.LastSettings.FitMode);
        Assert.Equal(75, svc.LastSettings.CustomScalePercent);
        Assert.Equal(2, svc.LastSettings.PagesPerSheet);
        Assert.Equal(PrintColorMode.Monochrome, svc.LastSettings.ColorMode);
    }

    [Fact]
    public async Task PrintCommand_CustomScaleClampedTo10To400()
    {
        var svc = new FakePrintService();
        var vm = new PrintDialogViewModel(svc, new FakePdfDocumentService(), 1);
        vm.SelectedPrinter = new PrinterInfo("p1", null);
        await Task.Delay(50);

        vm.IsCustomScale = true;
        vm.CustomScalePercent = 500;  // out of range

        await vm.PrintCommand.ExecuteAsync(null);

        Assert.Equal(400, svc.LastSettings!.CustomScalePercent);
    }
}
