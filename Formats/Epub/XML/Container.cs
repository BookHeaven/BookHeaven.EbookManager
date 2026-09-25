using System.Xml.Linq;
using System.Xml.Serialization;
using BookHeaven.EbookManager.Formats.Epub.Constants;

namespace BookHeaven.EbookManager.Formats.Epub.XML;

[XmlRoot("container", Namespace = Namespaces.Container, IsNullable = false)]
public class Container
{
	[XmlElement("rootfiles")]
	public RootFiles RootFiles { get; set; } = null!;

	public static Container Parse(XDocument document)
	{
		var rootFiles = new RootFiles { RootFile = [] };
		var rootfiles = document.Root?.Elements(Namespaces.ContainerNs + "rootfiles").FirstOrDefault();
		if (rootfiles is not null)
		{
			foreach (var rootfile in rootfiles.Elements(Namespaces.ContainerNs + "rootfile"))
			{
				rootFiles.RootFile.Add(new RootFile
				{
					FullPath = rootfile.Attribute("full-path")?.Value ?? string.Empty,
					MediaType = rootfile.Attribute("media-type")?.Value ?? string.Empty
				});
			}
		}

		return new Container { RootFiles = rootFiles };
	}
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