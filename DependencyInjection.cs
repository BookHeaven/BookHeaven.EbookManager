using BookHeaven.EbookManager.Abstractions;
using BookHeaven.EbookManager.Enums;
using BookHeaven.EbookManager.Formats.Epub.Services;
using BookHeaven.EbookManager.Formats.Pdf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BookHeaven.EbookManager;

public static class DependencyInjection
{
    /// <param name="services"></param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the EpubManager services
        /// </summary>
        /// <param name="ebookManagerOptions">Options for the EpubManager</param>
        public IServiceCollection AddEbookManager(Action<EbookManagerOptions>? ebookManagerOptions = null)
        {
            var options = new EbookManagerOptions();
            ebookManagerOptions?.Invoke(options);

            options.Validate();
        
            services.AddReaders();
            services.AddWriters();
            services.AddSingleton<EbookManagerProvider>();
            return services;
        }

        private void AddReaders()
        {
            services.AddKeyedTransient<IEbookReader, EpubReader>(Format.Epub);
            services.AddKeyedTransient<IEbookReader, PdfReader>(Format.Pdf);
        }

        private void AddWriters()
        {
            services.AddKeyedTransient<IEbookWriter, EpubWriter>(Format.Epub);
            services.AddKeyedTransient<IEbookWriter, PdfWriter>(Format.Pdf);
        }
    }
}

public class EbookManagerOptions
{
    public string CachePath { get; set; } = string.Empty;
    public bool UseCustomScheme { get; set; } = true;
    
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CachePath))
        {
            throw new ArgumentException("CachePath cannot be empty.");
        }
        
        Directory.CreateDirectory(CachePath);
        EbookManagerGlobals.CachePath = CachePath;
        EbookManagerGlobals.UseCustomScheme = UseCustomScheme;
    }
}