using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace FamilyLearning.Api.Infrastructure.Web;

/// <summary>Caches fingerprinted public assets while keeping stable filenames revalidated across deployments.</summary>
internal static partial class StaticFileCaching
{
    public static void Apply(StaticFileResponseContext context)
    {
        var response = context.Context.Response;
        response.Headers.CacheControl = response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status206PartialContent or StatusCodes.Status304NotModified &&
            ContentHashedAsset().IsMatch(context.File.Name) ? "public, max-age=31536000, immutable" : "no-cache";
    }

    // Angular emits eight-character hashes; HTML and service-worker control files must revalidate.
    [GeneratedRegex(@"-[A-Za-z0-9_-]{8}\.(?:js|css|woff2?|ttf|otf|svg|png|jpe?g|webp|avif|gif|ico)\z", RegexOptions.CultureInvariant)]
    private static partial Regex ContentHashedAsset();
}
