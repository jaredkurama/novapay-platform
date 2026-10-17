using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace NovaPay.Api;

public sealed class RequestMetrics
{
    private readonly ConcurrentDictionary<(string Route, int Status), long> counts = new();

    public void Record(string route, int status) =>
        counts.AddOrUpdate((route, status), 1, (_, count) => count + 1);

    public string Render()
    {
        var result = new StringBuilder("# HELP novapay_http_requests_total Completed application HTTP requests.\n# TYPE novapay_http_requests_total counter\n");
        foreach (var entry in counts.OrderBy(entry => entry.Key.Route).ThenBy(entry => entry.Key.Status))
        {
            result.Append("novapay_http_requests_total{route=\"").Append(entry.Key.Route)
                .Append("\",status=\"").Append(entry.Key.Status.ToString(CultureInfo.InvariantCulture))
                .Append("\"} ").Append(entry.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        return result.ToString();
    }
}
