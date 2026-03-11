using BookHeaven.EbookManager.Abstractions;
using BookHeaven.EbookManager.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace BookHeaven.EbookManager;

public class EbookManagerProvider(IServiceProvider serviceProvider)
{
    public IEbookReader GetReader(Format format) => serviceProvider.GetRequiredKeyedService<IEbookReader>(format);
    public IEbookWriter? GetWriter(Format format) => serviceProvider.GetKeyedService<IEbookWriter>(format);
}