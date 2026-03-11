using System.Runtime.InteropServices;
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
            Languages: languages ?? [],
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

    private static RelativePath AddDirChain(ProjectDependencyGraph graph, string projectRoot, params string[] dirsRel)
    {
        var rootDir = graph.UpsertProjectItem(RelativePath.Directory(projectRoot, "./"), ProjectItemType.Directory);

        RelativePath? parent = rootDir;

        foreach (var d in dirsRel)
        {
            var dirRel = RelativePath.Directory(projectRoot, d);
            var dirId = graph.UpsertProjectItem(dirRel, ProjectItemType.Directory);

            if (parent is not null)
                graph.AddChild(parent.Value, dirId);

            parent = dirId;
        }

        return parent ?? rootDir;
    }

    [Fact]
    public async Task SnapshotNull_ReturnsFullStructure_AndNoDeletions()
    {
        var t = new DateTime(2026, 03, 05, 12, 00, 00, DateTimeKind.Utc);

        _fs.File("A.cs", "class A {}", t);
        _fs.File("src/B.cs", "class B {}", t);
        _fs.Dir("src/EmptyDir");

        var opts = MakeOptions();
        ProjectDependencyGraph? snap = null;

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.Empty(changes.DeletedFiles);
        Assert.Empty(changes.DeletedDirectories);

        var rootDir = RelativePath.Directory(_fs.Root, "./");
        var srcDir = RelativePath.Directory(_fs.Root, "./src/");
        var emptyDir = RelativePath.Directory(_fs.Root, "./src/EmptyDir/");
        var aFile = RelativePath.File(_fs.Root, "./A.cs");
        var bFile = RelativePath.File(_fs.Root, "./src/B.cs");

        Assert.Contains(srcDir, changes.ChangedFilesByDirectory.Keys);
        Assert.Contains(bFile, changes.ChangedFilesByDirectory[srcDir]);

        if (changes.ChangedFilesByDirectory.TryGetValue(rootDir, out var rootChildren))
            Assert.Contains(aFile, rootChildren);

        Assert.Contains(emptyDir, changes.ChangedFilesByDirectory[srcDir]);
    }

    [Fact]
    public async Task Delta_IncludesAncestors_ForDeepChangedFile()
    {
        var oldT = new DateTime(2026, 03, 05, 12, 00, 00, DateTimeKind.Utc);
        var newT = new DateTime(2026, 03, 05, 12, 10, 00, DateTimeKind.Utc);

        _fs.File("src/a/b/C.cs", "class C {}", newT);

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        AddDirChain(snap, _fs.Root, "src/", "src/a/", "src/a/b/");
        AddFile(snap, _fs.Root, "src/a/b/C.cs", oldT);

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var srcDir = RelativePath.Directory(_fs.Root, "./src/");
        var aDir = RelativePath.Directory(_fs.Root, "./src/a/");
        var bDir = RelativePath.Directory(_fs.Root, "./src/a/b/");
        var cFile = RelativePath.File(_fs.Root, "./src/a/b/C.cs");

        Assert.Contains(srcDir, changes.ChangedFilesByDirectory.Keys);
        Assert.Contains(aDir, changes.ChangedFilesByDirectory[srcDir]);

        Assert.Contains(aDir, changes.ChangedFilesByDirectory.Keys);
        Assert.Contains(bDir, changes.ChangedFilesByDirectory[aDir]);

        Assert.Contains(bDir, changes.ChangedFilesByDirectory.Keys);
        Assert.Contains(cFile, changes.ChangedFilesByDirectory[bDir]);
    }

    [Fact]
    public async Task Delta_DoesNotInclude_UnchangedSiblingFiles()
    {
        var t = new DateTime(2026, 03, 05, 12, 00, 00, DateTimeKind.Utc);
        var newer = new DateTime(2026, 03, 05, 12, 05, 00, DateTimeKind.Utc);

        _fs.File("src/a/b/Changed.cs", "class Changed {}", newer);
        _fs.File("src/a/b/Keep.cs", "class Keep {}", t);

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        AddDirChain(snap, _fs.Root, "src/", "src/a/", "src/a/b/");
        AddFile(snap, _fs.Root, "src/a/b/Changed.cs", t);
        AddFile(snap, _fs.Root, "src/a/b/Keep.cs", t);

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var bDir = RelativePath.Directory(_fs.Root, "./src/a/b/");
        var changed = RelativePath.File(_fs.Root, "./src/a/b/Changed.cs");
        var keep = RelativePath.File(_fs.Root, "./src/a/b/Keep.cs");

        Assert.Contains(bDir, changes.ChangedFilesByDirectory.Keys);
        Assert.Contains(changed, changes.ChangedFilesByDirectory[bDir]);
        Assert.DoesNotContain(keep, changes.ChangedFilesByDirectory[bDir]);
    }

    [Fact]
    public async Task NewEmptyDirectory_IsReported_InDelta()
    {
        _fs.Dir("src");
        _fs.Dir("src/NewDir");

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);
        AddDirChain(snap, _fs.Root, "src/");

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var srcDir = RelativePath.Directory(_fs.Root, "./src/");
        var newDir = RelativePath.Directory(_fs.Root, "./src/NewDir/");

        Assert.Contains(srcDir, changes.ChangedFilesByDirectory.Keys);
        Assert.Contains(newDir, changes.ChangedFilesByDirectory.Keys);
        Assert.Contains(newDir, changes.ChangedFilesByDirectory[srcDir]);
    }

    [Fact]
    public async Task DeletedDirectories_AreCollapsed_AndDeletedFilesUnderThemAreRemoved()
    {
        _fs.Dir("src");
        _fs.File("src/Keep.cs", "class Keep {}");

        var t = new DateTime(2026, 03, 05, 12, 00, 00, DateTimeKind.Utc);

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        AddFile(snap, _fs.Root, "src/Keep.cs", t);
        AddFile(snap, _fs.Root, "src/OldDir/Del1.cs", t);
        AddFile(snap, _fs.Root, "src/OldDir/SubDir/Del2.cs", t);

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var oldDir = RelativePath.Directory(_fs.Root, "./src/OldDir/");
        var subDir = RelativePath.Directory(_fs.Root, "./src/OldDir/SubDir/");
        var del1 = RelativePath.File(_fs.Root, "./src/OldDir/Del1.cs");
        var del2 = RelativePath.File(_fs.Root, "./src/OldDir/SubDir/Del2.cs");

        Assert.Contains(oldDir, changes.DeletedDirectories);
        Assert.DoesNotContain(subDir, changes.DeletedDirectories);

        Assert.DoesNotContain(del1, changes.DeletedFiles);
        Assert.DoesNotContain(del2, changes.DeletedFiles);
    }

    [Fact]
    public async Task MillisecondDifferences_DoNotCountAsChanges()
    {
        var oldT = new DateTime(2026, 03, 05, 12, 00, 00, 100, DateTimeKind.Utc);
        var newT = new DateTime(2026, 03, 05, 12, 00, 00, 900, DateTimeKind.Utc); //only milliseconds differ

        _fs.File("src/Ms.cs", "class Ms {}", newT);

        var opts = MakeOptions();
        var snap = MakeDefaultSnapshotGraph(_fs.Root);
        AddFile(snap, _fs.Root, "src/Ms.cs", oldT);

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        Assert.Empty(changes.ChangedFilesByDirectory);
    }

    [Theory]
    [InlineData("bin", "src/bin/X.cs", true)] // passes
    [InlineData("BIN", "src/bin/X.cs", true)] // fails
    [InlineData("*.dev.cs", "src/A.dev.cs", true)] // passes
    [InlineData("**.dev.cs", "src/A.dev.cs", true)] // passes
    [InlineData(".git", ".git/hooks/Hook.cs", true)] // fails
    public async Task Exclusions_ActAsExpected_InScan(string exclusion, string relFile, bool shouldExclude)
    {
        _fs.File(relFile, "class X {}");
        _fs.File("src/Keep.cs", "class Keep {}");

        var opts = MakeOptions(exclusions: [exclusion]);
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var file = RelativePath.File(_fs.Root, "./" + relFile.Replace('\\', '/'));
        var appearsAnywhere = changes.ChangedFilesByDirectory.Values.Any(v => v.Contains(file));

        Assert.Equal(!shouldExclude, appearsAnywhere);
    }

    [Fact]
    public async Task ExcludedSnapshotItems_ShouldNotBeReportedAsDeleted_ButCurrentlyWillBe()
    {
        _fs.File("src/Keep.cs", "class Keep {}");

        var t = new DateTime(2026, 03, 05, 12, 00, 00, DateTimeKind.Utc);

        var opts = MakeOptions(exclusions: ["bin"]);
        var snap = MakeDefaultSnapshotGraph(_fs.Root);

        AddFile(snap, _fs.Root, "src/Keep.cs", t);
        AddFile(snap, _fs.Root, "src/bin/Gen.cs", t);

        var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

        var binDir = RelativePath.Directory(_fs.Root, "./src/bin/");
        var binFile = RelativePath.File(_fs.Root, "./src/bin/Gen.cs");

        Assert.DoesNotContain(binDir, changes.DeletedDirectories);
        Assert.DoesNotContain(binFile, changes.DeletedFiles);
    }

    [Theory]
    [InlineData("bin", "bin", true)] // passes
    [InlineData("BIN", "bin", true)] // fails
    [InlineData("**.dev.cs", "A.dev.cs", true)] // passes
    [InlineData("*.dev.cs", "A.dev.cs", true)] // passes
    [InlineData("*.dev.cs", "A.DEV.CS", true)] // fails
    [InlineData("bin*", "bin123", false)] // passes
    public void MatchesSuffixPattern_Behaviour_IsPinned(string pattern, string value, bool expected)
    {
        var actual = ChangeDetector.MatchesSuffixPattern(value, pattern);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Scan_Ignores_UnreadableDirectories_OnUnix()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        _fs.Dir("src");
        _fs.Dir("src/Locked");
        _fs.File("src/Ok.cs", "class Ok {}");

        var lockedAbs = Path.Combine(_fs.Root, "src", "Locked");
        var chmod = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = "000 \"" + lockedAbs + "\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        chmod!.WaitForExit();

        try
        {
            var opts = MakeOptions();
            var snap = MakeDefaultSnapshotGraph(_fs.Root);

            var changes = await ChangeDetector.GetProjectChangesAsync(opts, snap);

            var okFile = RelativePath.File(_fs.Root, "./src/Ok.cs");
            var srcDir = RelativePath.Directory(_fs.Root, "./src/");

            Assert.Contains(srcDir, changes.ChangedFilesByDirectory.Keys);
            Assert.Contains(okFile, changes.ChangedFilesByDirectory[srcDir]);
        }
        finally
        {
            var chmodBack = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = "755 \"" + lockedAbs + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            chmodBack!.WaitForExit();
        }
    }

}