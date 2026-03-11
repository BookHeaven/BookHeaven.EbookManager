using System.Xml.Linq;

namespace BookHeaven.EbookManager.Extensions;

public static class XElementExtensions
{
    public static XElement? SetItem(this XElement parentElement, XNamespace ns, string elementName, string value, int version = 2)
	{
		if(string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		var element = parentElement.Descendants().FirstOrDefault(x => x.Name.LocalName == elementName);
		if(element == null)
		{
			element = new XElement(ns + elementName);
			parentElement.Add(element);
		}
		element.Value = value;
		if (version != 3) return element;
		if(element.Attribute("id") == null)
		{
			element.SetAttributeValue(XName.Get("id"), $"{elementName}");
		}
		return element;
	}
    
    public static void SetMetaItem(this XElement parentElement, string elementName, string value, int version, string? refines = null)
	{
		var element = parentElement.Descendants().FirstOrDefault(x =>
			x.Name.LocalName == "meta" &&
			(x.Attribute("name")?.Value == elementName || x.Attribute("property")?.Value == elementName));
		element?.Remove();

		var metaName = parentElement.Name.Namespace + "meta";
		element = new XElement(metaName);
			
		if (version == 3)
		{
			element.SetAttributeValue(XName.Get("property"), elementName);
			if (!string.IsNullOrWhiteSpace(refines))
			{
				element.SetAttributeValue(XName.Get("refines"), $"#{refines}");
			}
			element.Value = value;
		}
		else
		{
			element.SetAttributeValue(XName.Get("name"), elementName);
			element.SetAttributeValue(XName.Get("content"), value);
		}
		parentElement.Add(element);
	}
}