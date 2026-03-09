# BookHeaven EbookManager
BookHeaven EbookManager is a .NET library developed for the BookHeaven ecosystem, but it can be used independently in other projects.<br/>
It exposes a set of Services to manipulate eBooks in a limited amount of formats.

It has been designed to extract the contents in html format and provides quite a few css variables to alter the styling of the text.<br/>

More detailed documentation will be added in the future.

## How to use
1. Register the services in your <code>program.cs</code>
```csharp
builder.Services.AddEbookManager();
```
You can optionally pass in a folder path as a parameter to use as a cache for temporary files (like extracted images).<br/>
If no folder is provided, the images will be loaded into base64 strings instead, which may consume a lot of memory.

2. Inject the EbookManagerProvider service and use it to get the appropriate reader or writer for your desired format at runtime.
```csharp
public class MyService(EbookManagerProvider ebookManagerProvider)
{
    private void MyMethod() 
    {
        var reader = ebookManagerProvider.GetReader(Format.Epub);
        var writer = ebookManagerProvider.GetWriter(Format.Epub);
    }
}
```

## Styling
The library exposes a method to apply some custom processing to the styling of a book (support will vary per format, more on that below).</br>
What this process does is basically replace the existing css and html properties with custom ones to be able to easily alter them using css variables.</br>
This process is done automatically for stylesheets on load, but it has to be done on demand for each chapter (for performance reasons, mostly)</br>

### Available css variables
<table>
    <thead>
        <tr>
            <th>Property</th>
            <th>CSS Variable</th>
            <th>Mode</th>
            <th>Unit</th>
        </tr>
    </thead>
    <tbody>
        <tr>
            <td>font-size</td>
            <td>var(--font-size)</td>
            <td>Max</td>
            <td>em</td>
        </tr>
        <tr>
            <td>line-height</td>
            <td>var(--line-height)</td>
            <td>Replace</td>
            <td></td>
        </tr>
        <tr>
            <td>text-indent</td>
            <td>var(--text-indent)</td>
            <td>Replace</td>
            <td>em</td>
        </tr>
        <tr>
            <td>margin-top, margin-bottom</td>
            <td>var(--paragraph-spacing)</td>
            <td>Max</td>
            <td>pt</td>
        </tr>
    </tbody>
</table>

### Replace modes
Not all properties can be handled the same way, so there are a few possible replace modes.

<table>
    <thead>
        <tr>
            <th>Name</th>
            <th>Description</th>
            <th>Result sample</th>
        </tr>
    </thead>
    <tbody>
        <tr>
            <td>Replace</td>
            <td>It replaces the original value</td>
            <td>text-indent: calc(var(--text-indent) * 1em);</td>
        </tr>
        <tr>
            <td>Add</td>
            <td>It adds to the original value. It handles positive and negative values.</td>
            <td>font-size: calc(16pt + (var(--font-size) * 1em));</td>
        </tr>
        <tr>
            <td>Max</td>
            <td>It will use whatever is greater between the original and the custom value.</td>
            <td>margin-bottom: max(20px, calc(var(--paragraph-spacing) * 1pt));</td>
        </tr>
    </tbody>
</table>

## Supported Formats and features

<table>
    <thead>
        <tr>
            <th>Format</th>
            <th>Read Metadata</th>
            <th>Replace Metadata</th>
            <th>Extract Cover</th>
            <th>Replace Cover</th>
            <th>Read Contents</th>
            <th>Custom styling</th>
        </tr>
    </thead>
    <tbody>
        <tr>
            <td>Epub</td>
            <td>✔️</td>
            <td>✔️</td>
            <td>✔️</td>
            <td>✔️</td>
            <td>✔️</td>
            <td>✔️</td>
        </tr>
        <tr>
            <td>Pdf (check notes below)</td>
            <td>✔️</td>
            <td>✔️</td>
            <td>✔️</td>
            <td>✔️</td>
            <td>✔️</td>
            <td>❌</td>
        </tr>
    </tbody>
</table>

### PDF notes
* Metadata in PDF files is limited to title and author, and most of the time they are empty.
* The cover is extracted from the first image of the first page of the PDF, so it will fail if there isn't any images there.
* Text extraction is very basic and may not work properly with complex layouts. It works best with image based PDFs like comics and mangas.

## :package: Credits
- HtmlAgilityPack (https://html-agility-pack.net/)
- HtmlAgilityPack.CssSelectors.NetCore (https://github.com/trenoncourt/HtmlAgilityPack.CssSelectors.NetCore)
- iText Core (https://github.com/itext/itext-dotnet)
- SkiaSharp (https://github.com/mono/SkiaSharp)
