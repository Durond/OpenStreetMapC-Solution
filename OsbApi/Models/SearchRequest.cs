namespace OsbApi.Models;

public class SearchRequest
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double RadiusMeters { get; set; } = 500;
    public string? Type { get; set; }
    public int? MinAccuracy { get; set; } // 0..100
    public string Format { get; set; } = "geojson"; // geojson | xml | pbf
}