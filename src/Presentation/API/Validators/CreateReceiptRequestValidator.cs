using API.Generated.Dtos;
using Application.Validation;
using FluentValidation;

namespace API.Validators;

public class CreateReceiptRequestValidator : AbstractValidator<CreateReceiptRequest>
{
	public const string DateMustBePriorToCurrentDate = "Date must be prior to the current date";

	public const string LocationMustNotBeEmpty = "Location must not be empty.";

	public const string SourcePaperlessDocumentIdOutOfRange = "Paperless document ID is out of range.";

	public CreateReceiptRequestValidator(AdmissionDatePolicy datePolicy)
	{
		RuleFor(x => x.Location)
			.NotEmpty()
			.WithMessage(LocationMustNotBeEmpty);

		RuleFor(x => x.Date)
			.Must(datePolicy.IsNotFuture)
			.WithMessage(DateMustBePriorToCurrentDate);

		RuleFor(x => x.SourcePaperlessDocumentId)
			.Must(id => id is null || (id >= 1 && id <= 2147483647))
			.WithMessage(SourcePaperlessDocumentIdOutOfRange);
	}
}
