using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class MarkerProbeRegistryFile
{
    public string version;
    public MarkerProbeRegistryEntry[] markers;
}

[Serializable]
public sealed class MarkerProbeRegistryEntry
{
    public int picoArUcoId;
    public string qrId;
    public string logicalMarkerId;
    public string businessObjectId;
}

/// <summary>Versioned external identity mapping used by the diagnostic PICO path.</summary>
public sealed class MarkerProbeRegistry
{
    private readonly Dictionary<int, MarkerProbeRegistryEntry> entries;

    private MarkerProbeRegistry(string version, string sourceSha256, MarkerProbeRegistryEntry[] values)
    {
        Version = string.IsNullOrEmpty(version) ? "unversioned" : version;
        SourceSha256 = sourceSha256 ?? string.Empty;
        entries = new Dictionary<int, MarkerProbeRegistryEntry>();
        if (values == null) return;
        foreach (MarkerProbeRegistryEntry value in values)
        {
            if (value == null || entries.ContainsKey(value.picoArUcoId)) continue;
            entries.Add(value.picoArUcoId, value);
        }
    }

    public string Version { get; }
    public string SourceSha256 { get; }

    public bool TryResolve(int picoArUcoId, out MarkerProbeRegistryEntry value)
    {
        return entries.TryGetValue(picoArUcoId, out value);
    }

    public static MarkerProbeRegistry LoadDefault()
    {
        TextAsset asset = Resources.Load<TextAsset>("MarkerProbeRegistry");
        if (asset != null)
        {
            try
            {
                MarkerProbeRegistryFile file = JsonUtility.FromJson<MarkerProbeRegistryFile>(asset.text);
                return new MarkerProbeRegistry(file?.version, ComputeSha256(asset.text), file?.markers);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MarkerProbe] Registry parse failed: {exception.Message}");
            }
        }

        return new MarkerProbeRegistry(
            "builtin-v1",
            ComputeSha256("builtin-v1:0|250"),
            new[]
            {
                new MarkerProbeRegistryEntry
                {
                    picoArUcoId = 0,
                    qrId = "0",
                    logicalMarkerId = "anchor_0",
                    businessObjectId = ""
                },
                new MarkerProbeRegistryEntry
                {
                    picoArUcoId = 250,
                    qrId = "250",
                    logicalMarkerId = "anchor_250",
                    businessObjectId = ""
                }
            });
    }

    private static string ComputeSha256(string text)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte item in hash) builder.Append(item.ToString("x2"));
            return builder.ToString();
        }
    }
}
