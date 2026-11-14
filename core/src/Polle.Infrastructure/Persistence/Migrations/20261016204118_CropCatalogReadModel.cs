using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CropCatalogReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. Records the read-only Crop model (a query over lakehouse.crops) in the snapshot,
            // so EF does not report pending model changes. No schema objects are created or changed.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
