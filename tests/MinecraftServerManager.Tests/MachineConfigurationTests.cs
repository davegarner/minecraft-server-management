using System.Text.Json.Nodes;
using MinecraftServerManager.Hosting;

namespace MinecraftServerManager.Tests;

public class MachineConfigurationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("mcm-config-").FullName;

    private string FilePath => Path.Combine(_directory, "appsettings.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private int Run(params string[] args) => MachineConfiguration.Run(args, FilePath, TextWriter.Null);

    private JsonObject Read() => (JsonObject)JsonNode.Parse(File.ReadAllText(FilePath))!;

    [Fact]
    public void FirstRun_WritesLocalhostDefaultAndDataDirectory()
    {
        Assert.Equal(0, Run("--keep-existing"));

        var config = Read();
        Assert.Equal("http://localhost:8080", (string?)config["Urls"]);
        Assert.Equal(Path.Combine(_directory, "data"), (string?)config["Manager"]!["DataDirectory"]);
    }

    [Fact]
    public void PortAndRemoteAccess_AreApplied()
    {
        Assert.Equal(0, Run("--port", "9090", "--remote-access"));
        Assert.Equal("http://0.0.0.0:9090", (string?)Read()["Urls"]);

        Assert.Equal(0, Run("--local-only"));
        Assert.Equal("http://localhost:9090", (string?)Read()["Urls"]); // port kept
    }

    [Fact]
    public void KeepExisting_LeavesAddressAndOtherKeysAlone()
    {
        File.WriteAllText(FilePath, """{ "Urls": "http://localhost:7000", "Custom": 42, "Manager": { "DataDirectory": "D:\\Panel" } }""");

        Assert.Equal(0, Run("--port", "8080", "--remote-access", "--keep-existing"));

        var config = Read();
        Assert.Equal("http://localhost:7000", (string?)config["Urls"]);
        Assert.Equal(42, (int?)config["Custom"]);
        Assert.Equal("D:\\Panel", (string?)config["Manager"]!["DataDirectory"]);
    }

    [Fact]
    public void WildcardHostsAreUnderstoodAsRemote()
    {
        File.WriteAllText(FilePath, """{ "Urls": "http://*:8123" }""");

        Assert.Equal(0, Run("--port", "8124"));

        Assert.Equal("http://0.0.0.0:8124", (string?)Read()["Urls"]);
    }

    [Theory]
    [InlineData("--port")]
    [InlineData("--port", "0")]
    [InlineData("--port", "70000")]
    [InlineData("--port", "abc")]
    [InlineData("--unknown")]
    public void InvalidArguments_ReturnTwoAndWriteNothing(params string[] args)
    {
        Assert.Equal(2, Run(args));
        Assert.False(File.Exists(FilePath));
    }
}
