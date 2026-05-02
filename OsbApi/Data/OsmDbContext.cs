using Microsoft.EntityFrameworkCore;
using OsbApi.Entities;

namespace OsbApi.Data;

public class OsmDbContext : DbContext
{
    public DbSet<OsmFeature> OsmFeatures => Set<OsmFeature>();

    public OsmDbContext(DbContextOptions<OsmDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.Entity<OsmFeature>(entity =>
        {
            entity.ToTable("osm_features");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OsmId).HasColumnName("osm_id");
            entity.Property(x => x.OsmType).HasColumnName("osm_type");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.ObjectType).HasColumnName("object_type");
            entity.Property(x => x.TagsJson).HasColumnName("tags").HasColumnType("jsonb");
            entity.Property(x => x.Geometry).HasColumnName("geom").HasColumnType("geography(Point,4326)");
            entity.Property(x => x.Accuracy).HasColumnName("accuracy");
            entity.HasIndex(x => x.OsmId).IsUnique();
        });
    }
}