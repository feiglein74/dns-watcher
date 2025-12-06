using System.Text.RegularExpressions;

namespace DnsWatcher.Tests;

/// <summary>
/// Tests fuer DNS-Formatierungsfunktionen
/// </summary>
public class DnsFormatterTests
{
    private static readonly Dictionary<int, string> QueryTypes = new()
    {
        { 1, "A" }, { 2, "NS" }, { 5, "CNAME" }, { 6, "SOA" },
        { 12, "PTR" }, { 15, "MX" }, { 16, "TXT" }, { 28, "AAAA" },
        { 33, "SRV" }, { 35, "NAPTR" }, { 37, "CERT" },
        // DNSSEC
        { 43, "DS" }, { 46, "RRSIG" }, { 47, "NSEC" }, { 48, "DNSKEY" },
        { 50, "NSEC3" }, { 51, "NSEC3PARAM" },
        // TSIG/TKEY
        { 249, "TKEY" }, { 250, "TSIG" },
        // Sonstige
        { 52, "TLSA" }, { 64, "SVCB" }, { 65, "HTTPS" },
        { 99, "SPF" }, { 255, "ANY" }, { 257, "CAA" }
    };

    // Duplikat der FormatQueryResults-Logik fuer Tests
    private static string FormatQueryResults(string? results)
    {
        if (string.IsNullOrEmpty(results)) return "";

        return Regex.Replace(
            results,
            @"type:\s*(\d+)\s+",
            match =>
            {
                if (int.TryParse(match.Groups[1].Value, out int typeNum))
                {
                    var typeName = QueryTypes.TryGetValue(typeNum, out var name) ? name : $"TYPE{typeNum}";
                    return $"{typeName} ";
                }
                return match.Value;
            });
    }

    [Fact]
    public void FormatQueryResults_Null_ReturnsEmpty()
    {
        Assert.Equal("", FormatQueryResults(null));
    }

    [Fact]
    public void FormatQueryResults_Empty_ReturnsEmpty()
    {
        Assert.Equal("", FormatQueryResults(""));
    }

    [Theory]
    [InlineData("type: 1 192.168.1.1", "A 192.168.1.1")]
    [InlineData("type: 5 www.example.com", "CNAME www.example.com")]
    [InlineData("type: 28 ::1", "AAAA ::1")]
    [InlineData("type: 12 server.local", "PTR server.local")]
    public void FormatQueryResults_KnownTypes(string input, string expected)
    {
        Assert.Equal(expected, FormatQueryResults(input));
    }

    [Fact]
    public void FormatQueryResults_UnknownType()
    {
        var result = FormatQueryResults("type: 999 somedata");
        Assert.Equal("TYPE999 somedata", result);
    }

    [Fact]
    public void FormatQueryResults_DnssecType()
    {
        var result = FormatQueryResults("type: 249 keydata");
        Assert.Equal("TKEY keydata", result);
    }

    [Fact]
    public void FormatQueryResults_MultipleTypes()
    {
        var input = "type: 5 login.mso.msidentity.com type: 1 20.190.160.0";
        var result = FormatQueryResults(input);
        Assert.Equal("CNAME login.mso.msidentity.com A 20.190.160.0", result);
    }

    [Fact]
    public void FormatQueryResults_NoTypePattern()
    {
        var input = "192.168.1.1";
        var result = FormatQueryResults(input);
        Assert.Equal("192.168.1.1", result);
    }

    [Fact]
    public void FormatQueryResults_MixedContent()
    {
        var input = "some prefix type: 5 example.com some suffix";
        var result = FormatQueryResults(input);
        Assert.Equal("some prefix CNAME example.com some suffix", result);
    }

    // Tests fuer QueryType Mapping
    [Theory]
    [InlineData(1, "A")]
    [InlineData(2, "NS")]
    [InlineData(5, "CNAME")]
    [InlineData(6, "SOA")]
    [InlineData(12, "PTR")]
    [InlineData(15, "MX")]
    [InlineData(16, "TXT")]
    [InlineData(28, "AAAA")]
    [InlineData(33, "SRV")]
    [InlineData(255, "ANY")]
    [InlineData(65, "HTTPS")]
    public void QueryTypes_AllKnownTypesAreMapped(int typeNum, string expected)
    {
        Assert.True(QueryTypes.TryGetValue(typeNum, out var name));
        Assert.Equal(expected, name);
    }
}

/// <summary>
/// Tests fuer Status-Code Mapping
/// </summary>
public class StatusCodeTests
{
    private static readonly Dictionary<int, string> StatusCodes = new()
    {
        { 0, "OK" },
        { 87, "Cached" },
        { 1168, "NotFound" },
        { 1214, "InvalidName" },
        { 1460, "Timeout" },
        { 9002, "ServFail" },
        { 9003, "NXDomain" },
        { 9004, "NotImpl" },
        { 9005, "Refused" },
        { 9501, "NoRecords" },
        { 9560, "Timeout" },
        { 9701, "NoRecord" },
        { 9702, "RecordFormat" },
        { 11001, "HostNotFound" },
        { 11002, "TryAgain" },
        { 11003, "NoRecovery" },
        { 11004, "NoData" }
    };

    [Theory]
    [InlineData(0, "OK")]
    [InlineData(87, "Cached")]
    [InlineData(9003, "NXDomain")]
    [InlineData(9005, "Refused")]
    [InlineData(11001, "HostNotFound")]
    public void StatusCodes_CommonCodesAreMapped(int code, string expected)
    {
        Assert.True(StatusCodes.TryGetValue(code, out var name));
        Assert.Equal(expected, name);
    }

    [Fact]
    public void StatusCodes_UnknownCodeNotMapped()
    {
        Assert.False(StatusCodes.TryGetValue(12345, out _));
    }
}
