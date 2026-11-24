using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddReceiptProvenanceTaxUnknown : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<string>(
			name: "SourceKind",
			schema: "receipts",
			table: "Receipts",
			type: "text",
			maxLength: 32,
			nullable: true);

		migrationBuilder.AddColumn<bool>(
			name: "TaxUnknown",
			schema: "receipts",
			table: "Receipts",
			type: "boolean",
			nullable: false,
			defaultValue: false);
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropColumn(
			name: "SourceKind",
			schema: "receipts",
			table: "Receipts");

		migrationBuilder.DropColumn(
			name: "TaxUnknown",
			schema: "receipts",
			table: "Receipts");
	}
}
