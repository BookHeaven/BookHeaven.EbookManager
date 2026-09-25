using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using BookHeaven.EbookManager.Formats.Epub.Constants;

namespace BookHeaven.EbookManager.Formats.Epub.XML;

[XmlRoot("nav", Namespace = Namespaces.Xhtml)]
public class Nav
{
    [XmlElement("ol")]
    public List<NavOl> ChapterList { get; set; } = [];

    public static Nav Parse(XDocument document)
    {
        var navElement = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "nav")
            ?? throw new Exception("Could not find navigation content in epub file.");

        var nav = new Nav
        {
            ChapterList = [.. navElement.Elements(Namespaces.XhtmlNs + "ol").Select(ParseOl)]
        };
        return nav;
    }

    private static NavOl ParseOl(XElement ol)
    {
        var navOl = new NavOl
        {
            Chapter = [.. ol.Elements(Namespaces.XhtmlNs + "li").Select(ParseLi)]
        };
        return navOl;
    }

    private static NavLi ParseLi(XElement li)
    {
        var navLi = new NavLi();

        var link = li.Element(Namespaces.XhtmlNs + "a");
        if (link is not null)
        {
            navLi.Link = new NavA
            {
                Href = link.Attribute("href")?.Value ?? string.Empty,
                Title = link.Attribute("title")?.Value,
                SimpleText = link.Value
            };
        }

        var label = li.Element(Namespaces.XhtmlNs + "span");
        if (label is not null)
        {
            navLi.Label = new NavSpan { Text = label.Value };
        }

        var childOls = li.Elements(Namespaces.XhtmlNs + "ol");
        var xElements = childOls as XElement[] ?? [.. childOls];
        if (xElements.Length != 0)
        {
            navLi.ChapterList = [.. xElements.Select(ParseOl)];
        }

        return navLi;
    }
}

public class NavOl
{
    [XmlElement("li")]
    public List<NavLi> Chapter { get; set; } = [];
}

public class NavLi
{
    [XmlElement("a")]
    public NavA? Link { get; set; }

    [XmlElement("span")]
    public NavSpan? Label { get; set; }

    [XmlElement("ol")]
    public List<NavOl> ChapterList { get; set; } = [];
}

public class NavSpan
{
    [XmlText]
    public string Text { get; set; } = string.Empty;
}

public class NavA
{
    [XmlAttribute("href")]
    public string Href { get; set; } = string.Empty;

    [XmlAttribute("title")]
    public string? Title { get; set; }
    
    [XmlText]
    public string SimpleText { get; set; } = string.Empty;
    
    [XmlAnyElement]
    public XmlElement? Child { get; set; }

    [XmlIgnore]
    public string Text
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SimpleText)) return SimpleText.Trim();
            if (!string.IsNullOrWhiteSpace(Title)) return Title.Trim();
            return Child is null ? string.Empty : GetTextFromLink(Child).Trim();
        }
    }
    
    private static string GetTextFromLink(XmlElement element)
    {
        var text = string.Empty;
        if (!element.HasChildNodes) return text;
        
        foreach (XmlNode child in element.ChildNodes)
        {
            switch (child.NodeType)
            {
                case XmlNodeType.Text:
                    text += child.Value;
                    break;
                case XmlNodeType.Element:
                    text += GetTextFromLink((XmlElement)child);
                    break;
            }
        }
        return text;
    }
}
