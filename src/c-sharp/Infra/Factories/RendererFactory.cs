using Archlens.Domain;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Renderers;
using System;

namespace Archlens.Infra.Factories;

public sealed class RendererFactory
{
    public static Renderer SelectRenderer(RenderOptions options) => options.Format switch
    {
        RenderFormat.Json => new JsonRenderer(),
        RenderFormat.PlantUML => new PlantUMLRenderer(),
        _ => throw new ArgumentOutOfRangeException(nameof(options))
    };
}