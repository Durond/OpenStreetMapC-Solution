using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using Newtonsoft.Json;
using NetTopologySuite.IO.Converters;
using OsbApi.Data;
using OsbApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OsmDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Default"),
        o => o.UseNetTopologySuite()
    );
});

builder.Services.AddSingleton(NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326));
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IFeatureFormattingService, FeatureFormattingService>();

builder.Services
    .AddControllers()
    .AddXmlSerializerFormatters()
    .AddNewtonsoftJson(opts =>
    {
        opts.SerializerSettings.Converters.Add(new GeometryConverter());
        opts.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OsmDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();