using BookHeaven.EbookManager.Abstractions;
using BookHeaven.EbookManager.Enums;

namespace BookHeaven.EbookManager.Formats;

public interface IEbookManagerProvider
{
    IEbookReader GetReader(Format format);
    IEbookWriter? GetWriter(Format format);
}