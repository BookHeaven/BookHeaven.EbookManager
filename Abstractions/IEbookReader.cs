using BookHeaven.EbookManager.Entities;

namespace BookHeaven.EbookManager.Abstractions;

public interface IEbookReader : IDisposable
{
    Task<Ebook> ReadMetadataAsync(string path);
    Task<Ebook> ReadAllAsync(string path);
    Task<string> ApplyHtmlProcessingAsync(string content);
}