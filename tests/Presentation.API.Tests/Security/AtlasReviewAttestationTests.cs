using System.Text;
using System.Text.Json.Nodes;
using API.Security;
using FluentAssertions;

namespace Presentation.API.Tests.Security;

public class AtlasReviewAttestationTests
{
	private const string Reviewer = "jayson";
	private const string Hash = "8c096a22a96daf6220539bf8aeff3dae7c9dac06f25aac1d264f81e04d0f4e59";

	private static string Header(object? overrides = null, string? reviewer = Reviewer, string? approvedAt = null)
	{
		JsonObject evidence = new()
		{
			["reviewer"] = reviewer,
			["approvedAt"] = approvedAt ?? DateTimeOffset.UtcNow.ToString("O"),
			["ocrSha256"] = Hash,
			["draftRevision"] = "4",
			["finding"] = AtlasReviewAttestation.ConfidenceFinding
		};
		if (overrides is string json)
		{
			return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
		}
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(evidence.ToJsonString()));
	}

	[Fact]
	public void Valid_attestation_parses_for_the_authenticated_reviewer()
	{
		AtlasReviewAttestation.TryParse(Header(), Reviewer, out AtlasReviewAttestation? attestation, out string error)
			.Should().BeTrue(error);
		attestation!.Reviewer.Should().Be(Reviewer);
		attestation.OcrSha256.Should().Be(Hash);
		attestation.DraftRevision.Should().Be("4");
	}

	[Fact]
	public void Missing_header_is_rejected()
	{
		AtlasReviewAttestation.TryParse(null, Reviewer, out _, out string error).Should().BeFalse();
		error.Should().Contain("required");
	}

	[Fact]
	public void Unauthenticated_caller_cannot_approve()
	{
		AtlasReviewAttestation.TryParse(Header(), null, out _, out string error).Should().BeFalse();
		error.Should().Contain("signed-in reviewer");
	}

	[Fact]
	public void Attestation_naming_another_reviewer_is_rejected()
	{
		AtlasReviewAttestation.TryParse(Header(reviewer: "someone-else"), Reviewer, out _, out string error).Should().BeFalse();
		error.Should().Contain("authenticated reviewer");
	}

	[Theory]
	[InlineData("not-base64!")]
	[InlineData("eyJvY2xTb25seSI6MX0=")]
	public void Malformed_evidence_is_rejected(string header)
	{
		AtlasReviewAttestation.TryParse(header, Reviewer, out _, out _).Should().BeFalse();
	}

	[Fact]
	public void Short_ocr_digest_is_rejected()
	{
		string json = $$"""{"reviewer":"{{Reviewer}}","approvedAt":"2026-11-20T06:30:00Z","ocrSha256":"abcd","draftRevision":"4","finding":"{{AtlasReviewAttestation.ConfidenceFinding}}"}""";
		AtlasReviewAttestation.TryParse(Header(json), Reviewer, out _, out string error).Should().BeFalse();
		error.Should().Contain("OCR digest");
	}

	[Fact]
	public void A_structural_finding_cannot_be_approved()
	{
		string json = $$"""{"reviewer":"{{Reviewer}}","approvedAt":"2026-11-20T06:30:00Z","ocrSha256":"{{Hash}}","draftRevision":"4","finding":"Printed total does not reconcile with items, tax and adjustments"}""";
		AtlasReviewAttestation.TryParse(Header(json), Reviewer, out _, out string error).Should().BeFalse();
		error.Should().Contain("cannot be approved");
	}

	[Fact]
	public void Binding_replaces_any_client_supplied_review()
	{
		AtlasReviewAttestation.TryParse(Header(), Reviewer, out AtlasReviewAttestation? attestation, out _).Should().BeTrue();
		const string payload = """{"expectedRevision":4,"review":{"reviewer":"mallory","approvedAt":"1999-01-01T00:00:00Z","ocrSha256":"old","draftRevision":"1"}}""";

		AtlasReviewAttestation.TryBind(payload, attestation!, out string bound, out string error).Should().BeTrue(error);

		JsonNode body = JsonNode.Parse(bound)!;
		body["review"]!["reviewer"]!.GetValue<string>().Should().Be(Reviewer);
		body["review"]!["ocrSha256"]!.GetValue<string>().Should().Be(Hash);
		body["review"]!["draftRevision"]!.GetValue<string>().Should().Be("4");
		body["expectedRevision"]!.GetValue<int>().Should().Be(4);
	}

	[Fact]
	public void Expired_approval_is_rejected()
	{
		string stale = Header(approvedAt: DateTimeOffset.UtcNow.AddHours(-25).ToString("O"));
		AtlasReviewAttestation.TryParse(stale, Reviewer, out _, out string error).Should().BeFalse();
		error.Should().Contain("expired");
	}

	[Fact]
	public void Future_dated_approval_is_rejected()
	{
		string ahead = Header(approvedAt: DateTimeOffset.UtcNow.AddMinutes(30).ToString("O"));
		AtlasReviewAttestation.TryParse(ahead, Reviewer, out _, out string error).Should().BeFalse();
		error.Should().Contain("future");
	}

	[Fact]
	public void Binding_rejects_a_non_object_body()
	{
		AtlasReviewAttestation.TryParse(Header(), Reviewer, out AtlasReviewAttestation? attestation, out _).Should().BeTrue();
		AtlasReviewAttestation.TryBind("[1,2,3]", attestation!, out _, out string error).Should().BeFalse();
		error.Should().Contain("JSON object");
	}
}
