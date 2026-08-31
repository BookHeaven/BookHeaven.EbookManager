using System.Xml;
using System.Xml.Serialization;
using BookHeaven.EbookManager.Formats.Epub.Constants;

namespace BookHeaven.EbookManager.Formats.Epub.XML;

[XmlRoot("nav", Namespace = Namespaces.Xhtml)]
public class Nav
{
    [XmlElement("ol")]
    public List<NavOl> ChapterList { get; set; } = [];
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
