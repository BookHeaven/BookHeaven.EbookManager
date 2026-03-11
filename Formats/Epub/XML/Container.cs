using System.Xml.Serialization;
using BookHeaven.EbookManager.Formats.Epub.Constants;

namespace BookHeaven.EbookManager.Formats.Epub.XML;

[XmlRoot("container", Namespace = Namespaces.Container, IsNullable = false)]
public class Container
{
	[XmlElement("rootfiles")]
	public RootFiles RootFiles { get; set; } = null!;
}

public class RootFiles
{
	[XmlElement("rootfile")]
	public List<RootFile> RootFile { get; set; } = null!;
}

public class RootFile
{
	[XmlAttribute("full-path")]
	public string FullPath { get; set; } = null!;

	[XmlAttribute("media-type")]
	public string MediaType { get; set; } = null!;
}