using Microsoft.Extensions.FileProviders;

namespace AgentTokenStats.Infrastructure;

public static class EmbeddedWebRoot
{
    public const string Folder = "wwwroot";

    public static IFileProvider Create(IWebHostEnvironment env)
    {
        var assembly = typeof(EmbeddedWebRoot).Assembly;
        try
        {
            var embedded = new ManifestEmbeddedFileProvider(assembly, Folder);
            if (embedded.GetFileInfo("index.html").Exists)
                return embedded;
        }
        catch (InvalidOperationException)
        {
            /* assembly was built without a wwwroot manifest */
        }

        var disk = Path.Combine(env.ContentRootPath, Folder);
        if (File.Exists(Path.Combine(disk, "index.html")))
            return new PhysicalFileProvider(disk);

        return new NullFileProvider();
    }

    public static bool HasIndex(IFileProvider provider) =>
        provider.GetFileInfo("index.html").Exists;
}
