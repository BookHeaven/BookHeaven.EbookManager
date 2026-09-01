using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using BookHeaven.EbookManager.Abstractions;
using BookHeaven.EbookManager.Entities;
using BookHeaven.EbookManager.Extensions;
using BookHeaven.EbookManager.Formats.Epub.XML;
using HtmlAgilityPack;
using HtmlAgilityPack.CssSelectors.NetCore;

namespace BookHeaven.EbookManager.Formats.Epub.Services;
public partial class EpubReader : IEbookReader
{
	private static readonly ConcurrentDictionary<Type, XmlSerializer> Serializers = [];
	private static readonly XmlReaderSettings XmlReaderSettings = new()
	{
		DtdProcessing = DtdProcessing.Parse,
		Async = true
	};
	
	private ZipArchive? _zipArchive;
	private SemaphoreSlim? _zipLock;

	private string _cacheFolderName = string.Empty;
	private Package? _package;
	private string? _rootFolder;
	private string? _coverPath;
	
	private readonly ConcurrentDictionary<string, string> _contentCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, byte[]> _images = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, ZipArchiveEntry?> _archiveEntries = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _contentLoaders = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _imageLoaders = new(StringComparer.OrdinalIgnoreCase);

	public async Task<Ebook> ReadMetadataAsync(string path)
	{
		return await ReadAsync(path);
	}
	
	public async Task<Ebook> ReadAllAsync(string path)
	{
		if (!string.IsNullOrWhiteSpace(EbookManagerGlobals.CachePath))
		{
			_cacheFolderName = Path.GetFileNameWithoutExtension(path);
		}
		return await ReadAsync(path, false);
	} 
	

	/// <summary>
	/// Reads the contents of an epub file. Already calls LoadEpub.
	/// </summary>
	/// <param name="path">Physical File path</param>
	/// <param name="metadataOnly">Whether to only retrieve metadata or the contents as well. True by default.</param>
	/// <returns></returns>
	private async Task<Ebook> ReadAsync(string path, bool metadataOnly = true)
	{
		var ebook = new Ebook
		{
			FilePath = path
		};
		
		var packagePath = await GetOpfPathAsync(path);

		try
		{
			_rootFolder = Path.GetDirectoryName(packagePath)!;
			_package = await ReadEntryAsync<Package>(packagePath);

			ebook.Cover = await LoadCoverImageAsBytesAsync();
			ebook.GetMetadataFromEpub(_package.Metadata);

			if (!metadataOnly)
			{
				// Load content (spine and chapters)
				ebook.Content = await LoadContent();
			}
		}
		catch (Exception e)
		{
			throw new Exception("Error reading epub file", e);
		}
		finally
		{
			if(metadataOnly) Dispose();
		}
			
		return ebook;
	}

	/// <summary>
	/// Gets the path to the OPF file inside the epub
	/// </summary>
	/// <returns>OPF path</returns>
	public async Task<string> GetOpfPathAsync(string epubPath)
	{
		_zipArchive = await ZipFile.OpenReadAsync(epubPath);
		_zipLock = new(1, 1);
		var container = await ReadEntryAsync<Container>("META-INF/container.xml");
		var rootFile = container.RootFiles.RootFile
			.FirstOrDefault(x => string.Equals(x.MediaType, "application/oebps-package+xml", StringComparison.OrdinalIgnoreCase))
			?? container.RootFiles.RootFile.FirstOrDefault()
			?? throw new Exception("No OPF root file found in epub container.");
		return rootFile.FullPath;
	}

	/// <summary>
	/// Returns the absolute path of a file inside the epub
	/// </summary>
	/// <param name="path">Relative path</param>
	/// <returns>Absolute path</returns>
	private string? GetAbsolutePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		var normalizedPath = NormalizeArchivePath(path);
		if (string.IsNullOrEmpty(normalizedPath))
		{
			return null;
		}

		if (!string.IsNullOrEmpty(_rootFolder))
		{
			var rootFolder = NormalizeArchivePath(_rootFolder);
			if (!string.IsNullOrEmpty(rootFolder)
				&& !normalizedPath.StartsWith(rootFolder + "/", StringComparison.Ordinal)
				&& !normalizedPath.Equals(rootFolder, StringComparison.Ordinal))
			{
				normalizedPath = $"{rootFolder.TrimEnd('/')}/{normalizedPath.TrimStart('/')}";
			}
		}

