using FluentAssertions;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Models;
using Xunit;

namespace MemoryTimeline.Tests.Models;

/// <summary>
/// Unit tests for the pure swimlane computation (no DB, no UI).
/// </summary>
public class LaneAssignmentTests
{
    private static TimelineEventDto CreateEvent(
        string id,
        string? category = null,
        List<string>? people = null,
        string? location = null,
        string? eraId = null,
        string? eraName = null,
        List<string>? tags = null)
    {
        return new TimelineEventDto
        {
            EventId = id,
            Title = $"Event {id}",
            StartDate = new DateTime(2024, 1, 1),
            Category = category,
            PeopleNames = people ?? new List<string>(),
            Location = location,
            EraId = eraId,
            EraName = eraName,
            TagNames = tags ?? new List<string>()
        };
    }

    #region Category Mode

    [Fact]
    public void ComputeLanes_CategoryMode_CreatesLanePerCategoryWithCorrectMembership()
    {
        // Arrange
        var work1 = CreateEvent("1", category: "work");
        var work2 = CreateEvent("2", category: "work");
        var travel = CreateEvent("3", category: "travel");
        var uncategorized = CreateEvent("4", category: null);

        // Act
        var lanes = LaneAssignment.ComputeLanes(
            new[] { work1, work2, travel, uncategorized }, LaneMode.Category);

        // Assert - alphabetical order, unassigned last, no empty lanes
        lanes.Should().HaveCount(3);
        lanes[0].Label.Should().Be("Travel");
        lanes[0].Events.Should().ContainSingle().Which.EventId.Should().Be("3");
        lanes[1].Label.Should().Be("Work");
        lanes[1].Events.Select(e => e.EventId).Should().BeEquivalentTo(new[] { "1", "2" });
        lanes[2].LaneKey.Should().Be(LaneAssignment.UnassignedLaneKey);
        lanes[2].Events.Should().ContainSingle().Which.EventId.Should().Be("4");
        lanes.Should().OnlyContain(l => l.Events.Count > 0, "empty lanes are suppressed");
    }

    [Fact]
    public void ComputeLanes_CategoryMode_StampsLaneKeyAndIndexOnEvents()
    {
        // Arrange
        var work = CreateEvent("1", category: "work");
        var travel = CreateEvent("2", category: "travel");
        var uncategorized = CreateEvent("3");

        // Act
        LaneAssignment.ComputeLanes(new[] { work, travel, uncategorized }, LaneMode.Category);

        // Assert - Travel(0), Work(1), (unassigned)(2)
        travel.LaneKey.Should().Be("travel");
        travel.LaneIndex.Should().Be(0);
        work.LaneKey.Should().Be("work");
        work.LaneIndex.Should().Be(1);
        uncategorized.LaneKey.Should().Be(LaneAssignment.UnassignedLaneKey);
        uncategorized.LaneIndex.Should().Be(2);
    }

    [Fact]
    public void ComputeLanes_CategoryMode_AbsentCategoriesGetNoLane()
    {
        // Arrange
        var work = CreateEvent("1", category: "work");

        // Act
        var lanes = LaneAssignment.ComputeLanes(new[] { work }, LaneMode.Category);

        // Assert
        lanes.Should().HaveCount(1);
        lanes.Should().NotContain(l => l.Label == "Education");
    }

    #endregion

    #region Person Mode

    [Fact]
    public void ComputeLanes_PersonMode_MultiPersonEventUsesFirstAlphabeticalName()
    {
        // Arrange - names deliberately unsorted and mixed-case
        var evt = CreateEvent("1", people: new List<string> { "Zoe", "anna" });

        // Act
        var lanes = LaneAssignment.ComputeLanes(new[] { evt }, LaneMode.Person);

        // Assert - the FIRST value alphabetically (case-insensitive) wins;
        // the event appears in exactly one lane.
        lanes.Should().HaveCount(1);
        lanes[0].Label.Should().Be("anna");
        lanes[0].Events.Should().ContainSingle().Which.EventId.Should().Be("1");
        evt.LaneKey.Should().Be("anna");
    }

