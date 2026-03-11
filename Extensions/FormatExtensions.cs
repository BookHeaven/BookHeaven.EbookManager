using System.ComponentModel;
using BookHeaven.EbookManager.Enums;

namespace BookHeaven.EbookManager.Extensions;

public static class FormatExtensions
{
    public static string GetExtension(this Format format) => format switch
    {
        Format.Epub => ".epub",
        Format.Pdf => ".pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };
    
    public static string GetName(this Format format)
    {
        var type = format.GetType();
        var memInfo = type.GetMember(format.ToString());
        if (memInfo.Length <= 0) return format.ToString();
        var attrs = memInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);
        return attrs.Length > 0 ? ((DescriptionAttribute)attrs[0]).Description : format.ToString();
    }
}