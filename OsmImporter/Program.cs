using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml;
using Npgsql;
using NpgsqlTypes;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;

static string GetRequiredEnv(string key)
{
    var value = Environment.GetEnvironmentVariable(key);
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"Missing environment variable: {key}");
    return value;
}

static async Task DownloadIfMissingAsync(string url, string path, CancellationToken ct)
{
    if (File.Exists(path) && new FileInfo(path).Length > 0)
        return;

    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);

    // HttpClient по умолчанию обрывает запрос через 100 с — региональные .pbf качаются дольше.
    using var http = new HttpClient { Timeout = TimeSpan.FromHours(24) };
    http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OsmImporter", "1.0"));

    Console.WriteLine("Скачивание началось (большие регионы — долго; в логе будут метки каждые ~50 МиБ).");

    try
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var inStream = await resp.Content.ReadAsStreamAsync(ct);
        await using var outStream = File.Create(path);

        var buffer = new byte[81920];
        long written;
        long nextLog = 50L * 1024 * 1024;
        written = 0;

        int read;
        while ((read = await inStream.ReadAsync(buffer, ct)) > 0)
        {
            await outStream.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;
            if (written >= nextLog)
            {
                Console.WriteLine($"Скачано ~{written / (1024.0 * 1024):F1} МиБ...");
                nextLog += 50L * 1024 * 1024;
            }
        }

        Console.WriteLine($"Файл сохранён: {path} ({written / (1024.0 * 1024):F1} МиБ).");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка загрузки: {ex.Message}");
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* ignore */
        }

        throw;
    }
}

