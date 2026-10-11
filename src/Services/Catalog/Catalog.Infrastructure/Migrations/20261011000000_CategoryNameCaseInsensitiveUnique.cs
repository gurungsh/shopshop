using Catalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Migrations
{
    /// <summary>
    /// Replaces the case-sensitive unique index on Categories.Name with one on lower("Name").
    /// The index keeps its name, so the model snapshot (HasIndex(Name).IsUnique()) still matches.
    /// </summary>
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20261011000000_CategoryNameCaseInsensitiveUnique")]
    public partial class CategoryNameCaseInsensitiveUnique : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Categories" GROUP BY lower("Name") HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Categories contain names that differ only by case; rename or merge them before applying this migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("""DROP INDEX "IX_Categories_Name";""");
            migrationBuilder.Sql("""CREATE UNIQUE INDEX "IX_Categories_Name" ON "Categories" (lower("Name"));""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX "IX_Categories_Name";""");
            migrationBuilder.Sql("""CREATE UNIQUE INDEX "IX_Categories_Name" ON "Categories" ("Name");""");
        }
    }
}
