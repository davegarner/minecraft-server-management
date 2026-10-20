using System.IO.Compression;
using MinecraftServerManager.Services;

namespace MinecraftServerManager.Tests;

public class WorldAndBackupTests : IDisposable
{
    private readonly TestServerDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ListWorlds_ReadsLevelDatAndGroupsDimensionFolders()
    {
        _dir.CreateWorld("world", seed: -1234567890123L, gameType: 1);
        Directory.CreateDirectory(Path.Combine(_dir.ServerDirectory, "world_nether"));
        File.WriteAllBytes(Path.Combine(_dir.ServerDirectory, "world_nether", "level.dat"), TestServerDirectory.CreateLevelDat("world", 1, 0, "x"));
        _dir.CreateWorld("creative", seed: 42);
        Directory.CreateDirectory(Path.Combine(_dir.ServerDirectory, "logs"));

        var worlds = _dir.Worlds.ListWorlds();

        Assert.Equal(["world", "creative"], worlds.Select(w => w.Name)); // active first, nether folded in
        var world = worlds[0];
        Assert.True(world.IsActive);
        Assert.Equal(-1234567890123L, world.Level?.Seed);
        Assert.Equal("1.21.10", world.Level?.Version);
        Assert.Equal("Creative", world.Level?.GameModeName);
        Assert.True(world.Level?.Hardcore);
        Assert.True(world.SizeBytes > worlds[1].SizeBytes); // includes world_nether
    }

