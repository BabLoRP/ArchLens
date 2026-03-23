using System;
using Archlens.Domain.Models.Records;

namespace Archlens.Infra;

public class ConfigManager(BaseOptions _baseOptions, ParserOptions _parserOptions, RenderOptions _renderOptions, SnapshotOptions _snapshotOptions)
{
    public BaseOptions GetBaseOptions()
    {
        if (_baseOptions == null) throw new Exception("LoadAsync must be run before getting options");
        return _baseOptions;
    }

    public ParserOptions GetParserOptions()
    {
        if (_parserOptions == null) throw new Exception("LoadAsync must be run before getting options");
        return _parserOptions;
    }

    public RenderOptions GetRenderOptions()
    {
        if (_renderOptions == null) throw new Exception("LoadAsync must be run before getting options");
        return _renderOptions;
    }

    public SnapshotOptions GetSnapshotOptions()
    {
        if (_snapshotOptions == null) throw new Exception("LoadAsync must be run before getting options");
        return _snapshotOptions;
    }
}
