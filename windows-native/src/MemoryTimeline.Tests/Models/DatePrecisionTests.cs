using FluentAssertions;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Data.Models;
using Xunit;

namespace MemoryTimeline.Tests.Models;

/// <summary>
/// Pure model tests for <see cref="DatePrecisionExtensions.GetWindow"/>,
/// <see cref="DatePrecisionParser"/>, and <see cref="DateDisplay"/>. No database.
/// </summary>
public class DatePrecisionTests
{
    #region GetWindow

    [Fact]
    public void GetWindow_ExactPrecision_ReturnsDegenerateWindowAtAnchor()
    {
        // Arrange
        var anchor = new DateTime(2019, 3, 14, 10, 30, 0);

        // Act
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(anchor, DatePrecision.Exact);

        // Assert
        earliest.Should().Be(anchor);
        latest.Should().Be(anchor);
    }

    [Fact]
    public void GetWindow_DayPrecision_SpansMidnightToEndOfDay()
    {
        // Arrange
        var anchor = new DateTime(2019, 3, 14, 10, 30, 0);

        // Act
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(anchor, DatePrecision.Day);

        // Assert
        earliest.Should().Be(new DateTime(2019, 3, 14, 0, 0, 0));
        latest.Should().Be(new DateTime(2019, 3, 14, 23, 59, 59));
    }

    [Fact]
    public void GetWindow_MonthPrecision_LeapYearFebruary_EndsOnThe29th()
    {
        // Arrange
        var anchor = new DateTime(2020, 2, 15, 8, 0, 0);

        // Act
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(anchor, DatePrecision.Month);

        // Assert
        earliest.Should().Be(new DateTime(2020, 2, 1, 0, 0, 0));
        latest.Should().Be(new DateTime(2020, 2, 29, 23, 59, 59));
    }

    [Fact]
    public void GetWindow_MonthPrecision_NonLeapFebruary_EndsOnThe28th()
    {
        // Act
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(
            new DateTime(2019, 2, 10), DatePrecision.Month);

        // Assert
        earliest.Should().Be(new DateTime(2019, 2, 1, 0, 0, 0));
        latest.Should().Be(new DateTime(2019, 2, 28, 23, 59, 59));
    }

    [Fact]
    public void GetWindow_SeasonPrecision_DecemberAnchor_CrossesIntoNextYear()
    {
        // Arrange - December's winter runs Dec 1 .. end of February NEXT year
        var anchor = new DateTime(1998, 12, 25);

        // Act
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(anchor, DatePrecision.Season);

        // Assert
        earliest.Should().Be(new DateTime(1998, 12, 1, 0, 0, 0));
        latest.Should().Be(new DateTime(1999, 2, 28, 23, 59, 59));
    }

    [Fact]
    public void GetWindow_SeasonPrecision_JanuaryAnchor_SharesTheWindowOfPreviousDecember()
    {
        // Act - January belongs to the winter that started the previous December
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(
            new DateTime(1999, 1, 15), DatePrecision.Season);

        // Assert - identical window to a December 1998 anchor
        earliest.Should().Be(new DateTime(1998, 12, 1, 0, 0, 0));
        latest.Should().Be(new DateTime(1999, 2, 28, 23, 59, 59));
    }

    [Fact]
    public void GetWindow_SeasonPrecision_WinterEndingInLeapYear_EndsOnFebruary29()
    {
        // Act
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(
            new DateTime(2020, 1, 15), DatePrecision.Season);

        // Assert
        earliest.Should().Be(new DateTime(2019, 12, 1, 0, 0, 0));
        latest.Should().Be(new DateTime(2020, 2, 29, 23, 59, 59));
    }

