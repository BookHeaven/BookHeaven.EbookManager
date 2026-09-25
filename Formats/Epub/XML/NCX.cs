using System.Xml.Linq;
using System.Xml.Serialization;
using BookHeaven.EbookManager.Formats.Epub.Constants;

namespace BookHeaven.EbookManager.Formats.Epub.XML;

[XmlRoot("ncx", Namespace = Namespaces.Ncx)]
public class NCX
{
	[XmlElement("head")]
	public NCXHead Head { get; set; } = null!;

	[XmlElement("docTitle")]
	public NCXText? DocTitle { get; set; } = null!;

	[XmlElement("docAuthor")]
	public NCXText? DocAuthor { get; set; }

	[XmlElement("navMap")]
	public List<NCXNavPoint> NavMap { get; set; } = [];

	[XmlAttribute("version")]
	public string Version { get; set; } = null!;

	[XmlAttribute("xmlns")]
	public string Xmlns { get; set; } = null!;

	[XmlAttribute("lang")]
	public string Lang { get; set; } = null!;

	public static NCX Parse(XDocument document)
	{
		var root = document.Root ?? throw new Exception("Empty NCX document.");
		var ncx = new NCX
		{
			Version = root.Attribute("version")?.Value ?? string.Empty,
			Xmlns = root.Attribute("xmlns")?.Value ?? string.Empty,
			Lang = root.Attribute("lang")?.Value ?? string.Empty
		};

		var head = root.Element(Namespaces.NcxNs + "head");
		if (head is not null)
		{
			ncx.Head = new NCXHead
			{
				Meta = head.Elements(Namespaces.NcxNs + "meta").Select(e => new NCXMeta
				{
					Name = e.Attribute("name")?.Value ?? string.Empty,
					Content = e.Attribute("content")?.Value ?? string.Empty
				}).ToList()
			};
		}

		var docTitle = root.Element(Namespaces.NcxNs + "docTitle");
		if (docTitle is not null)
		{
			ncx.DocTitle = new NCXText { Text = docTitle.Element(Namespaces.NcxNs + "text")?.Value ?? string.Empty };
		}

		var docAuthor = root.Element(Namespaces.NcxNs + "docAuthor");
		if (docAuthor is not null)
		{
			ncx.DocAuthor = new NCXText { Text = docAuthor.Element(Namespaces.NcxNs + "text")?.Value ?? string.Empty };
		}

		var navMap = root.Element(Namespaces.NcxNs + "navMap");
		ncx.NavMap = [];
		if (navMap is not null)
		{
			foreach (var navPoint in navMap.Elements(Namespaces.NcxNs + "navPoint"))
			{
				ncx.NavMap.Add(ParseNavPoint(navPoint));
			}
		}

		return ncx;
	}

	private static NCXNavPoint ParseNavPoint(XElement navPoint)
	{
		var result = new NCXNavPoint
		{
			Id = navPoint.Attribute("id")?.Value ?? string.Empty,
			PlayOrder = int.TryParse(navPoint.Attribute("playOrder")?.Value, out var playOrder) ? playOrder : 0
		};

		var navLabel = navPoint.Element(Namespaces.NcxNs + "navLabel");
		if (navLabel is not null)
		{
			result.NavLabel = new NCXText { Text = navLabel.Element(Namespaces.NcxNs + "text")?.Value ?? string.Empty };
		}

		var content = navPoint.Element(Namespaces.NcxNs + "content");
		if (content is not null)
		{
			result.Content = new NCXContent { Src = content.Attribute("src")?.Value ?? string.Empty };
		}

		var childNavPoints = navPoint.Elements(Namespaces.NcxNs + "navPoint");
		if (childNavPoints.Any())
		{
			result.NavPoints = childNavPoints.Select(ParseNavPoint).ToList();
		}

		return result;
	}
}

public class NCXHead
{
	[XmlElement("meta")]
	public List<NCXMeta> Meta { get; set; } = null!;
}

public class NCXMeta
{
	[XmlAttribute("name")]
	public string Name { get; set; } = null!;

	[XmlAttribute("content")]
	public string Content { get; set; } = null!;
}

public class NCXText
{
	[XmlElement("text")]
	public string Text { get; set; } = null!;
}

public class NCXNavPoint
{
	[XmlElement("navLabel")]
	public NCXText? NavLabel { get; set; }
	[XmlElement("content")]
	public NCXContent? Content { get; set; }

	[XmlElement("navPoint")]
	public List<NCXNavPoint> NavPoints { get; set; } = [];

	[XmlAttribute("id")]
	public string Id { get; set; } = null!;

	[XmlAttribute("playOrder")]
	public int PlayOrder { get; set; }
}

public class NCXContent
{
	[XmlAttribute("src")]
	public string Src { get; set; } = null!;
}