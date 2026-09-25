using System.Xml.Linq;
using System.Xml.Serialization;
using BookHeaven.EbookManager.Formats.Epub.Constants;

namespace BookHeaven.EbookManager.Formats.Epub.XML;

[XmlRoot("package", Namespace = Namespaces.Opf, IsNullable = false)]
public class Package
{
	[XmlAttribute("version")]
	public string Version { get; set; } = null!;

	[XmlAttribute("unique-identifier")]
	public string UniqueIdentifier { get; set; } = null!;

	[XmlElement("metadata")]
	public Metadata Metadata { get; set; } = null!;

	[XmlElement("manifest")]
	public Manifest Manifest { get; set; } = null!;

	[XmlElement("spine")]
	public Spine Spine { get; set; } = null!;

	[XmlElement("guide")]
	public Guide? Guide { get; set; }

	public static Package Parse(XDocument document)
	{
		var root = document.Root ?? throw new Exception("Empty package document.");
		var package = new Package
		{
			Version = GetAttr(root, "version") ?? string.Empty,
			UniqueIdentifier = GetAttr(root, "unique-identifier") ?? string.Empty
		};

		var metadataElement = root.Element(Namespaces.OpfNs + "metadata");
		package.Metadata = metadataElement is null ? new Metadata() : ParseMetadata(metadataElement);

		var manifest = new Manifest { Items = [] };
		var manifestElement = root.Element(Namespaces.OpfNs + "manifest");
		if (manifestElement is not null)
		{
			foreach (var item in manifestElement.Elements(Namespaces.OpfNs + "item"))
			{
				manifest.Items.Add(new Item
				{
					Id = GetAttr(item, "id") ?? string.Empty,
					Href = GetAttr(item, "href") ?? string.Empty,
					MediaType = GetAttr(item, "media-type") ?? string.Empty,
					Properties = GetAttr(item, "properties")
				});
			}
		}
		package.Manifest = manifest;

		var spine = new Spine { ItemRefs = [] };
		var spineElement = root.Element(Namespaces.OpfNs + "spine");
		if (spineElement is not null)
		{
			spine.Toc = GetAttr(spineElement, "toc");
			foreach (var itemRef in spineElement.Elements(Namespaces.OpfNs + "itemref"))
			{
				spine.ItemRefs.Add(new ItemRef
				{
					IdRef = GetAttr(itemRef, "idref") ?? string.Empty,
					Linear = GetAttr(itemRef, "linear") ?? string.Empty
				});
			}
		}
		package.Spine = spine;

		var guideElement = root.Element(Namespaces.OpfNs + "guide");
		if (guideElement is not null)
		{
			var guide = new Guide();
			foreach (var reference in guideElement.Elements(Namespaces.OpfNs + "reference"))
			{
				guide.References.Add(new Reference
				{
					Href = GetAttr(reference, "href") ?? string.Empty,
					Type = GetAttr(reference, "type") ?? string.Empty,
					Title = GetAttr(reference, "title") ?? string.Empty
				});
			}
			package.Guide = guide;
		}

		return package;
	}

	private static Metadata ParseMetadata(XElement metadataElement)
	{
		var metadata = new Metadata();

		metadata.Titles = ReadTextList(metadataElement, "title");
		metadata.Languages = ReadTextList(metadataElement, "language");
		metadata.Identifiers = metadataElement.Elements(Namespaces.DcNs + "identifier").Select(e => new Identifier
		{
			Id = GetAttr(e, "id") ?? string.Empty,
			Scheme = GetAttr(e, "scheme") ?? string.Empty,
			Value = e.Value
		}).ToList();
		metadata.Creators = ReadPeople(metadataElement, "creator", (fileAs, name, role) => new Creator { FileAs = fileAs, Name = name, Role = role });
		metadata.Contributors = ReadPeople(metadataElement, "contributor", (fileAs, name, role) => new Contributor { FileAs = fileAs, Name = name, Role = role });
		metadata.Publishers = ReadTextList(metadataElement, "publisher");
		metadata.Dates = ReadTextList(metadataElement, "date").Select(s => (string?)s).ToList();
		metadata.Rights = ReadTextList(metadataElement, "rights");
		metadata.Subjects = ReadTextList(metadataElement, "subject");
		metadata.Types = ReadTextList(metadataElement, "type");
		metadata.Descriptions = ReadTextList(metadataElement, "description");

		metadata.Meta = metadataElement.Elements(Namespaces.OpfNs + "meta").Select(e => new Meta
		{
			Name = GetAttr(e, "name"),
			Property = GetAttr(e, "property"),
			Content = GetAttr(e, "content"),
			Value = e.Value
		}).ToList();

		return metadata;
	}