static async Task FetchOverpassBboxAsync(
    string overpassUrl,
    double south,
    double west,
    double north,
    double east,
    string outputPath,
    CancellationToken ct)
{
    var keys = new[]
    {
        "amenity", "shop", "tourism", "leisure", "office", "craft",
        "healthcare", "education", "public_transport"
    };

    var sb = new StringBuilder();
    sb.AppendLine("[out:xml][timeout:900];");
    sb.AppendLine("(");
    foreach (var k in keys)
    {
        sb.Append("  node[\"");
        sb.Append(k);
        sb.Append("\"](");
        sb.Append(south.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(west.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(north.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(east.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(");");
    }

    sb.AppendLine(");");
    sb.AppendLine("out meta;");

    var query = sb.ToString();

    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(45) };
    http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OsmImporter", "1.0"));

    var body = "data=" + Uri.EscapeDataString(query);
    using var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");

    Console.WriteLine("Запрос к Overpass: только POI-узлы в заданном прямоугольнике (без скачивания целого региона с Geofabrik).");

    using var resp = await http.PostAsync(overpassUrl, content, ct);
    if (!resp.IsSuccessStatusCode)
    {
        var err = await resp.Content.ReadAsStringAsync(ct);
        var head = err.Length <= 500 ? err : err[..500];
        throw new InvalidOperationException($"Overpass HTTP {(int)resp.StatusCode}: {head}");
    }

    var dir = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);

    await using var outStream = File.Create(outputPath);
    await using var inStream = await resp.Content.ReadAsStreamAsync(ct);
    await inStream.CopyToAsync(outStream, ct);

    var len = new FileInfo(outputPath).Length;
    Console.WriteLine($"Ответ Overpass: {outputPath} ({len / 1024.0:F1} KiB).");
}

/// <summary>
/// Потоковый разбор OSM XML (ответ Overpass). В OsmSharp 7 нет XmlOsmStreamSource в пакете.
/// </summary>
static IEnumerable<OsmGeo> EnumerateOsmXml(string path)
{
    using var reader = XmlReader.Create(path, new XmlReaderSettings { IgnoreComments = true });
    while (reader.Read())
    {
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "node")
            continue;

        if (!long.TryParse(reader.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var osmId))
            continue;
        if (!double.TryParse(reader.GetAttribute("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
            continue;
        if (!double.TryParse(reader.GetAttribute("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            continue;

        var tags = new TagsCollection();
        if (reader.IsEmptyElement)
        {
            yield return new Node { Id = osmId, Latitude = lat, Longitude = lon, Tags = tags };
            continue;
        }

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "node")
                break;

            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "tag")
                continue;

            var k = reader.GetAttribute("k");
            var v = reader.GetAttribute("v");
            if (k != null && v != null)
                tags.AddOrReplace(k, v);
        }

        yield return new Node { Id = osmId, Latitude = lat, Longitude = lon, Tags = tags };
    }
}

static IEnumerable<OsmGeo> OpenImportStream(string importSource, string dataPath)
{
    if (importSource == "overpass")
    {
        foreach (var g in EnumerateOsmXml(dataPath))
            yield return g;
        yield break;
    }

    using var fs = File.OpenRead(dataPath);
    foreach (var g in new PBFOsmStreamSource(fs))
        yield return g;
}

var ct = CancellationToken.None;

var connectionString = GetRequiredEnv("ConnectionStrings__Default");
var importSource = Environment.GetEnvironmentVariable("OSM_IMPORT_SOURCE")?.Trim().ToLowerInvariant() ?? "pbf";
if (importSource is not "pbf" and not "overpass")
    throw new InvalidOperationException("OSM_IMPORT_SOURCE must be 'pbf' or 'overpass'.");

string? pbfUrl = null;
if (importSource == "pbf")
    pbfUrl = GetRequiredEnv("OSM_PBF_URL");

var dataPath = Environment.GetEnvironmentVariable("OSM_PBF_PATH");
if (string.IsNullOrWhiteSpace(dataPath))
    dataPath = importSource == "overpass" ? "/data/bbox.osm" : "/data/region.osm.pbf";

var maxFeatures = int.TryParse(Environment.GetEnvironmentVariable("IMPORT_MAX"), out var tmpMax) ? tmpMax : 200_000;
var bboxRaw = Environment.GetEnvironmentVariable("IMPORT_BBOX");

double? minLat = null, minLon = null, maxLat = null, maxLon = null;
if (!string.IsNullOrWhiteSpace(bboxRaw))
{
    var parts = bboxRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 4
        && double.TryParse(parts[0], out var a)
        && double.TryParse(parts[1], out var b)
        && double.TryParse(parts[2], out var c)
        && double.TryParse(parts[3], out var d))
    {
        minLat = Math.Min(a, c);
        maxLat = Math.Max(a, c);
        minLon = Math.Min(b, d);
        maxLon = Math.Max(b, d);
    }
    else
    {
        throw new InvalidOperationException($"Bad IMPORT_BBOX format: {bboxRaw}. Expected: minLat,minLon,maxLat,maxLon");
    }
}

if (importSource == "overpass")
{
    if (minLat is null)
        throw new InvalidOperationException("Для OSM_IMPORT_SOURCE=overpass задайте IMPORT_BBOX (minLat,minLon,maxLat,maxLon).");

    if (File.Exists(dataPath) && new FileInfo(dataPath).Length > 0)
        Console.WriteLine($"Перекачка пропущена, уже есть файл: {dataPath}");
    else
    {
        var overpassUrl = Environment.GetEnvironmentVariable("OVERPASS_URL")
                         ?? "https://overpass-api.de/api/interpreter";
        await FetchOverpassBboxAsync(
            overpassUrl,
            minLat.Value,
            minLon!.Value,
            maxLat!.Value,
            maxLon!.Value,
            dataPath,
            ct);
    }
}
else
{
    Console.WriteLine($"Downloading PBF if missing: {pbfUrl}");
    await DownloadIfMissingAsync(pbfUrl!, dataPath, ct);
}

Console.WriteLine($"Источник данных готов: {dataPath}");
if (minLat is not null && importSource == "pbf")
    Console.WriteLine($"Фильтр при импорте по bbox: lat[{minLat},{maxLat}] lon[{minLon},{maxLon}]");

// What we import: POI-like nodes with these tag keys.
var typeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "amenity",
    "shop",
    "tourism",
    "leisure",
    "office",
    "craft",
    "healthcare",
    "education",
    "public_transport"
};

await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync(ct);

await using (var cmd = new NpgsqlCommand("""
    INSERT INTO osm_features (osm_id, osm_type, name, object_type, tags, geom)
    VALUES (@osm_id, @osm_type, @name, @object_type, @tags::jsonb,
            ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)::geography)
    ON CONFLICT (osm_id) DO UPDATE SET
        name = EXCLUDED.name,
        object_type = EXCLUDED.object_type,
        tags = EXCLUDED.tags,
        geom = EXCLUDED.geom;
    """, conn))
{
    cmd.Parameters.Add("osm_id", NpgsqlDbType.Bigint).Value = 0L;
    cmd.Parameters.Add("osm_type", NpgsqlDbType.Text).Value = "node";
    cmd.Parameters.Add("name", NpgsqlDbType.Text).Value = DBNull.Value;
    cmd.Parameters.Add("object_type", NpgsqlDbType.Text).Value = "amenity";
    cmd.Parameters.Add("tags", NpgsqlDbType.Text).Value = "{}";
    cmd.Parameters.Add("lon", NpgsqlDbType.Double).Value = 0d;
    cmd.Parameters.Add("lat", NpgsqlDbType.Double).Value = 0d;

    int inserted = 0;

    foreach (var element in OpenImportStream(importSource, dataPath))
    {
        if (inserted >= maxFeatures)
            break;

        if (element.Type != OsmGeoType.Node)
            continue;

        var node = (Node)element;
        if (node.Id is null || node.Latitude is null || node.Longitude is null)
            continue;

        if (minLat is not null)
        {
            var lat = node.Latitude.Value;
            var lon = node.Longitude.Value;
            if (lat < minLat.Value || lat > maxLat!.Value || lon < minLon!.Value || lon > maxLon!.Value)
                continue;
        }

        var tags = node.Tags;
        if (tags is null || tags.Count == 0)
            continue;

        string? objectType = null;
        foreach (var k in typeKeys)
        {
            if (tags.ContainsKey(k))
            {
                objectType = k;
                break;
            }
        }
        if (objectType is null)
            continue;

        tags.TryGetValue("name", out var name);
        var tagsJson = JsonSerializer.Serialize(tags.ToDictionary(t => t.Key, t => t.Value));

        cmd.Parameters["osm_id"].Value = node.Id.Value;
        cmd.Parameters["osm_type"].Value = "node";
        cmd.Parameters["name"].Value = (object)name ?? DBNull.Value;
        cmd.Parameters["object_type"].Value = objectType;
        cmd.Parameters["tags"].Value = tagsJson;
        cmd.Parameters["lon"].Value = node.Longitude.Value;
        cmd.Parameters["lat"].Value = node.Latitude.Value;

        await cmd.ExecuteNonQueryAsync(ct);
        inserted++;

        if (inserted % 10_000 == 0)
            Console.WriteLine($"Imported: {inserted:N0}");
    }

    Console.WriteLine($"Done. Imported/upserted: {inserted:N0}");
}
