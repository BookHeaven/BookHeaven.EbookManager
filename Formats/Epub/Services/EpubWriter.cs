using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using BookHeaven.EbookManager.Abstractions;
using BookHeaven.EbookManager.Entities;
using BookHeaven.EbookManager.Extensions;
using BookHeaven.EbookManager.Formats.Epub.Constants;

namespace BookHeaven.EbookManager.Formats.Epub.Services;

public class EpubWriter : IEbookWriter
{
	private async Task<(string path, string xml)> LoadOpfAsync(string bookPath)
	{
		using var reader = new EpubReader();
		var opfPath = await reader.GetOpfPathAsync(bookPath);
		var xml = await reader.LoadFileContentAsync(opfPath);
		return (opfPath, xml);
	}
	    
	public async Task ReplaceMetadataAsync(string bookPath, Ebook ebook)
	{
		var opf = await LoadOpfAsync(bookPath);

		var package = XDocument.Parse(opf.xml);
		var version = int.TryParse(package.Root?.Attribute("version")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 2;
		var metadataElement = package.Descendants().First(x => x.Name.LocalName == "metadata");
		
		metadataElement.SetAttributeValue(XNamespace.Xmlns + "opf", null);
			
		metadataElement.SetItem(Namespaces.Dc, "title", ebook.Title);
		var authorItem = metadataElement.SetItem(Namespaces.Dc, "creator", ebook.Author);
		metadataElement.SetItem(Namespaces.Dc, "description", ebook.Synopsis ?? string.Empty);
		metadataElement.SetItem(Namespaces.Dc, "language", ebook.Language);
		metadataElement.SetItem(Namespaces.Dc, "publisher", ebook.Publisher ?? string.Empty);
		metadataElement.SetItem(Namespaces.Dc, "date", ebook.PublishDate ?? string.Empty);
		//metadataElement.SetItem(Namespaces.Dc, "rights", ebook.Rights ?? string.Empty);
		if (authorItem is not null)
		{
			var fileAs = ebook.Author;
			if (ebook.Author.Contains(' '))
			{
				var parts = ebook.Author.Split(' ');
				fileAs = $"{parts.Last()}, {string.Join(" ", parts.Take(parts.Length - 1))}";
			}

			if (version == 3)
			{
				metadataElement.SetMetaItem("file-as", fileAs, version, authorItem.Attribute("id")?.Value);
			}
			else
			{
				authorItem.SetAttributeValue(XName.Get("file-as", Namespaces.Opf), fileAs);
			}
		}

		if (!string.IsNullOrWhiteSpace(ebook.Series))
		{
			metadataElement.SetMetaItem("calibre:series", ebook.Series, version);
			metadataElement.SetMetaItem("calibre:series_index", ebook.SeriesIndex?.ToString("0.##", CultureInfo.InvariantCulture)!, version);
		}


		await using var archive = await ZipFile.OpenAsync(bookPath, ZipArchiveMode.Update);
		var packageEntry = archive.GetEntry(opf.path)!;
		await using var stream = await packageEntry.OpenAsync();
		stream.SetLength(0);
		var bytes = Encoding.UTF8.GetBytes(package.ToString());
		await stream.WriteAsync(bytes);
	}

	public async Task ReplaceCoverAsync(string bookPath, string newCoverPath)
	{
		//Find cover in meta elements
		var opf = await LoadOpfAsync(bookPath);

		var package = XDocument.Parse(opf.xml);
		var version = int.TryParse(package.Root?.Attribute("version")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 2;
		var rootFolder = Path.GetDirectoryName(opf.path)!;

		var metadataElement = package.Descendants().First(x => x.Name.LocalName == "metadata");

		//Find cover in manifest
		var manifestElement = package.Descendants().First(x => x.Name.LocalName == "manifest");
		var coverPath = version switch
		{
			3 => manifestElement.Descendants().First(x => x.Attribute("properties")?.Value == "cover-image").Attribute("href")?.Value,
			_ => manifestElement.Descendants().First(x => x.Attribute("id")?.Value == metadataElement.Descendants().FirstOrDefault(m => m.Attribute("name")?.Value == "cover")?.Attribute("content")?.Value).Attribute("href")?.Value
		};
		
		if(coverPath is null) return;

		//Replace cover
		var imageAsBytes = await File.ReadAllBytesAsync(newCoverPath);
		await using var archive = await ZipFile.OpenAsync(bookPath, ZipArchiveMode.Update);
		var coverEntry = archive.GetEntry(Path.Combine(rootFolder, coverPath).Replace("\\", "/"));
		if (coverEntry == null) return;
		await using var stream = await coverEntry.OpenAsync();
		stream.SetLength(0);
		await stream.WriteAsync(imageAsBytes);
	}
}