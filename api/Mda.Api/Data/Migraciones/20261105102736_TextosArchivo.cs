using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mda.Api.Data.Migraciones
{
    /// <inheritdoc />
    public partial class TextosArchivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "textos_archivo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Raiz = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ruta = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Tamano = table.Column<long>(type: "bigint", nullable: false),
                    ModificadoTicks = table.Column<long>(type: "bigint", nullable: false),
                    Origen = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Estado = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Texto = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_textos_archivo", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_textos_archivo_Raiz_Ruta",
                table: "textos_archivo",
                columns: new[] { "Raiz", "Ruta" },
                unique: true);

            // búsqueda de texto completo en español: acentos, plurales y conjugaciones («facturas» encuentra «factura»)
            migrationBuilder.Sql("CREATE INDEX ix_textos_archivo_texto ON textos_archivo USING GIN (to_tsvector('spanish', \"Texto\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_textos_archivo_texto;");

            migrationBuilder.DropTable(
                name: "textos_archivo");
        }
    }
}
