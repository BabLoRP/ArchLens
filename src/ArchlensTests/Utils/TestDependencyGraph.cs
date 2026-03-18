using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;

namespace ArchlensTests.Utils;

public static class TestDependencyGraph
{
    public static ProjectDependencyGraph MakeDependencyGraph(string rootPath)
    {
        var graph = new ProjectDependencyGraph(rootPath);

        var root = RelativePath.Directory(rootPath, rootPath);
        var shop = RelativePath.Directory(rootPath, "./Shop/");
        var warehouse = RelativePath.Directory(rootPath, "./Warehouse/");
        var inventory = RelativePath.Directory(rootPath, "./Inventory/");
        var ports = RelativePath.Directory(rootPath, "./Inventory/Ports");
        var suppliers = RelativePath.Directory(rootPath, "./Warehouse/Suppliers/");
        var stock = RelativePath.Directory(rootPath, "./Inventory/Stock/");
        var labels = RelativePath.Directory(rootPath, "./Inventory/Stock/Labels/");
        var tags = RelativePath.Directory(rootPath, "./Inventory/Stock/Tags/");
        var tools = RelativePath.Directory(rootPath, "./Inventory/Tools/");

        graph.UpsertProjectItem(root, ProjectItemType.Directory);
        graph.UpsertProjectItem(shop, ProjectItemType.Directory);
        graph.UpsertProjectItem(warehouse, ProjectItemType.Directory);
        graph.UpsertProjectItem(inventory, ProjectItemType.Directory);
        graph.UpsertProjectItem(ports, ProjectItemType.Directory);
        graph.UpsertProjectItem(suppliers, ProjectItemType.Directory);
        graph.UpsertProjectItem(stock, ProjectItemType.Directory);
        graph.UpsertProjectItem(labels, ProjectItemType.Directory);
        graph.UpsertProjectItem(tags, ProjectItemType.Directory);
        graph.UpsertProjectItem(tools, ProjectItemType.Directory);

        graph.AddChild(root, shop);
        graph.AddChild(root, inventory);
        graph.AddChild(root, warehouse);
        graph.AddChild(warehouse, suppliers);
        graph.AddChild(inventory, stock);
        graph.AddChild(inventory, ports);
        graph.AddChild(inventory, tools);
        graph.AddChild(stock, labels);
        graph.AddChild(stock, tags);

        var orderProcessor = RelativePath.File(rootPath, "./Shop/OrderProcessor.cs");
        var stockSupplier = RelativePath.File(rootPath, "./Warehouse/Suppliers/StockSupplier.cs");
        var shipmentSupplier = RelativePath.File(rootPath, "./Warehouse/Suppliers/ShipmentSupplier.cs");
        var priceTag = RelativePath.File(rootPath, "./Inventory/Stock/Labels/PriceTag.cs");
        var catalogue = RelativePath.File(rootPath, "./Inventory/Stock/Catalogue.cs");

        graph.UpsertProjectItem(orderProcessor, ProjectItemType.File);
        graph.UpsertProjectItem(stockSupplier, ProjectItemType.File);
        graph.UpsertProjectItem(shipmentSupplier, ProjectItemType.File);
        graph.UpsertProjectItem(priceTag, ProjectItemType.File);
        graph.UpsertProjectItem(catalogue, ProjectItemType.File);

        graph.AddChild(shop, orderProcessor);
        graph.AddChild(suppliers, stockSupplier);
        graph.AddChild(suppliers, shipmentSupplier);
        graph.AddChild(labels, priceTag);
        graph.AddChild(stock, catalogue);

        var dependencies = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [orderProcessor] = [stock, labels, tools], // shop 2 dependencies to stock and 1 to tools
            [stockSupplier] = [ports, tags, labels, warehouse], // suppliers 1 to ports, 2 to stock and 1 to warehouse
            [shipmentSupplier] = [ports, tags, warehouse],  // suppliers 1 to ports, 2 to stock and 1 to warehouse
            [priceTag] = [tags], // no visible deps at depth 1
            [catalogue] = [tools] // no visible deps at depth 1
        };

        foreach (var (source, targets) in dependencies)
            graph.AddDependencies(source, targets);

        return graph;
    }
}
