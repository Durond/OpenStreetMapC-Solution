using System.Xml.Serialization;

namespace OsbApi.Models;

[XmlRoot("place")]
public class PlaceDto
{
    public long Id { get; set; }
    public string OsmType { get; set; } = default!;
    public string? Name { get; set; }
    public string ObjectType { get; set; } = default!;
    public double Lat { get; set; }
    public double Lon { get; set; }
    public long OsmId { get; set; }
    public int Accuracy { get; set; }
}