using MinecraftServerManager.Services;

namespace MinecraftServerManager.Tests;

public class JavaPropertiesDocumentTests
{
    private const string VanillaFile = """
        #Minecraft server properties
        #Tue Oct 20 12:00:00 BST 2026
        level-name=world
        level-type=minecraft\:normal
        motd=A Minecraft Server
        resource-pack=https\://example.com/pack.zip
        generator-settings={}
        """;

    [Fact]
    public void Parse_ReadsValuesAndUnescapesColons()
    {
        var doc = JavaPropertiesDocument.Parse(VanillaFile);

        Assert.Equal("world", doc.Get("level-name"));
        Assert.Equal("minecraft:normal", doc.Get("level-type"));
        Assert.Equal("https://example.com/pack.zip", doc.Get("resource-pack"));
        Assert.Equal("{}", doc.Get("generator-settings"));
        Assert.Equal(5, doc.Count);
    }

    [Fact]
    public void ToText_PreservesCommentsOrderAndEscaping()
    {
        var doc = JavaPropertiesDocument.Parse(VanillaFile);

        var expected = VanillaFile.ReplaceLineEndings(Environment.NewLine) + Environment.NewLine;
        Assert.Equal(expected, doc.ToText());
    }

    [Theory]
    [InlineData("key=value", "key", "value")]
    [InlineData("key = value", "key", "value")]
    [InlineData("key:value", "key", "value")]
    [InlineData("key value", "key", "value")]
    [InlineData("   key=  value with spaces  ", "key", "value with spaces  ")]
    [InlineData("key==value", "key", "=value")]
    [InlineData("key=", "key", "")]
    [InlineData(@"tab\tkey=a\tb", "tab\tkey", "a\tb")]
    [InlineData("unicode=" + @"\" + "u00A7aHello", "unicode", "§aHello")]
    public void Parse_HandlesJavaSeparatorsAndEscapes(string line, string key, string value)
    {
        var doc = JavaPropertiesDocument.Parse(line);
        Assert.Equal(value, doc.Get(key));
    }

    [Fact]
    public void Parse_JoinsContinuationLines()
    {
        var doc = JavaPropertiesDocument.Parse("motd=Hello \\\n    World\nother=1\n");
        Assert.Equal("Hello World", doc.Get("motd"));
        Assert.Equal("1", doc.Get("other"));
    }

    [Fact]
    public void SetAndRemove_RoundTripThroughText()
    {
        var doc = JavaPropertiesDocument.Parse(VanillaFile);
        doc.Set("motd", "§aWelcome: #1 server! a=b");
        doc.Set("new-key", " leading space");
        Assert.True(doc.Remove("resource-pack"));
        Assert.False(doc.Remove("missing"));

        var text = doc.ToText();
        // Non-ASCII is written as a \uXXXX escape so the file is valid in both UTF-8 and ISO-8859-1.
        Assert.Contains("motd=" + @"\" + "u00A7aWelcome" + @"\: \#1 server\! a\=b", text);
        Assert.Contains(@"new-key=\ leading space", text);
        Assert.DoesNotContain("resource-pack", text);

        var reparsed = JavaPropertiesDocument.Parse(text);
        Assert.Equal("§aWelcome: #1 server! a=b", reparsed.Get("motd"));
        Assert.Equal(" leading space", reparsed.Get("new-key"));
        Assert.Equal(["level-name", "level-type", "motd", "generator-settings", "new-key"], reparsed.Entries.Select(e => e.Key));
    }

    [Fact]
    public void Load_FallsBackToLatin1ForNonUtf8Files()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [.. "motd="u8.ToArray(), 0xA7, (byte)'a', (byte)'X']); // "§aX" in ISO-8859-1
            Assert.Equal("§aX", JavaPropertiesDocument.Load(path).Get("motd"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
