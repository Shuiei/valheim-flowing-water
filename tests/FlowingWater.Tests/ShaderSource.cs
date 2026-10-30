using System;
using System.IO;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>The water's shader sources (unity/Assets/FlowingWater/Shaders), read for the tests that check them.</summary>
internal static class ShaderSource
{
    private const string Shaders = "unity/Assets/FlowingWater/Shaders/";

    /// <summary>The longest a match may take (the files are the repository's own, short).</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(1);

    /// <summary>A shader file's text.</summary>
    public static string Read(string file) => File.ReadAllText(Repository.File(Shaders + file));

    /// <summary>A function's body, from its signature to its closing brace (its braces balanced).</summary>
    public static string Function(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no {signature}");
        int open = source.IndexOf('{', start), depth = 0;
        for (int at = open; at < source.Length; at++)
        {
            depth += source[at] == '{' ? 1 : source[at] == '}' ? -1 : 0;
            if (depth == 0)
            {
                return source[start..(at + 1)];
            }
        }

        throw new InvalidDataException($"{signature} has no closing brace");
    }
}
