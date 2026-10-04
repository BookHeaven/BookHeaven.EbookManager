using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BookHeaven.EbookManager.Abstractions;
using BookHeaven.EbookManager.Entities;
using BookHeaven.EbookManager.Extensions;
using BookHeaven.EbookManager.Formats.Epub.XML;
using BookHeaven.EbookManager.Helpers;
using HtmlAgilityPack;
using HtmlAgilityPack.CssSelectors.NetCore;
using Microsoft.Extensions.Options;

namespace BookHeaven.EbookManager.Formats.Epub.Services;
public partial class EpubReader(IOptions<EbookManagerOptions> options) : IEbookReader
{
	private ZipArchive? _zipArchive;
	private SemaphoreSlim? _zipLock;

	private string? _cacheFolder;
	private Package? _package;
	private string? _rootFolder;
	private string? _coverPath;
	
	private readonly ConcurrentDictionary<string, string> _contentCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, byte[]> _images = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, ZipArchiveEntry?> _archiveEntries = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _contentLoaders = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _imageLoaders = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, Lazy<Task>> _extractions = new(StringComparer.OrdinalIgnoreCase);

	public async Task<Ebook> ReadMetadataAsync(string path)
	{
		return await ReadAsync(path);
	}

	public async Task<Ebook> ReadAllAsync(string path)
	{
		_cacheFolder = Path.GetFileNameWithoutExtension(path);
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
			_package = await ReadEntryAsync(packagePath, Package.Parse);

			ebook.Cover = await LoadCoverImageAsBytesAsync();
			ebook.GetMetadataFromEpub(_package!.Metadata);

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
			ResetState();
		}

