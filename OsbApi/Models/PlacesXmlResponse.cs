using System.Xml.Serialization;

namespace OsbApi.Models;

[XmlRoot("places")]
public class PlacesXmlResponse
{
    [XmlElement("place")]
    public List<PlaceDto> Places { get; set; } = new();
}