		return normalizedPath.TrimStart('/');
	}

	private static string NormalizeArchivePath(string value)
	{
		var normalized = WebUtility.UrlDecode(value).Replace('\\', '/').Trim();
		if (string.IsNullOrEmpty(normalized))
		{
			return string.Empty;
		}

		var segments = new List<string>();
		foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			switch (segment)
			{
				case ".":
					continue;
				case "..":
				{
					if (segments.Count > 0)
					{
						segments.RemoveAt(segments.Count - 1);
					}
					continue;
				}
				default:
					segments.Add(segment);
					break;
			}
		}

		return string.Join("/", segments);
	}

	/// <summary>
	/// Deserializes an entry from the epub file
	/// </summary>
	/// <typeparam name="T">Entry Type</typeparam>
	/// <param name="path">File path inside the epub</param>
	/// <returns>Object of type T</returns>
	/// <exception cref="Exception"></exception>
	private async Task<T> ReadEntryAsync<T>(string path)
	{
		var entry = _zipArchive!.GetEntry(GetAbsolutePath(path)!) ?? throw new Exception($"File not found inside epub. {GetAbsolutePath(path)}");

		await using var stream = await entry.OpenAsync();
		var serializer = Serializers.GetOrAdd(typeof(T), t => new XmlSerializer(t));
		try
		{
			using var reader = XmlReader.Create(stream, XmlReaderSettings);
			return (T)serializer.Deserialize(reader)!;
		}
		catch (Exception e)
		{
			throw new Exception($"Error deserializing entry: {GetAbsolutePath(path)}", e);
		}
	}

	/// <summary>
	/// Loads the cover image from the epub
	/// </summary>
	/// <returns>Image as bytes</returns>
	private async Task<byte[]?> LoadCoverImageAsBytesAsync()
	{
		var coverMeta = _package?.Metadata.Meta.FirstOrDefault(x =>
			string.Equals(x.Name, "cover", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(x.Property, "cover", StringComparison.OrdinalIgnoreCase));
		var coverId = coverMeta?.Content;

		var cover = _package?.Manifest.Items.FirstOrDefault(item => item.Id == coverId)
			?? _package?.Manifest.Items.FirstOrDefault(item => HasProperty(item, "cover-image"))
			?? _package?.Manifest.Items.FirstOrDefault(item => string.Equals(item.Id, "cover", StringComparison.OrdinalIgnoreCase));

		return cover is null
			? null
			: await LoadImageAsBytes(cover.Href);
	}

	/// <summary>
	/// Converts an image to bytes
	/// </summary>
	/// <param name="path">Path inside the epub</param>
	/// <returns>Image as bytes</returns>
	private async Task<byte[]> LoadImageAsBytes(string path)
	{
		if (_zipLock is null)
		{
			throw new Exception("EpubReader not initialized. Call GetOpfPathAsync first.");
		}

		var absolutePath = GetAbsolutePath(path)!;
		if (_images.TryGetValue(absolutePath, out var cachedImage))
		{
			return cachedImage;
		}

		var loader = _imageLoaders.GetOrAdd(
			absolutePath,
			static (path, reader) => new Lazy<Task<byte[]>>(() => reader.LoadBinaryResourceAsync(path), LazyThreadSafetyMode.ExecutionAndPublication),
			this);
		var bytes = await loader.Value;
		_images[absolutePath] = bytes;
		return bytes;
	}

	private async Task<byte[]> LoadBinaryResourceAsync(string absolutePath)
	{
		var memory = new MemoryStream();
		await _zipLock!.WaitAsync();
		try
		{
			var entry = GetArchiveEntry(absolutePath);
			if (entry is null)
			{
				return [];
			}

			await using var stream = await entry.OpenAsync();
			await stream.CopyToAsync(memory);
		}
		finally
		{
			_zipLock.Release();
		}

		return memory.ToArray();
	}

	private ZipArchiveEntry? GetArchiveEntry(string absolutePath)
	{
		return _archiveEntries.GetOrAdd(
			absolutePath,
			static (path, reader) => reader._zipArchive!.GetEntry(path),
			this);
	}

	/// <summary>
	/// Loads the content of the epub, which includes both the Spine (index) and the chapters
	/// </summary>
	/// <returns></returns>
	private async Task<Content> LoadContent()
	{
		var content = new Content();

		var cssFiles = _package!.Manifest.Items.Where(x => x.MediaType.Equals("text/css"));
		content.Stylesheets = await LoadStylesheets(cssFiles);

		List<TocEntry> tableOfContents;
		TocEntry? cover = null;
		var coverItem = _package!.Manifest.Items.FirstOrDefault(x => x.Id == _package.Spine.ItemRefs.FirstOrDefault()?.IdRef);
		if (coverItem != null)
		{
			_coverPath = coverItem.Href;
			cover = new()
			{
				Id = coverItem.Id,
				Title = "Cover",
			};
		}

		var navItem = _package.Manifest.Items.FirstOrDefault(i => HasProperty(i, "nav"));
		if (navItem is not null)
		{
			// V3 NAV TOC
			var nav = await LoadNavAsync(navItem.Href);
			tableOfContents = MapNavToTableOfContents(nav.ChapterList.SelectMany(x => x.Chapter));
		}
		else if (_package!.Spine.Toc != null)
		{
			// V2 NCX TOC
			var ncx = await ReadEntryAsync<NCX>(_package.Manifest.Items.First(x => x.Id == _package.Spine.Toc).Href);
			tableOfContents = MapNavMapToTableOfContents(ncx.NavMap);
		}
		else
		{
			throw new Exception("Error parsing epub: No Table of Contents found");
		}
		if(cover != null)
		{
			if(tableOfContents.Count == 1)
			{
				var entries = tableOfContents.First().Entries.ToList();
				entries.Insert(0, cover);
				tableOfContents.First().Entries = entries;
			}
			else
			{
				tableOfContents.Insert(0, cover);
			}
		}

		content.TableOfContents = tableOfContents;
		
		content.Chapters = await MapSpineToChapters(tocContainsId: id => content.GetChapterFromTableOfContents(id) is not null);
		ClearTransientCaches();

		return content;

	}

	private void ClearTransientCaches()
	{
		_contentCache.Clear();
		_images.Clear();
		_archiveEntries.Clear();
		_contentLoaders.Clear();
		_imageLoaders.Clear();
	}

	private async Task<IReadOnlyList<Stylesheet>> LoadStylesheets(IEnumerable<Item> cssFiles)
	{
		var cssTasks = cssFiles.Select(async item =>
		{
			var css = await LoadFileContentAsync(item.Href);
			var imports = CssImportRegex().Matches(css);
			foreach (var import in imports.Cast<Match>())
			{
				css = css.Replace(import.Value, null);
			}
			var fontFaces = FontFaceRegex().Matches(css);
			foreach (var fontFace in fontFaces.Cast<Match>())
			{
				css = css.Replace(fontFace.Value, null);
			}
			var processedCss = HtmlManager.ApplyCssProcessing(css);
			return new Stylesheet { Identifier= item.Href, Content = processedCss};
		});
		return await Task.WhenAll(cssTasks);
	}

	/// <summary>
	/// Recursively loads the chapters from the NCX TOC (optimized for lower-end devices)
	/// </summary>
	/// <param name="navpoints">List of NXC NavPoints</param>
	/// <returns>List of TocEntry</returns>
	private List<TocEntry> MapNavMapToTableOfContents(List<NCXNavPoint> navpoints)
	{
		var entries = new List<TocEntry>();
		foreach (var navPoint in navpoints)
		{
			if (_coverPath is not null && _coverPath.EndsWith(CleanPath(navPoint.Content?.Src) ?? " "))
			{
				continue;
			}

			var chapter = new TocEntry
			{
				Title = navPoint.NavLabel?.Text,
				Id = _package!.Manifest.Items.FirstOrDefault(x => x.Href.EndsWith(CleanPath(navPoint.Content?.Src) ?? " "))?.Id
			};

			if (navPoint.NavPoints.Count > 0)
			{
				// Recursively process child navpoints, but synchronously
				chapter.Entries = MapNavMapToTableOfContents(navPoint.NavPoints);
			}

			entries.Add(chapter);
		}
		return entries;
	}

	/// <summary>
	/// Maps the V3 NAV TOC to an EpubChapter list recursively
	/// </summary>
	/// <param name="navItems">List of Nav li items</param>
	/// <returns>List of TocEntry</returns>
	private List<TocEntry> MapNavToTableOfContents(IEnumerable<NavLi> navItems)
	{
		var entries = new List<TocEntry>();
		foreach (var navItem in navItems)
		{
			var href = navItem.Link?.Href;
			if (_coverPath is not null && !string.IsNullOrWhiteSpace(href) && _coverPath.EndsWith(CleanPath(href) ?? " "))
			{
				continue;
			}

			var chapter = new TocEntry
			{
				Title = GetNavItemTitle(navItem),
				Id = GetManifestItemId(href)
			};

			if (navItem.ChapterList.Count > 0)
			{
				chapter.Entries = MapNavToTableOfContents(navItem.ChapterList.SelectMany(x => x.Chapter));
			}

			if (navItem.Link is null && chapter.Entries.Count == 0 && string.IsNullOrWhiteSpace(chapter.Title))
			{
				continue;
			}

			entries.Add(chapter);
		}
		return entries;
	}

	private static string? GetNavItemTitle(NavLi navItem)
	{
		var title = navItem.Link?.Text ?? navItem.Label?.Text;
		return string.IsNullOrWhiteSpace(title) ? title : title.Trim();
	}

	private string? GetManifestItemId(string? href)
	{
		if (string.IsNullOrWhiteSpace(href))
		{
			return null;
		}

		var target = NormalizeArchivePath(CleanPath(href) ?? string.Empty);
		if (string.IsNullOrEmpty(target))
		{
			return null;
		}

		return _package?.Manifest.Items.FirstOrDefault(x =>
			string.Equals(NormalizeArchivePath(CleanPath(x.Href) ?? string.Empty), target, StringComparison.OrdinalIgnoreCase))?.Id;
	}

	private static bool HasProperty(Item item, string propertyName)
	{
		if (string.IsNullOrWhiteSpace(item.Properties))
		{
			return false;
		}

		return item.Properties
			.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Contains(propertyName, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Maps the spine to a list of SpineItem
	/// </summary>
	/// <returns></returns>
	private async Task<List<Chapter>> MapSpineToChapters(Func<string, bool> tocContainsId)
	{
		if (_package is null)
		{
			return [];
		}

		var manifestItems = _package.Manifest.Items.ToDictionary(static x => x.Id, static x => x, StringComparer.Ordinal);
		var chapters = new List<Chapter>(_package.Spine.ItemRefs.Count);
		var currentChapterId = string.Empty;
		
		foreach (var itemRef in _package.Spine.ItemRefs)
		{
			if (!manifestItems.TryGetValue(itemRef.IdRef, out var item))
			{
				continue;
			}

			if (tocContainsId(item.Id))
			{
				currentChapterId = item.Id;
			}

			var content = await LoadFileContentAsync(item.Href);
			var document = new HtmlDocument();
			document.LoadHtml(content);
			var stylesheets = GetStylesheetsFromHtml(document);
			var bodyNode = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
			var processedContent = await HtmlManager.ApplyHtmlProcessingAsync(bodyNode, LoadImageAsBytes, _cacheFolderName);
			var paragraphClass = processedContent.Length == 0 ? null : GetParagraphClass(processedContent);

			chapters.Add(new Chapter
			{
				Identifier = currentChapterId,
				Content = processedContent,
				Title = GetTitleFromHtml(document),
				Stylesheets = stylesheets,
				IsContentProcessed = true,
				ParagraphClassName = paragraphClass
			});
		}

		return chapters;
	}
		
	/// <summary>
	/// Gets the title of a chapter from the html document
	/// </summary>
	/// <param name="document">Html document</param>
	/// <returns>Title</returns>
	private static string? GetTitleFromHtml(HtmlDocument document)
	{
		var titleNode = document.QuerySelector("title");
		return titleNode != null ? DecodeNumericEntities(titleNode.InnerText) : null;

		static string DecodeNumericEntities(string input)
		{
			return NumericEntitiesRegex().Replace(input, match =>
			{
				var codePoint = int.Parse(match.Groups[1].Value);
				return char.ConvertFromUtf32(codePoint);
			});
		}
	}
		
	/// <summary>
	/// Gets the stylesheets referenced in the html
	/// </summary>
	/// <param name="document">Html document</param>
	/// <returns>List of paths</returns>
	private static List<string> GetStylesheetsFromHtml(HtmlDocument document)
	{
		var linkNodes = document.QuerySelectorAll("link[href]");
		return linkNodes is null ? [] : linkNodes.Select(link => link.GetAttributeValue("href", "")).ToList();
	}

	/// <summary>
	/// Tries to find the most common class in the html content, which is likely to be the paragraph class
	/// </summary>
	/// <param name="content">Html</param>
	/// <returns>Name of the class</returns>
	private static string? GetParagraphClass(string content)
	{
		const int minClassCount = 4;
		
		var matches = CssClassRegex().Matches(content);
		var classFrequency = new Dictionary<string, int>();
		foreach (Match match in matches)
		{
			var classes = match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			foreach (var className in classes)
			{
				if (!classFrequency.TryAdd(className, 1))
				{
					classFrequency[className]++;
				}
			}
		}
		
		return classFrequency.OrderByDescending(c => c.Value).FirstOrDefault(c => c.Value > minClassCount).Key;
	}

	/// <summary>
	/// Removes the anchor from a path (if any)
	/// </summary>
	/// <param name="path">Path inside the epub</param>
	/// <returns>Cleaned path</returns>
	private string? CleanPath(string? path) => path != null && path.Contains('#') ? path[..path.IndexOf('#')] : path;
	
	
	/// <summary>
	/// Loads the content of a file inside the epub
	/// </summary>
	/// <param name="path">Path inside the epub</param>
	/// <returns>Content as string</returns>
	/// <exception cref="Exception"></exception>
	public async Task<string> LoadFileContentAsync(string path)
	{
		if (_zipLock is null)
		{
			throw new Exception("EpubReader not initialized. Call GetOpfPathAsync first.");
		}

		var absolutePath = GetAbsolutePath(path)!;
		if (_contentCache.TryGetValue(absolutePath, out var cachedContent))
		{
			return cachedContent;
		}

		var loader = _contentLoaders.GetOrAdd(
			absolutePath,
			static (path, reader) => new Lazy<Task<string>>(() => reader.LoadTextResourceAsync(path), LazyThreadSafetyMode.ExecutionAndPublication),
			this);
		var content = await loader.Value;
		_contentCache[absolutePath] = content;
		return content;
	}

	private async Task<string> LoadTextResourceAsync(string absolutePath)
	{
		var memory = new MemoryStream();
		await _zipLock!.WaitAsync();
		try
		{
			var entry = GetArchiveEntry(absolutePath) ?? throw new Exception($"Could not load file: {absolutePath}");
			await using var stream = await entry.OpenAsync();
			await stream.CopyToAsync(memory);
		}
		finally
		{
			_zipLock.Release();
		}

		memory.Position = 0;
		using var reader = new StreamReader(memory, Encoding.UTF8, true);
		return await reader.ReadToEndAsync();
	}
	
	/// <summary>
	/// Loads the nav file from the epub
	/// </summary>
	/// <param name="path">Path to load</param>
	/// <returns>Nav object</returns>
	private async Task<Nav> LoadNavAsync(string path)
	{
		var content = await LoadFileContentAsync(path);
		var doc = XDocument.Parse(content);
		var navElement = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "nav")
			?? throw new Exception($"Could not find navigation content in epub file: {path}");
		var serializer = Serializers.GetOrAdd(typeof(Nav), t => new XmlSerializer(t));
		using var reader = navElement.CreateReader();
		return (Nav)serializer.Deserialize(reader)!;
	}

	[GeneratedRegex(@"@import\s*[^;]+;")]
	private static partial Regex CssImportRegex();
	[GeneratedRegex(@"@font-face\s*{[^}]+}")]
	private static partial Regex FontFaceRegex();
	[GeneratedRegex(@"class\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase, "es-ES")]
	private static partial Regex CssClassRegex();
	[GeneratedRegex("&#([0-9]+);")]
	private static partial Regex NumericEntitiesRegex();

    public void Dispose()
    {
	    _cacheFolderName = string.Empty;
	    _rootFolder = null;
	    _package = null;
	    _coverPath = null;
	    ClearTransientCaches();
	    _zipArchive?.Dispose();
	    _zipLock?.Dispose();
	    GC.SuppressFinalize(this);
    }
}
