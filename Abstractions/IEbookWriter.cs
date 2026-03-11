using BookHeaven.EbookManager.Entities;

namespace BookHeaven.EbookManager.Abstractions;

public interface IEbookWriter
{
    Task ReplaceMetadataAsync(string bookPath, Ebook ebook);
    Task ReplaceCoverAsync(string bookPath, string newCoverPath);
}