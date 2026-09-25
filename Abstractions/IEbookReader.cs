using BookHeaven.EbookManager.Entities;

namespace BookHeaven.EbookManager.Abstractions;

public interface IEbookReader
{
    Task<Ebook> ReadMetadataAsync(string path);
    Task<Ebook> ReadAllAsync(string path);
}