using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddReceiptPaperlessSourceUnique : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		// Fail-loud duplicate preflight. Historical retries may have linked the
		// same Paperless document to more than one receipt; the filtered unique
		// index below would abort mid-migration on those rows. Abort here instead,
		// before any schema change, naming the offending document. Receipts are
		// immutable source evidence: this migration never deletes or merges rows.
		// Reconcile duplicates with a separately authorized repair, then retry.
		migrationBuilder.Sql("""
			DO $$
			DECLARE
				dup_doc_id bigint;
				dup_count bigint;
				first_receipt_id text;
			BEGIN
				SELECT "SourcePaperlessDocumentId", COUNT(*)
				INTO dup_doc_id, dup_count
				FROM receipts."Receipts"
				WHERE "SourcePaperlessDocumentId" IS NOT NULL
				GROUP BY "SourcePaperlessDocumentId"
				HAVING COUNT(*) > 1
				ORDER BY "SourcePaperlessDocumentId"
				LIMIT 1;

				IF FOUND THEN
					SELECT MIN("Id"::text) INTO first_receipt_id
					FROM receipts."Receipts"
					WHERE "SourcePaperlessDocumentId" = dup_doc_id;
					RAISE EXCEPTION 'AddReceiptPaperlessSourceUnique aborted: Paperless document % is linked to % receipts (first receipt %). Duplicate source links must be reconciled before the unique index can be created. No rows were changed.', dup_doc_id, dup_count, first_receipt_id;
				END IF;
			END
			$$;
			""");

		// One Paperless source document imports at most once: a filtered unique
		// index on the optional source ID blocks a second receipt for the same
		// document even when the second import uses a different request ID.
		migrationBuilder.CreateIndex(
			name: "IX_Receipts_SourcePaperlessDocumentId",
			schema: "receipts",
			table: "Receipts",
			column: "SourcePaperlessDocumentId",
			unique: true,
			filter: "\"SourcePaperlessDocumentId\" IS NOT NULL");
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		// Dropping this index re-opens duplicate Paperless imports. Downgrade
		// only to restore a pre-index state, then re-apply promptly.
		migrationBuilder.DropIndex(
			name: "IX_Receipts_SourcePaperlessDocumentId",
			schema: "receipts",
			table: "Receipts");
	}
}
