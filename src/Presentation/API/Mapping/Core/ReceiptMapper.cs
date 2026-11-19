using API.Generated.Dtos;
using Common;
using Domain;
using Domain.Core;
using Presentation.API.Contracts.Receipts;
using Riok.Mapperly.Abstractions;

namespace Presentation.API.Mapping.Core;

[Mapper]
public static partial class ReceiptMapper
{
	[MapperIgnoreTarget(nameof(Receipt.PaidFromEnvelopeId))]
	[MapperIgnoreTarget(nameof(Receipt.OccasionId))]
	[MapperIgnoreTarget(nameof(Receipt.SourcePaperlessDocumentId))]
	private static partial Receipt ToDomainPartial(CreateReceiptRequest request);

	public static Receipt ToDomain(CreateReceiptRequest request)
	{
		Receipt receipt = ToDomainPartial(request);
		if (request.SourcePaperlessDocumentId.HasValue)
		{
			receipt.SetSourcePaperlessDocument(request.SourcePaperlessDocumentId.Value);
		}

		return receipt;
	}

	public static partial ReceiptResponse ToResponse(Receipt receipt);
}
