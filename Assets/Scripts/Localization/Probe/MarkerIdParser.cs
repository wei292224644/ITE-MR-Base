using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public enum MarkerIdParseFailure
{
    None,
    NullPayload,
    EmptyPayload,
    NonDecimalPayload,
    LeadingZeroNotCanonical,
    UnsupportedMarkerId
}

[Serializable]
public sealed class RawPayloadSummary
{
    public bool payloadWasNull;
    public int utf8ByteLength;
    public string sha256;
    public bool rawPayloadCaptured;
    public string plaintext;
}

[Serializable]
public sealed class MarkerIdParseResult
{
    public bool success;
    public string markerId;
    public MarkerIdParseFailure failure;
    public string failureDetail;
    public RawPayloadSummary rawPayload;
}

public interface IMarkerIdParser
{
    MarkerIdParseResult Parse(string rawPayload);
}

/// <summary>
/// Parser for this change's two printed fixtures. It intentionally accepts only the exact,
/// canonical payloads "0" and "250"; future URL, JSON, or lookup rules belong behind the
/// same interface in a later production change.
/// </summary>
public sealed class StandardFixtureMarkerIdParser : IMarkerIdParser
{
    private readonly bool includeRawPayloadPlaintext;

    public StandardFixtureMarkerIdParser(bool includeRawPayloadPlaintext = false)
    {
        this.includeRawPayloadPlaintext = includeRawPayloadPlaintext && Debug.isDebugBuild;
    }

    public bool IncludesRawPayloadPlaintext => includeRawPayloadPlaintext;

    public MarkerIdParseResult Parse(string rawPayload)
    {
        RawPayloadSummary summary = Summarize(rawPayload);

        if (rawPayload == null)
        {
            return Failure(summary, MarkerIdParseFailure.NullPayload, "RawPayload is null.");
        }

        if (rawPayload.Length == 0)
        {
            return Failure(summary, MarkerIdParseFailure.EmptyPayload, "RawPayload is empty.");
        }

        for (int i = 0; i < rawPayload.Length; i++)
        {
            if (rawPayload[i] < '0' || rawPayload[i] > '9')
            {
                return Failure(
                    summary,
                    MarkerIdParseFailure.NonDecimalPayload,
                    "RawPayload must contain ASCII decimal digits only.");
            }
        }

        if (rawPayload.Length > 1 && rawPayload[0] == '0')
        {
            return Failure(
                summary,
                MarkerIdParseFailure.LeadingZeroNotCanonical,
                "MarkerID must be canonical decimal without leading zeroes.");
        }

        if (rawPayload != "0" && rawPayload != "250")
        {
            return Failure(
                summary,
                MarkerIdParseFailure.UnsupportedMarkerId,
                "This probe supports only the standard fixture MarkerIDs 0 and 250.");
        }

        return new MarkerIdParseResult
        {
            success = true,
            markerId = rawPayload,
            failure = MarkerIdParseFailure.None,
            failureDetail = null,
            rawPayload = summary
        };
    }

    private static MarkerIdParseResult Failure(
        RawPayloadSummary summary,
        MarkerIdParseFailure failure,
        string detail)
    {
        return new MarkerIdParseResult
        {
            success = false,
            markerId = null,
            failure = failure,
            failureDetail = detail,
            rawPayload = summary
        };
    }

    private RawPayloadSummary Summarize(string rawPayload)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(rawPayload ?? string.Empty);
        byte[] digest;

        using (SHA256 sha256 = SHA256.Create())
        {
            digest = sha256.ComputeHash(utf8);
        }

        return new RawPayloadSummary
        {
            payloadWasNull = rawPayload == null,
            utf8ByteLength = utf8.Length,
            sha256 = ToLowerHex(digest),
            rawPayloadCaptured = includeRawPayloadPlaintext && rawPayload != null,
            plaintext = includeRawPayloadPlaintext ? rawPayload : null
        };
    }

    private static string ToLowerHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (byte value in bytes)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }
}
