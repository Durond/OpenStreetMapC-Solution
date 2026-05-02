using System.Text.Json;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Tags;
using OsmSharp.Streams;
using OsbApi.Entities;
using OsbApi.Models;

namespace OsbApi.Services;

public interface IFeatureFormattingService
{
    object ToGeoJson(IEnumerable<OsmFeature> features);
    PlacesXmlResponse ToPlaces(IEnumerable<OsmFeature> features);
    byte[] ToPbf(IEnumerable<OsmFeature> features);
}

public class FeatureFormattingService : IFeatureFormattingService
{
    public object ToGeoJson(IEnumerable<OsmFeature> features)
    {
        // Return a  GeoJSON 
        var geojson = new
        {
            type = "FeatureCollection",
            features = features.Select(f => new
            {
                type = "Feature",
                geometry = new
                {
                    type = "Point",
                    coordinates = new[] { f.Geometry.X, f.Geometry.Y }
                },
                properties = new
                {
                    id = f.Id,
                    osm_id = f.OsmId,
                    osm_type = f.OsmType,
                    name = f.Name,
                    object_type = f.ObjectType,
                    accuracy = f.Accuracy
                }
            })
        };

        return geojson;
    }

    public PlacesXmlResponse ToPlaces(IEnumerable<OsmFeature> features)
    {
        var resp = new PlacesXmlResponse();

        foreach (var f in features)
        {
            if (f.Geometry is Point point)
            {
                resp.Places.Add(new PlaceDto
                {
                    Id = f.Id,
                    OsmId = f.OsmId,
                    OsmType = f.OsmType,
                    Name = f.Name,
                    ObjectType = f.ObjectType,
                    Accuracy = f.Accuracy,
                    Lat = point.Y,
                    Lon = point.X
                });
            }
        }

        return resp;
    }

    public byte[] ToPbf(IEnumerable<OsmFeature> features)
    {
        // Minimal OSM PBF output:
        using var ms = new MemoryStream();
        var target = new PBFOsmStreamTarget(ms);

        foreach (var f in features)
        {
            if (!string.Equals(f.OsmType, "node", StringComparison.OrdinalIgnoreCase))
                continue;

            var node = new Node
            {
                Id = f.OsmId,
                Latitude = f.Geometry.Y,
                Longitude = f.Geometry.X,
                Tags = TryParseTags(f.TagsJson)
            };

            target.AddNode(node);
        }

        target.Close();
        return ms.ToArray();
    }

    private static TagsCollection? TryParseTags(string tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson))
            return null;

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(tagsJson);
            if (dict is null || dict.Count == 0)
                return null;

            var tags = new TagsCollection();
            foreach (var (k, v) in dict)
                tags.AddOrReplace(k, v);
            return tags;
        }
        catch
        {
            return null;
        }
    }
}
