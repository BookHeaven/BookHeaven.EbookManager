using System.Xml.Linq;

namespace BookHeaven.EbookManager.Formats.Epub.Constants;

internal static class Namespaces
{
    public const string Opf = "http://www.idpf.org/2007/opf";
    public const string Dc = "http://purl.org/dc/elements/1.1/";
    public const string Dcterms = "http://purl.org/dc/terms/";
    public const string Xhtml = "http://www.w3.org/1999/xhtml";
    public const string Container = "urn:oasis:names:tc:opendocument:xmlns:container";
    public const string Ncx = "http://www.daisy.org/z3986/2005/ncx/";

    public static readonly XNamespace OpfNs = XNamespace.Get(Opf);
    public static readonly XNamespace DcNs = XNamespace.Get(Dc);
    public static readonly XNamespace DctermsNs = XNamespace.Get(Dcterms);
    public static readonly XNamespace XhtmlNs = XNamespace.Get(Xhtml);
    public static readonly XNamespace ContainerNs = XNamespace.Get(Container);
    public static readonly XNamespace NcxNs = XNamespace.Get(Ncx);
}