	private static List<string> ReadTextList(XElement parent, string localName)
	{
		var elements = parent.Elements(Namespaces.DcNs + localName);
		return elements.Any() ? elements.Select(e => e.Value).ToList() : [];
	}

	private static List<T> ReadPeople<T>(XElement parent, string localName, Func<string?, string, string?, T> factory)
	{
		var elements = parent.Elements(Namespaces.DcNs + localName);
		return elements.Any()
			? elements.Select(e => factory(GetAttr(e, "file-as"), e.Value, GetAttr(e, "role"))).ToList()
			: [];
	}

	/// <summary>
	/// Reads an attribute that may be declared without a namespace (the common case)
	/// or in the OPF namespace (e.g. <c>opf:role</c> in EPUB 2 files).
	/// </summary>
	private static string? GetAttr(XElement element, string name)
		=> element.Attribute(name)?.Value ?? element.Attribute(Namespaces.OpfNs + name)?.Value;
}

public class Metadata
{
	[XmlElement(ElementName = "title", Namespace = Namespaces.Dc)]
	public List<string> Titles { get; set; } = [];

	[XmlElement(ElementName = "language", Namespace = Namespaces.Dc)]
	public List<string> Languages { get; set; } = [];

	[XmlElement(ElementName = "identifier", Namespace = Namespaces.Dc)]
	public List<Identifier> Identifiers { get; set; } = [];

	[XmlElement(ElementName = "creator", Namespace = Namespaces.Dc)]
	public List<Creator>? Creators { get; set; } = [];

	[XmlElement(ElementName = "contributor", Namespace = Namespaces.Dc)]
	public List<Contributor>? Contributors { get; set; } = [];

	[XmlElement(ElementName = "publisher", Namespace = Namespaces.Dc)]
	public List<string>? Publishers { get; set; } = [];

	[XmlElement(ElementName = "date", Namespace = Namespaces.Dc)]
	public List<string?>? Dates { get; set; } = [];

	[XmlElement(ElementName = "rights", Namespace = Namespaces.Dc)]
	public List<string>? Rights { get; set; } = [];

	[XmlElement(ElementName = "subject", Namespace = Namespaces.Dc)]
	public List<string>? Subjects { get; set; } = [];

	[XmlElement(ElementName = "type", Namespace = Namespaces.Dc)]
	public List<string>? Types { get; set; } = [];

	[XmlElement(ElementName = "description", Namespace = Namespaces.Dc)]
	public List<string>? Descriptions { get; set; } = [];

	[XmlElement("meta")]
	public List<Meta> Meta { get; set; } = [];

}

public class Creator
{
	[XmlAttribute("file-as")]
	public string? FileAs { get; set; }

	[XmlText]
	public string Name { get; set; } = null!;

	[XmlAttribute("role")]
	public string? Role { get; set; }
}

public class Contributor
{
	[XmlAttribute("file-as")]
	public string? FileAs { get; set; }

	[XmlText]
	public string Name { get; set; } = null!;

	[XmlAttribute("role")]
	public string? Role { get; set; }
}

public class Identifier
{
	[XmlAttribute(AttributeName = "id")]
	public string Id { get; set; } = null!;
	[XmlAttribute(Namespace = Namespaces.Opf, AttributeName = "scheme")]
	public string Scheme { get; set; } = null!;
	[XmlText]
	public string Value { get; set; } = null!;
}

public class Meta
{
	[XmlAttribute("name")]
	public string? Name { get; set; }
		
	[XmlAttribute("property")]
	public string? Property { get; set; }

	[XmlAttribute("content")]
	public string? Content { get; set; }
		
	[XmlText]
	public string? Value { get; set; }
}

public class Manifest
{
	[XmlElement("item")]
	public List<Item> Items { get; set; } = null!;
}

public class Item
{
	[XmlAttribute("id")]
	public string Id { get; set; } = null!;

	[XmlAttribute("href")]
	public string Href { get; set; } = null!;

	[XmlAttribute("media-type")]
	public string MediaType { get; set; } = null!;
		
	[XmlAttribute("properties")]
	public string? Properties { get; set; }
}

public class Spine
{
	[XmlAttribute("toc")]
	public string? Toc { get; set; }

	[XmlElement("itemref")]
	public List<ItemRef> ItemRefs { get; set; } = [];
}

public class ItemRef
{
	[XmlAttribute("idref")]
	public string IdRef { get; set; } = null!;

	[XmlAttribute("linear")]
	public string Linear { get; set; } = null!;
}

public class Guide
{
	[XmlElement("reference")]
	public List<Reference> References { get; set; } = [];
}

public class Reference
{
	[XmlAttribute("href")]
	public string Href { get; set; } = null!;

	[XmlAttribute("type")]
	public string Type { get; set; } = null!;

	[XmlAttribute("title")]
	public string Title { get; set; } = null!;

}