    [Fact]
    public void ComputeLanes_PersonMode_EventWithoutPeopleGoesToUnassignedLaneLast()
    {
        // Arrange
        var withPerson = CreateEvent("1", people: new List<string> { "Anna" });
        var alone = CreateEvent("2");

        // Act
        var lanes = LaneAssignment.ComputeLanes(new[] { withPerson, alone }, LaneMode.Person);

        // Assert
        lanes.Should().HaveCount(2);
        lanes[0].Label.Should().Be("Anna");
        lanes[^1].LaneKey.Should().Be(LaneAssignment.UnassignedLaneKey);
        lanes[^1].Events.Should().ContainSingle().Which.EventId.Should().Be("2");
    }

    #endregion

    #region Location / Era / Tag Modes

    [Fact]
    public void ComputeLanes_LocationMode_CaseInsensitiveValuesShareOneLane()
    {
        // Arrange
        var lower = CreateEvent("1", location: "paris");
        var upper = CreateEvent("2", location: "Paris");

        // Act
        var lanes = LaneAssignment.ComputeLanes(new[] { lower, upper }, LaneMode.Location);

        // Assert - one lane, first-seen casing provides the label
        lanes.Should().HaveCount(1);
        lanes[0].Label.Should().Be("paris");
        lanes[0].Events.Should().HaveCount(2);
    }

    [Fact]
    public void ComputeLanes_EraMode_UsesEraIdKeysAndEraNameLabels()
    {
        // Arrange
        var college = CreateEvent("1", eraId: "era-1", eraName: "College");
        var army = CreateEvent("2", eraId: "era-2", eraName: "Army");
        var noEra = CreateEvent("3");

        // Act
        var lanes = LaneAssignment.ComputeLanes(new[] { college, army, noEra }, LaneMode.Era);

        // Assert - ordered by era NAME, keyed by era ID, unassigned last
        lanes.Should().HaveCount(3);
        lanes[0].Label.Should().Be("Army");
        lanes[0].LaneKey.Should().Be("era-2");
        lanes[1].Label.Should().Be("College");
        lanes[1].LaneKey.Should().Be("era-1");
        lanes[2].LaneKey.Should().Be(LaneAssignment.UnassignedLaneKey);
    }

    [Fact]
    public void ComputeLanes_TagMode_UsesFirstAlphabeticalTag()
    {
        // Arrange
        var evt = CreateEvent("1", tags: new List<string> { "travel", "Alps" });

        // Act
        var lanes = LaneAssignment.ComputeLanes(new[] { evt }, LaneMode.Tag);

        // Assert
        lanes.Should().HaveCount(1);
        lanes[0].Label.Should().Be("Alps");
        evt.LaneKey.Should().Be("Alps");
    }

    #endregion

    #region Auto Mode (passthrough)

    [Fact]
    public void ComputeLanes_AutoMode_ReturnsNoLanesAndClearsAssignments()
    {
        // Arrange - stale lane state from a previous mode
        var evt = CreateEvent("1", category: "work");
        evt.LaneKey = "work";
        evt.LaneIndex = 3;

        // Act
        var lanes = LaneAssignment.ComputeLanes(new[] { evt }, LaneMode.Auto);

        // Assert - Auto is a passthrough: no lanes, assignments cleared, so the
        // caller keeps the classic track-stacked positions untouched.
        lanes.Should().BeEmpty();
        evt.LaneKey.Should().BeNull();
        evt.LaneIndex.Should().Be(0);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ComputeLanes_EmptyInput_ReturnsNoLanes()
    {
        // Act
        var lanes = LaneAssignment.ComputeLanes(Array.Empty<TimelineEventDto>(), LaneMode.Category);

        // Assert
        lanes.Should().BeEmpty();
    }

    [Fact]
    public void ComputeLanes_BlankValues_CountAsUnassigned()
    {
        // Arrange - whitespace-only values must not create a phantom lane
        var blankCategory = CreateEvent("1", category: "   ");
        var blankPerson = CreateEvent("2", people: new List<string> { "  " });

        // Act
        var categoryLanes = LaneAssignment.ComputeLanes(new[] { blankCategory }, LaneMode.Category);
        var personLanes = LaneAssignment.ComputeLanes(new[] { blankPerson }, LaneMode.Person);

        // Assert
        categoryLanes.Should().ContainSingle().Which.LaneKey.Should().Be(LaneAssignment.UnassignedLaneKey);
        personLanes.Should().ContainSingle().Which.LaneKey.Should().Be(LaneAssignment.UnassignedLaneKey);
    }

    #endregion
}
