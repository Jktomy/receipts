using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddItemPackageContent : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		// Optional measurable amount inside each package (e.g. 64 fl oz per carton).
		// When set, item quantity counts packages. Null means quantity is the measured amount.
		migrationBuilder.AddColumn<decimal>(
			name: "PackageContentQuantity",
			schema: "receipts",
			table: "ReceiptItems",
			type: "numeric(18,4)",
			nullable: true);

		migrationBuilder.AddColumn<string>(
			name: "PackageContentUnit",
			schema: "receipts",
			table: "ReceiptItems",
			type: "character varying(16)",
			maxLength: 16,
			nullable: true);
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropColumn(
			name: "PackageContentQuantity",
			schema: "receipts",
			table: "ReceiptItems");

		migrationBuilder.DropColumn(
			name: "PackageContentUnit",
			schema: "receipts",
			table: "ReceiptItems");
	}
}
