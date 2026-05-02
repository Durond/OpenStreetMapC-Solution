using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NetTopologySuite.Geometries;
using OsbApi.Data;
using OsbApi.Models;
using OsbApi.Services;

namespace OsbApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlacesController : ControllerBase
{
    private readonly OsmDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly GeometryFactory _geometryFactory;
    private readonly IFeatureFormattingService _formatter;

    public PlacesController(
        OsmDbContext db,
        IMemoryCache cache,
        GeometryFactory geometryFactory,
        IFeatureFormattingService formatter)
    {
        _db = db;
        _cache = cache;
        _geometryFactory = geometryFactory;
        _formatter = formatter;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] SearchRequest request)
    {
        string cacheKey = $"search:{request.Lat:F5}:{request.Lon:F5}:{request.RadiusMeters}:{request.Type}:{request.MinAccuracy}:{request.Format}";

        if (_cache.TryGetValue(cacheKey, out object? cached))
            return Ok(cached);

        var point = _geometryFactory.CreatePoint(new Coordinate(request.Lon, request.Lat));

        var query = _db.OsmFeatures
            .Where(f => f.Geometry != null && f.Geometry.IsWithinDistance(point, request.RadiusMeters));

        if (!string.IsNullOrEmpty(request.Type))
            query = query.Where(f => f.ObjectType == request.Type);

        if (request.MinAccuracy is not null)
            query = query.Where(f => f.Accuracy >= request.MinAccuracy.Value);

        var features = await query.Take(500).ToListAsync();

        object response;

        switch (request.Format.ToLowerInvariant())
        {
            case "geojson":
                response = _formatter.ToGeoJson(features);
                break;
            case "xml":
                return new ObjectResult(_formatter.ToPlaces(features))
                {
                    ContentTypes = { "application/xml" }
                };
            case "pbf":
                var bytes = _formatter.ToPbf(features);
                return File(bytes, "application/x-protobuf");
            default:
                response = _formatter.ToGeoJson(features);
                break;
        }

        _cache.Set(cacheKey, response, TimeSpan.FromMinutes(5));

        return Ok(response);
    }
}