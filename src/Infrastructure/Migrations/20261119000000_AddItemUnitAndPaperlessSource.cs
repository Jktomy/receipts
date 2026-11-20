using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddItemUnitAndPaperlessSource : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		// Optional canonical unit for item quantities (see Domain.Core.UnitCatalog).
		// Null preserves the historical meaning of a plain countable item.
		migrationBuilder.AddColumn<string>(
			name: "UnitOfMeasure",
			schema: "receipts",
			table: "ReceiptItems",
			type: "character varying(16)",
			maxLength: 16,
			nullable: true);

		// Optional Paperless-ngx source document for drill-down from a receipt
		// back to its original document. Null for receipts imported otherwise.
		migrationBuilder.AddColumn<long>(
			name: "SourcePaperlessDocumentId",
			schema: "receipts",
			table: "Receipts",
			type: "bigint",
			nullable: true);
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropColumn(
			name: "UnitOfMeasure",
			schema: "receipts",
			table: "ReceiptItems");

		migrationBuilder.DropColumn(
			name: "SourcePaperlessDocumentId",
			schema: "receipts",
			table: "Receipts");
	}
}