		return ebook;
	}

	/// <summary>
	/// Gets the path to the OPF file inside the epub
	/// </summary>
	/// <returns>OPF path</returns>
	internal async Task<string> GetOpfPathAsync(string epubPath)
	{
		_zipArchive = await ZipFile.OpenReadAsync(epubPath);
		_zipLock = new(1, 1);
		var container = await ReadEntryAsync("META-INF/container.xml", Container.Parse);
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
	/// <param name="parser">Function that builds the typed object from the parsed document.</param>
	/// <returns>Object of type T</returns>
	/// <exception cref="Exception"></exception>
	private async Task<T> ReadEntryAsync<T>(string path, Func<XDocument, T> parser)
	{
		var content = await LoadFileContentAsync(path);
		try
		{
			return parser(XDocument.Parse(content));
		}
		catch (Exception e)
		{
			throw new Exception($"Error parsing entry: {path}", e);
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
		await _zipLock!.WaitAsync();
		try
		{
			var entry = GetArchiveEntry(absolutePath);
			if (entry is null)
			{
				return [];
			}

			await using var stream = await entry.OpenAsync();
			var buffer = new byte[entry.Length];
			await stream.ReadExactlyAsync(buffer);
			return buffer;
		}
		finally
		{
			_zipLock.Release();
		}
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

		// Normalized href (and id) -> item id, built once: GetManifestItemId used to
		// re-normalize every manifest href for every nav entry (O(entries x items)).
		var hrefToItemId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var item in _package!.Manifest.Items)
		{
			var normalized = NormalizeArchivePath(CleanPath(item.Href) ?? string.Empty);
			if (normalized.Length > 0)
				hrefToItemId.TryAdd(normalized, item.Id);
			hrefToItemId.TryAdd(item.Id, item.Id);
		}

		var navItem = _package!.Manifest.Items.FirstOrDefault(i => HasProperty(i, "nav"));
		if (navItem is not null)
		{
			// V3 NAV TOC
			var nav = await LoadNavAsync(navItem.Href);
			tableOfContents = MapNavToTableOfContents(nav.ChapterList.SelectMany(x => x.Chapter), hrefToItemId);
		}
		else if (_package.Spine.Toc != null)
		{
			// V2 NCX TOC
			var ncx = await ReadEntryAsync(_package.Manifest.Items.First(x => x.Id == _package.Spine.Toc).Href, NCX.Parse);
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
		_extractions.Clear();
	}

	private async Task<IReadOnlyList<Stylesheet>> LoadStylesheets(IEnumerable<Item> cssFiles)
	{
		var cssTasks = cssFiles.Select(async item =>
		{
			var css = await LoadFileContentAsync(item.Href);
			// Single-pass removal: the old Matches + per-match Replace loop allocated a
			// MatchCollection plus a new string per match.
			css = CssImportRegex().Replace(css, _ => string.Empty);
			css = FontFaceRegex().Replace(css, _ => string.Empty);
			var processedCss = HtmlHelpers.ApplyCssProcessing(css);
			return new Stylesheet { Identifier = Path.GetFileNameWithoutExtension(item.Href), Content = processedCss  };
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
	/// <param name="hrefToItemId">Dictionary of href to item id</param>
	/// <returns>List of TocEntry</returns>
	private List<TocEntry> MapNavToTableOfContents(IEnumerable<NavLi> navItems, Dictionary<string, string> hrefToItemId)
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
				Id = GetManifestItemId(href, hrefToItemId)
			};

			if (navItem.ChapterList.Count > 0)
			{
				chapter.Entries = MapNavToTableOfContents(navItem.ChapterList.SelectMany(x => x.Chapter), hrefToItemId);
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

	private string? GetManifestItemId(string? href, Dictionary<string, string> hrefToItemId)
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

		return hrefToItemId.GetValueOrDefault(target);
	}

	private static bool HasProperty(Item item, string propertyName)
	{
		if (string.IsNullOrWhiteSpace(item.Properties))
		{
			return false;
		}

		// Span-based token scan: the old Split(' ') allocated an array on every call,
		// and this runs inside a FirstOrDefault predicate over the whole manifest.
		var properties = item.Properties.AsSpan();
		var start = 0;
		while (start < properties.Length)
		{
			while (start < properties.Length && properties[start] == ' ')
				start++;
			var end = start;
			while (end < properties.Length && properties[end] != ' ')
				end++;
			if (end > start && properties.Slice(start, end - start).Equals(propertyName, StringComparison.OrdinalIgnoreCase))
				return true;
			start = end;
		}
		return false;
	}

	private async Task ExtractEntryToFolderAsync(string path, string destinationPath)
	{
		var absolutePath = GetAbsolutePath(path);
		if (absolutePath is null)
		{
			throw new Exception($"Invalid path: {path}");
		}

		await _zipLock!.WaitAsync();
		try
		{
			var entry = GetArchiveEntry(absolutePath);
			if (entry is null)
			{
				throw new Exception($"File not found inside epub: {absolutePath}");
			}
			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
			await entry.ExtractToFileAsync(destinationPath, overwrite: true);
		}
		catch (Exception e)
		{
			throw new Exception($"Error extracting entry: {absolutePath} to {destinationPath}", e);
		}
		finally
		{
			_zipLock.Release();
		}
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
		var itemRefs = _package.Spine.ItemRefs;
		var chapters = new Chapter[itemRefs.Count];
		var currentChapterId = string.Empty;

		// Resolve each spine item up front (cheap, in order) so the workers only do
		// the expensive load + parse + process work.
		var workItems = new (Item Item, string ChapterId)[itemRefs.Count];
		var workCount = 0;
		foreach (var itemRef in itemRefs)
		{
			if (!manifestItems.TryGetValue(itemRef.IdRef, out var item))
			{
				continue;
			}

			if (tocContainsId(item.Id))
			{
				currentChapterId = item.Id;
			}

			workItems[workCount++] = (item, currentChapterId);
		}

		// Chapters are independent, so process them in parallel (bounded; fully async,
		// zip access is serialized by _zipLock inside the loaders). Indexed writes keep
		// the output identical to a sequential run.
		const int maxParallelChapters = 4;
		using var throttle = new SemaphoreSlim(maxParallelChapters);
		var tasks = new Task[workCount];
		for (var i = 0; i < workCount; i++)
		{
			var (item, chapterId) = workItems[i];
			var index = i; // capture per iteration: the loop variable is shared across lambdas
			await throttle.WaitAsync();
			tasks[i] = Task.Run(async () =>
			{
				try
				{
					chapters[index] = await ProcessChapterAsync(item, chapterId);
				}
				finally
				{
					throttle.Release();
				}
			});
		}

		await Task.WhenAll(tasks);
		return [.. chapters];
	}

	/// <summary>
	/// Loads, parses and processes a single spine item into a <see cref="Chapter"/>.
	/// </summary>
	private async Task<Chapter> ProcessChapterAsync(Item item, string chapterId)
	{
		var content = await LoadFileContentAsync(item.Href);

		var document = new HtmlDocument();
		document.LoadHtml(content);

		var nodes = CollectNodes(document.DocumentNode);

		var stylesheets = GetStylesheets(nodes.AllLinks);
		var paragraphClass = GetParagraphClass(nodes.Paragraphs);
		var title = GetTitle(nodes.Title);

		var processedContent = await ApplyHtmlProcessingAsync(nodes.Body!, nodes);

		return new Chapter
		{
			Identifier = chapterId,
			Content = processedContent,
			Title = title,
			Stylesheets = stylesheets,
			ParagraphClassName = paragraphClass
		};
	}

	/// <summary>
	/// Gets the title of a chapter from the collected title node.
	/// </summary>
	/// <param name="titleNode">The title element, if any.</param>
	/// <returns>Title</returns>
	private static string? GetTitle(HtmlNode? titleNode)
	{
		if (titleNode is null)
			return null;
		return DecodeNumericEntities(titleNode.InnerText);

		static string DecodeNumericEntities(string input)
		{
			return NumericEntitiesRegex().Replace(input, match =>
			{
				var group = match.Groups[1];
				// Parse from the match span: Group.Value would allocate per entity.
				var codePoint = int.Parse(input.AsSpan(group.Index, group.Length));
				return char.ConvertFromUtf32(codePoint);
			});
		}
	}

	/// <summary>
	/// Gets the stylesheets referenced by the collected link elements.
	/// </summary>
	/// <param name="links">All link elements in the document.</param>
	/// <returns>List of stylesheet names</returns>
	private static List<string> GetStylesheets(IReadOnlyList<HtmlNode> links)
	{
		var result = new List<string>(links.Count);
		foreach (var link in links)
		{
			var href = link.GetAttributeValue("href", string.Empty);
			if (href.Length > 0)
				result.Add(Path.GetFileNameWithoutExtension(href));
		}
		return result;
	}

	/// <summary>
	/// Tries to find the class used by a majority of the text-containing paragraphs, which is likely to be the paragraph class
	/// </summary>
	/// <param name="paragraphs">Collected paragraph elements.</param>
	/// <returns>Name of the class</returns>
	private static string? GetParagraphClass(IReadOnlyList<HtmlNode> paragraphs)
	{
		const double majorityThreshold = 0.5;

		var classFrequency = new Dictionary<string, int>();
		var paragraphCount = 0;
		foreach (var paragraph in paragraphs)
		{
			if (!HasNonWhitespaceText(paragraph))
			{
				continue;
			}

			paragraphCount++;
			CountClasses(paragraph.GetAttributeValue("class", string.Empty), classFrequency);
		}

		if (paragraphCount == 0)
		{
			return null;
		}

		var threshold = paragraphCount * majorityThreshold;
		string? bestClass = null;
		var bestCount = 0;
		foreach (var (className, count) in classFrequency)
		{
			if (count > threshold && count > bestCount)
			{
				bestCount = count;
				bestClass = className;
			}
		}

		return bestClass;
	}

	/// <summary>
	/// True if the subtree contains any non-whitespace text. Reads text-node values directly:
	/// <c>HtmlNode.InnerText</c> on a text node returns the stored value without allocating,
	/// while on an element it would build a full string (the old per-paragraph check).
	/// </summary>
	private static bool HasNonWhitespaceText(HtmlNode node)
	{
		for (var current = node; current is not null; current = current.NextSibling)
		{
			if (current.NodeType == HtmlNodeType.Text)
			{
				var value = current.InnerText;
				for (var i = 0; i < value.Length; i++)
				{
					if (!char.IsWhiteSpace(value[i]))
						return true;
				}
			}
			else if (current.HasChildNodes && HasNonWhitespaceText(current.FirstChild!))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Counts each whitespace-separated class token in <paramref name="classAttribute"/> without
	/// allocating a split array, accumulating frequencies in <paramref name="frequency"/>.
	/// </summary>
	private static void CountClasses(string classAttribute, Dictionary<string, int> frequency)
	{
		var start = -1;
		for (var i = 0; i <= classAttribute.Length; i++)
		{
			var isBoundary = i == classAttribute.Length || char.IsWhiteSpace(classAttribute[i]);
			if (!isBoundary)
			{
				if (start < 0)
					start = i;
				continue;
			}

			if (start >= 0)
			{
				var token = classAttribute.AsSpan(start, i - start).ToString();
				if (!frequency.TryAdd(token, 1))
					frequency[token]++;
				start = -1;
			}
		}
	}

	/// <summary>
	/// Processes a chapter body, collecting the needed nodes in a single tree walk scoped to
	/// <paramref name="content"/>. Convenience overload for tests and external callers.
	/// </summary>
	public async Task<string> ApplyHtmlProcessingAsync(HtmlNode content)
	{
		var nodes = new ChapterNodes { Body = content };
		CollectNodesWalk(content, nodes, bodyDepth: 1);
		return await ApplyHtmlProcessingAsync(content, nodes);
	}

	private async Task<string> ApplyHtmlProcessingAsync(HtmlNode content, ChapterNodes nodes)
	{
		if (content.ChildNodes.Count == 0)
			return string.Empty;

		foreach (var scriptNode in nodes.Scripts)
		{
			scriptNode.Remove();
		}

		foreach (var linkNode in nodes.BodyLinks)
		{
			if (IsStylesheetLink(linkNode))
				linkNode.Remove();
		}

		foreach (var imageNode in nodes.Images)
		{
			if (IsCenteredDivImage(imageNode))
			{
				var parent = imageNode.ParentNode!;
				parent.SetAttributeValue("style", "margin: 0 auto;text-align:center;");
				nodes.StyledElements.Add(parent);
			}
		}

		DropCapHelper.ConvertDropCaps(nodes.Paragraphs);

		if (nodes.Images.Count > 0)
		{
			foreach (var imageNode in nodes.Images)
			{
				var attributeName = imageNode.Name == "img" ? "src" : "href";

				var src = imageNode.GetAttributeValue(attributeName, string.Empty);
				if (string.IsNullOrEmpty(src) && attributeName == "href")
					src = imageNode.GetAttributeValue("xlink:href", string.Empty);
				if (string.IsNullOrEmpty(src)) continue;
				var fileName = Path.GetFileName(src);

				var imagePath = Path.Combine(options.Value.CachePath, _cacheFolder!, fileName);
				if (!File.Exists(imagePath))
				{
					// Chapters are processed in parallel: dedupe concurrent extractions of the
					// same destination so two workers never write the same file at once.
					await _extractions.GetOrAdd(imagePath, _ => new Lazy<Task>(() => ExtractEntryToFolderAsync(src, imagePath))).Value;
				}

				var url = "/cache/" + _cacheFolder + "/" + fileName;
				imageNode.SetAttributeValue(attributeName, url);
				imageNode.SetAttributeValue("class", (imageNode.GetAttributeValue("class", string.Empty)) + " zoomable");
			}
		}

		ApplyCssToDom(nodes.Styles, nodes.StyledElements);
		return content.InnerHtml;
	}

	/// <summary>
	/// All the node buckets a chapter needs, gathered in a single tree walk so the DOM is
	/// traversed once instead of once per CSS selector.
	/// </summary>
	internal sealed class ChapterNodes
	{
		public HtmlNode? Body;
		public HtmlNode? Title;
		public List<HtmlNode> AllLinks = [];
		public List<HtmlNode> BodyLinks = [];
		public List<HtmlNode> Scripts = [];
		public List<HtmlNode> Images = [];
		public List<HtmlNode> Styles = [];
		public List<HtmlNode> StyledElements = [];
		public List<HtmlNode> Paragraphs = [];
	}

	/// <summary>
	/// Single-pass collection of every node the chapter pipeline needs. Replaces the ~10
	/// separate <c>QuerySelectorAll</c>/<c>SelectNodes</c> calls (each a full tree walk) with one.
	/// </summary>
	private static ChapterNodes CollectNodes(HtmlNode root)
	{
		var nodes = new ChapterNodes();
		CollectNodesWalk(root, nodes, bodyDepth: 0);
		if (nodes.Body is null)
		{
			// No <body> element: treat the whole document as the body.
			nodes = new ChapterNodes();
			CollectNodesWalk(root, nodes, bodyDepth: 1);
			nodes.Body = root;
		}
		return nodes;
	}

	private static void CollectNodesWalk(HtmlNode node, ChapterNodes nodes, int bodyDepth)
	{
		if (node.NodeType == HtmlNodeType.Document)
		{
			for (var child = node.FirstChild; child is not null; child = child.NextSibling)
				CollectNodesWalk(child, nodes, bodyDepth);
			return;
		}

		if (node.NodeType != HtmlNodeType.Element)
			return;

		var inBody = bodyDepth > 0;

		if (node.Name == "link")
		{
			nodes.AllLinks.Add(node);
			if (inBody)
				nodes.BodyLinks.Add(node);
		}
		else if (node.Name == "title" && nodes.Title is null)
		{
			nodes.Title = node;
		}
		else if (node.Name == "body")
		{
			nodes.Body ??= node;
			for (var child = node.FirstChild; child is not null; child = child.NextSibling)
				CollectNodesWalk(child, nodes, bodyDepth + 1);
			return;
		}
		else if (inBody)
		{
			switch (node.Name)
			{
				case "script":
					nodes.Scripts.Add(node);
					break;
				case "img":
				case "image":
					nodes.Images.Add(node);
					break;
				case "style":
					nodes.Styles.Add(node);
					break;
				case "p":
					nodes.Paragraphs.Add(node);
					break;
			}
		}

		if (inBody && node.Attributes?["style"] is not null)
			nodes.StyledElements.Add(node);

		for (var child = node.FirstChild; child is not null; child = child.NextSibling)
			CollectNodesWalk(child, nodes, inBody ? bodyDepth + 1 : bodyDepth);
	}

	private static bool IsStylesheetLink(HtmlNode link)
		=> string.Equals(link.GetAttributeValue("rel", string.Empty), "stylesheet", StringComparison.OrdinalIgnoreCase);

	private static bool IsCenteredDivImage(HtmlNode image)
	{
		if (image.Name != "img")
			return false;
		var parent = image.ParentNode;
		return parent is not null
			&& parent.Name == "div"
			&& parent.ChildNodes.Count == 1;
	}

	/// <summary>
	/// Applies the CSS variable processing directly on the DOM (inline <c>style</c> attributes and
	/// <c>&lt;style&gt;</c> blocks) so the chapter is serialized to a string only once. Running the regex
	/// over the full <see cref="HtmlNode.InnerHtml"/> would allocate a second full-size copy of the HTML.
	/// Scoping the regex to each attribute also avoids matching across attribute boundaries.
	/// </summary>
	private static void ApplyCssToDom(IReadOnlyList<HtmlNode> styleNodes, IReadOnlyList<HtmlNode> styledElements)
	{
		foreach (var styleNode in styleNodes)
		{
			var css = styleNode.InnerHtml;
			var processed = HtmlHelpers.ApplyCssProcessing(css);
			if (!string.Equals(processed, css, StringComparison.Ordinal))
				styleNode.InnerHtml = processed;
		}

		foreach (var element in styledElements)
		{
			var css = element.GetAttributeValue("style", string.Empty);
			if (!HtmlHelpers.HasTargetProperty(css))
				continue;
			var processed = HtmlHelpers.ApplyCssProcessing(css);
			if (!string.Equals(processed, css, StringComparison.Ordinal))
				element.SetAttributeValue("style", processed);
		}
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
	internal async Task<string> LoadFileContentAsync(string path)
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
		await _zipLock!.WaitAsync();
		try
		{
			var entry = GetArchiveEntry(absolutePath) ?? throw new Exception($"Could not load file: {absolutePath}");
			await using var stream = await entry.OpenAsync();
			var length = (int)entry.Length;
			var buffer = ArrayPool<byte>.Shared.Rent(length);
			try
			{
				await stream.ReadExactlyAsync(buffer.AsMemory(0, length));
				// Decode straight from the pooled buffer: skips the StreamReader's async
				// state machine and internal buffer growth (both showed up in the trace).
				var span = buffer.AsSpan(0, length);
				if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
					span = span.Slice(3); // strip BOM, as the old StreamReader(detectEncodingFromByteOrderMarks: true) did
				return Encoding.UTF8.GetString(span);
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
		finally
		{
			_zipLock.Release();
		}
	}

	/// <summary>
	/// Loads the nav file from the epub
	/// </summary>
	/// <param name="path">Path to load</param>
	/// <returns>Nav object</returns>
	private async Task<Nav> LoadNavAsync(string path)
	{
		var content = await LoadFileContentAsync(path);
		return Nav.Parse(XDocument.Parse(content));
	}

	[GeneratedRegex(@"@import\s*[^;]+;")]
	private static partial Regex CssImportRegex();
	[GeneratedRegex(@"@font-face\s*{[^}]+}")]
	private static partial Regex FontFaceRegex();
	[GeneratedRegex("&#([0-9]+);")]
	private static partial Regex NumericEntitiesRegex();

    /// <summary>
    /// Releases the zip archive and clears all transient state so the reader can be reused
    /// for a different file. Called automatically after every public read operation.
    /// </summary>
    internal void ResetState()
    {
	    _cacheFolder = null;
	    _rootFolder = null;
	    _package = null;
	    _coverPath = null;
	    ClearTransientCaches();
	    _zipArchive?.Dispose();
	    _zipArchive = null;
	    _zipLock?.Dispose();
	    _zipLock = null;
    }
}
