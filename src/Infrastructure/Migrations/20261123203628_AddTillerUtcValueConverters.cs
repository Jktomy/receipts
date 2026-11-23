using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTillerUtcValueConverters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "tiller",
                table: "TillerTransactions",
                type: "timestamptz",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "SupersededBy",
                schema: "tiller",
                table: "TillerTransactions",
                type: "text",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "tiller",
                table: "TillerTransactions",
                type: "text",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerTransactions",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "SplitGroupId",
                schema: "tiller",
                table: "TillerTransactions",
                type: "text",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                schema: "tiller",
                table: "TillerTransactions",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Institution",
                schema: "tiller",
                table: "TillerTransactions",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "tiller",
                table: "TillerTransactions",
                type: "text",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "tiller",
                table: "TillerTransactions",
                type: "timestamptz",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                schema: "tiller",
                table: "TillerTransactions",
                type: "text",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                schema: "tiller",
                table: "TillerTransactions",
                type: "numeric(18,2)",
                precision: 19,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)",
                oldPrecision: 19,
                oldScale: 4);

            migrationBuilder.AlterColumn<string>(
                name: "Account",
                schema: "tiller",
                table: "TillerTransactions",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerSyncEvents",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "OccurredAt",
                schema: "tiller",
                table: "TillerSyncEvents",
                type: "timestamptz",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                schema: "tiller",
                table: "TillerSyncEvents",
                type: "text",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "ErrorMessage",
                schema: "tiller",
                table: "TillerSyncEvents",
                type: "text",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "tiller",
                table: "TillerSelectedSpreadsheet",
                type: "timestamptz",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerSelectedSpreadsheet",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "SupersededAt",
                schema: "tiller",
                table: "TillerReceiptLinks",
                type: "timestamptz",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Reviewer",
                schema: "tiller",
                table: "TillerReceiptLinks",
                type: "text",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                schema: "tiller",
                table: "TillerReceiptLinks",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "LinkKind",
                schema: "tiller",
                table: "TillerReceiptLinks",
                type: "text",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "tiller",
                table: "TillerReceiptLinks",
                type: "timestamptz",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "Reviewer",
                schema: "tiller",
                table: "TillerLinkEvents",
                type: "text",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                schema: "tiller",
                table: "TillerLinkEvents",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "At",
                schema: "tiller",
                table: "TillerLinkEvents",
                type: "timestamptz",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                schema: "tiller",
                table: "TillerLinkEvents",
                type: "text",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerKnownIds",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "SeenAt",
                schema: "tiller",
                table: "TillerKnownIds",
                type: "timestamptz",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                schema: "tiller",
                table: "TillerKnownIds",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "CanonicalProviderTransactionId",
                schema: "tiller",
                table: "TillerKnownIds",
                type: "text",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "tiller",
                table: "TillerCheckpoints",
                type: "timestamptz",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerCheckpoints",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "tiller",
                table: "TillerAccountMappings",
                type: "timestamptz",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "TillerAccountName",
                schema: "tiller",
                table: "TillerAccountMappings",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "TillerAccountId",
                schema: "tiller",
                table: "TillerAccountMappings",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerAccountMappings",
                type: "text",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "tiller",
                table: "TillerAccountMappings",
                type: "timestamptz",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "tiller",
                table: "TillerTransactions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamptz");

            migrationBuilder.AlterColumn<string>(
                name: "SupersededBy",
                schema: "tiller",
                table: "TillerTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "tiller",
                table: "TillerTransactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "SplitGroupId",
                schema: "tiller",
                table: "TillerTransactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                schema: "tiller",
                table: "TillerTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Institution",
                schema: "tiller",
                table: "TillerTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                schema: "tiller",
                table: "TillerTransactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "tiller",
                table: "TillerTransactions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamptz");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                schema: "tiller",
                table: "TillerTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                schema: "tiller",
                table: "TillerTransactions",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 19,
                oldScale: 4);

            migrationBuilder.AlterColumn<string>(
                name: "Account",
                schema: "tiller",
                table: "TillerTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerSyncEvents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "OccurredAt",
                schema: "tiller",
                table: "TillerSyncEvents",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamptz");

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                schema: "tiller",
                table: "TillerSyncEvents",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "ErrorMessage",
                schema: "tiller",
                table: "TillerSyncEvents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "tiller",
                table: "TillerSelectedSpreadsheet",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamptz");

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerSelectedSpreadsheet",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "SupersededAt",
                schema: "tiller",
                table: "TillerReceiptLinks",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamptz",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Reviewer",
                schema: "tiller",
                table: "TillerReceiptLinks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                schema: "tiller",
                table: "TillerReceiptLinks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "LinkKind",
                schema: "tiller",
                table: "TillerReceiptLinks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "tiller",
                table: "TillerReceiptLinks",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamptz");

            migrationBuilder.AlterColumn<string>(
                name: "Reviewer",
                schema: "tiller",
                table: "TillerLinkEvents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                schema: "tiller",
                table: "TillerLinkEvents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "At",
                schema: "tiller",
                table: "TillerLinkEvents",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamptz");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                schema: "tiller",
                table: "TillerLinkEvents",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerKnownIds",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "SeenAt",
                schema: "tiller",
                table: "TillerKnownIds",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamptz");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                schema: "tiller",
                table: "TillerKnownIds",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "CanonicalProviderTransactionId",
                schema: "tiller",
                table: "TillerKnownIds",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "tiller",
                table: "TillerCheckpoints",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamptz");

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerCheckpoints",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "tiller",
                table: "TillerAccountMappings",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamptz");

            migrationBuilder.AlterColumn<string>(
                name: "TillerAccountName",
                schema: "tiller",
                table: "TillerAccountMappings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "TillerAccountId",
                schema: "tiller",
                table: "TillerAccountMappings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "SpreadsheetId",
                schema: "tiller",
                table: "TillerAccountMappings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "tiller",
                table: "TillerAccountMappings",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamptz");
        }
    }
}
