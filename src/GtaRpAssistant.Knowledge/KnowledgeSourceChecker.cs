using System.Net;

namespace GtaRpAssistant.Knowledge;

public sealed record KnowledgeSourceCheck(string ArticleId, string Url, bool Available, int? StatusCode, string Message);

public static class KnowledgeSourceChecker
{
    public static async Task<IReadOnlyList<KnowledgeSourceCheck>> CheckAsync(IReadOnlyList<KnowledgePackArticle> articles, HttpClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var results = new List<KnowledgeSourceCheck>(articles.Count);
        foreach (var article in articles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(article.Source.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                results.Add(new(article.Id, article.Source.Url ?? string.Empty, false, null, "Source must be an absolute HTTPS URL."));
                continue;
            }
            if (!await IsPublicHostAsync(uri, cancellationToken))
            {
                results.Add(new(article.Id, uri.AbsoluteUri, false, null, "Source host resolves to a local or private address."));
                continue;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                results.Add(new(article.Id, uri.AbsoluteUri, response.IsSuccessStatusCode, (int)response.StatusCode, response.IsSuccessStatusCode ? "OK" : response.ReasonPhrase ?? "HTTP error"));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                results.Add(new(article.Id, uri.AbsoluteUri, false, null, "Timeout"));
            }
            catch (HttpRequestException ex)
            {
                results.Add(new(article.Id, uri.AbsoluteUri, false, null, ex.Message));
            }
        }
        return results;
    }

    private static async Task<bool> IsPublicHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken); }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException) { return false; }
        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
                && !(b[0] == 169 && b[1] == 254)
                && !(b[0] == 172 && b[1] is >= 16 and <= 31)
                && !(b[0] == 192 && b[1] == 168);
        }
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
        var bytes = address.GetAddressBytes();
        return (bytes[0] & 0xfe) != 0xfc;
    }
}