    [Theory]
    [InlineData(3, 3, 5)]   // spring: Mar-May
    [InlineData(4, 3, 5)]
    [InlineData(5, 3, 5)]
    [InlineData(6, 6, 8)]   // summer: Jun-Aug
    [InlineData(7, 6, 8)]
    [InlineData(8, 6, 8)]
    [InlineData(9, 9, 11)]  // autumn: Sep-Nov
    [InlineData(10, 9, 11)]
    [InlineData(11, 9, 11)]
    public void GetWindow_SeasonPrecision_NonWinterMonths_UseSameYearWindow(
        int anchorMonth, int expectedStartMonth, int expectedEndMonth)
    {
        // Act
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(
            new DateTime(2019, anchorMonth, 10), DatePrecision.Season);

        // Assert
        earliest.Should().Be(new DateTime(2019, expectedStartMonth, 1, 0, 0, 0));
        latest.Should().Be(new DateTime(
            2019, expectedEndMonth, DateTime.DaysInMonth(2019, expectedEndMonth), 23, 59, 59));
    }

    [Fact]
    public void GetWindow_YearPrecision_SpansWholeCalendarYear()
    {
        // Act
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(
            new DateTime(1998, 7, 1), DatePrecision.Year);

        // Assert
        earliest.Should().Be(new DateTime(1998, 1, 1, 0, 0, 0));
        latest.Should().Be(new DateTime(1998, 12, 31, 23, 59, 59));
    }

    [Theory]
    [InlineData(1990)] // first year of the decade
    [InlineData(1994)] // middle
    [InlineData(1999)] // last year of the decade
    public void GetWindow_DecadePrecision_AllAnchorsInDecade_ShareTheSameWindow(int anchorYear)
    {
        // Act
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(
            new DateTime(anchorYear, 6, 15), DatePrecision.Decade);

        // Assert
        earliest.Should().Be(new DateTime(1990, 1, 1, 0, 0, 0));
        latest.Should().Be(new DateTime(1999, 12, 31, 23, 59, 59));
    }

    [Fact]
    public void GetWindow_DecadePrecision_YearTwoThousand_StartsANewDecade()
    {
        // Act
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(
            new DateTime(2000, 1, 1), DatePrecision.Decade);

        // Assert
        earliest.Should().Be(new DateTime(2000, 1, 1, 0, 0, 0));
        latest.Should().Be(new DateTime(2009, 12, 31, 23, 59, 59));
    }

    [Fact]
    public void GetWindow_UnknownPrecision_ReturnsDegenerateWindowAtAnchor()
    {
        // Arrange - Unknown cannot honestly derive a window; documented degenerate
        var anchor = new DateTime(2011, 7, 1, 12, 0, 0);

        // Act
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(anchor, DatePrecision.Unknown);

        // Assert
        earliest.Should().Be(anchor);
        latest.Should().Be(anchor);
    }

    #endregion

    #region DatePrecisionParser

