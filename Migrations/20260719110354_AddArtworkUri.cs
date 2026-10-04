using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicPlatform.API.Migrations
{
    /// <inheritdoc />
    public partial class AddArtworkUri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtworkUri",
                table: "Songs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtworkUri",
                table: "Songs");
        }
    }
}
