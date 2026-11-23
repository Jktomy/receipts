using API.Tiller;
using Application.Models.Tiller;
using FluentAssertions;

namespace Presentation.API.Tests.Mapping;

public class TillerMapperTests
{
	private readonly TillerMapper _mapper = new();

	[Fact]
	public void ToSpreadsheetDtos_MapsVerifiedWireShape()
	{
		// Act
		IReadOnlyList<TillerSpreadsheetDto> result = _mapper.ToSpreadsheetDtos(
			[new TillerSpreadsheet("sheet-1", "Household", true, true, 24)]);

		// Assert
		result.Should().ContainSingle().Which.Should().Be(
			new TillerSpreadsheetDto("sheet-1", "Household", true, true, 24));
	}

	[Fact]
	public void ToAccountDtos_MapsDisplayNameShape()
	{
		// Act
		IReadOnlyList<TillerAccountDto> result = _mapper.ToAccountDtos(
			[new TillerAccount("acc-x1", "Rewards Checking", "Amex", "checking", "Asset")]);

		// Assert
		result.Should().ContainSingle().Which.Should().Be(
			new TillerAccountDto("acc-x1", "Rewards Checking", "Amex", "checking", "Asset"));
	}

	[Theory]
	[InlineData(TillerMatchDecision.Linked, "linked")]
	[InlineData(TillerMatchDecision.Manual, "manual")]
	[InlineData(TillerMatchDecision.Ambiguous, "ambiguous")]
	[InlineData(TillerMatchDecision.Taken, "taken")]
	[InlineData(TillerMatchDecision.None, "none")]
	[InlineData(TillerMatchDecision.Unknown, "unknown")]
	public void ToSuggestionDto_LowercasesEveryDecision(TillerMatchDecision decision, string expected)
	{
		// Act
		TillerMatchSuggestionDto result = _mapper.ToSuggestionDto(
			new TillerMatchSuggestion(decision, null, null,
				[new TillerMatchCandidate(Guid.NewGuid(), "t-1", 1)]));

		// Assert: the UI binds these exact lowercase tokens.
		result.Decision.Should().Be(expected);
		result.Candidates.Should().HaveCount(1);
	}
}
