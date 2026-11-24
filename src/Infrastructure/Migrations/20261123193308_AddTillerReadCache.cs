using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddTillerReadCache : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.EnsureSchema(
			name: "tiller");

		migrationBuilder.CreateTable(
			name: "TillerAccountMappings",
			schema: "tiller",
			columns: table => new
			{
				Id = table.Column<Guid>(type: "uuid", nullable: false),
				ReceiptsAccountId = table.Column<Guid>(type: "uuid", nullable: false),
				TillerAccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				TillerAccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				SpreadsheetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
				UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_TillerAccountMappings", x => x.Id);
				table.ForeignKey(
					name: "FK_TillerAccountMappings_Accounts_ReceiptsAccountId",
					column: x => x.ReceiptsAccountId,
					principalSchema: "library",
					principalTable: "Accounts",
					principalColumn: "Id",
					onDelete: ReferentialAction.Restrict);
			});

		migrationBuilder.CreateTable(
			name: "TillerCheckpoints",
			schema: "tiller",
			columns: table => new
			{
				Id = table.Column<Guid>(type: "uuid", nullable: false),
				SpreadsheetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				SyncedThroughDate = table.Column<DateOnly>(type: "date", nullable: true),
				Completed = table.Column<bool>(type: "boolean", nullable: false),
				UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_TillerCheckpoints", x => x.Id);
			});

		migrationBuilder.CreateTable(
			name: "TillerKnownIds",
			schema: "tiller",
			columns: table => new
			{
				Id = table.Column<Guid>(type: "uuid", nullable: false),
				ProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				SpreadsheetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				CanonicalProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
				SeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_TillerKnownIds", x => x.Id);
			});

		migrationBuilder.CreateTable(
			name: "TillerLinkEvents",
			schema: "tiller",
			columns: table => new
			{
				Id = table.Column<Guid>(type: "uuid", nullable: false),
				ProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
				ReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
				Reviewer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
				At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_TillerLinkEvents", x => x.Id);
			});

		migrationBuilder.CreateTable(
			name: "TillerReceiptLinks",
			schema: "tiller",
			columns: table => new
			{
				Id = table.Column<Guid>(type: "uuid", nullable: false),
				ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
				ProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				LinkKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
				Reviewer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
				CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
				SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_TillerReceiptLinks", x => x.Id);
			});

		migrationBuilder.CreateTable(
			name: "TillerSelectedSpreadsheet",
			schema: "tiller",
			columns: table => new
			{
				Id = table.Column<Guid>(type: "uuid", nullable: false),
				SpreadsheetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_TillerSelectedSpreadsheet", x => x.Id);
			});

		migrationBuilder.CreateTable(
			name: "TillerSyncEvents",
			schema: "tiller",
			columns: table => new
			{
				Id = table.Column<Guid>(type: "uuid", nullable: false),
				SpreadsheetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
				Success = table.Column<bool>(type: "boolean", nullable: false),
				Inserted = table.Column<int>(type: "integer", nullable: false),
				Updated = table.Column<int>(type: "integer", nullable: false),
				Reconciled = table.Column<int>(type: "integer", nullable: false),
				Duplicates = table.Column<int>(type: "integer", nullable: false),
				ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
				OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_TillerSyncEvents", x => x.Id);
			});

		migrationBuilder.CreateTable(
			name: "TillerTransactions",
			schema: "tiller",
			columns: table => new
			{
				Id = table.Column<Guid>(type: "uuid", nullable: false),
				ProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				SpreadsheetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				Date = table.Column<DateOnly>(type: "date", nullable: false),
				Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
				Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
				Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
				Account = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				Institution = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
				Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
				SplitGroupId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
				Revision = table.Column<int>(type: "integer", nullable: false),
				SupersededBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
				CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
				UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_TillerTransactions", x => x.Id);
			});

		migrationBuilder.CreateIndex(
			name: "IX_TillerAccountMappings_ReceiptsAccountId",
			schema: "tiller",
			table: "TillerAccountMappings",
			column: "ReceiptsAccountId");

		migrationBuilder.CreateIndex(
			name: "IX_TillerCheckpoints_SpreadsheetId",
			schema: "tiller",
			table: "TillerCheckpoints",
			column: "SpreadsheetId",
			unique: true);

		migrationBuilder.CreateIndex(
			name: "IX_TillerKnownIds_SpreadsheetId_ProviderTransactionId",
			schema: "tiller",
			table: "TillerKnownIds",
			columns: new[] { "SpreadsheetId", "ProviderTransactionId" },
			unique: true);

		migrationBuilder.CreateIndex(
			name: "IX_TillerLinkEvents_ProviderTransactionId",
			schema: "tiller",
			table: "TillerLinkEvents",
			column: "ProviderTransactionId");

		migrationBuilder.CreateIndex(
			name: "IX_TillerReceiptLinks_ProviderTransactionId_ReceiptId",
			schema: "tiller",
			table: "TillerReceiptLinks",
			columns: new[] { "ProviderTransactionId", "ReceiptId" });

		migrationBuilder.CreateIndex(
			name: "IX_TillerTransactions_SpreadsheetId_ProviderTransactionId",
			schema: "tiller",
			table: "TillerTransactions",
			columns: new[] { "SpreadsheetId", "ProviderTransactionId" },
			unique: true);
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			name: "TillerAccountMappings",
			schema: "tiller");

		migrationBuilder.DropTable(
			name: "TillerCheckpoints",
			schema: "tiller");

		migrationBuilder.DropTable(
			name: "TillerKnownIds",
			schema: "tiller");

		migrationBuilder.DropTable(
			name: "TillerLinkEvents",
			schema: "tiller");

		migrationBuilder.DropTable(
			name: "TillerReceiptLinks",
			schema: "tiller");

		migrationBuilder.DropTable(
			name: "TillerSelectedSpreadsheet",
			schema: "tiller");

		migrationBuilder.DropTable(
			name: "TillerSyncEvents",
			schema: "tiller");

		migrationBuilder.DropTable(
			name: "TillerTransactions",
			schema: "tiller");
	}
}
