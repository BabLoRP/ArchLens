using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Renderers;
using System;

namespace Archlens.Infra.Factories;

public sealed class RendererFactory
{
    public static IRenderer SelectRenderer(RenderOptions options) => options.Format switch
    {
        RenderFormat.Json => new JsonRenderer(),
        RenderFormat.PlantUML => new PlantUMLRenderer(),
        _ => throw new ArgumentOutOfRangeException(nameof(options))
    };
}