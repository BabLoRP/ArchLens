using Archlens.Application;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;
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

    private static ProjectDependencyGraph MakeDefaultSnapshotGraph(string projectRoot)
    {
        var graph = new ProjectDependencyGraph(projectRoot);
        _ = graph.UpsertProjectItem(
            RelativePath.Directory(projectRoot, "./"),
            ProjectItemType.Directory);

        return graph;
    }

    private static RelativePath AddFile(
        ProjectDependencyGraph graph,
        string projectRoot,
        string relPath,
        DateTime lastWriteUtc,
        IEnumerable<string>? dependencies = null)
    {
        var file = RelativePath.File(projectRoot, relPath);
        var fileId = graph.UpsertProjectItem(file, ProjectItemType.File);

        graph.UpsertProjectItems([
            new ProjectItem(
                Path: fileId,
                Name: Path.GetFileName(relPath),
                LastWriteTime: lastWriteUtc,
                Type: ProjectItemType.File)
        ]);

        var parentDirRel = Path.GetDirectoryName(relPath)?.Replace('\\', '/') ?? "./";
        var parent = graph.UpsertProjectItem(
            RelativePath.Directory(projectRoot, parentDirRel),
            ProjectItemType.Directory);

        graph.AddChild(parent, fileId);

        if (dependencies is not null)
        {
            var depMap = new Dictionary<RelativePath, Dependency>();
            foreach (var dep in dependencies)
            {
                var depPath = RelativePath.File(projectRoot, dep);

                if (depMap.TryGetValue(depPath, out var existing))
                    depMap[depPath] = existing with { Count = existing.Count + 1 };
                else
                    depMap[depPath] = new Dependency(1, DependencyType.Uses);
            }

            graph.AddDependencies(fileId, depMap);
        }

        return fileId;
    }

    public void Dispose() => _fs.Dispose();

    [Fact]
    public async Task Returns_NewFiles_When_NotInLastSavedGraph()
    {
        var t = DateTime.UtcNow;
        _fs.File("src/A.cs", "class A {}", t);

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var srcPath = RelativePath.Directory(_fs.Root, "./src/");
        var aPath = RelativePath.File(_fs.Root, "./src/A.cs");

        Assert.Contains(srcPath, changed.ChangedFilesByDirectory.Keys);
        Assert.Contains(aPath, changed.ChangedFilesByDirectory[srcPath]);
    }

    [Fact]
    public async Task DoesNotReturn_Unchanged_When_TimestampsEqual()
    {
        var t = DateTime.UtcNow.AddMinutes(-5);
        _fs.File("src/B.cs", "class B {}", t);

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);
        AddFile(snap, _fs.Root, "src/B.cs", t);

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
        AddFile(snap, _fs.Root, "src/C.cs", oldT);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.Single(changed.ChangedFilesByDirectory);
        var mod = changed.ChangedFilesByDirectory.Single();

        var srcPath = RelativePath.Directory(_fs.Root, "./src/");
        var cPath = RelativePath.File(_fs.Root, "./src/C.cs");
        Assert.Equal(srcPath, mod.Key);
        Assert.Contains(cPath, mod.Value);
    }

    [Fact]
    public async Task Respects_FileExtensions_Filter()
    {
        _fs.File("src/A.txt", "text");
        _fs.File("src/B.cs", "class B {}");

        var opts = MakeOptions(extensions: [".cs"]);
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var srcPath = RelativePath.Directory(_fs.Root, "./src/");
        var aPath = RelativePath.File(_fs.Root, "./src/A.txt");
        var bPath = RelativePath.File(_fs.Root, "./src/B.cs");

        Assert.DoesNotContain(aPath, changed.ChangedFilesByDirectory[srcPath]);

        Assert.Contains(srcPath, changed.ChangedFilesByDirectory.Keys);
        Assert.Contains(bPath, changed.ChangedFilesByDirectory[srcPath]);
    }

    [Fact]
    public async Task Excludes_DirectoryPrefix_RelativeWithSlash()
    {
        _fs.File("Tests/X.cs", "class X {}");
        _fs.File("src/Y.cs", "class Y {}");

        var opts = MakeOptions(exclusions: ["Tests/"]);
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var srcPath = RelativePath.Directory(_fs.Root, "./src/");
        var testPath = RelativePath.Directory(_fs.Root, "./Tests/");

        Assert.DoesNotContain(testPath, changed.ChangedFilesByDirectory.Keys);
        Assert.Contains(srcPath, changed.ChangedFilesByDirectory.Keys);
    }

    [Fact]
    public async Task Excludes_Segment_bin_Anywhere()
    {
        _fs.File("src/bin/Gen.cs", "class Gen {}");
        _fs.File("src/good/Ok.cs", "class Ok {}");

        var opts = MakeOptions(exclusions: ["bin"]);
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var srcPath = RelativePath.Directory(_fs.Root, "./src/");
        var goodDirPath = RelativePath.Directory(_fs.Root, "./src/good/");
        var binDirPath = RelativePath.Directory(_fs.Root, "./src/bin/");
        var genPath = RelativePath.File(_fs.Root, "./src/bin/Gen.cs");

        Assert.Contains(goodDirPath, changed.ChangedFilesByDirectory.Keys);
        Assert.DoesNotContain(binDirPath, changed.ChangedFilesByDirectory.Keys);
    }

    [Fact]
    public async Task Excludes_FilenameSuffix_Wildcard_With_TrailingDot()
    {
        _fs.File("src/A.dev.cs", "class ADev {}");
        _fs.File("src/A.cs", "class A {}");

        var opts = MakeOptions(exclusions: ["**.dev.cs."]);
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changed = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var srcPath = RelativePath.Directory(_fs.Root, "./src/");
        var aDevPath = RelativePath.File(_fs.Root, "./src/A.dev.cs");
        var aPath = RelativePath.File(_fs.Root, "./src/A.cs");

        Assert.Contains(srcPath, changed.ChangedFilesByDirectory.Keys);

        Assert.DoesNotContain(aDevPath, changed.ChangedFilesByDirectory[srcPath]);
        Assert.Contains(aPath, changed.ChangedFilesByDirectory[srcPath]);
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

        var srcPath = RelativePath.Directory(_fs.Root, "./src/");
        var aCsPath = RelativePath.File(_fs.Root, "./src/A.cs");
        var aGoPath = RelativePath.File(_fs.Root, "./src/A.go");
        var aKtPath = RelativePath.File(_fs.Root, "./src/A.kt");

        Assert.Contains(srcPath, changed.ChangedFilesByDirectory.Keys);
        Assert.Contains(aCsPath, changed.ChangedFilesByDirectory[srcPath]);
        Assert.Contains(aGoPath, changed.ChangedFilesByDirectory[srcPath]);
        Assert.DoesNotContain(aKtPath, changed.ChangedFilesByDirectory[srcPath]);
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
        AddFile(snap, _fs.Root, "src/Deleted.cs", DateTime.UtcNow.AddMinutes(-5)); // not in _fs (deleted)

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var deletedPath = RelativePath.File(_fs.Root, "./src/Deleted.cs");
        Assert.Contains(deletedPath, changes.DeletedFiles);
    }

    [Fact]
    public async Task File_in_SubDir_Deleted_Recognised()
    {
        _fs.Dir("src");
        _fs.Dir("src/Dir");
        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);
        AddFile(snap, _fs.Root, "src/Dir/Deleted.cs", DateTime.UtcNow.AddMinutes(-5));  // not in _fs (deleted)

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var deletedPath = RelativePath.File(_fs.Root, "./src/Dir/Deleted.cs");
        Assert.Contains(deletedPath, changes.DeletedFiles);
    }

    [Fact]
    public async Task Dir_in_Root_Deleted_Recognised()
    {
        var t = DateTime.UtcNow.AddMinutes(-5);

        _fs.File("src/Keep.cs", "class Keep {}", t);
        _fs.File("src/Dir/Keep.cs", "class Keep {}", t);

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);
        AddFile(snap, _fs.Root, "src/Keep.cs", t);
        AddFile(snap, _fs.Root, "src/Dir/Keep.cs", t);
        AddFile(snap, _fs.Root, "src/OldDir/Old.cs", t); // not in _fs (deleted)

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var keepPath = RelativePath.File(_fs.Root, "./src/Keep.cs");
        var subDirKeepPath = RelativePath.File(_fs.Root, "./src/Dir/Keep.cs");
        var subDirPath = RelativePath.Directory(_fs.Root, "./src/Dir/");
        var oldDirPath = RelativePath.Directory(_fs.Root, "./src/OldDir/");

        Assert.DoesNotContain(keepPath, changes.DeletedFiles);
        Assert.DoesNotContain(subDirKeepPath, changes.DeletedFiles);
        Assert.DoesNotContain(subDirPath, changes.DeletedDirectories);

        Assert.Contains(oldDirPath, changes.DeletedDirectories);
    }

    [Fact]
    public async Task Dir_in_SubDir_Deleted_Recognised()
    {
        var t = DateTime.UtcNow.AddMinutes(-5);

        _fs.File("src/Keep.cs", "class Keep {}", t);
        _fs.File("src/Dir/Keep.cs", "class Keep {}", t);        

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        AddFile(snap, _fs.Root, "src/Keep.cs", t);
        AddFile(snap, _fs.Root, "src/Dir/Keep.cs", t);
        AddFile(snap, _fs.Root, "src/Dir/OldDir/Old.cs", t); // not in _fs (deleted)

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var keepPath = RelativePath.File(_fs.Root, "./src/Keep.cs");
        var subDirKeepPath = RelativePath.File(_fs.Root, "./src/Dir/Keep.cs");
        var subDirPath = RelativePath.Directory(_fs.Root, "./src/Dir/");
        var subOldDirPath = RelativePath.Directory(_fs.Root, "./src/Dir/OldDir/");

        Assert.DoesNotContain(keepPath, changes.DeletedFiles);
        Assert.DoesNotContain(subDirKeepPath, changes.DeletedFiles);
        Assert.DoesNotContain(subDirPath, changes.DeletedDirectories);

        Assert.Contains(subOldDirPath, changes.DeletedDirectories);
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
        AddFile(snap, _fs.Root, "src/Keep.cs", t);
        AddFile(snap, _fs.Root, "src/OldDir/Del1.cs", t);  // not in _fs (deleted)
        AddFile(snap, _fs.Root, "src/OldDir/Del2.cs", t);  // not in _fs (deleted)
        AddFile(snap, _fs.Root, "src/OldDir/SubDir/Del3.cs", t);  // not in _fs (deleted)

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var keepPath = RelativePath.File(_fs.Root, "./src/Keep.cs");
        Assert.DoesNotContain(keepPath, changes.DeletedFiles);

        var srcPath = RelativePath.Directory(_fs.Root, "./src/");
        var oldDirPath = RelativePath.Directory(_fs.Root, "./src/OldDir/");

        Assert.Contains(srcPath, changes.ChangedFilesByDirectory);
        Assert.Contains(oldDirPath, changes.DeletedDirectories);
    }

}