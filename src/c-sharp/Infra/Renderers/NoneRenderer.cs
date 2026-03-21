using System;
using Archlens.Domain;
using Archlens.Domain.Models.Records;

namespace Archlens.Infra.Renderers;

public sealed class NoneRenderer : RendererBase
{
    public override string FileExtension => "";

    protected override string Render(RenderGraph graph, View view, RenderOptions options)
    {
        Console.WriteLine("Info: Renderer is none - no output will be rendered.");
        return "";
    }
}