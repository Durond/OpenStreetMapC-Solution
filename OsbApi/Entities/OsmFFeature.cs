using NetTopologySuite.Geometries;

namespace OsbApi.Entities;

public class OsmFeature
{
    public long Id { get; set; }
    public long OsmId { get; set; }
    public string OsmType { get; set; } = default!;
    public string? Name { get; set; }
    public string ObjectType { get; set; } = default!;
    public string TagsJson { get; set; } = "{}";
    public Point Geometry { get; set; } = default!;
    public int Accuracy { get; set; } // 0..100
}