using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace API.Security;

/// <summary>
/// Reviewer attestation for a LOW-confidence Paperless extraction. The browser
/// sends it as a base64 JSON header; the reviewer identity is taken from the
/// authenticated principal, never from the payload, so a request body cannot
/// claim someone else's approval. Binding to the exact OCR text and draft
/// revision is re-checked by the bridge, which is the only component holding
/// the source document.
/// </summary>
public sealed record AtlasReviewAttestation(string Reviewer, DateTimeOffset ApprovedAt, string OcrSha256, string DraftRevision)
{
	public const string HeaderName = "X-Atlas-Review-Attestation";
	public const string ConfidenceFinding = "Model did not attest high confidence";
	private const int MaximumHeaderLength = 2048;
	private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);
	private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);

	public static bool TryParse(string? headerValue, string? principalName, out AtlasReviewAttestation? attestation, out string error)
	{
		attestation = null;
		error = string.Empty;
		if (string.IsNullOrWhiteSpace(principalName))
		{
			error = "A signed-in reviewer is required to approve an import.";
			return false;
		}
		if (string.IsNullOrWhiteSpace(headerValue))
		{
			error = "Reviewer attestation is required to approve an import.";
			return false;
		}
		if (headerValue.Length > MaximumHeaderLength)
		{
			error = "Reviewer attestation is too large.";
			return false;
		}

		string json;
		try
		{
			json = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(headerValue.Trim())));
		}
		catch (FormatException)
		{
			error = "Reviewer attestation is not valid base64.";
			return false;
		}

		try
		{
			JsonNode? root = JsonNode.Parse(json);
			if (root is not JsonObject evidence)
			{
				error = "Reviewer attestation must be a JSON object.";
				return false;
			}
			string reviewer = Text(evidence, "reviewer");
			if (!string.Equals(reviewer, principalName.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				error = "Reviewer attestation must name the authenticated reviewer.";
				return false;
			}
			if (!DateTimeOffset.TryParse(Text(evidence, "approvedAt"), out DateTimeOffset approvedAt))
			{
				error = "Reviewer attestation has no valid approval timestamp.";
				return false;
			}
			// An approval is evidence for a review session, not a permanent key:
			// the same OCR and revision must not stay approvable indefinitely.
			DateTimeOffset now = DateTimeOffset.UtcNow;
			if (now - approvedAt > MaximumAge)
			{
				error = "Reviewer attestation has expired; approve the draft again.";
				return false;
			}
			if (approvedAt - now > MaximumClockSkew)
			{
				error = "Reviewer attestation is timestamped in the future.";
				return false;
			}
			string hash = Text(evidence, "ocrSha256");
			if (hash.Length != 64 || !hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
			{
				error = "Reviewer attestation is not bound to a source OCR digest.";
				return false;
			}
			string revision = Text(evidence, "draftRevision");
			if (revision.Length is 0 or > 32)
			{
				error = "Reviewer attestation is not bound to a draft revision.";
				return false;
			}
			if (!string.Equals(Text(evidence, "finding"), ConfidenceFinding, StringComparison.Ordinal))
			{
				error = "Reviewer attestation covers a finding that cannot be approved.";
				return false;
			}
			attestation = new(reviewer, approvedAt, hash, revision);
			return true;
		}
		catch (JsonException)
		{
			error = "Reviewer attestation is not valid JSON.";
			return false;
		}
	}

	/// <summary>Copies the attestation into the bridge's review field, replacing anything sent by the client.</summary>
	public static bool TryBind(string payload, AtlasReviewAttestation attestation, out string bound, out string error)
	{
		bound = payload;
		error = string.Empty;
		JsonNode? root;
		try
		{
			root = JsonNode.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
		}
		catch (JsonException)
		{
			error = "Import body is not valid JSON.";
			return false;
		}
		if (root is not JsonObject body)
		{
			error = "Import body must be a JSON object.";
			return false;
		}
		body["review"] = new JsonObject
		{
			["reviewer"] = attestation.Reviewer,
			["approvedAt"] = attestation.ApprovedAt.ToUniversalTime().ToString("O"),
			["ocrSha256"] = attestation.OcrSha256,
			["draftRevision"] = attestation.DraftRevision
		};
		bound = body.ToJsonString();
		return true;
	}

	private static string Text(JsonObject source, string name)
		=> source[name] is JsonValue value && value.TryGetValue(out string? text) ? text.Trim() : string.Empty;

	private static string Pad(string value)
		=> value.Length % 4 == 0 ? value : value.PadRight(value.Length + (4 - value.Length % 4), '=');
}
