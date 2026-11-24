namespace Domain.Core;

public class Receipt
{
	public Guid Id { get; set; }
	public string Location { get; set; }
	public DateOnly Date { get; set; }
	public Money TaxAmount { get; set; }
	public bool TaxUnknown { get; set; }
	public string? SourceKind { get; set; }
	// Optional receipt-level "paid from" envelope, applied to its items as receipt-sourced.
	public Guid? PaidFromEnvelopeId { get; set; }
	public Guid? OccasionId { get; set; }
	/// <summary>Paperless-ngx document ID this receipt was imported from, if any. Never a financial reference.</summary>
	public long? SourcePaperlessDocumentId { get; set; }

	public const string LocationCannotBeEmpty = "Location cannot be empty";
	public const string SourcePaperlessDocumentIdOutOfRange = "Paperless document ID is out of range.";

	public const string SourceKindUnknown = "unknown";

	public static readonly string[] AdmittedSourceKinds = ["photo-observed", "user-reported", "paperless-verified"];

	public const string SourceKindNotAdmitted = "Source kind is not admitted. Use photo-observed, user-reported, or paperless-verified.";

	public const string PaperlessVerifiedRequiresDocumentId = "paperless-verified requires the linked Paperless document ID.";

	public Receipt(Guid id, string location, DateOnly date, Money taxAmount, string? sourceKind = null, bool taxUnknown = false)
	{
		if (string.IsNullOrWhiteSpace(location))
		{
			throw new ArgumentException(LocationCannotBeEmpty, nameof(location));
		}

		Id = id;
		Location = location;
		Date = date;
		TaxAmount = taxAmount;
		SourceKind = sourceKind;
		TaxUnknown = taxUnknown;
	}
}
