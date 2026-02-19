using Archlens.Application;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using ArchlensTests.Utils;

namespace ArchlensTests.Application;

public sealed class ChangeDetectorTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    private ParserOptions MakeOptions(IReadOnlyList<string>? exclusions = null, IReadOnlyList<string>? extensions = null, IReadOnlyList<Language>? languages = default)
        => new(
            BaseOptions: new BaseOptions(
                FullRootPath: _fs.Root,
                ProjectRoot: _fs.Root,
                ProjectName: "TestProject"
            ),
            Languages: languages,
            Exclusions: exclusions ?? [],
            FileExtensions: extensions ?? [".cs"]
        );

    public void Dispose() => _fs.Dispose();

    private static SnapshotGraph MakeDefaultSnapshotGraph(string projectRoot)
    {
        return new SnapshotGraph(projectRoot)
        {
            Name = "src",
            Path = "./",
            LastWriteTime = DateTime.UtcNow
        };
    }

    private sealed class SnapshotGraph(string projectRoot) : DependencyGraph(projectRoot)
    {
        private readonly Dictionary<string, DependencyGraph> _children = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string relPath, DateTime lastWriteUtc)
        {
            var n = new DependencyGraphLeaf(projectRoot) { Name = System.IO.Path.GetFileName(relPath), Path = relPath, LastWriteTime = lastWriteUtc };
            var dir = System.IO.Path.GetDirectoryName(relPath) ?? ".";
            _children[relPath] = n;
            _children[dir] = new DependencyGraphNode(projectRoot) { Name = dir, Path = dir, LastWriteTime = lastWriteUtc };
        }

        public override IReadOnlyList<DependencyGraph> GetChildren() => [.. _children.Values];

        public override DependencyGraph GetChild(string path)
        {
            var key = path.Replace('\\', '/');
            if (_children.TryGetValue(key, out var n)) return n;
            if (_children.TryGetValue(key.TrimEnd('/'), out n)) return n;
            return null!;
        }
    }

    [Fact]
    public async Task Returns_NewFiles_When_NotInLastSavedGraph()
    {
        var t = DateTime.UtcNow;
        _fs.File("src/A.cs", "class A {}", t);

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.Contains("./src/", changed.ChangedFilesByDirectory.Keys);
        Assert.Contains("./src/A.cs", changed.ChangedFilesByDirectory["./src/"]);
    }

    [Fact]
    public async Task DoesNotReturn_Unchanged_When_TimestampsEqual()
    {
        var t = DateTime.UtcNow.AddMinutes(-5);
        _fs.File("src/B.cs", "class B {}", t);

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);
        snap.AddFile("src/B.cs", t);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.Empty(changed.ChangedFilesByDirectory);
    }

    [Fact]
    public async Task Returns_Modified_When_CurrentIsNewer()
    {
        var oldT = DateTime.UtcNow.AddMinutes(-10);
        var newT = DateTime.UtcNow.AddMinutes(-1);

        _fs.File("src/C.cs", "class C {}", newT);

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);
        snap.AddFile("src/C.cs", oldT);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.Single(changed.ChangedFilesByDirectory);
        var mod = changed.ChangedFilesByDirectory.Single();
        Assert.Equal("./src/", mod.Key);
        Assert.Contains("./src/C.cs", mod.Value);
    }

    [Fact]
    public async Task Respects_FileExtensions_Filter()
    {
        _fs.File("src/A.txt", "text");
        _fs.File("src/B.cs", "class B {}");

        var opts = MakeOptions(extensions: [".cs"]);
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var srcKey = "./src/";
        Assert.DoesNotContain("./src/A.txt", changed.ChangedFilesByDirectory[srcKey]);

        Assert.Contains(srcKey, changed.ChangedFilesByDirectory.Keys);
        Assert.Contains("./src/B.cs", changed.ChangedFilesByDirectory[srcKey]);
    }

    [Fact]
    public async Task Excludes_DirectoryPrefix_RelativeWithSlash()
    {
        _fs.File("Tests/X.cs", "class X {}");
        _fs.File("src/Y.cs", "class Y {}");

        var opts = MakeOptions(exclusions: ["Tests/"]);
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.DoesNotContain("./Tests/", changed.ChangedFilesByDirectory.Keys);
        Assert.Contains("./src/", changed.ChangedFilesByDirectory.Keys);
    }

    [Fact]
    public async Task Excludes_Segment_bin_Anywhere()
    {
        _fs.File("src/bin/Gen.cs", "class Gen {}");
        _fs.File("src/good/Ok.cs", "class Ok {}");

        var opts = MakeOptions(exclusions: ["bin"]);
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.Contains("./src/good/", changed.ChangedFilesByDirectory.Keys);
        Assert.DoesNotContain("./src/bin/", changed.ChangedFilesByDirectory.Keys);
        Assert.DoesNotContain("./src/bin/Gen.cs",
                              changed.ChangedFilesByDirectory.GetValueOrDefault("./src/") ?? []);
    }

    [Fact]
    public async Task Excludes_FilenameSuffix_Wildcard_With_TrailingDot()
    {
        _fs.File("src/A.dev.cs", "class ADev {}");
        _fs.File("src/A.cs", "class A {}");

        var opts = MakeOptions(exclusions: ["**.dev.cs."]);
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var srcKey = "./src/";
        Assert.Contains(srcKey, changed.ChangedFilesByDirectory.Keys);

        Assert.DoesNotContain("./src/A.dev.cs", changed.ChangedFilesByDirectory[srcKey]);
        Assert.Contains("./src/A.cs", changed.ChangedFilesByDirectory[srcKey]);
    }

    [Fact]
    public async Task Detects_Changes_WithMultipleParsers()
    {
        _fs.File("src/A.cs", "class ACS {}");
        _fs.File("src/A.go", "class AGO {}");
        _fs.File("src/A.kt", "class AKT {}");

        var opts = MakeOptions(extensions: [".cs", ".go"], languages: [Language.CSharp, Language.Go]);
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var srcKey = "./src/";
        Assert.Contains(srcKey, changed.ChangedFilesByDirectory.Keys);
        Assert.Contains("./src/A.cs", changed.ChangedFilesByDirectory[srcKey]);
        Assert.Contains("./src/A.go", changed.ChangedFilesByDirectory[srcKey]);
        Assert.DoesNotContain("./src/A.kt", changed.ChangedFilesByDirectory[srcKey]);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        _fs.File("src/A.cs", "class A {}");

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await ChangeDetector.GetProjectChangesAsync(opts, snap, cts.Token));
    }

    [Fact]
    public async Task File_in_Root_Deleted_Recognised() 
    {
        _fs.Dir("src");
        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);
        snap.AddFile("src/Deleted.cs", DateTime.UtcNow.AddMinutes(-5)); // not in _fs (deleted)

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.Contains("./src/Deleted.cs", changes.DeletedFiles);
    }

    [Fact]
    public async Task File_in_SubDir_Deleted_Recognised()
    {
        _fs.Dir("src");
        _fs.Dir("src/Dir");
        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);
        snap.AddFile("src/Dir/Deleted.cs", DateTime.UtcNow.AddMinutes(-5));  // not in _fs (deleted)

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.Contains("./src/Dir/Deleted.cs", changes.DeletedFiles);
    }

    [Fact]
    public async Task Dir_in_Root_Deleted_Recognised()
    {
        var t = DateTime.UtcNow.AddMinutes(-5);

        _fs.File("src/Keep.cs", "class Keep {}", t);
        _fs.File("src/Dir/Keep.cs", "class Keep {}", t);

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);
        snap.AddFile("src/Keep.cs", t);
        snap.AddFile("src/Dir/Keep.cs", t);
        snap.AddFile("src/OldDir/Old.cs", t); // not in _fs (deleted)

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.DoesNotContain("./src/Keep.cs", changes.DeletedFiles);
        Assert.DoesNotContain("./src/Dir/Keep.cs", changes.DeletedFiles);
        Assert.DoesNotContain("./src/Dir/", changes.DeletedDirectories);

        Assert.Contains("./src/OldDir/", changes.DeletedDirectories);
    }

    [Fact]
    public async Task Dir_in_SubDir_Deleted_Recognised()
    {
        var t = DateTime.UtcNow.AddMinutes(-5);

        _fs.File("src/Keep.cs", "class Keep {}", t);
        _fs.File("src/Dir/Keep.cs", "class Keep {}", t);        

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        snap.AddFile("src/Keep.cs", t);
        snap.AddFile("src/Dir/Keep.cs", t);
        snap.AddFile("src/Dir/OldDir/Old.cs", t); // not in _fs (deleted)

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.DoesNotContain("./src/Keep.cs", changes.DeletedFiles);
        Assert.DoesNotContain("./src/Dir/Keep.cs", changes.DeletedFiles);
        Assert.DoesNotContain("./src/Dir/", changes.DeletedDirectories);

        Assert.Contains("./src/Dir/OldDir/", changes.DeletedDirectories);
        //Assert.Contains("./src/Dir/OldDir/Old.cs", changes.DeletedFiles); we are collapsing the dirs, so it doesnt show nested children of the dir
    }


    [Fact]
    public async Task Removes_Files_And_SubDirs_Under_Deleted_Dir_Recognised()
    {
        _fs.Dir("src");
        _fs.File("src/Keep.cs");

        var t = DateTime.UtcNow.AddMinutes(-5);
        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);
        snap.AddFile("src/Keep.cs", t);
        snap.AddFile("src/OldDir/Del1.cs", t);  // not in _fs (deleted)
        snap.AddFile("src/OldDir/Del2.cs", t);  // not in _fs (deleted)
        snap.AddFile("src/OldDir/SubDir/Del3.cs", t);  // not in _fs (deleted)

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.DoesNotContain("src/Keep.cs", changes.DeletedFiles);

        Assert.Contains("./src/", changes.ChangedFilesByDirectory);
        Assert.Contains("./src/OldDir/", changes.DeletedDirectories);
    }

}