## Caly Pdf Reader: A Fast, Cross-Platform Pdf Reader

<p align="center">
  <img src="https://github.com/user-attachments/assets/604c1d8a-5cdf-49c6-8be3-d85bfb680a99" />
</p>

**Caly Pdf Reader** is a free, cross-platform and open-source Pdf reader built with performance and efficiency in mind. Written in C# (net10.0 with AOT), it's designed to be lightweight, fast, and consume minimal memory.

> [!IMPORTANT]
> The development is currently in alpha.

https://github.com/user-attachments/assets/2c577270-3bc8-4583-9fbe-e37228bb9ce4

### Cross-Platform Compatibility

Caly Pdf Reader leverages the power of [Avalonia UI](https://github.com/AvaloniaUI/Avalonia), [SkiaSharp](https://github.com/mono/SkiaSharp) and [PdfPig](https://github.com/UglyToad/PdfPig) to run seamlessly on Windows, macOS, and Linux.

As of now, only the Windows, Linux and Android versions have been tested. Android version runs, but is not optimised for the platfotm. Better mobile support is planned, including iOS.

## Key Features

* **Tabbed Interface:** Effortlessly manage multiple Pdf documents in separate tabs.
* **Lightning-Fast Navigation:** Navigate through pages with smooth performance.
* **Text Selection and Copy/Paste:** Select, copy, and paste text from Pdfs to the clipboard.
* **Powerful Search:** Quickly locate specific text within documents using the built-in search function.
* **Thumbnail View:** Get a visual overview of all pages with the intuitive thumbnail sidebar.
* **Bookmark Support:** Navigate through bookmarks for quick access to important sections.
* **Zoom In/Out:** Zoom in/out of the document.
* **Rotate pages:** Rotate all or single pages of the document.
* **Minimalist UI:** Enjoy a clean and distraction-free reading experience.

![caly preview](https://github.com/user-attachments/assets/d4382989-2d2e-4fb8-a40e-69757660598a)

## Usage

1. Open the application and click on the "+" (New Tab) button to open a new Pdf document. You can also drag and drop your document or set Caly Pdf Reader as your default reader application.
2. Select a Pdf file to open using the file dialog.
3. The document will be displayed in the tab, and you can navigate through the pages using the navigation buttons.
4. To select text in the document, click and drag the mouse over the text.
5. To copy the selected text, right-click and select "Copy" or use `Ctrl+C`.
6. To search for text in the document, click on the "Search" button in the left menu and enter the text to search for. You can also use `Ctrl+F`.
7. To view page thumbnails, click on the "Thumbnails" button in the left menu.
8. To navigate through bookmarks, click on the "Bookmarks" button in the left menu.
9. To zoom in and out of your document, either use the scroll wheel while pressing `Ctrl`, or use the "Zoom" buttons in the top menu.
10. To pan / move the view in your document, use left-click while pressing `Ctrl`.
11. To rotate pages, either right-click and select one of the rotate options, or use the "Rotate" buttons in the top menu.

## Why Caly Pdf Reader?

* **Open Source:** The source code is freely available on GitHub, allowing for community contributions and customization.
* **Cross-Platform:** Run Caly Pdf Reader on your preferred operating system.
* **Lightweight & Fast:** Designed for optimal performance and minimal resource consumption.
* **Active Development:** We are constantly working on improving Caly Pdf Reader with new features and bug fixes.

![caly preview 2](https://github.com/user-attachments/assets/f850dc94-cefb-43c7-b856-acd34e2fc373)

## Getting Started
### `master` branch
The `master` branch of Caly Pdf Reader uses NuGet packages. You can ignore the `external` folder.

### `develop` branch
The `develop` branch of Caly Pdf Reader uses submodules (located in the `external` folder), you will need to run the following command after cloning it:
```
git submodule update --init --recursive
```

> [!IMPORTANT]
> If the submodule branches appear as 'detached', you can re-link each of them to their respective `develop-caly` branches.

### Publishing
Caly Pdf Reader is a net10.0 application with Native AOT enabled.

#### Release (AOT)
To publish the application, run the following (example for Windows):
```
dotnet publish -r win-x64 -c Release -f net10.0
```

#### Portable
The `Portable` configuration publishes Caly as a single file, using `AOT` and linking skia static libraries (see https://github.com/CalyPdf/SkiaSharp.Static). `Portable` configuration is only available for `win-x64` and `linux-x64` for the moment.

To publish the application (`Portable`), run the following (example for Windows):
```
dotnet publish -r win-x64 -c Portable -f net10.0
```

## Contributing

We welcome contributions from the community! If you find a bug, have a feature request, or want to contribute code, please feel free to do it!

You can also help Caly Pdf Reader by contributing to:
- https://github.com/UglyToad/PdfPig
- https://github.com/BobLd/PdfPig.Rendering.Skia

## License

Caly Pdf Reader is licensed under the [MIT License](https://github.com/CalyPdf/Caly/blob/master/LICENSE).

![caly preview 3](https://github.com/user-attachments/assets/49a5cea8-a407-48b2-9f53-3d3022a120ba)
