using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

public partial class AddTillerExecutorMetadata : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<string>(
			name: "ExecutorMetadataJson",
			schema: "tiller",
			table: "TillerSelectedSpreadsheet",
			type: "text",
			nullable: true);
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropColumn(
			name: "ExecutorMetadataJson",
			schema: "tiller",
			table: "TillerSelectedSpreadsheet");
	}
}