    [Theory]
    [InlineData("exact", DatePrecision.Exact)]
    [InlineData("day", DatePrecision.Day)]
    [InlineData("month", DatePrecision.Month)]
    [InlineData("season", DatePrecision.Season)]
    [InlineData("year", DatePrecision.Year)]
    [InlineData("decade", DatePrecision.Decade)]
    [InlineData("unknown", DatePrecision.Unknown)]
    public void Parse_CanonicalLowercaseValues_MapToEnum(string value, DatePrecision expected)
    {
        DatePrecisionParser.Parse(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("SEASON", DatePrecision.Season)]
    [InlineData("YeAr", DatePrecision.Year)]
    [InlineData("Exact", DatePrecision.Exact)]
    [InlineData(" decade ", DatePrecision.Decade)]
    public void Parse_MixedCaseAndWhitespace_MapsCaseInsensitively(string value, DatePrecision expected)
    {
        DatePrecisionParser.Parse(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("fortnight")]
    public void Parse_NullEmptyOrUnrecognized_FallsBackToDay(string? value)
    {
        DatePrecisionParser.Parse(value).Should().Be(DatePrecision.Day);
    }

    [Theory]
    [InlineData(DatePrecision.Exact)]
    [InlineData(DatePrecision.Day)]
    [InlineData(DatePrecision.Month)]
    [InlineData(DatePrecision.Season)]
    [InlineData(DatePrecision.Year)]
    [InlineData(DatePrecision.Decade)]
    [InlineData(DatePrecision.Unknown)]
    public void ToStringValue_RoundTripsThroughParse(DatePrecision precision)
    {
        DatePrecisionParser.Parse(DatePrecisionParser.ToStringValue(precision))
            .Should().Be(precision);
    }

    #endregion

    #region DateDisplay

    [Fact]
    public void FormatPrecise_ExactPrecision_IncludesTime()
    {
        DateDisplay.FormatPrecise(new DateTime(2019, 3, 14, 10, 30, 0), DatePrecision.Exact)
            .Should().Be("14 Mar 2019, 10:30");
    }

    [Fact]
    public void FormatPrecise_DayPrecision_ShowsDayMonthYear()
    {
        DateDisplay.FormatPrecise(new DateTime(2019, 3, 14, 10, 30, 0), DatePrecision.Day)
            .Should().Be("14 Mar 2019");
    }

    [Fact]
    public void FormatPrecise_MonthPrecision_ShowsFullMonthAndYear()
    {
        DateDisplay.FormatPrecise(new DateTime(2019, 3, 14), DatePrecision.Month)
            .Should().Be("March 2019");
    }

    [Fact]
    public void FormatPrecise_SeasonPrecision_SummerAnchor_ShowsSeasonAndYear()
    {
        DateDisplay.FormatPrecise(new DateTime(1998, 7, 4), DatePrecision.Season)
            .Should().Be("Summer 1998");
    }

    [Fact]
    public void FormatPrecise_SeasonPrecision_WinterIsLabeledByItsStartingDecemberYear()
    {
        // December 1998, January 1999, and February 1999 share one window and one label
        DateDisplay.FormatPrecise(new DateTime(1998, 12, 25), DatePrecision.Season)
            .Should().Be("Winter 1998");
        DateDisplay.FormatPrecise(new DateTime(1999, 1, 15), DatePrecision.Season)
            .Should().Be("Winter 1998");
        DateDisplay.FormatPrecise(new DateTime(1999, 2, 10), DatePrecision.Season)
            .Should().Be("Winter 1998");
    }

    [Fact]
    public void FormatPrecise_YearPrecision_ShowsYearOnly()
    {
        DateDisplay.FormatPrecise(new DateTime(1998, 7, 1), DatePrecision.Year)
            .Should().Be("1998");
    }

    [Fact]
    public void FormatPrecise_DecadePrecision_ShowsDecade()
    {
        DateDisplay.FormatPrecise(new DateTime(1994, 6, 15), DatePrecision.Decade)
            .Should().Be("1990s");
    }

    [Fact]
    public void FormatPrecise_UnknownPrecision_ShowsDateUnknown()
    {
        DateDisplay.FormatPrecise(new DateTime(2011, 7, 1), DatePrecision.Unknown)
            .Should().Be("Date unknown");
    }

    [Fact]
    public void FormatPrecise_DayPrecisionWithEndDate_ShowsRange()
    {
        DateDisplay.FormatPrecise(
                new DateTime(2019, 3, 14), DatePrecision.Day, new DateTime(2019, 3, 20))
            .Should().Be("14 Mar 2019 - 20 Mar 2019");
    }

    [Fact]
    public void FormatPrecise_CoarsePrecisionWithEndDate_IgnoresEnd()
    {
        DateDisplay.FormatPrecise(
                new DateTime(1998, 7, 4), DatePrecision.Season, new DateTime(1998, 8, 1))
            .Should().Be("Summer 1998");
    }

    [Theory]
    [InlineData(DatePrecision.Exact, "Exact time")]
    [InlineData(DatePrecision.Day, "Day")]
    [InlineData(DatePrecision.Season, "Season")]
    [InlineData(DatePrecision.Unknown, "Unknown")]
    public void GetPrecisionLabel_ReturnsShortHumanLabel(DatePrecision precision, string expected)
    {
        DateDisplay.GetPrecisionLabel(precision).Should().Be(expected);
    }

    #endregion
}
