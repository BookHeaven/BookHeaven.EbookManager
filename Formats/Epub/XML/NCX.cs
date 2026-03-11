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