    [Fact]
    public async Task CreateWorld_WritesServerPropertiesWithoutStartingServer()
    {
        await _dir.Worlds.CreateWorldAsync(new NewWorldOptions
        {
            Name = "flat-test",
            Seed = "12345",
            LevelType = "minecraft:flat",
            GeneratorSettings = """{"layers":[{"block":"minecraft:bedrock","height":1}],"biome":"minecraft:plains"}""",
            Hardcore = true,
            Difficulty = "easy",
            StartServer = false,
        });

        var props = _dir.Properties.Load();
        Assert.Equal("flat-test", props.Get("level-name"));
        Assert.Equal("12345", props.Get("level-seed"));
        Assert.Equal("minecraft:flat", props.Get("level-type"));
        Assert.Equal("hard", props.Get("difficulty")); // hardcore forces hard
        Assert.Contains(@"level-type=minecraft\:flat", File.ReadAllText(_dir.Properties.FilePath));
        Assert.Equal(ServerState.Stopped, _dir.Server.State);

        _dir.CreateWorld("flat-test", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _dir.Worlds.CreateWorldAsync(new NewWorldOptions { Name = "flat-test", StartServer = false }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _dir.Worlds.CreateWorldAsync(new NewWorldOptions { Name = "other", GeneratorSettings = "{oops", StartServer = false }));
    }

    [Fact]
    public async Task Backup_ExcludesSessionLockAndParsesBack()
    {
        _dir.CreateWorld("my_world_1", 7);

        var backup = await _dir.Backups.CreateAsync("my_world_1", BackupKind.Manual);

        Assert.Equal("my_world_1", backup.World);
        Assert.Equal(BackupKind.Manual, backup.Kind);
        using var zip = ZipFile.OpenRead(Path.Combine(_dir.Backups.BackupDirectory, backup.FileName));
        var entries = zip.Entries.Select(e => e.FullName).Order().ToList();
        Assert.Equal(["my_world_1/level.dat", "my_world_1/region/r.0.0.mca"], entries);
        Assert.Single(_dir.Backups.List());
    }

    [Fact]
    public async Task Retention_KeepsNewestScheduledBackupsOnly()
    {
        _dir.CreateWorld("world", 1);
        _dir.Settings.Update(s => s.BackupRetentionCount = 2);

        await _dir.Backups.CreateAsync("world", BackupKind.Manual);
        for (var i = 0; i < 4; i++) await _dir.Backups.CreateAsync("world", BackupKind.Scheduled);

        var backups = _dir.Backups.List();
        Assert.Equal(2, backups.Count(b => b.Kind == BackupKind.Scheduled));
        Assert.Single(backups, b => b.Kind == BackupKind.Manual);
    }

    [Fact]
    public async Task Restore_ReplacesWorldAndKeepsSafetyBackup()
    {
        var path = _dir.CreateWorld("world", 99);
        var original = File.ReadAllBytes(Path.Combine(path, "region", "r.0.0.mca"));
        var backup = await _dir.Backups.CreateAsync("world", BackupKind.Manual);

        File.WriteAllBytes(Path.Combine(path, "region", "r.0.0.mca"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(path, "added-later.txt"), "x");

        await _dir.Backups.RestoreAsync(backup.FileName, backupCurrentFirst: true);

        Assert.Equal(original, File.ReadAllBytes(Path.Combine(path, "region", "r.0.0.mca")));
        Assert.False(File.Exists(Path.Combine(path, "added-later.txt")));
        Assert.Contains(_dir.Backups.List(), b => b.Kind == BackupKind.PreRestore);
        Assert.Empty(Directory.GetDirectories(_dir.ServerDirectory, "*.restore-old-*"));
    }

    [Fact]
    public async Task Restore_RejectsArchivesWithPathTraversal()
    {
        Directory.CreateDirectory(_dir.Backups.BackupDirectory);
        var evil = Path.Combine(_dir.Backups.BackupDirectory, "evil_2026-01-01_00-00-00_manual.zip");
        using (var zip = ZipFile.Open(evil, ZipArchiveMode.Create))
            zip.CreateEntry("../outside.txt");

        await Assert.ThrowsAsync<InvalidDataException>(() => _dir.Backups.RestoreAsync(Path.GetFileName(evil), backupCurrentFirst: false));
        Assert.False(File.Exists(Path.Combine(_dir.Root, "outside.txt")));
        Assert.Null(_dir.Backups.TryResolve("../server.properties"));
    }

    [Fact]
    public async Task Import_UsesFolderContainingLevelDatAndRejectsZipSlip()
    {
        var zipPath = Path.Combine(_dir.Root, "upload.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            Write(zip, "saves/My World/level.dat", TestServerDirectory.CreateLevelDat("My World", 5, 0, "1.21.10"));
            Write(zip, "saves/My World/region/r.0.0.mca", [1, 2, 3]);
            Write(zip, "saves/My World/session.lock", [0]);
            Write(zip, "__MACOSX/saves/My World/._level.dat", [0]);
        }

        await using (var stream = File.OpenRead(zipPath))
            await _dir.Worlds.ImportWorldAsync(stream, "imported", makeActive: true);

        var imported = Path.Combine(_dir.ServerDirectory, "imported");
        Assert.True(File.Exists(Path.Combine(imported, "level.dat")));
        Assert.True(File.Exists(Path.Combine(imported, "region", "r.0.0.mca")));
        Assert.False(File.Exists(Path.Combine(imported, "session.lock")));
        Assert.Equal("imported", _dir.Properties.LevelName);

        var evilPath = Path.Combine(_dir.Root, "evil.zip");
        using (var zip = ZipFile.Open(evilPath, ZipArchiveMode.Create))
        {
            Write(zip, "w/level.dat", [0]);
            Write(zip, "w/../../escaped.txt", [0]);
        }
        await using (var stream = File.OpenRead(evilPath))
            await Assert.ThrowsAsync<InvalidDataException>(() => _dir.Worlds.ImportWorldAsync(stream, "evil", makeActive: false));
        Assert.False(Directory.Exists(Path.Combine(_dir.ServerDirectory, "evil")));
        Assert.False(File.Exists(Path.Combine(_dir.ServerDirectory, "escaped.txt")));
    }

    [Fact]
    public async Task DeleteWorld_TakesBackupFirst()
    {
        _dir.CreateWorld("old", 3);

        await _dir.Worlds.DeleteWorldAsync("old", backupFirst: true);

        Assert.False(Directory.Exists(Path.Combine(_dir.ServerDirectory, "old")));
        Assert.Single(_dir.Backups.List(), b => b.World == "old" && b.Kind == BackupKind.PreDelete);
    }

    [Fact]
    public void Eula_IsAcceptedOnlyAfterAccepting()
    {
        Assert.False(_dir.Properties.IsEulaAccepted());
        _dir.Properties.AcceptEula();
        Assert.True(_dir.Properties.IsEulaAccepted());
        Assert.Contains("eula=true", File.ReadAllText(Path.Combine(_dir.ServerDirectory, "eula.txt")));
    }

    [Theory]
    [InlineData("world", BackupKind.Scheduled)]
    [InlineData("my_world_2026", BackupKind.PreUpdate)]
    public void BackupFileNames_RoundTrip(string world, BackupKind kind)
    {
        var timestamp = new DateTime(2026, 10, 20, 21, 39, 19);
        var parsed = BackupService.ParseFileName(BackupService.BuildFileName(world, timestamp, kind));

        Assert.NotNull(parsed);
        Assert.Equal(world, parsed.Value.World);
        Assert.Equal(timestamp, parsed.Value.Created);
        Assert.Equal(kind, parsed.Value.Kind);
        Assert.Equal("world", BackupService.ParseFileName("world_2026-10-20_21-39-19_manual_2.zip")?.World);
        Assert.Null(BackupService.ParseFileName("random.zip"));
    }

    private static void Write(ZipArchive zip, string name, byte[] content)
    {
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(content);
    }
}
