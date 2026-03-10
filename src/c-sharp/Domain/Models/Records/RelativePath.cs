using System.Linq;
using Archlens.Domain.Utils;

namespace Archlens.Domain.Models.Records;

public readonly record struct RelativePath
{
    public string Value { get; }

    private RelativePath(string value) => Value = value;

    public static RelativePath File(string projectRoot, string input)
        => new(PathNormaliser.NormaliseFile(projectRoot, input));

    public static RelativePath Directory(string projectRoot, string input)
        => new(PathNormaliser.NormaliseModule(projectRoot, input));

    public string GetName() => Value.Split('/').Where(s => !string.IsNullOrEmpty(s)).Last() ?? Value;

    public override string ToString() => Value;
}