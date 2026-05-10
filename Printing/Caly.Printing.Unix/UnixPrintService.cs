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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core;
using Caly.Core.Services.Interfaces;
using Caly.Printing.Core;
using SharpIpp;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

namespace Caly.Printing.Unix;

/// <summary>
/// Unix (Linux/macOS) print service.
/// <para>
/// Printer discovery tries CUPS IPP first, then falls back to <c>lpstat -a</c>.
/// Jobs are sent via SharpIppNext as JPEG images over IPP.
/// </para>
/// </summary>
public sealed class UnixPrintService : IPrintService, IDisposable
{
    private const string CupsLocalUriStr = "ipp://localhost:631/";

    /// <summary>
    /// Builds the standard CUPS IPP URI for a named printer on localhost.
    /// </summary>
    private static Uri BuildCupsUri(string printerName)
        => new($"{CupsLocalUriStr}printers/{Uri.EscapeDataString(printerName)}");

    // Re-used across all IPP calls for this service instance.
    private readonly SharpIppClient _ippClient = new();

    private static readonly Uri s_cupsLocalUri = new(CupsLocalUriStr);

    public void Dispose() => _ippClient.Dispose();

    // -------------------------------------------------------------------------
    // Printer discovery
    // -------------------------------------------------------------------------

    public Task<IReadOnlyList<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken token)
    {
        return Task.Run(() => GetUnixPrintersCore(token), token);
    }

    private async Task<IReadOnlyList<PrinterInfo>> GetUnixPrintersCore(CancellationToken token)
    {
        try
        {
            var cupsPrinters = await GetCupsPrintersAsync(token)
                .ConfigureAwait(false);

            if (cupsPrinters.Count > 0)
            {
                return cupsPrinters;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UnixPrintService: CUPS discovery failed: {ex.Message}");
        }

        return await GetLpstatPrintersAsync(token)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PrinterInfo>> GetCupsPrintersAsync(CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        var request = new CUPSGetPrintersRequest
        {
            OperationAttributes = new CUPSGetPrintersOperationAttributes
            {
                PrinterUri = s_cupsLocalUri
            }
        };

        var response = await _ippClient.GetCUPSPrintersAsync(request, token)
            .ConfigureAwait(false);

        var attrs = response.PrintersAttributes;
        if (attrs is null || attrs.Length == 0)
        {
            return [];
        }

        var result = new List<PrinterInfo>(attrs.Length);
        foreach (var p in attrs)
        {
            var name = p.PrinterName;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var uriStr = p.PrinterUriSupported?.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
            if (uriStr is null || !Uri.TryCreate(uriStr, UriKind.Absolute, out var uri))
            {
                uri = BuildCupsUri(name);
            }

            result.Add(new PrinterInfo(name, uri));
        }

        return result;
    }

    private static async Task<IReadOnlyList<PrinterInfo>> GetLpstatPrintersAsync(CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        var output = await RunProcessAsync("lpstat", ["-a"], token)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        var result = new List<PrinterInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var space = line.IndexOf(' ');
            var name = space > 0 ? line[..space] : line;
            if (!string.IsNullOrWhiteSpace(name))
            {
                result.Add(new PrinterInfo(name, BuildCupsUri(name)));
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Printing
    // -------------------------------------------------------------------------

    public Task PrintDocumentAsync(
        PrinterInfo printer,
        IPdfDocumentService documentService,
        IReadOnlyList<PrintPageInfo> pages,
        IProgress<int>? progress,
        CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        return PrintIppAsync(printer, documentService, pages, progress, token);
    }

    // -------------------------------------------------------------------------
    // Unix — IPP path (JPEG images)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Renders each page to a JPEG and sends all pages as a single CUPS job using
    /// <c>Create-Job</c> followed by one <c>Send-Document</c> per page.
    /// <para>
    /// <c>RequestingUserName</c> is set on every request. For localhost connections CUPS
    /// trusts this attribute as the authenticated user, which satisfies the
    /// <c>Require user @OWNER</c> policy that <c>Send-Document</c> enforces.
    /// Without it, <c>Send-Document</c> returns HTTP 401 even from localhost.
    /// </para>
    /// </summary>
    private async Task PrintIppAsync(
        PrinterInfo printer,
        IPdfDocumentService documentService,
        IReadOnlyList<PrintPageInfo> pages,
        IProgress<int>? progress,
        CancellationToken token)
    {
        // PDF points are 1/72 inch; render at 200 DPI for print quality.
        const float printScale = 200f / 72f;

        string docName = documentService.FileName ?? "Document";
        string userName = Environment.UserName;
        int? jobId = null;
        bool jobClosed = false;
        int pagesProcessed = 0;

        try
        {
            // Create-Job owns all pages in one queue entry and sets the job owner so
            // subsequent Send-Document requests pass the @OWNER policy check.
            var createRequest = new CreateJobRequest
            {
                OperationAttributes = new CreateJobOperationAttributes
                {
                    PrinterUri = printer.IppUri,
                    RequestingUserName = userName,
                    JobName = docName
                },
                JobTemplateAttributes = new JobTemplateAttributes
                {
                    PrintScaling = PrintScaling.Fit,
                    //PrinterResolution = new Resolution(200, 200, ResolutionUnit.DotsPerInch)
                }
            };

            var createResponse = await _ippClient.CreateJobAsync(createRequest, token)
                .ConfigureAwait(false);

            if ((short)createResponse.StatusCode >= 0x0100)
            {
                throw new InvalidOperationException($"The printer rejected the job (IPP status {createResponse.StatusCode:X4}: {createResponse.StatusCode}).");
            }

            jobId = createResponse.JobAttributes?.JobId
                ?? throw new InvalidOperationException("The printer did not return a job-id.");

            for (int i = 0; i < pages.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                // Render one page, encode it, then dispose — only one bitmap in memory at a time.
                using var bitmap = await PrintServiceHelper.RenderPageToBitmapAsync(documentService, pages[i], token, printScale)
                    .ConfigureAwait(false);

                if (bitmap is null)
                {
                    continue;
                }

                bool isLast = i == pages.Count - 1;

                using var jpegStream = PrintServiceHelper.EncodeJpeg(bitmap);

                var sendRequest = new SendDocumentRequest
                {
                    Document = jpegStream,
                    OperationAttributes = new SendDocumentOperationAttributes
                    {
                        PrinterUri = printer.IppUri,
                        RequestingUserName = userName,
                        JobId = jobId.Value,
                        DocumentFormat = "image/jpeg",
                        LastDocument = isLast
                    }
                };

                var sendResponse = await _ippClient.SendDocumentAsync(sendRequest, token)
                    .ConfigureAwait(false);

                if ((short)sendResponse.StatusCode >= 0x0100)
                {
                    throw new InvalidOperationException($"Send-Document failed (IPP status {sendResponse.StatusCode:X4}: {sendResponse.StatusCode}).");
                }

                if (isLast)
                {
                    jobClosed = true;
                }

                progress?.Report(++pagesProcessed);
            }
        }
        finally
        {
            // If the job was opened but never closed (exception, cancellation, or all pages
            // rendered null), close it so it does not linger in the queue.
            // CancellationToken.None ensures cleanup always runs.
            if (jobId.HasValue && !jobClosed)
            {
                using var emptyStream = new System.IO.MemoryStream();
                var finalRequest = new SendDocumentRequest
                {
                    Document = emptyStream,
                    OperationAttributes = new SendDocumentOperationAttributes
                    {
                        PrinterUri = printer.IppUri,
                        RequestingUserName = userName,
                        JobId = jobId.Value,
                        DocumentFormat = "image/jpeg",
                        LastDocument = true
                    }
                };

                _ = await _ippClient.SendDocumentAsync(finalRequest, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<string> RunProcessAsync(string command, string[] args, CancellationToken token)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null)
        {
            return string.Empty;
        }

        var output = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        return output;
    }
}
