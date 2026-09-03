using BookHeaven.EbookManager.Enums;

namespace BookHeaven.EbookManager.Abstractions;

public interface IEbookManagerProvider
{
    IEbookReader GetReader(Format format);
    IEbookWriter? GetWriter(Format format);
}